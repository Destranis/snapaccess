using FluentAssertions;
using SnapAccess;
using Xunit;

namespace SnapAccess.Core.Tests;

// Characterization tests pinning the current behavior of the Loc string table.
public class LocTests
{
    [Fact]
    public void Get_KnownKey_ReturnsLocalizedString()
    {
        Loc.Get("mod_loaded").Should().Be("Snap Access loaded. F1 for help.");
    }

    [Fact]
    public void Get_UnknownKey_ReturnsTheKeyItself()
    {
        Loc.Get("this_key_does_not_exist").Should().Be("this_key_does_not_exist");
    }

    [Fact]
    public void Get_WithArgs_FormatsPlaceholders()
    {
        Loc.Get("input_deleted", "five").Should().Be("deleted five");
    }

    [Fact]
    public void Get_WithArgs_MissingPlaceholderArg_ReturnsRawTemplate()
    {
        // "deleted {0}" formatted with no args throws FormatException internally;
        // Loc swallows it and returns the raw template.
        Loc.Get("input_deleted", new object[0]).Should().Be("deleted {0}");
    }

    [Fact]
    public void Get_WithArgs_OnLiteralKey_ReturnsKeyUnchanged()
    {
        Loc.Get("no_such_key", "ignored").Should().Be("no_such_key");
    }
}
