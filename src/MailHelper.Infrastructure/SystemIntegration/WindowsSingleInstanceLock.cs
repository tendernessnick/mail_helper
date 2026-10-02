using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using MailHelper.Core.Abstractions;

namespace MailHelper.Infrastructure.SystemIntegration;

/// <summary>ISingleInstanceLock Windows 实现（docs/10 §4 P-03）：命名 Mutex + 二次启动经命名管道唤起主窗口
/// （EX-TC-07；04 §4.4）。由 App/SingleInstance.cs 静态类等价迁移（MS1）：逻辑逐行保留，仅改为实例实现。
/// mutexName/pipeName 可注入（契约测试用唯一名称隔离，模式同 AutostartService 注入 runKeyPath）。
/// 语义边界：Windows 命名互斥体同线程可重入——TryAcquireFirst 的「第二实例返回 false」以跨进程/跨线程
/// 竞争为前提（真实二次启动=独立进程）；同线程重复调用会递归计数而假成功（契约测试以另一线程覆盖）。
/// 管道用同步 API 完成一次性通知（规避 async void 与 .Result 红线）。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSingleInstanceLock(string? mutexName = null, string? pipeName = null) : ISingleInstanceLock
{
    public const string MutexName = "Local\\MailHelper-SingleInstance";
    public const string PipeName = "MailHelper-SingleInstance-Pipe";

    private readonly string _mutexName = mutexName ?? MutexName;
    private readonly string _pipeName = pipeName ?? PipeName;

    private Mutex? _mutex;
    private CancellationTokenSource? _listenCts;

    /// <summary>尝试成为首个实例；已有实例在运行时返回 false。</summary>
    public bool TryAcquireFirst()
    {
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
        return createdNew || _mutex.WaitOne(0);
    }

    /// <summary>通知运行中的实例唤起主窗口（二次启动路径：发送后立即退出进程）。</summary>
    public void NotifyRunningInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(500);
            var payload = Encoding.UTF8.GetBytes("SHOW");
            client.Write(payload, 0, payload.Length);
            client.Flush();
        }
        catch (IOException)
        {
            // 首实例管道尚未就绪：无可唤起，静默退出
        }
        catch (TimeoutException)
        {
        }
    }

    /// <summary>首实例开始监听唤起请求（进程生命周期后台任务，Dispose 时取消）。</summary>
    public void StartListening(Action activate)
    {
        _listenCts = new CancellationTokenSource();
        var ct = _listenCts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var pipe = CreateServerPipe();
                    await pipe.WaitForConnectionAsync(ct);
                    var buffer = new byte[16];
                    _ = await pipe.ReadAsync(buffer, ct);
                    pipe.Dispose();
                    if (Encoding.UTF8.GetString(buffer).StartsWith("SHOW"))
                    {
                        activate();
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (IOException)
                {
                    // 客户端异常断开：继续下一轮监听
                }
            }
        }, ct);
    }

    /// <summary>释放互斥并停止监听（等价原静态类 StopListening；App OnExit 调用）。</summary>
    public void Dispose()
    {
        _listenCts?.Cancel();
        _listenCts?.Dispose();
        _listenCts = null;
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose();
        _mutex = null;
    }

    // ACL 版命名管道：仅当前用户可连（09 章 §4 数据最小化）
    private NamedPipeServerStream CreateServerPipe() => NamedPipeServerStreamAcl.Create(
        _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, CreatePipeSecurity());

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        var identity = WindowsIdentity.GetCurrent();
        security.AddAccessRule(new PipeAccessRule(
            identity.User!,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        return security;
    }
}
