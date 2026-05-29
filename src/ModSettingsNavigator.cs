using System;

namespace SnapAccess;

/// <summary>
/// In-game mod settings menu. Toggle with F4.
/// Up/Down to browse settings, Enter/Left/Right to toggle, F4 or Escape to close.
/// </summary>
public class ModSettingsNavigator
{
    private bool _isOpen = false;
    private int _index = 0;

    public bool IsOpen => _isOpen;

    /// <summary>
    /// Handles input when settings menu is open.
    /// Returns true if input was consumed.
    /// </summary>
    public bool HandleInput()
    {
        // F4 toggles settings menu
        if (SDLInput.IsKeyDown(SDLInput.Key.F4))
        {
            if (_isOpen)
                Close();
            else
                Open();
            return true;
        }

        if (!_isOpen) return false;

        var settings = ModSettings.AllSettings;
        if (settings.Count == 0) return true;

        if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
        {
            _index = (_index - 1 + settings.Count) % settings.Count;
            AnnounceCurrent();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
        {
            _index = (_index + 1) % settings.Count;
            AnnounceCurrent();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsKeyDown(SDLInput.Key.Space) ||
                 SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
        {
            ToggleCurrent();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
        {
            SetCurrent(false);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
        {
            SetCurrent(true);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Escape) || SDLInput.IsKeyDown(SDLInput.Key.Backspace) ||
                 SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
        {
            Close();
        }

        return true; // Consume all input while open
    }

    private void Open()
    {
        _isOpen = true;
        _index = 0;
        AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("mod_settings_opened"));
        AnnounceCurrent();
    }

    private void Close()
    {
        _isOpen = false;
        ModSettings.Instance.Save();
        AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("mod_settings_closed"));
    }

    private void AnnounceCurrent()
    {
        if (_index < 0 || _index >= ModSettings.AllSettings.Count) return;
        var def = ModSettings.AllSettings[_index];
        bool value = def.Get(ModSettings.Instance);
        string state = value ? Loc.Get("settings_on") : Loc.Get("settings_off");
        AnnouncementService.Instance.Announce(
            Loc.Get(def.LocKey) + ", " + state + ", " + (_index + 1) + " " + Loc.Get("log_of") + " " + ModSettings.AllSettings.Count);
    }

    private void ToggleCurrent()
    {
        if (_index < 0 || _index >= ModSettings.AllSettings.Count) return;
        var def = ModSettings.AllSettings[_index];
        bool current = def.Get(ModSettings.Instance);
        def.Set(ModSettings.Instance, !current);
        string state = !current ? Loc.Get("settings_on") : Loc.Get("settings_off");
        AnnouncementService.Instance.AnnounceInterrupt(state);
    }

    private void SetCurrent(bool value)
    {
        if (_index < 0 || _index >= ModSettings.AllSettings.Count) return;
        var def = ModSettings.AllSettings[_index];
        bool current = def.Get(ModSettings.Instance);
        if (current == value)
        {
            AnnouncementService.Instance.Announce(value ? Loc.Get("settings_already_on") : Loc.Get("settings_already_off"));
            return;
        }
        def.Set(ModSettings.Instance, value);
        AnnouncementService.Instance.AnnounceInterrupt(value ? Loc.Get("settings_on") : Loc.Get("settings_off"));
    }
}
