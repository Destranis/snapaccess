using FluentAssertions;
using SnapAccess;
using Xunit;

namespace SnapAccess.Core.Tests;

public class TurnTextParserTests
{
    [Theory]
    [InlineData("3 / 6", "3")]
    [InlineData("10 / 6", "10")]
    // The bug: the game's TMP turn label nests size tags, e.g. "2 / 6" where the
    // current turn is large. The closing "</size>" tag contains a '/', so a naive
    // Split('/') broke the string mid-tag and left a stray '<' for the screen
    // reader to read aloud ("Turn 2 less-than..."). Markup must be stripped first.
    [InlineData("<size=490>2</size> / 6", "2")]
    [InlineData("<size=490>2</size><size=294> / 6</size>", "2")]
    public void ParseCurrent_ReturnsCurrentTurnNumber_StrippingMarkup(string raw, string expected)
    {
        TurnTextParser.ParseCurrent(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("FINAL TURN")]
    [InlineData("<color=#fff>FINAL TURN</color>")]
    [InlineData("final turn")]
    public void ParseCurrent_FinalTurn_ReturnsFinalSentinel(string raw)
    {
        TurnTextParser.ParseCurrent(raw).Should().Be("FINAL");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Loading")] // no slash, not final
    public void ParseCurrent_Unparseable_ReturnsNull(string raw)
    {
        TurnTextParser.ParseCurrent(raw).Should().BeNull();
    }

    [Theory]
    [InlineData("<size=490>2</size>", "2")]
    [InlineData("a<b>c</b>d", "acd")]
    [InlineData("plain", "plain")]
    [InlineData(null, "")]
    public void StripMarkup_RemovesTags(string raw, string expected)
    {
        TurnTextParser.StripMarkup(raw).Should().Be(expected);
    }
}
