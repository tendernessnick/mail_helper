using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;

namespace MailHelper.Infrastructure.Storage;

/// <summary>账户与同步断点仓储（IAccountStore 实现）。</summary>
public sealed class AccountRepository : IAccountStore
{
    private readonly string _dbPath;

    public AccountRepository(string dbPath) => _dbPath = dbPath;

    public async Task UpsertAccountAsync(Account account, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var existing = await db.Accounts.FindAsync(new object[] { account.Id }, ct);
            if (existing is null)
            {
                db.Accounts.Add(ToEntity(account));
            }
            else
            {
                existing.Email = account.Email;
                existing.DisplayName = account.DisplayName;
                existing.TenantId = account.TenantId;
                existing.Channel = account.Channel.ToString();
                existing.AzureClientId = account.AzureClientId;
                existing.Status = account.Status.ToString();
            }

            await db.SaveChangesAsync(ct);
        }, ct);

    public async Task<Account?> FindAccountAsync(string accountId, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var row = await db.Accounts.FindAsync(new object[] { accountId }, ct);
            return row is null ? null : ToDomain(row);
        }, ct);

    public async Task<SyncCheckpoint?> GetCheckpointAsync(string accountId, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var row = await db.SyncStates.FindAsync(new object[] { accountId }, ct);
            return row is null
                ? null
                : new SyncCheckpoint(
                    row.AccountId,
                    row.DeltaLink,
                    row.ImapUidWatermark,
                    row.LastSyncAtUtc is { } at ? MailRepository.FromUnixSeconds(at) : null,
                    row.LastSyncStatus);
        }, ct);

    public async Task SaveCheckpointAsync(SyncCheckpoint checkpoint, CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var existing = await db.SyncStates.FindAsync(new object[] { checkpoint.AccountId }, ct);
            var lastSync = checkpoint.LastSyncAtUtc is { } at ? MailRepository.ToUnixSeconds(at) : (long?)null;
            if (existing is null)
            {
                db.SyncStates.Add(new SyncStateEntity
                {
                    AccountId = checkpoint.AccountId,
                    DeltaLink = checkpoint.DeltaLink,
                    ImapUidWatermark = checkpoint.ImapUidWatermark,
                    LastSyncAtUtc = lastSync,
                    LastSyncStatus = checkpoint.LastSyncStatus,
                });
            }
            else
            {
                existing.DeltaLink = checkpoint.DeltaLink;
                existing.ImapUidWatermark = checkpoint.ImapUidWatermark;
                existing.LastSyncAtUtc = lastSync;
                existing.LastSyncStatus = checkpoint.LastSyncStatus;
            }

            await db.SaveChangesAsync(ct);
        }, ct);

    private static AccountEntity ToEntity(Account account) => new()
    {
        Id = account.Id,
        Email = account.Email,
        DisplayName = account.DisplayName,
        TenantId = account.TenantId,
        Channel = account.Channel.ToString(),
        AzureClientId = account.AzureClientId,
        Status = account.Status.ToString(),
        CreatedAtUtc = MailRepository.ToUnixSeconds(account.CreatedAtUtc),
    };

    private static Account ToDomain(AccountEntity e) => new(
        e.Id,
        e.Email,
        e.DisplayName,
        e.TenantId,
        Enum.TryParse<ChannelKind>(e.Channel, ignoreCase: false, out var channel) ? channel : ChannelKind.Graph,
        e.AzureClientId,
        Enum.TryParse<AccountStatus>(e.Status, ignoreCase: false, out var status) ? status : AccountStatus.Active,
        MailRepository.FromUnixSeconds(e.CreatedAtUtc));
}
