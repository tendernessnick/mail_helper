using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.ViewModels;

namespace MailHelper.App.Avalonia;

/// <summary>规则管理页（FR-10 / 05 §3.3）：列表+筛选+启停/删除/编辑+试跑预览（RulesViewModel 共享）。</summary>
public partial class RulesPage : UserControl
{
    private RulesViewModel ViewModel => (RulesViewModel)DataContext!;

    public RulesPage()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private async void OnNewRule(object? sender, RoutedEventArgs e)
    {
        var draft = new ClassifyRule(
            Id: Guid.NewGuid(),
            Name: string.Empty,
            Kind: RuleKind.SenderDomain,
            Pattern: string.Empty,
            Category: null,
            ImportanceHint: null,
            Weight: RuleSet.DefaultWeight(RuleKind.SenderDomain),
            Priority: 0,
            Enabled: true,
            Source: RuleSource.User);
        await EditAsync(draft);
    }

    private async void OnEditRule(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedRow is not { } row || row.IsBuiltin)
        {
            return; // 内置规则可禁用不可编辑（05 §3.3）
        }

        await EditAsync(row.Rule);
    }

    private async Task EditAsync(ClassifyRule draft)
    {
        var dialog = new RuleEditWindow(draft);
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null || await dialog.ShowDialog<bool>(owner) is not true)
        {
            return;
        }

        await ViewModel.SaveRuleAsync(dialog.Result);
    }
}

/// <summary>Filter 字符串 ↔ RadioButton（All/Builtin/User/Feedback，ConverterParameter 比对）。</summary>
public sealed class FilterToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter as string : BindingOperations.DoNothing;
}
