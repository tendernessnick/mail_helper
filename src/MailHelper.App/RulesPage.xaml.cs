using System.Windows;
using System.Windows.Controls;
using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.Services;
using MailHelper.ViewModels;

namespace MailHelper.App;

/// <summary>规则管理页（FR-10 / 05 §3.3）。列表+筛选+编辑弹窗+试跑预览。</summary>
public partial class RulesPage : UserControl
{
    private readonly RulesViewModel _viewModel;
    private readonly LanguageService _language;

    public RulesPage(RulesViewModel viewModel, LanguageService language)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _language = language;
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not RulesViewModel vm || FilterBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        vm.Filter = item.Tag as string ?? "All";
    }

    private void OnNewRuleClick(object sender, RoutedEventArgs e)
    {
        var draft = new ClassifyRule(
            Guid.NewGuid(), string.Empty, RuleKind.SenderAddress, string.Empty,
            CategoryIds.Finance, null, RuleSet.DefaultWeight(RuleKind.SenderAddress), 100, true,
            RuleSource.User);
        ShowEditDialog(draft, isNew: true);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RuleRowViewModel row)
        {
            row.RequestDelete(row); // 内置规则由服务端拒绝（RuleValidationException）
        }
    }

    private async void ShowEditDialog(ClassifyRule draft, bool isNew)
    {
        var dialog = new RuleEditDialog(draft, _language) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _viewModel.SaveRuleAsync(dialog.Result);
            await _viewModel.LoadAsync();
        }
        catch (RuleValidationException ex)
        {
            MessageBox.Show(ex.Message, "规则校验", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
