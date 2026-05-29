using System.Collections.Generic;

namespace SnapAccess;

/// <summary>
/// Browsable announcement history. Press O to open, Up/Down to navigate,
/// O or Escape to close. Shows most recent announcements first.
/// Adapted from AccessibleArena's GameLogNavigator pattern.
/// </summary>
public class GameLogNavigator
{
    private readonly List<string> _items = new List<string>();
    private int _currentIndex;
    private bool _isActive;

    public bool IsActive => _isActive;

    public void Open()
    {
        _items.Clear();
        IReadOnlyList<string> history = AnnouncementService.Instance.History;
        // Reverse: newest first
        for (int i = history.Count - 1; i >= 0; i--)
        {
            _items.Add(history[i]);
        }
        if (_items.Count == 0)
        {
            AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("log_empty"));
            return;
        }
        _isActive = true;
        _currentIndex = 0;
        AnnouncementService.Instance.AnnounceInterrupt(
            Loc.Get("log_opened", _items.Count.ToString()));
    }

    public void Close()
    {
        if (_isActive)
        {
            _isActive = false;
            _currentIndex = 0;
            AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("log_closed"));
        }
    }

    /// <summary>
    /// Processes input when the game log is active.
    /// Returns true if input was consumed (log is active).
    /// </summary>
    public bool HandleInput()
    {
        if (!_isActive) return false;

        // O, Backspace, Escape: close
        if (SDLInput.IsKeyDown(SDLInput.Key.O) ||
            SDLInput.IsKeyDown(SDLInput.Key.Backspace) ||
            SDLInput.IsKeyDown(SDLInput.Key.Escape))
        {
            Close();
            return true;
        }

        // Down: next (older)
        if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
        {
            if (_currentIndex < _items.Count - 1)
            {
                _currentIndex++;
                AnnounceCurrentItem();
            }
            else
            {
                AnnouncementService.Instance.Announce(Loc.Get("log_end"), AnnouncementPriority.Normal);
            }
            return true;
        }

        // Up: previous (newer)
        if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
        {
            if (_currentIndex > 0)
            {
                _currentIndex--;
                AnnounceCurrentItem();
            }
            else
            {
                AnnouncementService.Instance.Announce(Loc.Get("log_start"), AnnouncementPriority.Normal);
            }
            return true;
        }

        // Home: jump to newest
        if (SDLInput.IsKeyDown(SDLInput.Key.Home))
        {
            _currentIndex = 0;
            AnnounceCurrentItem();
            return true;
        }

        // End: jump to oldest
        if (SDLInput.IsKeyDown(SDLInput.Key.End))
        {
            _currentIndex = _items.Count - 1;
            AnnounceCurrentItem();
            return true;
        }

        // Consume all other input while log is open
        return true;
    }

    private void AnnounceCurrentItem()
    {
        if (_currentIndex >= 0 && _currentIndex < _items.Count)
        {
            string text = _items[_currentIndex];
            string msg = $"{_currentIndex + 1} {Loc.Get("log_of")} {_items.Count}: {text}";
            AnnouncementService.Instance.AnnounceInterrupt(msg);
        }
    }
}
