using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MailHelper.Infrastructure.Storage;

/// <summary>dotnet-ef 设计时工厂（迁移生成用；运行时经 MailDatabase.CreateContext）。</summary>
public sealed class MailHelperDbContextFactory : IDesignTimeDbContextFactory<MailHelperDbContext>
{
    public MailHelperDbContext CreateDbContext(string[] args)
    {
        var path = Path.Combine(Path.GetTempPath(), "mailhelper-design-time.db");
        var options = new DbContextOptionsBuilder<MailHelperDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        return new MailHelperDbContext(options);
    }
}
