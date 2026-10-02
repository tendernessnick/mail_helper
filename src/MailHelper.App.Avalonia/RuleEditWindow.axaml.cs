using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.ViewModels;

namespace MailHelper.App.Avalonia;

/// <summary>规则编辑弹窗（FR-10；与 WPF RuleEditDialog 同逻辑：新建/编辑 → ClassifyRule 结果）。</summary>
public partial class RuleEditWindow : Window
{
    private readonly ClassifyRule _original;
    private readonly bool _isNew;

    /// <summary>确定后的规则（保存点击后有效）。</summary>
    public ClassifyRule Result { get; private set; }

    public RuleEditWindow(ClassifyRule draft)
    {
        InitializeComponent();
        _original = draft;
        _isNew = draft.Name.Length == 0;

        Title = _isNew ? "规则编辑（新建）" : "规则编辑：" + draft.Name;
        KindBox.SelectedIndex = draft.Kind switch
        {
            RuleKind.SenderDomain => 1,
            RuleKind.SubjectRegex => 2,
            RuleKind.SubjectKeyword => 3,
            _ => 0,
        };
        PatternBox.Text = draft.Pattern;
        WeightBox.Text = draft.Weight.ToString("0.#");
        EnabledBox.IsChecked = draft.Enabled;

        CategoryBox.Items.Add(new ComboBoxItem { Content = "（仅重要度）" });
        foreach (var definition in CategoryCatalog.All)
        {
            CategoryBox.Items.Add(new ComboBoxItem { Content = definition.Label });
        }

        CategoryBox.SelectedIndex = draft.Category is { } c
            ? CategoryCatalog.All.IndexIf(d => d.Id == c) + 1
            : 0;

        ImportanceBox.SelectedIndex = draft.ImportanceHint is { } hint ? (int)hint + 1 : 0;
        Result = draft;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var kind = KindBox.SelectedIndex switch
        {
            1 => RuleKind.SenderDomain,
            2 => RuleKind.SubjectRegex,
            3 => RuleKind.SubjectKeyword,
            _ => RuleKind.SenderAddress,
        };
        string? category = CategoryBox.SelectedIndex <= 0
            ? null
            : CategoryCatalog.All[CategoryBox.SelectedIndex - 1].Id;
        Importance? hint = ImportanceBox.SelectedIndex <= 0 ? null : (Importance)(ImportanceBox.SelectedIndex - 1);
        if (!double.TryParse(WeightBox.Text, out var weight))
        {
            weight = RuleSet.DefaultWeight(kind);
        }

        Result = _original with
        {
            Kind = kind,
            Pattern = (PatternBox.Text ?? string.Empty).Trim(),
            Category = category,
            ImportanceHint = hint,
            Weight = weight,
            Enabled = EnabledBox.IsChecked == true,
            Name = _isNew ? $"用户规则:{(PatternBox.Text ?? string.Empty).Trim()}" : _original.Name,
        };
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

}
