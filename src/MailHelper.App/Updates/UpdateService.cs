using Velopack;
using Velopack.Sources;

namespace MailHelper.App.Updates;

/// <summary>应用内更新（S15，08 §4.3）：更新源为 GitHub Releases。
/// 只认正式版（prerelease=false），beta 渠道预发布仅供发布前人工验证，不推送已在用用户；
/// 版本比较依据 Velopack 清单与安装包元数据，未安装环境（源码/裸 exe 运行）检查入口禁用。</summary>
public sealed class UpdateService
{
    /// <summary>更新源仓库（CI vpk upload github 与此一致；改仓库名需同步）。</summary>
    public const string RepoUrl = "https://github.com/tendernessnick/mail_helper";

    /// <summary>当前版本（AssemblyVersion，CI 以 tag 注入 -p:Version）。</summary>
    public static string CurrentVersion { get; } =
        (typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0)).ToString(3);

    /// <summary>当前进程是否处于 Velopack 安装/便携环境（决定「检查更新」入口可用性）。</summary>
    public static bool IsInstalled => CreateManager().IsInstalled;

    /// <summary>检查更新：无更新返回 null，有更新返回可供下载的 UpdateInfo。</summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct)
    {
        var mgr = CreateManager();
        if (!mgr.IsInstalled)
        {
            return null;
        }

        return await mgr.CheckForUpdatesAsync().WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>后台下载更新包（progress 0–100），完成后需调用 ApplyUpdateAndRestart 生效。</summary>
    public async Task DownloadUpdateAsync(UpdateInfo info, Action<int>? progress, CancellationToken ct)
    {
        var mgr = CreateManager();
        await mgr.DownloadUpdatesAsync(info, progress, ct).ConfigureAwait(false);
    }

    /// <summary>应用更新并重启（Velopack 接管退出与替换，托盘/单实例随之重建）。</summary>
    public void ApplyUpdateAndRestart(UpdateInfo info)
    {
        var mgr = CreateManager();
        mgr.ApplyUpdatesAndRestart(info.TargetFullRelease);
    }

    private static UpdateManager CreateManager() =>
        new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
}
