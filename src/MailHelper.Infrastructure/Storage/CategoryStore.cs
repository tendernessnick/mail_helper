using MailHelper.Core;
using MailHelper.Core.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace MailHelper.Infrastructure.Storage;

/// <summary>自定义类别仓储（S14-C/CHG-012：categories 表；内置七类静态见 CategoryIds）。</summary>
public sealed class CategoryStore : ICategoryStore
{
    private readonly string _dbPath;

    public CategoryStore(string dbPath) => _dbPath = dbPath;

    public async Task<IReadOnlyList<CategoryDefinition>> GetAllAsync(CancellationToken ct) =>
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var rows = await db.Categories.AsNoTracking()
                .OrderBy(c => c.Sort).ThenBy(c => c.Id)
                .ToListAsync(ct);
            return (IReadOnlyList<CategoryDefinition>)rows
                .Select(c => new CategoryDefinition(c.Id, c.Label, c.Icon, c.ColorHex, c.Sort, IsBuiltin: false))
                .ToList();
        }, ct);

    public async Task UpsertAsync(CategoryDefinition category, CancellationToken ct)
    {
        Validate(category);
        await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            var existing = await db.Categories.FirstOrDefaultAsync(c => c.Id == category.Id, ct);
            if (existing is null)
            {
                db.Categories.Add(new CategoryEntity
                {
                    Id = category.Id, Label = category.Label, Icon = category.Icon,
                    ColorHex = category.ColorHex, Sort = category.Sort,
                });
            }
            else
            {
                existing.Label = category.Label;
                existing.Icon = category.Icon;
                existing.ColorHex = category.ColorHex;
                existing.Sort = category.Sort;
            }

            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task<int> DeleteAsync(string id, CancellationToken ct)
    {
        if (CategoryIds.All.Contains(id, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("内置类别不可删除");
        }

        return await MailDatabase.WithDbAsync(_dbPath, async db =>
        {
            // 事务：该类别邮件归「其他」、指向它的规则（含改判学习产物）一并删除（EX-07 精神）
            await using var connection = (SqliteConnection)db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
            }

            await using var tx = connection.BeginTransaction();
            try
            {
                var migrate = connection.CreateCommand();
                migrate.Transaction = tx;
                migrate.CommandText = "UPDATE messages SET category='other' WHERE category=@id";
                migrate.Parameters.AddWithValue("@id", id);
                var migrated = await migrate.ExecuteNonQueryAsync(ct);

                var dropRules = connection.CreateCommand();
                dropRules.Transaction = tx;
                dropRules.CommandText = "DELETE FROM rules WHERE category=@id";
                dropRules.Parameters.AddWithValue("@id", id);
                await dropRules.ExecuteNonQueryAsync(ct);

                var drop = connection.CreateCommand();
                drop.Transaction = tx;
                drop.CommandText = "DELETE FROM categories WHERE id=@id";
                drop.Parameters.AddWithValue("@id", id);
                await drop.ExecuteNonQueryAsync(ct);

                tx.Commit();
                return migrated;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }, ct);
    }

    private static void Validate(CategoryDefinition category)
    {
        if (CategoryIds.All.Contains(category.Id, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("内置类别不可修改");
        }

        if (string.IsNullOrWhiteSpace(category.Label))
        {
            throw new InvalidOperationException("类别名称不能为空");
        }
    }
}
