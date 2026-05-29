using System.Collections.Generic;
using UnityEngine;

namespace SnapAccess;

/// <summary>
/// Per-frame key consumption tracking. Prevents the same key press
/// from being processed by multiple navigators in the same frame.
/// Auto-resets each frame using Time.frameCount.
/// </summary>
public static class InputGuard
{
    private static readonly HashSet<SDLInput.Key> _consumedKeys = new HashSet<SDLInput.Key>();
    private static readonly HashSet<SDLInput.GamepadButton> _consumedButtons = new HashSet<SDLInput.GamepadButton>();
    private static int _lastFrame = -1;

    private static void EnsureFrame()
    {
        int frame = Time.frameCount;
        if (frame != _lastFrame)
        {
            _consumedKeys.Clear();
            _consumedButtons.Clear();
            _lastFrame = frame;
        }
    }

    /// <summary>Mark a key as consumed this frame.</summary>
    public static void ConsumeKey(SDLInput.Key key)
    {
        EnsureFrame();
        _consumedKeys.Add(key);
    }

    /// <summary>Check if a key was already consumed this frame.</summary>
    public static bool IsConsumed(SDLInput.Key key)
    {
        EnsureFrame();
        return _consumedKeys.Contains(key);
    }

    /// <summary>
    /// If the key is down and not yet consumed, consume it and return true.
    /// Otherwise return false.
    /// </summary>
    public static bool GetKeyDownAndConsume(SDLInput.Key key)
    {
        if (SDLInput.IsKeyDown(key) && !IsConsumed(key))
        {
            ConsumeKey(key);
            return true;
        }
        return false;
    }

    /// <summary>Mark a gamepad button as consumed this frame.</summary>
    public static void ConsumeButton(SDLInput.GamepadButton button)
    {
        EnsureFrame();
        _consumedButtons.Add(button);
    }

    /// <summary>Check if a gamepad button was already consumed this frame.</summary>
    public static bool IsButtonConsumed(SDLInput.GamepadButton button)
    {
        EnsureFrame();
        return _consumedButtons.Contains(button);
    }

    /// <summary>
    /// If the button is down and not yet consumed, consume it and return true.
    /// Otherwise return false.
    /// </summary>
    public static bool GetButtonDownAndConsume(SDLInput.GamepadButton button)
    {
        if (SDLInput.IsButtonDown(button) && !IsButtonConsumed(button))
        {
            ConsumeButton(button);
            return true;
        }
        return false;
    }
}
