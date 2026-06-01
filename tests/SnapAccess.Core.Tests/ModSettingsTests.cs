using FluentAssertions;
using SnapAccess;
using Xunit;

namespace SnapAccess.Core.Tests;

public class ModSettingsTests
{
    [Fact]
    public void NewInstance_AllSettingsDefaultToTrue()
    {
        var s = new ModSettings();
        s.PositionCounts.Should().BeTrue();
        s.VerboseCardInfo.Should().BeTrue();
        s.OpponentAnnouncements.Should().BeTrue();
        s.AutoTurnAnnounce.Should().BeTrue();
        s.TransitionAnnouncements.Should().BeTrue();
        s.TutorialMessages.Should().BeTrue();
    }

    [Fact]
    public void Parse_PrettyJson_AppliesValues()
    {
        string json = "{\n  \"PositionCounts\": false,\n  \"VerboseCardInfo\": true,\n"
                    + "  \"OpponentAnnouncements\": false,\n  \"AutoTurnAnnounce\": true,\n"
                    + "  \"TransitionAnnouncements\": false,\n  \"TutorialMessages\": true\n}";

        var s = ModSettings.Parse(json);

        s.PositionCounts.Should().BeFalse();
        s.VerboseCardInfo.Should().BeTrue();
        s.OpponentAnnouncements.Should().BeFalse();
        s.TutorialMessages.Should().BeTrue();
    }

    [Fact]
    public void Parse_CompactSingleLineJson_AppliesValues()
    {
        // The original line-split parser silently ignored single-line JSON,
        // leaving every value at its default. A robust parser must handle it.
        string json = "{\"PositionCounts\":false,\"VerboseCardInfo\":false,"
                    + "\"OpponentAnnouncements\":true,\"AutoTurnAnnounce\":false,"
                    + "\"TransitionAnnouncements\":true,\"TutorialMessages\":false}";

        var s = ModSettings.Parse(json);

        s.PositionCounts.Should().BeFalse();
        s.VerboseCardInfo.Should().BeFalse();
        s.OpponentAnnouncements.Should().BeTrue();
        s.AutoTurnAnnounce.Should().BeFalse();
        s.TransitionAnnouncements.Should().BeTrue();
        s.TutorialMessages.Should().BeFalse();
    }

    [Fact]
    public void SerializeThenParse_RoundTripsAllValues()
    {
        var original = new ModSettings
        {
            PositionCounts = false,
            VerboseCardInfo = true,
            OpponentAnnouncements = false,
            AutoTurnAnnounce = false,
            TransitionAnnouncements = true,
            TutorialMessages = false,
        };

        var restored = ModSettings.Parse(original.Serialize());

        restored.PositionCounts.Should().Be(original.PositionCounts);
        restored.VerboseCardInfo.Should().Be(original.VerboseCardInfo);
        restored.OpponentAnnouncements.Should().Be(original.OpponentAnnouncements);
        restored.AutoTurnAnnounce.Should().Be(original.AutoTurnAnnounce);
        restored.TransitionAnnouncements.Should().Be(original.TransitionAnnouncements);
        restored.TutorialMessages.Should().Be(original.TutorialMessages);
    }

    [Fact]
    public void Parse_UnknownKeys_AreIgnored_KnownDefaultsKept()
    {
        var s = ModSettings.Parse("{ \"SomethingElse\": false, \"VerboseCardInfo\": false }");
        s.VerboseCardInfo.Should().BeFalse();
        s.PositionCounts.Should().BeTrue(); // untouched default
    }
}
