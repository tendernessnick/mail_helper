using FluentAssertions;
using MailHelper.Core.Services;
using Xunit;

namespace MailHelper.Services.Tests;

/// <summary>S0 骨架冒烟：同步状态机枚举与 04 章 §2.2 一致（S4 起扩展 EX-TC-01~08 相关状态用例）。</summary>
public class SanityTests
{
    [Fact]
    public void SyncStateMatchesStateMachine()
    {
        Enum.GetNames<SyncState>().Should().BeEquivalentTo(new[]
        {
            "Idle", "Syncing", "Offline", "Error", "ReauthRequired",
        });
    }
}
