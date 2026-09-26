namespace MailHelper.Core.Services;

/// <summary>同步状态机（04 章 §2.2：Idle → Syncing → (Idle | Offline | Error | ReauthRequired)；03 章 MOD-02）。</summary>
public enum SyncState
{
    Idle,
    Syncing,
    Offline,
    Error,
    ReauthRequired,
}
