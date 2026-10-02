using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.ViewModels;

namespace MailHelper.App.Avalonia;

/// <summary>设置页（FR-03/04/06、05 §3.4 五分组 + 自定义类别；SettingsViewModel 共享；
/// 属性 setter 即保存——绑定即存，与 WPF 行为一致）。</summary>
public partial class SettingsPage : UserControl
{
    private readonly ICategoryStore _categories;
    private readonly IAutoStarter _autostart;
    private bool _loaded;

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;

    public SettingsPage(ICategoryStore categories, IAutoStarter autostart)
    {
        AvaloniaXamlLoader.Load(this);
        _categories = categories;
        _autostart = autostart;

        Loaded += async (_, _) =>
        {
            await ViewModel.LoadAsync(CancellationToken.None);
            await CategoryCatalog.RefreshAsync(_categories);
            var list = this.FindControl<ItemsControl>("CustomCategoriesList");
            var languageBox = this.FindControl<ComboBox>("LanguageBox");
            var autostartCheck = this.FindControl<CheckBox>("AutostartCheck");
            if (list is null || languageBox is null || autostartCheck is null)
            {
                return; // XAML 名解析失败（如测试宿主异常）：不参与后续交互
            }

            list.ItemsSource = CategoryCatalog.All.Where(c => !c.IsBuiltin).ToList();
            languageBox.SelectedIndex = ViewModel.Language switch
            {
                "zh-CN" => 1,
                "en" => 2,
                _ => 0,
            };
            autostartCheck.IsChecked = ViewModel.Autostart || _autostart.IsEnabled;
            _loaded = true;
        };
    }

    private void ReloadCustomCategories()
    {
        var list = this.FindControl<ItemsControl>("CustomCategoriesList");
        if (list is not null)
        {
            list.ItemsSource = CategoryCatalog.All.Where(c => !c.IsBuiltin).ToList();
        }
    }

    private async void OnAddCategory(object? sender, RoutedEventArgs e)
    {
        var label = NewCategoryName.Text?.Trim();
        if (string.IsNullOrEmpty(label))
        {
            return;
        }

        var icon = string.IsNullOrWhiteSpace(NewCategoryIcon.Text) ? "📌" : NewCategoryIcon.Text.Trim();
        var id = "custom-" + Guid.NewGuid().ToString("N")[..8]; // WPF 同款 slug
        try
        {
            await _categories.UpsertAsync(new CategoryDefinition(id, label, icon, "#3A7BD5",
                Sort: CategoryIds.Defaults.Length + CategoryCatalog.All.Count(c => !c.IsBuiltin), IsBuiltin: false),
                CancellationToken.None);
            NewCategoryName.Text = string.Empty;
            NewCategoryIcon.Text = string.Empty;
            await CategoryCatalog.RefreshAsync(_categories);
            ReloadCustomCategories();
        }
        catch (InvalidOperationException)
        {
            // 重名等校验失败：静默保留输入（与 WPF 提示语义等价的简化态）
        }
    }

    private async void OnDeleteCategory(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            await _categories.DeleteAsync(id, CancellationToken.None);
            await CategoryCatalog.RefreshAsync(_categories);
            ReloadCustomCategories();
        }
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loaded && LanguageBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.Language = tag; // setter 即保存（NFR-12：重启生效）
        }
    }

    private void OnAutostartChecked(object? sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            _ = ViewModel.ToggleAutostartAsync(true);
        }
    }

    private void OnAutostartUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            _ = ViewModel.ToggleAutostartAsync(false);
        }
    }

    private void OnClearLocalData(object? sender, RoutedEventArgs e) => App.RequestClearLocalData?.Invoke();
}
