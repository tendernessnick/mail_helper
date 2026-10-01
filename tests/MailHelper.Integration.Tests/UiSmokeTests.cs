using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using FluentAssertions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.UIA3;
using Microsoft.Data.Sqlite;
using Xunit;
using Application = FlaUI.Core.Application;

namespace MailHelper.Integration.Tests;

/// <summary>UI 冒烟（总控指令六.S6：FlaUI 点击级验证；引入理由已在 PROGRESS 登记）。
/// DEV 模式（MAILHELPER_DEV=1 + 独立临时数据目录）拉起真实 WPF 进程：
/// Onboarding 隐私勾选 → 连接 → 同步+分类 → 三栏列表出现 → 点击标已读 → 截图存 artifacts/screens/。
/// WPF 重渲染会使既有 UIA 元素引用失效（COM 0x80040201）——所有探测每轮重找并吞瞬态异常。</summary>
public class UiSmokeTests
{
    [Fact]
    public void DevFlow_SignIn_ListShows_ClickMarksRead_ScreenshotTaken()
    {
        // 测试宿主声明 PerMonitorV2 DPI 感知：否则 FlaUI 拿到的是虚拟化矩形，
        // GDI 截图只覆盖高 DPI 物理窗口的左上角（截图像素与 UIA DIP 几何整体错位）
        SetProcessDpiAwarenessContext(new IntPtr(-4));

        var dataDir = Path.Combine(Path.GetTempPath(), "mh-ui-" + Guid.NewGuid().ToString("N"));
        var repoRoot = FindRepoRoot();
        // TFM 输出目录可能带 Windows SDK 后缀（net8.0-windows / net8.0-windows10.0.x）：通配取最新构建
        var exePath = Directory.GetFiles(
                Path.Combine(repoRoot, "src", "MailHelper.App", "bin", "Release"),
                "MailHelper.App.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        exePath.Should().NotBeNull("应先构建 App（dotnet build MailHelper.sln -c Release）");
        var exePathFull = Path.GetFullPath(exePath!);

        var psi = new ProcessStartInfo(exePathFull)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            EnvironmentVariables =
            {
                ["MAILHELPER_DEV"] = "1",
                ["MAILHELPER_DATA_DIR"] = dataDir,
                // UIA 探针依赖中文 AutomationProperties.Name（ByName「类别导航」/「邮件列表」）：
                // CI（en-US）下 auto 语言会渲染英文，强制 zh-CN 保证元素名确定
                ["MAILHELPER_LANG"] = "zh-CN",
            },
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("App 进程启动失败");
        var appOutput = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (appOutput) { appOutput.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (appOutput) { appOutput.AppendLine("[err] " + e.Data); } } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 等主窗口句柄出现再 Attach（过早 Attach 会因主模块未就绪抛 NRE）
        var handleDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < handleDeadline)
        {
            process.Refresh();
            if (process.HasExited)
            {
                lock (appOutput)
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "mh-ui-app-output.txt"), appOutput.ToString());
                }

