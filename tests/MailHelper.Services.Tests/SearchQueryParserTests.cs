using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;
using Xunit;

namespace MailHelper.Services.Tests;

/// <summary>搜索语法解析（FR-13 / MOD-09）：from:/cat:/p: 前缀 + 自由文本；大小写不敏感；
/// 非法前缀值整 token 留作自由文本；重复前缀首个生效（D-58）。</summary>
public class SearchQueryParserTests
{
    [Fact]
    public void PlainText_OnlyFreeText()
    {
        var q = SearchQueryParser.Parse("缴费 截止");

        q.FreeText.Should().Be("缴费 截止");
        q.FromFilter.Should().BeNull();
        q.Category.Should().BeNull();
        q.Importance.Should().BeNull();
    }

    [Fact]
    public void FromFilter_Extracted()
    {
        var q = SearchQueryParser.Parse("from:hku.hk 学费");

        q.FromFilter.Should().Be("hku.hk");
        q.FreeText.Should().Be("学费");
    }

    [Fact]
    public void Category_AcceptsEnumName_CaseInsensitive()
    {
        SearchQueryParser.Parse("cat:finance").Category.Should().Be(CategoryIds.Finance);
        SearchQueryParser.Parse("cat:FINANCE").Category.Should().Be(CategoryIds.Finance);
        SearchQueryParser.Parse("cat:course").Category.Should().Be(CategoryIds.Course);
    }

    [Fact]
    public void Importance_AcceptsP0ThroughP3()
    {
        SearchQueryParser.Parse("p:P0").Importance.Should().Be(Importance.P0);
        SearchQueryParser.Parse("p:p1").Importance.Should().Be(Importance.P1);
    }

    [Fact]
    public void InvalidPrefixValue_StaysAsFreeText()
    {
        var q = SearchQueryParser.Parse("cat:unknown 请假");

        q.Category.Should().BeNull();
        q.FreeText.Should().Be("cat:unknown 请假"); // 无法识别 → 用户直觉：当作普通词搜
    }

    [Fact]
    public void Mixed_Tokens_OrderIndependent()
    {
        var q = SearchQueryParser.Parse("p:P1 from:bursary@hku.hk cat:course deadline 学费");

        q.Importance.Should().Be(Importance.P1);
        q.FromFilter.Should().Be("bursary@hku.hk");
        q.Category.Should().Be(CategoryIds.Course);
        q.FreeText.Should().Be("deadline 学费");
    }

    [Fact]
    public void DuplicatePrefix_FirstWins()
    {
        var q = SearchQueryParser.Parse("from:a@x.com from:b@y.com");

        q.FromFilter.Should().Be("a@x.com");
        q.FreeText.Should().Be("from:b@y.com"); // 第二个不再解析
    }

    [Fact]
    public void Empty_ReturnsEmptyQuery()
    {
        var q = SearchQueryParser.Parse("   ");

        q.FreeText.Should().BeEmpty();
        q.FromFilter.Should().BeNull();
        q.Category.Should().BeNull();
        q.Importance.Should().BeNull();
    }
}
