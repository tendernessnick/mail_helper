using System.Windows;
using System.Windows.Controls;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.App.ViewModels;

namespace MailHelper.App;

/// <summary>设置页（FR-03/05 §3.4 五分组 + S14-C 自定义类别）。清除本地数据带二次确认（不可逆，09 §6.4）。</summary>
public partial class SettingsPage : UserControl
{
    private readonly SettingsViewModel _viewModel;
    private readonly ICategoryStore _categories;
    private readonly MainViewModel _main;
    private bool _loaded;

    public SettingsPage(SettingsViewModel viewModel, ICategoryStore categories, MainViewModel main)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _categories = categories;
        _main = main;
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            await viewModel.LoadAsync(CancellationToken.None);
            await ReloadCategoryListAsync();
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

    private async Task ReloadCategoryListAsync()
    {
        await CategoryCatalog.RefreshAsync(_categories);
        CustomCategoriesList.ItemsSource = CategoryCatalog.All.Where(c => !c.IsBuiltin).ToList();
        _main.RebuildCategories(); // 分类栏同步重建
    }

    private async void OnAddCategoryClick(object sender, RoutedEventArgs e)
    {
        var label = NewCategoryName.Text.Trim();
        if (label.Length == 0)
        {
            return;
        }

        var icon = string.IsNullOrWhiteSpace(NewCategoryIcon.Text) ? "📌" : NewCategoryIcon.Text.Trim();
        var color = (NewCategoryColor.SelectedItem as ComboBoxItem)?.Tag as string ?? "#3A7BD5";
        var id = "custom-" + Guid.NewGuid().ToString("N")[..8]; // 不可变 slug：改名走编辑语义，此处从简
        try
        {
            await _categories.UpsertAsync(new CategoryDefinition(id, label, icon, color,
                Sort: CategoryIds.Defaults.Length + CategoryCatalog.All.Count(c => !c.IsBuiltin), IsBuiltin: false),
                CancellationToken.None);
            NewCategoryName.Text = string.Empty;
            await ReloadCategoryListAsync();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "无法添加类别", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnDeleteCategoryClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CategoryDefinition definition || definition.IsBuiltin)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"删除类别「{definition.Label}」？该类邮件将归入「其他」，相关规则一并删除。",
            "删除类别", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await _categories.DeleteAsync(definition.Id, CancellationToken.None);
        await ReloadCategoryListAsync();
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
