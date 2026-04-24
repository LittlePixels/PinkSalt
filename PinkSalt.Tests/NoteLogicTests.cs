using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PinkSalt;
using Xunit;

namespace PinkSalt.Tests;

/// <summary>
/// Tests for logic extracted from MainWindow: tag parsing, search filtering,
/// wiki-link regex, and numbered-list continuation.
/// </summary>

// ---- Tag parsing ----
// Mirrors: TagsTextBox.Text.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t))

public class TagParsingTests
{
    private static List<string> ParseTags(string input) =>
        input.Split(',')
             .Select(t => t.Trim())
             .Where(t => !string.IsNullOrEmpty(t))
             .ToList();

    [Fact]
    public void ParseTags_CommaSeparated_ReturnsEachTag()
    {
        var tags = ParseTags("alpha, beta, gamma");
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, tags);
    }

    [Fact]
    public void ParseTags_TrimsWhitespaceAroundEachTag()
    {
        var tags = ParseTags("  spaces  ,  around  ");
        Assert.Equal(new[] { "spaces", "around" }, tags);
    }

    [Fact]
    public void ParseTags_SingleTag_ReturnsSingleElement()
    {
        var tags = ParseTags("only");
        Assert.Equal(new[] { "only" }, tags);
    }

    [Fact]
    public void ParseTags_EmptyString_ReturnsEmpty()
    {
        Assert.Empty(ParseTags(""));
    }

    [Fact]
    public void ParseTags_OnlyCommasAndSpaces_ReturnsEmpty()
    {
        Assert.Empty(ParseTags("  ,  ,  "));
    }

    [Fact]
    public void ParseTags_TrailingComma_IgnoresBlankEntry()
    {
        var tags = ParseTags("tag1, tag2,");
        Assert.Equal(new[] { "tag1", "tag2" }, tags);
    }

    [Fact]
    public void ParseTags_NoComma_ReturnsSingleTag()
    {
        var tags = ParseTags("nocomma");
        Assert.Single(tags);
        Assert.Equal("nocomma", tags[0]);
    }
}

// ---- Search / filter logic ----
// Mirrors: notes.Where(n => n.Title.ToLower().Contains(q) || n.Content.ToLower().Contains(q)
//                        || n.Tags.Any(t => t.ToLower().Contains(q)))

public class SearchFilterTests
{
    private static List<Note> Filter(IEnumerable<Note> notes, string query)
    {
        var q = query.ToLower();
        return notes.Where(n =>
            n.Title.ToLower().Contains(q) ||
            n.Content.ToLower().Contains(q) ||
            n.Tags.Any(t => t.ToLower().Contains(q))).ToList();
    }

    private static Note Make(string title = "", string content = "", params string[] tags) =>
        new Note { Id = Guid.NewGuid(), Title = title, Content = content, Tags = tags.ToList() };

    [Fact]
    public void Filter_ByTitle_ReturnsMatchingNote()
    {
        var notes = new[] { Make(title: "Meeting Notes"), Make(title: "Shopping List") };
        var result = Filter(notes, "meeting");
        Assert.Single(result);
        Assert.Equal("Meeting Notes", result[0].Title);
    }

    [Fact]
    public void Filter_ByContent_ReturnsMatchingNote()
    {
        var notes = new[] { Make(content: "Buy milk and eggs"), Make(content: "Q3 roadmap") };
        var result = Filter(notes, "roadmap");
        Assert.Single(result);
    }

    [Fact]
    public void Filter_ByTag_ReturnsMatchingNote()
    {
        var notes = new[] { Make(tags: new[] { "work", "project" }), Make(tags: new[] { "personal" }) };
        var result = Filter(notes, "project");
        Assert.Single(result);
    }

    [Fact]
    public void Filter_IsCaseInsensitive()
    {
        var notes = new[] { Make(title: "UPPERCASE TITLE") };
        Assert.Single(Filter(notes, "uppercase"));
        Assert.Single(Filter(notes, "UPPERCASE"));
        Assert.Single(Filter(notes, "Uppercase"));
    }

    [Fact]
    public void Filter_NoMatch_ReturnsEmpty()
    {
        var notes = new[] { Make(title: "Alpha"), Make(title: "Beta") };
        Assert.Empty(Filter(notes, "zzznomatch"));
    }

