using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace MailHelper.Infrastructure.Sync;

/// <summary>专用 STA 线程调度器：Outlook COM 对象须在其创建的 STA 单元内使用，
/// 所有调用经此序列化投递（避免 MAPI RPC_E_WRONG_THREAD）。</summary>
internal sealed class StaDispatcher : IDisposable
{
    private readonly Thread _thread;
    private readonly BlockingCollection<(Func<object?> Work, TaskCompletionSource<object?> Done)> _queue = new();
    private Exception? _startupError;

    public StaDispatcher()
    {
        _thread = new Thread(RunLoop)
        {
            Name = "MailHelper.OutlookCOM",
            IsBackground = true,
        };
        using var started = new ManualResetEventSlim();
        _thread.Start(started);
        started.Wait(TimeSpan.FromSeconds(10));
        if (_startupError is not null)
        {
            throw _startupError; // Office 未安装等：启动期即失败（TestAsync 通道健康检查会捕获）
        }
    }

    private void RunLoop(object? state)
    {
        try
        {
            // Outlook COM 需要 STA 单元（MAPI 强制）
            _ = Thread.CurrentThread.TrySetApartmentState(ApartmentState.STA);
        }
        catch (ThreadStateException ex)
        {
            _startupError = ex;
            ((ManualResetEventSlim)state!).Set();
            return;
        }

        ((ManualResetEventSlim)state!).Set();
        foreach (var (work, done) in _queue.GetConsumingEnumerable())
        {
            try
            {
                done.TrySetResult(work());
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }
    }

    public T Invoke<T>(Func<T> work)
    {
        var source = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add((() => work(), source));
        var result = source.Task.GetAwaiter().GetResult();
        return result is null && !typeof(T).IsValueType ? default! : (T)result!;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        _queue.Dispose();
    }
}

/// <summary>经典 Outlook COM 数据源（dynamic 晚绑定，无 PIA 依赖；04 §4.3 落地路径 CHG-011）。
/// 前置条件：本机安装经典版 Outlook 且已登录学校账号。所有 COM 调用经 StaDispatcher 序列化。仅 Windows。</summary>
[SupportedOSPlatform("windows")]
public sealed class OutlookComMailSource : IOutlookMailSource
{
    // OlDefaultFolders.olFolderInbox = 6；OlSortOrder 倒序=1
    private const int OlFolderInbox = 6;

    private readonly StaDispatcher? _dispatcher;
    private dynamic? _app;
    private dynamic? _ns;
    private dynamic? _inbox;
    private bool _disposed;

    public OutlookComMailSource() =>
        _dispatcher = new StaDispatcher(); // 启动即验证 COM 单元可用（Office 缺失此处抛出）

    public string GetAccountAddress() => _dispatcher!.Invoke(() =>
    {
        EnsureConnected();
        dynamic accounts = _app!.Session.Accounts;
        if (accounts.Count >= 1)
        {
            dynamic first = accounts[1];
            return (string)(first.SmtpAddress ?? first.UserName ?? "unknown") ?? "unknown";
        }

        return "unknown";
    });

