using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace MailHelper.App;

/// <summary>单实例（04 §4：命名 Mutex + 二次启动经命名管道唤起主窗口，EX-TC-07）。
/// 管道用同步 API 完成一次性通知（规避 async void 与 .Result 红线）。</summary>
public static class SingleInstance
{
    public const string MutexName = "Local\\MailHelper-SingleInstance";
    public const string PipeName = "MailHelper-SingleInstance-Pipe";

    private static Mutex? _mutex;
    private static CancellationTokenSource? _listenCts;

    /// <summary>尝试成为首个实例；已有实例在运行时返回 false。</summary>
    public static bool TryAcquireFirstInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        return createdNew || _mutex.WaitOne(0);
    }

    /// <summary>通知运行中的实例唤起主窗口（二次启动路径：发送后立即退出进程）。</summary>
    public static void NotifyRunningInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
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

    /// <summary>首实例开始监听唤起请求（进程生命周期后台任务，Exit 时取消）。</summary>
    public static void StartListening(Action activate)
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

    public static void StopListening()
    {
        _listenCts?.Cancel();
        _listenCts?.Dispose();
        _listenCts = null;
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose();
        _mutex = null;
    }

    // ACL 版命名管道：仅当前用户可连（09 章 §4 数据最小化）
    private static NamedPipeServerStream CreateServerPipe() => NamedPipeServerStreamAcl.Create(
        PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, CreatePipeSecurity());

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
