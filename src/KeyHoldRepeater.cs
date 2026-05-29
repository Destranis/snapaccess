using System;
using UnityEngine;

namespace SnapAccess;

/// <summary>
/// Enables hold-to-repeat for navigation keys.
/// Hold a key: 0.5s initial delay, then repeats every 0.1s.
/// Adapted from AccessibleArena's KeyHoldRepeater pattern.
/// </summary>
public class KeyHoldRepeater
{
    private const float InitialDelay = 0.5f;
    private const float RepeatInterval = 0.1f;

    private SDLInput.Key _heldKey;
    private float _holdTimer;
    private bool _isHolding;

    /// <summary>
    /// Checks if the given key was just pressed or is being held for repeat.
    /// Call this instead of SDLInput.IsKeyDown() for navigation keys.
    /// </summary>
    /// <returns>True if the key was consumed (pressed or repeated).</returns>
    public bool Check(SDLInput.Key key, Action action)
    {
        // Key released — stop holding
        if (_isHolding && _heldKey == key && !SDLInput.IsKeyHeld(key))
        {
            _isHolding = false;
            return false;
        }

        // Key just pressed — execute immediately, start tracking hold
        if (SDLInput.IsKeyDown(key))
        {
            _isHolding = false;
            action();
            _heldKey = key;
            _holdTimer = 0f;
            _isHolding = true;
            return true;
        }

        // Key held — repeat after initial delay
        if (_isHolding && _heldKey == key && SDLInput.IsKeyHeld(key))
        {
            _holdTimer += Time.unscaledDeltaTime;
            if (_holdTimer >= InitialDelay)
            {
                _holdTimer -= RepeatInterval;
                if (_holdTimer < InitialDelay - RepeatInterval)
                    _holdTimer = InitialDelay - RepeatInterval;
                action();
            }
            return true;
        }

        return false;
    }

    /// <summary>Resets the hold state. Call when the handler deactivates or changes context.</summary>
    public void Reset()
    {
        _isHolding = false;
        _holdTimer = 0f;
    }
}
