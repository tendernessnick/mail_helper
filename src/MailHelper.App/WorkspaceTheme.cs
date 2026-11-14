using System.Windows;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Services;
using Microsoft.Win32;

namespace MailHelper.App;

/// <summary>在构造页面前应用已保存外观；切换偏好在下次启动生效。</summary>
internal static class WorkspaceTheme
{
    internal static bool IsDark { get; private set; }

    internal static async Task ApplyAsync(Application application, ISettingsStore settings)
    {
        var preference = await settings.GetAsync(SettingsService.KeyTheme, CancellationToken.None);
        IsDark = preference == "dark" || (preference is null or "auto" && SystemPrefersDark());
        if (IsDark)
        {
            application.Resources.MergedDictionaries[0] = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/DarkDesignTokens.xaml", UriKind.Absolute),
            };
        }
    }

    private static bool SystemPrefersDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
