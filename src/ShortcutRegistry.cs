using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;

namespace SnapAccess;

/// <summary>
/// Centralized key binding registry. Maps named actions to SDLInput keys.
/// Supports runtime rebinding and JSON persistence.
/// </summary>
public static class ShortcutRegistry
{
    private static readonly Dictionary<string, SDLInput.Key> _bindings = new Dictionary<string, SDLInput.Key>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SDLInput.GamepadButton> _gamepadBindings = new Dictionary<string, SDLInput.GamepadButton>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers all default key bindings.</summary>
    public static void Initialize()
    {
        RegisterDefaults();
        LoadCustomBindings();
        MelonLogger.Msg($"ShortcutRegistry initialized with {_bindings.Count} keyboard + {_gamepadBindings.Count} gamepad bindings");
    }

    /// <summary>Checks if the action's bound key was just pressed this frame.</summary>
    public static bool IsDown(string action)
    {
        if (_bindings.TryGetValue(action, out var key))
            return SDLInput.IsKeyDown(key);
        return false;
    }

    /// <summary>Checks if the action's bound gamepad button was just pressed this frame.</summary>
    public static bool IsButtonDown(string action)
    {
        if (_gamepadBindings.TryGetValue(action, out var btn))
            return SDLInput.IsButtonDown(btn);
        return false;
    }

    /// <summary>Checks if the action's key OR gamepad button was just pressed.</summary>
    public static bool IsActionDown(string action)
    {
        return IsDown(action) || IsButtonDown(action);
    }

    /// <summary>Gets the key name for display in help text.</summary>
    public static string GetKeyName(string action)
    {
        if (_bindings.TryGetValue(action, out var key))
            return key.ToString();
        return action;
    }

    /// <summary>Rebinds an action to a new key at runtime.</summary>
    public static void Rebind(string action, SDLInput.Key key)
    {
        _bindings[action] = key;
    }

    /// <summary>Gets the currently bound key for an action.</summary>
    public static SDLInput.Key GetKey(string action)
    {
        return _bindings.TryGetValue(action, out var key) ? key : (SDLInput.Key)0;
    }

    private static void RegisterDefaults()
    {
        // Global hotkeys
        Bind("debug_toggle", SDLInput.Key.F12);
        Bind("help", SDLInput.Key.F1);
        Bind("repeat_last", SDLInput.Key.F3);
        Bind("mod_settings", SDLInput.Key.F4);
        Bind("game_log", SDLInput.Key.O);

        // Navigation
        Bind("nav_left", SDLInput.Key.Left, SDLInput.GamepadButton.DPadLeft);
        Bind("nav_right", SDLInput.Key.Right, SDLInput.GamepadButton.DPadRight);
        Bind("nav_up", SDLInput.Key.Up, SDLInput.GamepadButton.DPadUp);
        Bind("nav_down", SDLInput.Key.Down, SDLInput.GamepadButton.DPadDown);
        Bind("confirm", SDLInput.Key.Return, SDLInput.GamepadButton.South);
        Bind("back", SDLInput.Key.Backspace, SDLInput.GamepadButton.East);
        Bind("cancel", SDLInput.Key.Escape);
        Bind("home", SDLInput.Key.Home);
        Bind("end", SDLInput.Key.End);
        Bind("tab", SDLInput.Key.Tab);

        // Battlefield
        Bind("hand", SDLInput.Key.C);
        Bind("locations", SDLInput.Key.B);
        Bind("end_turn", SDLInput.Key.E, SDLInput.GamepadButton.Start);
        Bind("turn_info", SDLInput.Key.T);
        Bind("timer", SDLInput.Key.W);
        Bind("energy", SDLInput.Key.A, SDLInput.GamepadButton.LeftShoulder);
        Bind("zone_summary", SDLInput.Key.Z);
        Bind("drawn_cards", SDLInput.Key.D);
        Bind("silence", SDLInput.Key.S);
        Bind("snap", SDLInput.Key.G);
        Bind("retreat", SDLInput.Key.R);
        Bind("tutorial_info", SDLInput.Key.I, SDLInput.GamepadButton.North);
        Bind("tutorial_advance", SDLInput.Key.Space);
        Bind("quickplay_1", SDLInput.Key.Num1);
        Bind("quickplay_2", SDLInput.Key.Num2);
        Bind("quickplay_3", SDLInput.Key.Num3);
    }

    private static void Bind(string action, SDLInput.Key key, SDLInput.GamepadButton? gamepad = null)
    {
        _bindings[action] = key;
        if (gamepad.HasValue)
            _gamepadBindings[action] = gamepad.Value;
    }

    private static void LoadCustomBindings()
    {
        try
        {
            string path = Path.Combine("UserData", "SnapAccess_Keys.json");
            if (!File.Exists(path)) return;

            string json = File.ReadAllText(path);
            // Simple JSON parsing for key overrides: {"action": "KeyName", ...}
            json = json.Trim().TrimStart('{').TrimEnd('}');
            foreach (string pair in json.Split(','))
            {
                string[] kv = pair.Split(':');
                if (kv.Length != 2) continue;
                string action = kv[0].Trim().Trim('"');
                string keyName = kv[1].Trim().Trim('"');
                if (Enum.TryParse<SDLInput.Key>(keyName, true, out var key))
                {
                    _bindings[action] = key;
                    DebugLogger.Log(LogCategory.State, "ShortcutRegistry", $"Custom binding: {action} = {key}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.State, "ShortcutRegistry", $"LoadCustomBindings error: {ex.Message}");
        }
    }
}
