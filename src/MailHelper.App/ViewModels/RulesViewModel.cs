using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.Services;

namespace MailHelper.App.ViewModels;

/// <summary>规则列表行（05 §3.3 表格列；Enabled 编辑回调写回服务）。</summary>
public partial class RuleRowViewModel : ObservableObject
{
    public required ClassifyRule Rule { get; init; }
    public required Func<ClassifyRule, bool, Task> SetEnabledAsync { get; init; }
    public required Action<RuleRowViewModel> RequestDelete { get; init; }

    public string Name => Rule.Name;
    public string KindText => KindLabel(Rule.Kind);
    public string PatternText => Rule.Pattern;
    public string CategoryText => Rule.Category is { } c
        ? Array.Find(MainViewModel.CategoryLabels, p => p.Category == c).Label
        : "—";
    public double Weight => Rule.Weight;
    public bool IsBuiltin => Rule.Source == RuleSource.Builtin;
    public string SourceTag => Rule.Source switch
    {
        RuleSource.Builtin => "内置",
        RuleSource.Feedback => "反馈",
        _ => "自定义",
    };

    [ObservableProperty]
    private bool enabled;

    partial void OnEnabledChanged(bool value)
    {
        if (value != Rule.Enabled)
        {
            _ = SetEnabledAsync(Rule, value);
        }
    }

    internal static string KindLabel(RuleKind kind) => kind switch
    {
        RuleKind.SenderAddress => "发件人地址",
        RuleKind.SenderDomain => "发件人域名",
        RuleKind.SubjectRegex => "主题正则",
        _ => "主题关键词",
    };
}

/// <summary>规则管理页（FR-10 / 05 §3.3）：列表+筛选+启停/删除+试跑预览。</summary>
public partial class RulesViewModel : ObservableObject
{
    private readonly RuleManagementService _manager;
    private readonly LanguageService _language;

    public RulesViewModel(RuleManagementService manager, LanguageService language)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _language = language ?? throw new ArgumentNullException(nameof(language));
    }

    public ObservableCollection<RuleRowViewModel> Rows { get; } = new();

    public ObservableCollection<string> PreviewLines { get; } = new();

    [ObservableProperty]
    private string filter = "All"; // All | Builtin | User | Feedback（05 §3.3 筛选）

    [ObservableProperty]
    private RuleRowViewModel? selectedRow;

    [ObservableProperty]
    private bool hasPreview;

    [ObservableProperty]
    private string previewSummary = string.Empty;

    /// <summary>当前编辑草稿（编辑对话框确定后经 SaveAsync 落库）。</summary>
    public Func<ClassifyRule, Task>? SaveHandler { get; set; }

    public async Task LoadAsync()
    {
        Rows.Clear();
        foreach (var rule in _manager.ListAll().Where(MatchesFilter).OrderBy(r => r.Source).ThenBy(r => r.Name))
        {
            Rows.Add(new RuleRowViewModel
            {
                Rule = rule,
                SetEnabledAsync = (r, enabled) => SetEnabledAsync(r.Id, enabled),
                RequestDelete = row => _ = DeleteRuleAsync(row.Rule.Id),
                Enabled = rule.Enabled,
            });
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken ct)
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        await _manager.DeleteAsync(row.Rule.Id, ct); // 内置规则抛 RuleValidationException，由 UI 提示
        await LoadAsync();
    }

    private async Task DeleteRuleAsync(Guid id)
    {
        await _manager.DeleteAsync(id, CancellationToken.None);
        await LoadAsync();
    }

    /// <summary>编辑对话框确定后落库（校验失败抛 RuleValidationException，UI 提示）。</summary>
    public async Task SaveRuleAsync(ClassifyRule rule)
    {
        await _manager.SaveAsync(rule, CancellationToken.None);
        await LoadAsync();
    }

    private async Task SetEnabledAsync(Guid id, bool enabled)
    {
        await _manager.SetEnabledAsync(id, enabled, CancellationToken.None);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task PreviewAsync(CancellationToken ct)
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        var hits = await _manager.PreviewAsync(row.Rule, ct);
        PreviewLines.Clear();
        foreach (var mail in hits)
        {
            PreviewLines.Add($"{mail.Subject} · {mail.FromAddress}");
        }

        HasPreview = true;
        PreviewSummary = string.Format(_language.T("rules.preview.result"), hits.Count);
        OnPropertyChanged(nameof(PreviewSummary));
        _ = ct;
    }

    partial void OnFilterChanged(string value) => _ = LoadAsync();

    private bool MatchesFilter(ClassifyRule rule) => Filter switch
    {
        "Builtin" => rule.Source == RuleSource.Builtin,
        "User" => rule.Source == RuleSource.User,
        "Feedback" => rule.Source == RuleSource.Feedback,
        _ => true,
    };
}
