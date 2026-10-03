using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailHelper.Infrastructure.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ddl_scanned_at_utc",
                table: "messages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "schedule_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    account_id = table.Column<string>(type: "TEXT", nullable: false),
                    message_id = table.Column<string>(type: "TEXT", nullable: true),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    course = table.Column<string>(type: "TEXT", nullable: true),
                    course_code = table.Column<string>(type: "TEXT", nullable: true),
                    link = table.Column<string>(type: "TEXT", nullable: true),
                    dedupe_key = table.Column<string>(type: "TEXT", nullable: false),
                    due_at_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    reminded_due_at = table.Column<long>(type: "INTEGER", nullable: true),
                    created_at_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_schedule_account_dedupe",
                table: "schedule_items",
                columns: new[] { "account_id", "dedupe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_schedule_account_status_due",
                table: "schedule_items",
                columns: new[] { "account_id", "status", "due_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_items");

            migrationBuilder.DropColumn(
                name: "ddl_scanned_at_utc",
                table: "messages");
        }
    }
}
