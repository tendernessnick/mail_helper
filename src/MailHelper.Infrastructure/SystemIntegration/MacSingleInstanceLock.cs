using System.Net;
using System.Net.Sockets;
using System.Text;
using MailHelper.Core.Abstractions;

namespace MailHelper.Infrastructure.SystemIntegration;

/// <summary>ISingleInstanceLock macOS 实现（docs/10 §4 P-03）：Unix domain socket 文件锁。
/// socket 落数据目录（instance.sock）→ MAILHELPER_DATA_DIR 隔离即测试隔离（Windows 版全局 Mutex 的
/// 脆弱性在此规避，docs/10 §8.2）。bind = 所有权标记；探测可连=已有实例；连不上且文件存在=崩溃残留，
/// 经多次复probe确认后清理再 bind（避免误删「已 bind 未监听」的存活实例，见 StartListening 同步语义注释）。
/// AF_UNIX 在 .NET 8 全平台可用 → 契约测试全平台可跑；真实 macOS 行为由 CI macos job 兜底。</summary>
public sealed class MacSingleInstanceLock : ISingleInstanceLock
{
    private readonly string _socketPath;
    private Socket? _listener;
    private CancellationTokenSource? _listenCts;

    public MacSingleInstanceLock(string? socketPathOverride = null) =>
        _socketPath = socketPathOverride ?? DefaultSocketPath(new MacAppPaths().DataDir);

    public static string DefaultSocketPath(string dataDir) => Path.Combine(dataDir, "instance.sock");

    public bool TryAcquireFirst()
    {
        if (File.Exists(_socketPath) && IsSocketAlive(_socketPath))
        {
            return false; // 已有实例（bind 存活）
        }

        if (File.Exists(_socketPath))
        {
            // 崩溃残留：复probe（100ms×3）确认非「bind 未监听」窗口后再清理
            for (var attempt = 0; attempt < 3 && File.Exists(_socketPath); attempt++)
            {
                Thread.Sleep(100);
                if (IsSocketAlive(_socketPath))
                {
                    return false;
                }
            }

            try { File.Delete(_socketPath); } catch (IOException) { }
        }

        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
            listener.Listen(backlog: 4);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            listener.Dispose();
            return false;
        }

        _listener = listener;
        return true;
    }

    public void NotifyRunningInstance()
    {
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(_socketPath));
            client.Send(Encoding.UTF8.GetBytes("SHOW"));
        }
        catch (SocketException)
        {
            // 首实例未就绪：无可唤起，静默（与 Windows 实现同语义）
        }
        catch (FileNotFoundException)
        {
        }
    }

    public void StartListening(Action activate)
    {
        _listenCts = new CancellationTokenSource();
        var ct = _listenCts.Token;
        var listener = _listener ?? throw new InvalidOperationException("先 TryAcquireFirst 再 StartListening");
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var client = await listener.AcceptAsync(ct).ConfigureAwait(false);
                    var buffer = new byte[16];
                    var read = await client.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (Encoding.UTF8.GetString(buffer, 0, read).StartsWith("SHOW"))
                    {
                        activate();
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException)
                {
                    // 客户端异常断开：继续监听
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }, ct);
    }

    public void Dispose()
    {
        _listenCts?.Cancel();
        _listenCts?.Dispose();
        _listenCts = null;
        _listener?.Dispose();
        _listener = null;
        try { File.Delete(_socketPath); } catch (IOException) { }
    }

    private static bool IsSocketAlive(string path)
    {
        try
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            probe.Connect(new UnixDomainSocketEndPoint(path));
            return true; // 能连上=存活实例
        }
        catch (SocketException)
        {
            return false; // 连接被拒：文件存在但无监听者（残留或 bind 未监听窗口）
        }
    }
}
