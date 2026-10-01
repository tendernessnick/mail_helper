using Microsoft.EntityFrameworkCore;

namespace MailHelper.Infrastructure.Storage;

/// <summary>EF Core 8 上下文（04 章 §3.2 DDL 的模型映射；FTS5 虚表与触发器由迁移内原生 SQL 创建）。</summary>
public sealed class MailHelperDbContext : DbContext
{
    public MailHelperDbContext(DbContextOptions<MailHelperDbContext> options) : base(options)
    {
    }

    internal DbSet<AccountEntity> Accounts => Set<AccountEntity>();
    internal DbSet<SyncStateEntity> SyncStates => Set<SyncStateEntity>();
    internal DbSet<MessageEntity> Messages => Set<MessageEntity>();
    internal DbSet<RuleEntity> Rules => Set<RuleEntity>();
    internal DbSet<ClassificationFeedbackEntity> ClassificationFeedback => Set<ClassificationFeedbackEntity>();
    internal DbSet<NotificationLogEntity> NotificationLogs => Set<NotificationLogEntity>();
    internal DbSet<SettingEntity> Settings => Set<SettingEntity>();
    internal DbSet<CategoryEntity> Categories => Set<CategoryEntity>(); // S14-C/CHG-012 自定义类别

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var account = modelBuilder.Entity<AccountEntity>();
        account.ToTable("accounts");
        account.HasKey(x => x.Id);
        account.Property(x => x.Id).HasColumnName("id").HasColumnType("TEXT");
        account.Property(x => x.Email).HasColumnName("email").HasColumnType("TEXT").IsRequired();
        account.HasIndex(x => x.Email).IsUnique();
        account.Property(x => x.DisplayName).HasColumnName("display_name").HasColumnType("TEXT");
        account.Property(x => x.TenantId).HasColumnName("tenant_id").HasColumnType("TEXT");
        account.Property(x => x.Channel).HasColumnName("channel").HasColumnType("TEXT").IsRequired();
        account.Property(x => x.AzureClientId).HasColumnName("azure_client_id").HasColumnType("TEXT");
        account.Property(x => x.Status).HasColumnName("status").HasColumnType("TEXT").IsRequired();
        account.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("INTEGER");

        var sync = modelBuilder.Entity<SyncStateEntity>();
        sync.ToTable("sync_state");
        sync.HasKey(x => x.AccountId);
        sync.Property(x => x.AccountId).HasColumnName("account_id").HasColumnType("TEXT");
        sync.HasOne<AccountEntity>().WithMany().HasForeignKey(x => x.AccountId);
        sync.Property(x => x.DeltaLink).HasColumnName("delta_link").HasColumnType("TEXT");
        sync.Property(x => x.ImapUidWatermark).HasColumnName("imap_uid_watermark").HasColumnType("TEXT");
        sync.Property(x => x.LastSyncAtUtc).HasColumnName("last_sync_at_utc").HasColumnType("INTEGER");
        sync.Property(x => x.LastSyncStatus).HasColumnName("last_sync_status").HasColumnType("TEXT");

        var message = modelBuilder.Entity<MessageEntity>();
        message.ToTable("messages");
        message.HasKey(x => x.Id);
        message.Property(x => x.Id).HasColumnName("id").HasColumnType("TEXT");
        message.Property(x => x.AccountId).HasColumnName("account_id").HasColumnType("TEXT").IsRequired();
        message.HasOne<AccountEntity>().WithMany().HasForeignKey(x => x.AccountId);
        message.Property(x => x.InternetMessageId).HasColumnName("internet_message_id").HasColumnType("TEXT");
        message.Property(x => x.Subject).HasColumnName("subject").HasColumnType("TEXT");
        message.Property(x => x.FromName).HasColumnName("from_name").HasColumnType("TEXT");
        message.Property(x => x.FromAddress).HasColumnName("from_address").HasColumnType("TEXT");
        message.Property(x => x.BodyPreview).HasColumnName("body_preview").HasColumnType("TEXT");
        message.Property(x => x.BodyPath).HasColumnName("body_path").HasColumnType("TEXT");
        message.Property(x => x.ReceivedAtUtc).HasColumnName("received_at_utc").HasColumnType("INTEGER").IsRequired();
        message.Property(x => x.HasAttachments).HasColumnName("has_attachments").HasColumnType("INTEGER").IsRequired().HasDefaultValue(false);
        message.Property(x => x.IsRead).HasColumnName("is_read").HasColumnType("INTEGER").IsRequired().HasDefaultValue(false);
        message.Property(x => x.Category).HasColumnName("category").HasColumnType("TEXT").IsRequired().HasDefaultValue("other");
        message.Property(x => x.Importance).HasColumnName("importance").HasColumnType("INTEGER").IsRequired().HasDefaultValue(2);
        message.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("REAL");
        message.Property(x => x.ClassifiedBy).HasColumnName("classified_by").HasColumnType("TEXT").IsRequired().HasDefaultValue("rule");
        message.Property(x => x.ClassifiedAtUtc).HasColumnName("classified_at_utc").HasColumnType("INTEGER");
        message.Property(x => x.RemoteChangeKey).HasColumnName("remote_change_key").HasColumnType("TEXT");
        message.Property(x => x.IsDeletedRemote).HasColumnName("is_deleted_remote").HasColumnType("INTEGER").IsRequired().HasDefaultValue(false);

