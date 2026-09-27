using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.Services;
using MailHelper.Core.TextProcessing;
using Xunit;

namespace MailHelper.Core.Tests.Evaluation;

/// <summary>分类器评估流水线（06 章 §4；总控指令七：以 dotnet test 形式对样本集全量评估，
/// 输出混淆矩阵报告到 artifacts/eval/；达标线任一不满足即测试失败）。
/// 达标线：类别准确率 ≥85%、P0 召回 ≥95%、P0 误报 ≤10%、待确认率 ≤30%（下限 15% 为指示性，D-39）。</summary>
public class ClassifierEvaluationTests
{
    private static string CorpusDir => CorpusLoader.FindRepoPath("tests", "fixtures", "sample-corpus");
    private static string RulePackPath => CorpusLoader.FindRepoPath("src", "MailHelper.Infrastructure", "Rules", "rules.builtin.json");

    [Fact]
    public async Task Evaluation_MeetsReleaseGates_AndWritesReport()
    {
        RuleSetParser.TryParseFile(RulePackPath, out var ruleSet, out var error).Should().BeTrue(error ?? "规则包解析失败");
        var engine = new RuleEngine(ruleSet!);
        var samples = CorpusLoader.Load(CorpusDir, "labels-eval.csv");
        samples.Should().HaveCountGreaterThan(100);

        var categories = Enum.GetValues<MailCategory>();
        var confusion = new int[categories.Length, categories.Length];
        var trueP0 = 0;
        var predP0TrueP0 = 0;
        var predP0FalsePositive = 0;
        var pendingReview = 0;

        foreach (var sample in samples)
        {
            // 模拟 ClassificationService 管线：预处理 → 截断 500 → 分类（02 §9 / 03 §5.3）
            var bodyText = TextNormalizer.Normalize(sample.Body);
            if (bodyText.Length > RemoteMessageMapper.PreviewLength)
            {
                bodyText = bodyText[..RemoteMessageMapper.PreviewLength];
            }

            var result = await engine.ClassifyAsync(
                new ClassifiedInput(sample.Subject, sample.FromName, sample.FromAddress, bodyText, ReceivedAtUtc: null),
                CancellationToken.None);

            confusion[(int)sample.Category, (int)result.Category]++;

            if (sample.Importance == Importance.P0)
            {
                trueP0++;
                if (result.Importance == Importance.P0)
                {
                    predP0TrueP0++;
                }
            }

            if (result.Importance == Importance.P0 && sample.Importance != Importance.P0)
            {
                predP0FalsePositive++;
            }

            if (result.Confidence < ClassificationService.DefaultReviewThreshold)
            {
                pendingReview++;
            }
        }

        var total = samples.Count;
        var diagonal = Enumerable.Range(0, categories.Length).Sum(i => confusion[i, i]);
        var accuracy = (double)diagonal / total;
        var p0Recall = trueP0 == 0 ? 1.0 : (double)predP0TrueP0 / trueP0;
        var p0Predicted = predP0TrueP0 + predP0FalsePositive;
        var p0FalsePositiveRate = p0Predicted == 0 ? 0.0 : (double)predP0FalsePositive / p0Predicted;
        var pendingRate = (double)pendingReview / total;

        WriteReport(categories, confusion, total, accuracy, trueP0, predP0TrueP0, p0FalsePositiveRate, pendingRate);

        accuracy.Should().BeGreaterThanOrEqualTo(0.85, "类别准确率达标线（FR-07 AC1 / 06 §4.1）");
        p0Recall.Should().BeGreaterThanOrEqualTo(0.95, "P0 召回达标线（FR-08 AC1）");
        p0FalsePositiveRate.Should().BeLessThanOrEqualTo(0.10, "P0 误报达标线（FR-08 AC2）");
        pendingRate.Should().BeLessThanOrEqualTo(0.30, "待确认率上限（06 §4.1）");
    }

    private static void WriteReport(
        MailCategory[] categories, int[,] confusion, int total, double accuracy,
        int trueP0, int predP0TrueP0, double p0FpRate, double pendingRate)
    {
        var reportDir = Path.Combine(CorpusLoader.FindRepoRoot(), "artifacts", "eval");
        Directory.CreateDirectory(reportDir);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# 分类器评估报告（评估集，只测不改）");
        sb.AppendLine();
        sb.AppendLine($"- 样本数: {total}");
        sb.AppendLine($"- 类别准确率: {accuracy:P2}（达标线 ≥85%）");
        sb.AppendLine($"- P0 召回率: {(trueP0 == 0 ? 1.0 : (double)predP0TrueP0 / trueP0):P2}（{predP0TrueP0}/{trueP0}，达标线 ≥95%）");
        sb.AppendLine($"- P0 误报率: {p0FpRate:P2}（达标线 ≤10%）");
        sb.AppendLine($"- 待确认率: {pendingRate:P2}（上限 ≤30%；健康区间下限 15% 为指示性，D-39）");
        sb.AppendLine();
        sb.AppendLine("## 混淆矩阵（行=真实，列=预测）");
        sb.AppendLine();
        sb.Append("| 真实\\预测 | ").Append(string.Join(" | ", categories.Select(c => c.ToString()))).AppendLine(" |");
        sb.Append("| --- | ").Append(string.Join(" | ", categories.Select(_ => "---"))).AppendLine(" |");
        for (var i = 0; i < categories.Length; i++)
        {
            sb.Append($"| {categories[i]} | ").Append(string.Join(" | ", Enumerable.Range(0, categories.Length).Select(j => confusion[i, j].ToString()))).AppendLine(" |");
        }

        File.WriteAllText(Path.Combine(reportDir, "evaluation-report.md"), sb.ToString());
    }
}
