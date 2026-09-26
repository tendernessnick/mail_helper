namespace MailHelper.Core.Rules;

/// <summary>规则文件热重载（04 章 §2.3：FileSystemWatcher + 防抖）。文件变更后 500ms 防抖窗口合并，解析成功触发 RuleSetChanged。
/// 解析失败保留旧规则集并记录 LastError（回退内置默认策略，09 章 §2）。</summary>
public sealed class FileRuleSetWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _debounce;
    private readonly string _filePath;
    private volatile bool _disposed;

    public FileRuleSetWatcher(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        var dir = Path.GetDirectoryName(_filePath)
            ?? throw new ArgumentException("路径必须包含目录", nameof(filePath));
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(_filePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => ScheduleReload();
        _watcher.Created += (_, _) => ScheduleReload();
        _watcher.Renamed += (_, _) => ScheduleReload();
        _debounce = new System.Threading.Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>热重载成功时触发（线程：线程池）。</summary>
    public event EventHandler<RuleSet>? RuleSetChanged;

    /// <summary>最近一次解析失败原因（成功后清空）。</summary>
    public string? LastError { get; private set; }

    private void ScheduleReload() => _debounce.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);

    private void Reload()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (RuleSetParser.TryParseFile(_filePath, out var ruleSet, out var error) && ruleSet is not null)
            {
                LastError = null;
                RuleSetChanged?.Invoke(this, ruleSet);
            }
            else
            {
                LastError = error ?? "未知解析错误";
            }
        }
        catch (Exception ex)
        {
            // 文件监视线程不容忍未处理异常；解析异常保留旧规则集（回退策略，09 章 §2）
            LastError = ex.Message;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