    [Fact]
    public void Filter_MultipleMatches_ReturnsAll()
    {
        var notes = new[] { Make(title: "Note A"), Make(title: "Note B"), Make(title: "Other") };
        var result = Filter(notes, "note");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Filter_MatchesPartialTitle()
    {
        var notes = new[] { Make(title: "My important note") };
        Assert.Single(Filter(notes, "import"));
    }

    [Fact]
    public void Filter_TagPartialMatch_ReturnsNote()
    {
        var notes = new[] { Make(tags: "javascript") };
        Assert.Single(Filter(notes, "script"));
    }

    [Fact]
    public void Filter_EmptyCollection_ReturnsEmpty()
    {
        Assert.Empty(Filter(Array.Empty<Note>(), "anything"));
    }
}

// ---- Wiki-link regex ----
// Pattern: @"\[\[([^\]]+)\]\]"  (used in RenderMarkdownPreview and UpdateNoteGraph)

public class WikiLinkRegexTests
{
    private static readonly Regex WikiLink = new(@"\[\[([^\]]+)\]\]");

    private static string[] ExtractLinks(string text) =>
        WikiLink.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public void WikiLink_SingleLink_ExtractsTitle()
    {
        Assert.Equal(new[] { "Note Title" }, ExtractLinks("[[Note Title]]"));
    }

    [Fact]
    public void WikiLink_MultipleLinks_ExtractsAll()
    {
        var links = ExtractLinks("See [[Alpha]] and also [[Beta]] for details.");
        Assert.Equal(new[] { "Alpha", "Beta" }, links);
    }

    [Fact]
    public void WikiLink_EmbeddedInText_StillMatches()
    {
        var links = ExtractLinks("Some text before [[My Note]] and after.");
        Assert.Single(links);
        Assert.Equal("My Note", links[0]);
    }

    [Fact]
    public void WikiLink_NoLinks_ReturnsEmpty()
    {
        Assert.Empty(ExtractLinks("No wiki links here."));
    }

    [Fact]
    public void WikiLink_LinkWithSpaces_FullyCaptures()
    {
        Assert.Equal(new[] { "Note With Spaces" }, ExtractLinks("[[Note With Spaces]]"));
    }

    [Fact]
    public void WikiLink_LinkWithSpecialChars_Captures()
    {
        Assert.Equal(new[] { "C# Notes" }, ExtractLinks("[[C# Notes]]"));
    }

    [Fact]
    public void WikiLink_EmptyBrackets_NoMatch()
    {
        Assert.Empty(ExtractLinks("[[]]"));
    }

    [Fact]
    public void WikiLink_OnlyOneBracketPair_NoMatch()
    {
        Assert.Empty(ExtractLinks("[Single Bracket]"));
    }
}

// ---- Numbered list continuation ----
// Mirrors AutoContinueList regex: @"^(\d+)\. "

public class ListContinuationTests
{
    private static string? NextListPrefix(string prevLineText)
    {
        var trimmed = prevLineText.TrimStart();

        if (trimmed.StartsWith("- ")) return "- ";
        if (trimmed.StartsWith("* ")) return "* ";

        var match = Regex.Match(trimmed, @"^(\d+)\. ");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
            return (num + 1) + ". ";

        return null;
    }

    [Fact]
    public void UnorderedDash_ReturnsDashPrefix()
    {
        Assert.Equal("- ", NextListPrefix("- Item text"));
    }

    [Fact]
    public void UnorderedAsterisk_ReturnsAsteriskPrefix()
    {
        Assert.Equal("* ", NextListPrefix("* Item text"));
    }

    [Fact]
    public void NumberedList_IncrementsNumber()
    {
        Assert.Equal("2. ", NextListPrefix("1. First item"));
        Assert.Equal("6. ", NextListPrefix("5. Fifth item"));
        Assert.Equal("11. ", NextListPrefix("10. Tenth item"));
    }

    [Fact]
    public void NumberedList_LargeNumber_IncrementsCorrectly()
    {
        Assert.Equal("100. ", NextListPrefix("99. Item"));
    }

    [Fact]
    public void PlainText_ReturnsNull()
    {
        Assert.Null(NextListPrefix("Just a regular line"));
    }

    [Fact]
    public void EmptyLine_ReturnsNull()
    {
        Assert.Null(NextListPrefix(""));
    }

    [Fact]
    public void IndentedBullet_StillMatchesDash()
    {
        Assert.Equal("- ", NextListPrefix("   - indented item"));
    }

    [Fact]
    public void Heading_ReturnsNull()
    {
        Assert.Null(NextListPrefix("## My Heading"));
    }
}
