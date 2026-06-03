using System;
using System.Collections.Generic;

namespace SnapAccess;

/// <summary>
/// Centralized announcement service wrapping a screen-reader output with
/// priority-based speech, duplicate suppression, critical cooldown, and history.
/// </summary>
public class AnnouncementService
{
    private const int MaxHistory = 50;
    private const double CriticalCooldownSeconds = 15.0;

    private readonly ISpeechOutput _speech;
    private readonly Func<DateTime> _clock;
    private readonly Action<string> _log;

    private string _lastAnnouncement;
    private DateTime _criticalActiveUntil = DateTime.MinValue;
    private readonly List<string> _history = new List<string>();

    public static AnnouncementService Instance { get; private set; }

    public IReadOnlyList<string> History => _history;

    /// <param name="speech">Screen-reader output seam. Required.</param>
    /// <param name="clock">UTC clock; defaults to <see cref="DateTime.UtcNow"/>. Injectable for tests.</param>
    /// <param name="log">Optional diagnostic logger called once per announcement.</param>
    public AnnouncementService(ISpeechOutput speech, Func<DateTime> clock = null, Action<string> log = null)
    {
        _speech = speech ?? throw new ArgumentNullException(nameof(speech));
        _clock = clock ?? (() => DateTime.UtcNow);
        _log = log;
        Instance = this;
    }

    /// <summary>
    /// Announce a message with the given priority.
    /// Low/Normal: queued (no interrupt). High and above: interrupt current speech.
    /// Duplicate messages are suppressed unless priority >= High.
    /// During critical cooldown (15s after a Critical message), only Critical messages interrupt.
    /// </summary>
    public void Announce(string message, AnnouncementPriority priority = AnnouncementPriority.Normal)
    {
        if (string.IsNullOrEmpty(message))
            return;

        // Duplicate suppression: skip identical text unless priority is High or above
        if (message == _lastAnnouncement && priority < AnnouncementPriority.High)
            return;

        _lastAnnouncement = message;
        AddToHistory(message);
        _log?.Invoke($"{priority}: {message}");

        bool criticalActive = _clock() < _criticalActiveUntil;

        if (priority == AnnouncementPriority.Critical)
        {
            _criticalActiveUntil = _clock().AddSeconds(CriticalCooldownSeconds);
            _speech.Say(message, interrupt: true);
        }
        else if (priority >= AnnouncementPriority.High && !criticalActive)
        {
            _speech.Say(message, interrupt: true);
        }
        else
        {
            // Low, Normal, or suppressed by critical cooldown — queue without interrupt
            _speech.Say(message, interrupt: false);
        }
    }

    /// <summary>Shortcut for Immediate priority (interrupts current speech).</summary>
    public void AnnounceInterrupt(string message)
    {
        Announce(message, AnnouncementPriority.Immediate);
    }

    /// <summary>Repeat the last announcement at Immediate priority.</summary>
    public void RepeatLast()
    {
        if (!string.IsNullOrEmpty(_lastAnnouncement))
        {
            _speech.Say(_lastAnnouncement, interrupt: true);
        }
    }

    /// <summary>Stop all current speech.</summary>
    public void Silence()
    {
        _speech.Silence();
    }

    private void AddToHistory(string message)
    {
        if (_history.Count > 0 && _history[_history.Count - 1] == message)
            return;

        _history.Add(message);
        if (_history.Count > MaxHistory)
            _history.RemoveAt(0);
    }
}
