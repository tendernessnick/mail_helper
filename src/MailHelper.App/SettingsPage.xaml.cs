using System.Windows;
using System.Windows.Controls;
using MailHelper.App.ViewModels;

namespace MailHelper.App;

/// <summary>设置页（FR-03/05 §3.4 五分组）。清除本地数据带二次确认（不可逆，09 §6.4）。</summary>
public partial class SettingsPage : UserControl
{
    private readonly SettingsViewModel _viewModel;
    private bool _loaded;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            await viewModel.LoadAsync(CancellationToken.None);
            LanguageBox.SelectedIndex = viewModel.Language switch
            {
                "zh-CN" => 1,
                "en" => 2,
                _ => 0,
            };
            ThemeBox.SelectedIndex = viewModel.Theme switch
            {
                "light" => 1,
                "dark" => 2,
                _ => 0,
            };
            AutostartCheckbox().IsChecked = viewModel.Autostart;
            _loaded = true;
        };
    }

    private CheckBox AutostartCheckbox() =>
        (CheckBox)FindName("AutostartBox")!;

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded && LanguageBox.SelectedItem is ComboBoxItem item)
        {
            _viewModel.Language = item.Tag as string ?? "auto";
        }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded && ThemeBox.SelectedItem is ComboBoxItem item)
        {
            _viewModel.Theme = item.Tag as string ?? "auto";
        }
    }

    private void OnAutostartChecked(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            _ = _viewModel.ToggleAutostartAsync(true);
        }
    }

    private void OnAutostartUnchecked(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            _ = _viewModel.ToggleAutostartAsync(false);
        }
    }

    private void OnClearDataClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "将删除本机全部邮件缓存、规则与设置（令牌同时清除），且不可恢复。继续吗？",
            "清除本地数据", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm == MessageBoxResult.Yes)
        {
            App.RequestClearLocalData?.Invoke();
        }
    }
}
