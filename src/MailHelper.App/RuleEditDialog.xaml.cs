using System.Windows;
using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.Services;

namespace MailHelper.App;

/// <summary>规则编辑弹窗（05 §2 RuleEdit；FR-10 四种类型 + 类别/重要度建议/权重/启停）。</summary>
public partial class RuleEditDialog : Window
{
    private readonly ClassifyRule _original;
    private readonly bool _isNew;

    /// <summary>确定后的规则（ShowDialog 返回 true 时有效）。</summary>
    public ClassifyRule Result { get; private set; }

    public RuleEditDialog(ClassifyRule draft, LanguageService language)
    {
        InitializeComponent();
        _original = draft;
        _isNew = draft.Name.Length == 0;

        Title = _isNew ? language.T("rules.edit.title") + "（新建）" : language.T("rules.edit.title") + "：" + draft.Name;
        KindBox.SelectedIndex = (int)draft.Kind;
        PatternBox.Text = draft.Pattern;
        WeightBox.Text = draft.Weight.ToString("0.#");
        EnabledBox.IsChecked = draft.Enabled;

        // 类别：（无）+ 类别目录（S14-C：内置七类 + 自定义）
        CategoryBox.Items.Add("(仅重要度)");
        foreach (var definition in CategoryCatalog.All)
        {
            CategoryBox.Items.Add(definition.Label);
        }

        CategoryBox.SelectedIndex = draft.Category is { } c
            ? CategoryCatalog.All.IndexIf(d => d.Id == c) + 1
            : 0;

        // 重要度建议：（无）/P3/P2/P1/P0
        ImportanceBox.Items.Add("(无)");
        foreach (var label in new[] { "P3", "P2", "P1", "P0" })
        {
            ImportanceBox.Items.Add(label);
        }

        ImportanceBox.SelectedIndex = draft.ImportanceHint is { } hint ? (int)hint + 1 : 0;
        Result = draft;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var kind = (RuleKind)(KindBox.SelectedIndex switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            _ => 0,
        });
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
            Pattern = PatternBox.Text.Trim(),
            Category = category,
            ImportanceHint = hint,
            Weight = weight,
            Enabled = EnabledBox.IsChecked == true,
            Name = _isNew ? $"用户规则:{PatternBox.Text.Trim()}" : _original.Name,
        };
        DialogResult = true;
    }
}
