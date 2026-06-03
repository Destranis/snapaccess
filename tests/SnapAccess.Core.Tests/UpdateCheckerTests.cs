using FluentAssertions;
using SnapAccess;
using Xunit;

namespace SnapAccess.Core.Tests;

public class UpdateCheckerTests
{
    // --- ParseTagName: pull the version out of a GitHub release JSON payload ---

    [Fact]
    public void ParseTagName_ExtractsTag_AndStripsLeadingV()
    {
        UpdateChecker.ParseTagName("{\"tag_name\":\"v0.6.0\",\"name\":\"Release\"}")
            .Should().Be("0.6.0");
    }

    [Fact]
    public void ParseTagName_WithoutLeadingV_ReturnsTagAsIs()
    {
        UpdateChecker.ParseTagName("{ \"tag_name\": \"0.6.0\" }").Should().Be("0.6.0");
    }

    [Fact]
    public void ParseTagName_NoTagField_ReturnsNull()
    {
        UpdateChecker.ParseTagName("{\"name\":\"no tag here\"}").Should().BeNull();
    }

    // --- IsNewer: semantic version comparison ---

    [Theory]
    [InlineData("0.5.0", "0.6.0", true)]   // newer minor
    [InlineData("0.5.0", "0.5.1", true)]   // newer patch
    [InlineData("0.5.0", "0.5.0", false)]  // equal is NOT an update
    [InlineData("0.5.0", "0.4.0", false)]  // downgrade is NOT an update
    [InlineData("0.9.0", "0.10.0", true)]  // numeric, not lexical: '10' > '9'
    public void IsNewer_ComparesSemantically(string current, string latest, bool expected)
    {
        UpdateChecker.IsNewer(current, latest).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsNewer_NullOrEmptyLatest_IsFalse(string latest)
    {
        UpdateChecker.IsNewer("0.5.0", latest).Should().BeFalse();
    }
}
