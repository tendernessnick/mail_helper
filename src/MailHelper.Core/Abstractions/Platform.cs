namespace MailHelper.Core.Abstractions;

/// <summary>应用数据/日志/正文缓存/临时文件路径抽象（docs/10 §4 P-02）。
/// Windows=%AppData%\MailHelper（漫游，等价迁移前 Bootstrapper/App.xaml.cs 硬编码）；
/// macOS=~/Library/Application Support/MailHelper。MAILHELPER_DATA_DIR 覆盖语义由实现承载。</summary>
public interface IAppPaths
{
    /// <summary>数据根目录（数据库、日志、正文缓存、规则 JSON 的父目录）。</summary>
    string DataDir { get; }

    /// <summary>日志目录（默认 DataDir/logs，04 §6 滚动文件）。</summary>
    string LogsDir { get; }

    /// <summary>正文磁盘缓存目录（默认 DataDir/bodies，03 §6）。</summary>
    string BodiesDir { get; }

    /// <summary>SQLite 数据库文件路径（默认 DataDir/mailhelper.db）。</summary>
    string DbPath { get; }

    /// <summary>系统临时目录下的文件路径（崩溃日志/阅读窗格副本等一次性文件）。</summary>
    string GetTempFilePath(string fileName);
}

/// <summary>单实例锁（docs/10 §4 P-03；EX-TC-07 语义：二次启动经通道唤起既有实例后退出）。
/// Windows=命名 Mutex + ACL 命名管道；macOS=Unix domain socket 文件锁（socket 落数据目录随测试隔离）。</summary>
public interface ISingleInstanceLock : IDisposable
{
    /// <summary>尝试成为首个实例；已有实例在运行时返回 false（调用方应唤起后退出）。</summary>
    bool TryAcquireFirst();

    /// <summary>通知运行中的实例唤起主窗口（二次启动路径：发送后调用方立即退出进程）。</summary>
    void NotifyRunningInstance();

    /// <summary>首实例开始监听唤起请求（进程生命周期后台任务，Dispose 时取消）。</summary>
    void StartListening(Action activate);
}

/// <summary>开机自启（docs/10 §4 P-04；设置键 app.autostart，默认关）。
/// Windows=注册表 Run 键；macOS=~/Library/LaunchAgents plist + launchctl。</summary>
public interface IAutoStarter
{
    /// <summary>当前是否已启用（以平台注册状态为准）。</summary>
    bool IsEnabled { get; }

    void Enable();

    void Disable();
}

/// <summary>更新安装策略（docs/10 §4 P-08）：检查/下载逻辑平台无关，安装动作分平台。
/// Windows=Inno 静默重装（/SILENT，S17）；macOS=引导下载 DMG 手动安装（MS9，自动更新列后续）。</summary>
public interface IUpdateInstaller
{
    /// <summary>应用已下载到本地的更新包（调用方随后退出应用，安装器接管收尾）。</summary>
    void Install(string localPackagePath);
}