        message.HasIndex(x => new { x.AccountId, x.ReceivedAtUtc })
            .HasDatabaseName("idx_messages_account_received")
            .IsDescending(false, true);
        message.HasIndex(x => new { x.AccountId, x.Category, x.Importance, x.ReceivedAtUtc })
            .HasDatabaseName("idx_messages_account_cat_imp")
            .IsDescending(false, false, true, true);
        message.HasIndex(x => x.AccountId)
            .HasDatabaseName("idx_messages_pending_class")
            .HasFilter("classified_at_utc IS NULL");
        message.HasIndex(x => x.FromAddress).HasDatabaseName("idx_messages_from");

        var rule = modelBuilder.Entity<RuleEntity>();
        rule.ToTable("rules");
        rule.HasKey(x => x.Id);
        rule.Property(x => x.Id).HasColumnName("id").HasColumnType("TEXT");
        rule.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
        rule.Property(x => x.Kind).HasColumnName("kind").HasColumnType("TEXT").IsRequired();
        rule.Property(x => x.Pattern).HasColumnName("pattern").HasColumnType("TEXT").IsRequired();
        rule.Property(x => x.Category).HasColumnName("category").HasColumnType("TEXT");
        rule.Property(x => x.ImportanceHint).HasColumnName("importance_hint").HasColumnType("INTEGER");
        rule.Property(x => x.Weight).HasColumnName("weight").HasColumnType("REAL").IsRequired();
        rule.Property(x => x.Priority).HasColumnName("priority").HasColumnType("INTEGER").IsRequired();
        rule.Property(x => x.Enabled).HasColumnName("enabled").HasColumnType("INTEGER").IsRequired();
        rule.Property(x => x.Source).HasColumnName("source").HasColumnType("TEXT").IsRequired();
        rule.Property(x => x.BuiltinVersion).HasColumnName("builtin_version").HasColumnType("TEXT");
        rule.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("INTEGER");

        var feedback = modelBuilder.Entity<ClassificationFeedbackEntity>();
        feedback.ToTable("classification_feedback");
        feedback.HasKey(x => x.Id);
        feedback.Property(x => x.Id).HasColumnName("id").HasColumnType("TEXT");
        feedback.Property(x => x.MessageId).HasColumnName("message_id").HasColumnType("TEXT").IsRequired();
        feedback.HasOne<MessageEntity>().WithMany().HasForeignKey(x => x.MessageId);
        feedback.Property(x => x.OldCategory).HasColumnName("old_category").HasColumnType("TEXT");
        feedback.Property(x => x.NewCategory).HasColumnName("new_category").HasColumnType("TEXT");
        feedback.Property(x => x.OldImportance).HasColumnName("old_importance").HasColumnType("INTEGER");
        feedback.Property(x => x.NewImportance).HasColumnName("new_importance").HasColumnType("INTEGER");
        feedback.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("INTEGER").IsRequired();

        var notification = modelBuilder.Entity<NotificationLogEntity>();
        notification.ToTable("notification_log");
        notification.HasKey(x => x.Id);
        notification.Property(x => x.Id).HasColumnName("id").HasColumnType("TEXT");
        notification.Property(x => x.MessageId).HasColumnName("message_id").HasColumnType("TEXT").IsRequired();
        notification.HasOne<MessageEntity>().WithMany().HasForeignKey(x => x.MessageId);
        notification.HasIndex(x => x.MessageId).IsUnique(); // 防重复通知（04 §8）
        notification.Property(x => x.Level).HasColumnName("level").HasColumnType("TEXT").IsRequired();
        notification.Property(x => x.SentAtUtc).HasColumnName("sent_at_utc").HasColumnType("INTEGER").IsRequired();

        var setting = modelBuilder.Entity<SettingEntity>();
        setting.ToTable("settings");
        setting.HasKey(x => x.Key);
        setting.Property(x => x.Key).HasColumnName("key").HasColumnType("TEXT");
        setting.Property(x => x.Value).HasColumnName("value").HasColumnType("TEXT");

        // S14-C/CHG-012：自定义类别（内置七类代码静态，不在此表）
        var category = modelBuilder.Entity<CategoryEntity>();
        category.ToTable("categories");
        category.HasKey(x => x.Id);
        category.Property(x => x.Id).HasColumnName("id").HasColumnType("TEXT");
        category.Property(x => x.Label).HasColumnName("label").HasColumnType("TEXT").IsRequired();
        category.Property(x => x.Icon).HasColumnName("icon").HasColumnType("TEXT").IsRequired();
        category.Property(x => x.ColorHex).HasColumnName("color_hex").HasColumnType("TEXT").IsRequired();
        category.Property(x => x.Sort).HasColumnName("sort").HasColumnType("INTEGER");
    }
}
