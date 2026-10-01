using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace MailHelper.App.Notifications;

/// <summary>托盘常驻（04 §4：H.NotifyIcon.Wpf；角标图标 DrawText 动态生成）。
/// 菜单：打开主界面/立即同步/设置（S9 前禁用占位）/退出；关窗后进程驻留（FR-14 AC3，TC-019）。</summary>
public sealed class TrayIconController : IDisposable
{
    private readonly Action _showMainWindow;
    private readonly Action _syncNow;
    private readonly Action _quit;
    private TaskbarIcon? _icon;
    private int _unread;
    private bool _minimizeHintShown;

    public TrayIconController(Action showMainWindow, Action syncNow, Action quit)
    {
        _showMainWindow = showMainWindow;
        _syncNow = syncNow;
        _quit = quit;
    }

    /// <summary>角标未读数（MainViewModel.UnreadTotal 驱动；0 时无角标）。</summary>
    public void UpdateUnread(int unread)
    {
        _unread = Math.Max(0, unread);
        if (_icon is not null)
        {
            _icon.Icon = GenerateIcon(_unread);
            _icon.ToolTipText = _unread > 0 ? $"MailHelper（{_unread} 封未读）" : "MailHelper";
        }
    }

    public void ShowMinimizedHint()
    {
        if (_minimizeHintShown)
        {
            return;
        }

        _minimizeHintShown = true;
        _icon?.ShowNotification(
            title: "MailHelper 仍在运行",
            message: "已最小化到托盘，新邮件通知会照常弹出。",
            icon: NotificationIcon.None);
    }

    public void Initialize()
    {
        var menu = new ContextMenu();

        var open = new MenuItem { Header = "打开主界面", FontWeight = FontWeights.SemiBold };
        open.Click += (_, _) => _showMainWindow();
        var sync = new MenuItem { Header = "立即同步", InputGestureText = "Ctrl+R" };
        sync.Click += (_, _) => _syncNow();
        var settings = new MenuItem { Header = "设置（随 S9 开放）", IsEnabled = false };
        var quit = new MenuItem { Header = "退出" };
        quit.Click += (_, _) => _quit();

        menu.Items.Add(open);
        menu.Items.Add(sync);
        menu.Items.Add(new Separator());
        menu.Items.Add(settings);
        menu.Items.Add(quit);

        _icon = new TaskbarIcon
        {
            ToolTipText = "MailHelper",
            Icon = GenerateIcon(0),
            ContextMenu = menu,
        };
        _icon.TrayLeftMouseDown += (_, _) => _showMainWindow(); // 单击即唤起（双击语义一并覆盖）
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>图标生成（S14-A 换品牌底图）：app.ico 32px 帧 + 未读 >0 时叠加红色角标数字。
    /// WPF 渲染 → PNG → 手工包装 ICO 容器（Icon 构造不接受裸 PNG 流），避免引 System.Drawing.Common。</summary>
    private static System.Drawing.Icon GenerateIcon(int unread)
    {
        const int size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(BaseIcon, new Rect(0, 0, size, size));
            if (unread > 0)
            {
                dc.DrawEllipse(Brushes.Firebrick, new Pen(Brushes.White, 1.5), new Point(25, 7), 8, 8);
                var label = unread > 99 ? "99" : unread.ToString(CultureInfo.InvariantCulture);
                var badge = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), unread > 9 ? 9 : 11, Brushes.White, 1.25);
                dc.DrawText(badge, new Point(25 - badge.Width / 2, 7 - badge.Height / 2));
            }
        }

        var bitmap = new RenderTargetBitmap(size * 2, size * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var pngStream = new MemoryStream();
        encoder.Save(pngStream);

        using var icoStream = WrapPngAsIco(pngStream.ToArray());
        var icon = new System.Drawing.Icon(icoStream);
        return icon;
    }

    /// <summary>品牌底图（S14-A）：app.ico 的 32px 帧，懒加载缓存。</summary>
    private static readonly Lazy<ImageSource> BaseIconSource = new(() =>
    {
        var decoder = BitmapDecoder.Create(
            new Uri("pack://application:,,,/Assets/app.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames
            .OrderBy(f => f.PixelWidth)
            .First(f => f.PixelWidth >= 32);
        frame.Freeze();
        return (ImageSource)frame;
    });

    private static ImageSource BaseIcon => BaseIconSource.Value;

    /// <summary>把单帧 PNG 封装为 ICO 容器（Vista+ 支持内嵌 PNG；目录项 22 字节）。</summary>
    private static MemoryStream WrapPngAsIco(byte[] png)
    {
        var stream = new MemoryStream(22 + png.Length);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((short)0);       // reserved
        writer.Write((short)1);       // type = icon
        writer.Write((short)1);       // image count
        writer.Write((byte)32);       // width（logical，实际 64px 由 PNG 自带尺寸决定）
        writer.Write((byte)32);       // height
        writer.Write((byte)0);        // palette
        writer.Write((byte)0);        // reserved
        writer.Write((short)1);       // color planes
        writer.Write((short)32);      // bits per pixel
        writer.Write(png.Length);
        writer.Write(22);             // data offset
        writer.Write(png);
        stream.Position = 0;
        return stream;
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
