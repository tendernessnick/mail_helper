using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailHelper.Infrastructure.Storage.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    email = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: true),
                    tenant_id = table.Column<string>(type: "TEXT", nullable: true),
                    channel = table.Column<string>(type: "TEXT", nullable: false),
                    azure_client_id = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rules",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    pattern = table.Column<string>(type: "TEXT", nullable: false),
                    category = table.Column<string>(type: "TEXT", nullable: true),
                    importance_hint = table.Column<int>(type: "INTEGER", nullable: true),
                    weight = table.Column<double>(type: "REAL", nullable: false),
                    priority = table.Column<int>(type: "INTEGER", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    builtin_version = table.Column<string>(type: "TEXT", nullable: true),
                    updated_at_utc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    account_id = table.Column<string>(type: "TEXT", nullable: false),
                    internet_message_id = table.Column<string>(type: "TEXT", nullable: true),
                    subject = table.Column<string>(type: "TEXT", nullable: true),
                    from_name = table.Column<string>(type: "TEXT", nullable: true),
                    from_address = table.Column<string>(type: "TEXT", nullable: true),
                    body_preview = table.Column<string>(type: "TEXT", nullable: true),
                    body_path = table.Column<string>(type: "TEXT", nullable: true),
                    received_at_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    has_attachments = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    is_read = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    category = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "other"),
                    importance = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 2),
                    confidence = table.Column<double>(type: "REAL", nullable: true),
                    classified_by = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "rule"),
                    classified_at_utc = table.Column<long>(type: "INTEGER", nullable: true),
                    remote_change_key = table.Column<string>(type: "TEXT", nullable: true),
                    is_deleted_remote = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sync_state",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "TEXT", nullable: false),
                    delta_link = table.Column<string>(type: "TEXT", nullable: true),
                    imap_uid_watermark = table.Column<string>(type: "TEXT", nullable: true),
                    last_sync_at_utc = table.Column<long>(type: "INTEGER", nullable: true),
                    last_sync_status = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_state", x => x.account_id);
                    table.ForeignKey(
                        name: "FK_sync_state_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "classification_feedback",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    message_id = table.Column<string>(type: "TEXT", nullable: false),
                    old_category = table.Column<string>(type: "TEXT", nullable: true),
                    new_category = table.Column<string>(type: "TEXT", nullable: true),
                    old_importance = table.Column<int>(type: "INTEGER", nullable: true),
                    new_importance = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at_utc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classification_feedback", x => x.id);
                    table.ForeignKey(
                        name: "FK_classification_feedback_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_log",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    message_id = table.Column<string>(type: "TEXT", nullable: false),
                    level = table.Column<string>(type: "TEXT", nullable: false),
                    sent_at_utc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_log", x => x.id);
                    table.ForeignKey(
                        name: "FK_notification_log_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_email",
                table: "accounts",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_classification_feedback_message_id",
                table: "classification_feedback",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "idx_messages_account_cat_imp",
                table: "messages",
                columns: new[] { "account_id", "category", "importance", "received_at_utc" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "idx_messages_account_received",
                table: "messages",
                columns: new[] { "account_id", "received_at_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_messages_from",
                table: "messages",
                column: "from_address");

            migrationBuilder.CreateIndex(
                name: "idx_messages_pending_class",
                table: "messages",
                column: "account_id",
                filter: "classified_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_notification_log_message_id",
                table: "notification_log",
                column: "message_id",
                unique: true);

            // FTS5 全文检索（04 §3.2）：外部内容表 + 三触发器同步 messages → messages_fts。
            // tokenize='trigram' 系 CHG-005 最小偏差：默认 unicode61 无法做中文子串检索（FR-13/TC-017）。
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE messages_fts USING fts5(
                    subject, from_name, body_preview,
                    content='messages', content_rowid='rowid', tokenize='trigram');
                CREATE TRIGGER messages_ai AFTER INSERT ON messages BEGIN
                    INSERT INTO messages_fts(rowid, subject, from_name, body_preview)
                    VALUES (new.rowid, new.subject, new.from_name, new.body_preview);
                END;
                CREATE TRIGGER messages_ad AFTER DELETE ON messages BEGIN
                    INSERT INTO messages_fts(messages_fts, rowid, subject, from_name, body_preview)
                    VALUES ('delete', old.rowid, old.subject, old.from_name, old.body_preview);
                END;
                CREATE TRIGGER messages_au AFTER UPDATE ON messages BEGIN
                    INSERT INTO messages_fts(messages_fts, rowid, subject, from_name, body_preview)
                    VALUES ('delete', old.rowid, old.subject, old.from_name, old.body_preview);
                    INSERT INTO messages_fts(rowid, subject, from_name, body_preview)
                    VALUES (new.rowid, new.subject, new.from_name, new.body_preview);
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS messages_au; DROP TRIGGER IF EXISTS messages_ad; DROP TRIGGER IF EXISTS messages_ai; DROP TABLE IF EXISTS messages_fts;");

            migrationBuilder.DropTable(
                name: "classification_feedback");

            migrationBuilder.DropTable(
                name: "notification_log");

            migrationBuilder.DropTable(
                name: "rules");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "sync_state");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
