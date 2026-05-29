using System;
using System.Collections.Generic;

namespace SnapAccess;

/// <summary>
/// Centralized announcement service wrapping ScreenReader with priority-based
/// speech, duplicate suppression, critical cooldown, and history.
/// </summary>
public class AnnouncementService
{
    private const int MaxHistory = 50;
    private const double CriticalCooldownSeconds = 15.0;

    private string _lastAnnouncement;
    private DateTime _criticalActiveUntil = DateTime.MinValue;
    private readonly List<string> _history = new List<string>();

    public static AnnouncementService Instance { get; private set; }

    public IReadOnlyList<string> History => _history;

    public AnnouncementService()
    {
        Instance = this;
    }

    /// <summary>
    /// Announce a message with the given priority.
    /// Low/Normal: queued (no interrupt). High/Immediate/Critical: interrupt current speech.
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
        DebugLogger.Log(LogCategory.State, "Announce", $"{priority}: {message}");

        bool criticalActive = DateTime.UtcNow < _criticalActiveUntil;

        if (priority == AnnouncementPriority.Critical)
        {
            _criticalActiveUntil = DateTime.UtcNow.AddSeconds(CriticalCooldownSeconds);
            ScreenReader.Say(message, interrupt: true);
        }
        else if (priority >= AnnouncementPriority.Immediate && !criticalActive)
        {
            ScreenReader.Say(message, interrupt: true);
        }
        else if (priority >= AnnouncementPriority.High && !criticalActive)
        {
            ScreenReader.Say(message, interrupt: true);
        }
        else
        {
            // Low, Normal, or suppressed by critical cooldown — queue without interrupt
            ScreenReader.Say(message, interrupt: false);
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
            ScreenReader.Say(_lastAnnouncement, interrupt: true);
        }
    }

    /// <summary>Stop all current speech.</summary>
    public void Silence()
    {
        ScreenReader.Silence();
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
