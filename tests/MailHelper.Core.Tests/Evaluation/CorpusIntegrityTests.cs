using FluentAssertions;
using MailHelper.Core;
using Xunit;

namespace MailHelper.Core.Tests.Evaluation;

/// <summary>语料完整性（06 章 §2：≥600 封、7 类别、标注与划分齐全、R 边界样例在评估集）。</summary>
public class CorpusIntegrityTests
{
    private static string CorpusDir => CorpusLoader.FindRepoPath("tests", "fixtures", "sample-corpus");

    [Fact]
    public void Corpus_HasAtLeast600EmlFiles_WithCompleteLabels()
    {
        var files = Directory.GetFiles(CorpusDir, "*.eml");
        files.Should().HaveCountGreaterThanOrEqualTo(600);

        var train = File.ReadAllLines(Path.Combine(CorpusDir, "labels-train.csv")).Skip(1).Where(l => l.Length > 0).Count();
        var eval = File.ReadAllLines(Path.Combine(CorpusDir, "labels-eval.csv")).Skip(1).Where(l => l.Length > 0).Count();
        (train + eval).Should().Be(files.Length);

        var trainRatio = (double)train / (train + eval);
        trainRatio.Should().BeInRange(0.55, 0.65, "训练/评估 60/40 划分（±5%）");

        var evalSamples = CorpusLoader.Load(CorpusDir, "labels-eval.csv");
        evalSamples.Should().NotBeEmpty();
        evalSamples.Should().OnlyContain(s => Enum.IsDefined(s.Category) && Enum.IsDefined(s.Importance));
    }

    [Fact]
    public void Corpus_CoversAllCategoriesAndImportanceLevels()
    {
        var all = CorpusLoader.Load(CorpusDir, "labels-train.csv")
            .Concat(CorpusLoader.Load(CorpusDir, "labels-eval.csv"));

        all.GroupBy(s => s.Category).Select(g => g.Key).Should().BeEquivalentTo(Enum.GetValues<MailCategory>());
        all.Should().Contain(s => s.Importance == Importance.P0, "评估 P0 召回需要 P0 样本");
        all.Should().Contain(s => s.Importance == Importance.P3);
        all.Should().Contain(s => s.Subject.Any(char.IsAsciiLetter) && s.Subject.Any(c => c > 127), "中英文混合");
    }

    [Fact]
    public void Corpus_ContainsBoundarySamples_InEvalSet()
    {
        var evalSamples = CorpusLoader.Load(CorpusDir, "labels-eval.csv");

        var r01 = evalSamples.Single(s => s.File.StartsWith("r01"));
        r01.Category.Should().Be(MailCategory.Finance);
        r01.Importance.Should().Be(Importance.P0);

        var r02 = evalSamples.Single(s => s.File.StartsWith("r02"));
        r02.Category.Should().Be(MailCategory.Career);
        r02.Importance.Should().Be(Importance.P1);

        var r05 = evalSamples.Single(s => s.File.StartsWith("r05"));
        r05.Category.Should().Be(MailCategory.Other);

        var r06 = evalSamples.Single(s => s.File.StartsWith("r06"));
        r06.Category.Should().Be(MailCategory.Course);
        r06.Body.Should().Contain("From: office@hku.hk"); // 引文块存在（预处理必须剥离）

        var r07 = evalSamples.Single(s => s.File.StartsWith("r07"));
        r07.Subject.Should().Match(s => s.Trim('!').Length >= 60); // ReDoS 探针
    }

    [Fact]
    public void Generator_IsDeterministic_SameSeedSameCorpus()
    {
        var temp = Path.Combine(Path.GetTempPath(), "mh-corpus-" + Guid.NewGuid().ToString("N"));
        try
        {
            SampleCorpus.Generate(temp, SampleCorpus.Seed);

            var generated = Directory.GetFiles(temp, "*.eml").Select(f => Path.GetFileName(f)!).OrderBy(f => f, StringComparer.Ordinal).ToList();
            var committed = Directory.GetFiles(CorpusDir, "*.eml").Select(f => Path.GetFileName(f)!).OrderBy(f => f, StringComparer.Ordinal).ToList();
            generated.Should().Equal(committed, "同种子重生成必须与已提交语料一致（防漂移）");

            var spot = generated.First(f => f.StartsWith("r0", StringComparison.Ordinal));
            File.ReadAllBytes(Path.Combine(temp, spot)).Should().Equal(File.ReadAllBytes(Path.Combine(CorpusDir, spot)));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