    public IReadOnlyList<OutlookMessageSummary> FetchInboxSince(DateTime? sinceUtc, int take) =>
        _dispatcher!.Invoke(() =>
        {
            EnsureConnected();
            dynamic items = _inbox!.Items;
            items.Sort("[ReceivedTime]", true); // 倒序：最新在前，便于窗口截取
            if (sinceUtc is { } since)
            {
                // Jet 语法过滤：本地时区比较（ReceivedTime 为本地时间）
                string filter = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "[ReceivedTime] > '{0:MM'/'dd'/'yyyy HH:mm}'",
                    since.ToLocalTime());
                items = items.Restrict(filter);
            }

            var summaries = new List<OutlookMessageSummary>();
            var count = items.Count;
            // 倒序集合：从最新往旧取 take 条，再升序输出
            var buffer = new List<(DateTimeOffset Received, OutlookMessageSummary Summary)>();
            for (var i = 1; i <= count && buffer.Count < take; i++)
            {
                dynamic item = items[i];
                if (item.Class != 43) // OlObjectClass.olMail = 43（会议请求等跳过）
                {
                    continue;
                }

                var receivedLocal = (DateTime)item.ReceivedTime;
                var received = new DateTimeOffset(receivedLocal, TimeZoneInfo.Local.GetUtcOffset(receivedLocal));
                if (sinceUtc is { } lowerBound && received <= lowerBound)
                {
                    break; // 倒序：已越过窗口下界
                }

                var bodyText = string.Empty;
                var bodyHtml = string.Empty;
                try
                {
                    bodyText = (string)(item.Body ?? string.Empty);
                }
                catch (COMException)
                {
                } // DRM/受保护项跳过正文

                try
                {
                    bodyHtml = (string)(item.HTMLBody ?? string.Empty);
                }
                catch (COMException)
                {
                }

                var attachments = 0;
                try
                {
                    attachments = item.Attachments.Count;
                }
                catch (COMException)
                {
                }

                buffer.Add((received, new OutlookMessageSummary(
                    EntryId: (string)item.EntryID,
                    InternetMessageId: TryGet(() => (string)item.PropertyAccessor.GetProperty(
                        "http://schemas.microsoft.com/mapi/proptag/0x1035001F")),
                    Subject: (string)(item.Subject ?? string.Empty),
                    FromName: TryGet(() =>
                    {
                        dynamic sender = item.Sender;
                        dynamic exch = sender.GetExchangeUser();
                        return exch is null ? (string)(sender.Name ?? string.Empty) : (string)exch.Name;
                    }) ?? string.Empty,
                    FromAddress: TryGet(() =>
                    {
                        dynamic sender = item.Sender;
                        return (string)(sender.SmtpAddress
                            ?? (sender.AddressEntry.Type == "EX"
                                ? sender.AddressEntry.GetExchangeUser().PrimarySmtpAddress
                                : sender.Address));
                    }) ?? string.Empty,
                    ReceivedAtUtc: received,
                    IsRead: !((bool)item.UnRead),
                    AttachmentCount: attachments,
                    TextBody: bodyText,
                    HtmlBody: bodyHtml)));
            }

            buffer.Sort((x, y) => x.Received.CompareTo(y.Received)); // 升序输出（协调器水位语义）
            foreach (var (_, summary) in buffer)
            {
                summaries.Add(summary);
            }

            return (IReadOnlyList<OutlookMessageSummary>)summaries;
        });

    private void EnsureConnected()
    {
        if (_inbox is not null)
        {
            return;
        }

        var outlookType = Type.GetTypeFromProgID("Outlook.Application")
            ?? throw new InvalidOperationException("未找到经典版 Outlook（Outlook.Application COM 未注册）");
        _app = Activator.CreateInstance(outlookType) // Outlook 单实例模型：已运行时附着既有会话（含用户登录态）
            ?? throw new InvalidOperationException("Outlook.Application 实例化失败");
        object sessionObj = _app.Session; // 单次动态求值
        if (sessionObj is null)
        {
            throw new InvalidOperationException("Outlook 会话不可用（请确认已启动并登录 Outlook）");
        }

        dynamic session = sessionObj;
        _ns = session;
        _inbox = session.GetDefaultFolder(OlFolderInbox);
    }

    private static T? TryGet<T>(Func<T> getter)
    {
        try
        {
            return getter();
        }
        catch (COMException)
        {
            return default;
        }
        catch (RuntimeBinderException)
        {
            return default;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _dispatcher?.Invoke<object?>(() =>
            {
                _inbox = null;
                _ns = null;
                if (_app is not null && Marshal.IsComObject(_app))
                {
                    Marshal.ReleaseComObject(_app);
                }

                _app = null;
                return null;
            });
        }
        catch (ObjectDisposedException)
        {
        }

        _dispatcher?.Dispose();
    }
}
