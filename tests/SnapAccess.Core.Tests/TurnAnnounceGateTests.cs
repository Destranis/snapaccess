using FluentAssertions;
using SnapAccess;
using Xunit;

namespace SnapAccess.Core.Tests;

// The battlefield re-runs its turn-start announcement whenever the hand count
// changes, and playing a card changes the hand count. The gate suppresses those
// repeats so a turn is announced once, when its number actually changes.
public class TurnAnnounceGateTests
{
    [Fact]
    public void ShouldAnnounce_FirstTurn_IsTrue()
    {
        new TurnAnnounceGate().ShouldAnnounce("1").Should().BeTrue();
    }

    [Fact]
    public void ShouldAnnounce_SameTurnAgain_IsFalse()
    {
        var gate = new TurnAnnounceGate();
        gate.ShouldAnnounce("3");
        gate.ShouldAnnounce("3").Should().BeFalse();
    }

    [Fact]
    public void ShouldAnnounce_NewTurnNumber_IsTrue()
    {
        var gate = new TurnAnnounceGate();
        gate.ShouldAnnounce("2");
        gate.ShouldAnnounce("3").Should().BeTrue();
    }

    [Fact]
    public void ShouldAnnounce_SequenceWithRepeats_AnnouncesOncePerTurn()
    {
        var gate = new TurnAnnounceGate();
        // current turn 2 (start), then two card plays re-fire with the same number,
        // then turn 3 starts and re-fires once.
        gate.ShouldAnnounce("2").Should().BeTrue();
        gate.ShouldAnnounce("2").Should().BeFalse();
        gate.ShouldAnnounce("2").Should().BeFalse();
        gate.ShouldAnnounce("3").Should().BeTrue();
        gate.ShouldAnnounce("3").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ShouldAnnounce_NullOrEmpty_IsFalse_AndDoesNotChangeState(string value)
    {
        var gate = new TurnAnnounceGate();
        gate.ShouldAnnounce(value).Should().BeFalse();
        gate.ShouldAnnounce("1").Should().BeTrue(); // state was not consumed by the null call
    }

    [Fact]
    public void Reset_AllowsTheSameTurnToAnnounceAgain()
    {
        var gate = new TurnAnnounceGate();
        gate.ShouldAnnounce("1");
        gate.Reset();
        gate.ShouldAnnounce("1").Should().BeTrue();
    }
}