                throw new InvalidOperationException($"App 进程提前退出，ExitCode={process.ExitCode}");
            }

            if (process.MainWindowHandle != 0)
            {
                break;
            }

            Thread.Sleep(200);
        }

        // 直接按进程 ID 定位主窗口（绕开 FlaUI Application 进程管理的不稳定路径）
        try
        {
            using var automation = new UIA3Automation();
            var mainWindows = new[] { process };

            // Onboarding：勾选隐私 → 连接（05 §3.1 / 09 §6.3）
            var privacy = Retry(() => Find(mainWindows, automation, w => w.FindFirstDescendant(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.CheckBox))),
                TimeSpan.FromSeconds(15), "隐私复选框")!.AsCheckBox();
            privacy.IsChecked = true;

            Retry(() => Find(mainWindows, automation, w => w.FindFirstDescendant(cf => cf.ByText("连接学校邮箱"))),
                TimeSpan.FromSeconds(10), "连接按钮")!.AsButton().Invoke();

            // 同步+分类后列表出现并加载满（M1 出口全链路：登录→同步→分类→浏览）
            var itemCount = Retry(() =>
            {
                var list = FindListBox(mainWindows, automation);
                var count = list?.Items.Length;
                return count is >= 15 ? count : null;
            }, TimeSpan.FromSeconds(30), "邮件列表加载");
            itemCount.Should().BeGreaterThanOrEqualTo(15, "DEV 种子 20 封（两个批次页）");

            // 布局几何留档：与 05 §3.2 线框比对（窄左栏 ~220px / 中栏弹性 / 右栏阅读）
            var layoutLine = Retry(() =>
            {
                var window = FindWindow(automation, process);
                var nav = Find(mainWindows, automation, w => w.FindFirstDescendant(cf => cf.ByName("类别导航")));
                var list = FindListBox(mainWindows, automation);
                if (window is null || nav is null || list is null)
                {
                    return null;
                }

                try
                {
                    var wr = window.Properties.BoundingRectangle.Value;
                    var nr = nav.Properties.BoundingRectangle.Value;
                    var lr = list.Properties.BoundingRectangle.Value;
                    return $"[layout] window={wr.Width:F0}x{wr.Height:F0} nav=[x{nr.X - wr.X:F0},w{nr.Width:F0}] list=[x{lr.X - wr.X:F0},w{lr.Width:F0},y{lr.Y - wr.Y:F0}]";
                }
                catch (COMException)
                {
                    return null;
                }
            }, TimeSpan.FromSeconds(10), "布局几何");
            Console.WriteLine(layoutLine);

            // 点击第一封（SelectionItemPattern，避免物理坐标点击的不确定性）
            Retry<object?>(() =>
            {
                FindListBox(mainWindows, automation)?.Items.FirstOrDefault()?.AsListBoxItem().Select();
                return new object();
            }, TimeSpan.FromSeconds(10), "选中第一封");

            var screensDir = Path.Combine(repoRoot, "artifacts", "screens");
            Directory.CreateDirectory(screensDir);
            Retry<object?>(() =>
            {
                process.Refresh();
                Console.WriteLine($"[shot] HasExited={process.HasExited} ExitCode={(process.HasExited ? process.ExitCode : 0)} MainWindowHandle={process.MainWindowHandle}");
                var window = FindWindow(automation, process);
                if (window is null)
                {
                    return null;
                }

                using var capture = Capture.Element(window);
                capture.ToFile(Path.Combine(screensDir, "s6-inbox.png"));
                return new object();
            }, TimeSpan.FromSeconds(10), "截图");
            File.Exists(Path.Combine(screensDir, "s6-inbox.png")).Should().BeTrue();

            // 已读验证：左栏「收件箱（全部）」未读徽章计数应减少（UC-04 点击标已读 → 未读数联动）
            var unreadAfter = Retry<int?>(() =>
            {
                var nav = Find(mainWindows, automation, w =>
                    w.FindFirstDescendant(cf => cf.ByName("类别导航")));
                var firstRow = nav?.AsListBox()?.Items.FirstOrDefault();
                var badge = firstRow?.FindFirstDescendant(cf => cf.ByAutomationId("unread-badge"));
                var name = SafeName(badge);
                Console.WriteLine($"[nav] badge='{name}'");
                if (name is null)
                {
                    return null;
                }

                var digits = new string(name.Where(char.IsDigit).ToArray());
                return digits.Length > 0 && int.TryParse(digits, out var count) && count < 20 ? count : null;
            }, TimeSpan.FromSeconds(10), "未读徽章减少");
            unreadAfter.Should().BeLessThan(20, "点击一封已读后，全部收件箱未读数应从 20 减少");

            // TC-019 关窗常驻（FR-14 AC3）：关窗 → 进程驻留 → 命名管道唤起 → 窗口重现
            Retry<object?>(() =>
            {
                var window = FindWindow(automation, process);
                window?.Close(); // WM_CLOSE → 触发 Hide 而非退出
                return new object();
            }, TimeSpan.FromSeconds(10), "关闭主窗口");
            var closeDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (DateTime.UtcNow < closeDeadline)
            {
                process.Refresh();
                if (IsWindowGone(automation, process))
                {
                    break;
                }

                Thread.Sleep(200);
            }

            process.Refresh();
            process.HasExited.Should().BeFalse("关窗后进程应驻留托盘（FR-14 AC3）");

            using (var client = new NamedPipeClientStream(".", "MailHelper-SingleInstance-Pipe", PipeDirection.Out))
            {
                client.Connect(2000); // EX-TC-07：二次启动路径同款管道
                var payload = System.Text.Encoding.UTF8.GetBytes("SHOW");
                client.Write(payload, 0, payload.Length);
                client.Flush();
            }

            Retry<object?>(() =>
            {
                var window = FindWindow(automation, process);
                return window is not null && !window.Properties.IsOffscreen.Value ? new object() : null;
            }, TimeSpan.FromSeconds(10), "管道唤起后窗口重现");
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                    var closeDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                    while (!process.HasExited && DateTime.UtcNow < closeDeadline)
                    {
                        Thread.Sleep(200);
                    }

                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // 进程已退出
            }

            SqliteConnection.ClearAllPools();
            if (Environment.GetEnvironmentVariable("MAILHELPER_UI_KEEP") != "1")
            {
                try { Directory.Delete(dataDir, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                Console.WriteLine($"[ui-test] dataDir 保留于 {dataDir}");
            }
            else
            {
                Console.WriteLine($"[ui-test] dataDir 保留于 {dataDir}");
            }
        }
    }

    private static Window? FindWindow(UIA3Automation automation, Process process) =>
        automation.GetDesktop().FindFirstChild(cf =>
            cf.ByProcessId(process.Id).And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window)))?.AsWindow();

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    private static bool IsWindowGone(UIA3Automation automation, Process process)
    {
        try
        {
            return FindWindow(automation, process) is null;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static AutomationElement? Find(Process[] app, UIA3Automation automation, Func<Window, AutomationElement?> probe)
    {
        var window = FindWindow(automation, app[0]);
        return window is null ? null : probe(window);
    }

    private static ListBox? FindListBox(Process[] app, UIA3Automation automation)
    {
        var element = Find(app, automation, w => w.FindFirstDescendant(cf => cf.ByName("邮件列表")));
        return element?.AsListBox();
    }

    private static string? SafeName(AutomationElement? element)
    {
        try
        {
            return element?.Properties.Name.Value;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static T? Retry<T>(Func<T?> probe, TimeSpan timeout, string what)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = probe();
                if (result is not null && !EqualityComparer<T>.Default.Equals(result, default!))
                {
                    return result;
                }
            }
            catch (COMException)
            {
                // 引用失效/线程忙：下一轮重试
            }

            Thread.Sleep(200);
        }

        throw new TimeoutException($"等待 {what} 超时");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MailHelper.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent!;
        }

        throw new DirectoryNotFoundException("未找到仓库根");
    }
}
