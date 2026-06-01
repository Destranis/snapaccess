namespace SnapAccess;

/// <summary>
/// Decides whether a turn-start announcement should fire. The battlefield detects
/// turn changes by watching the hand count, but the hand count also drops when the
/// player plays a card, which would re-announce the same turn. This gate only lets
/// an announcement through when the turn identity actually changes.
/// </summary>
public sealed class TurnAnnounceGate
{
    private string _lastAnnounced;

    /// <summary>
    /// Returns true when <paramref name="currentTurn"/> is a new, non-empty turn
    /// identity since the last announcement. Returns false for null/empty input or
    /// a repeat of the current turn, and leaves state unchanged in those cases.
    /// </summary>
    public bool ShouldAnnounce(string currentTurn)
    {
        if (string.IsNullOrEmpty(currentTurn)) return false;
        if (currentTurn == _lastAnnounced) return false;
        _lastAnnounced = currentTurn;
        return true;
    }

    /// <summary>Forgets the last announced turn so the next call always announces. Call on new game.</summary>
    public void Reset() => _lastAnnounced = null;
}
