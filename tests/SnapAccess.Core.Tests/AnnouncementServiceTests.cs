using System;
using System.Collections.Generic;
using FluentAssertions;
using SnapAccess;
using Xunit;

namespace SnapAccess.Core.Tests;

public class AnnouncementServiceTests
{
    private sealed class FakeSpeech : ISpeechOutput
    {
        public readonly List<(string Text, bool Interrupt)> Calls = new();
        public int SilenceCount;
        public void Say(string text, bool interrupt) => Calls.Add((text, interrupt));
        public void Silence() => SilenceCount++;
    }

    private sealed class FakeClock
    {
        public DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public DateTime Get() => Now;
        public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
    }

    private static AnnouncementService Make(FakeSpeech speech, FakeClock clock = null)
        => new AnnouncementService(speech, (clock ?? new FakeClock()).Get);

    [Fact]
    public void Announce_Normal_QueuesWithoutInterrupt()
    {
        var speech = new FakeSpeech();
        Make(speech).Announce("hi", AnnouncementPriority.Normal);
        speech.Calls.Should().ContainSingle().Which.Should().Be(("hi", false));
    }

    [Theory]
    [InlineData(AnnouncementPriority.High)]
    [InlineData(AnnouncementPriority.Immediate)]
    [InlineData(AnnouncementPriority.Critical)]
    public void Announce_HighOrAbove_Interrupts(AnnouncementPriority priority)
    {
        var speech = new FakeSpeech();
        Make(speech).Announce("hi", priority);
        speech.Calls.Should().ContainSingle().Which.Interrupt.Should().BeTrue();
    }

    [Fact]
    public void Announce_NullOrEmpty_DoesNothing()
    {
        var speech = new FakeSpeech();
        var svc = Make(speech);
        svc.Announce(null);
        svc.Announce("");
        speech.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Announce_DuplicateNormal_IsSuppressed()
    {
        var speech = new FakeSpeech();
        var svc = Make(speech);
        svc.Announce("same", AnnouncementPriority.Normal);
        svc.Announce("same", AnnouncementPriority.Normal);
        speech.Calls.Should().HaveCount(1);
    }

    [Fact]
    public void Announce_DuplicateHigh_IsNotSuppressed()
    {
        var speech = new FakeSpeech();
        var svc = Make(speech);
        svc.Announce("same", AnnouncementPriority.High);
        svc.Announce("same", AnnouncementPriority.High);
        speech.Calls.Should().HaveCount(2);
    }

    [Fact]
    public void Critical_StartsCooldown_DuringWhichHighIsNotInterrupting()
    {
        var speech = new FakeSpeech();
        var clock = new FakeClock();
        var svc = Make(speech, clock);

        svc.Announce("boom", AnnouncementPriority.Critical);
        svc.Announce("status", AnnouncementPriority.High); // within cooldown

        speech.Calls[0].Should().Be(("boom", true));
        speech.Calls[1].Should().Be(("status", false)); // queued, not interrupting
    }

    [Fact]
    public void Critical_AlwaysInterrupts_EvenDuringCooldown()
    {
        var speech = new FakeSpeech();
        var svc = Make(speech);
        svc.Announce("boom", AnnouncementPriority.Critical);
        svc.Announce("boom2", AnnouncementPriority.Critical);
        speech.Calls[1].Should().Be(("boom2", true));
    }

    [Fact]
    public void AfterCooldownExpires_HighInterruptsAgain()
    {
        var speech = new FakeSpeech();
        var clock = new FakeClock();
        var svc = Make(speech, clock);

        svc.Announce("boom", AnnouncementPriority.Critical);
        clock.Advance(16); // past the 15s cooldown
        svc.Announce("status", AnnouncementPriority.High);

        speech.Calls[1].Should().Be(("status", true));
    }

    [Fact]
    public void History_RecordsAnnouncementsInOrder()
    {
        var speech = new FakeSpeech();
        var svc = Make(speech);
        svc.Announce("a");
        svc.Announce("b");
        svc.Announce("c");
        svc.History.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void History_IsCappedAtFifty()
    {
        var speech = new FakeSpeech();
        var svc = Make(speech);
        for (int i = 1; i <= 60; i++) svc.Announce("msg" + i);
        svc.History.Should().HaveCount(50);
        svc.History[0].Should().Be("msg11");
        svc.History[^1].Should().Be("msg60");
    }

    [Fact]
    public void RepeatLast_SaysLastMessageInterrupting()
    {
        var speech = new FakeSpeech();
        var svc = Make(speech);
        svc.Announce("only", AnnouncementPriority.Normal);
        svc.RepeatLast();
        speech.Calls[^1].Should().Be(("only", true));
    }

    [Fact]
    public void Silence_DelegatesToSpeechOutput()
    {
        var speech = new FakeSpeech();
        Make(speech).Silence();
        speech.SilenceCount.Should().Be(1);
    }

    [Fact]
    public void Constructor_NullSpeech_Throws()
    {
        Action act = () => new AnnouncementService(null);
        act.Should().Throw<ArgumentNullException>();
    }
}
