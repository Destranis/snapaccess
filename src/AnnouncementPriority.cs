namespace SnapAccess;

/// <summary>
/// Priority levels for screen reader announcements.
/// Higher priorities interrupt lower ones; duplicates are suppressed below High.
/// </summary>
public enum AnnouncementPriority
{
    /// <summary>Background info, won't interrupt other speech.</summary>
    Low,

    /// <summary>Default navigation announcements (card focus, element read).</summary>
    Normal,

    /// <summary>State changes (turn change, screen transition). Bypasses duplicate suppression.</summary>
    High,

    /// <summary>Interrupts current speech (card play confirmation, errors).</summary>
    Immediate,

    /// <summary>Interrupts everything, sets 15s cooldown blocking non-critical (game over, retreat).</summary>
    Critical
}
