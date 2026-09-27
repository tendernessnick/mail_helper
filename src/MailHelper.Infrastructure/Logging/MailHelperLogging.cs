using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace MailHelper.Infrastructure.Logging;

/// <summary>Serilog 文件日志工厂（04 章 §6：滚动文件 %AppData%\MailHelper\logs\mailhelper-.log，
/// 日切 + 保留 14 天 + 单文件 10MB；结构化字段 event/latency_ms/batch_size 等）。</summary>
public static class MailHelperLogging
{
    public static ILoggerFactory CreateFileLoggerFactory(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, "mailhelper-.log"),
                rollingInterval: Serilog.RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        return new SerilogLoggerFactory(serilogLogger, dispose: true);
    }
}
