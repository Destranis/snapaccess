using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using MelonLoader;

namespace SnapAccess;

/// <summary>
/// Specialized handler for the Play Deck Tray (Quick Switcher).
/// </summary>
public class PlayDeckTrayHandler : IHandler
{
    private class TrayDeck
    {
        public string Name;
        public Button Button;
    }

    private readonly List<TrayDeck> _decks = new List<TrayDeck>();
    private int _focusIndex = -1;
    private Button _editButton;
    private Button _equipButton;
    private Button _closeButton;
    private bool _isActive = false;
    private float _lastScanTime = 0f;

    public bool IsActive => _isActive;

    public bool Update()
    {
        if (!ScanForTray())
        {
            _isActive = false;
            return false;
        }

        _isActive = true;
        ProcessInput();
        return true;
    }

    private bool ScanForTray()
    {
        if (Time.time - _lastScanTime < 1.0f) return _isActive;
        _lastScanTime = Time.time;

        GameObject root = GameObject.Find("PlayDeckTrayView_Landscape(Clone)") ?? GameObject.Find("OverlayCanvas");
        if (root == null || !root.activeInHierarchy) return false;

        // Check if any deck slots exist
        _decks.Clear();
        _editButton = null;
        _equipButton = null;
        _closeButton = null;

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        foreach (var btn in buttons)
        {
            if (btn == null || !btn.gameObject.activeInHierarchy) continue;
            string goName = btn.gameObject.name;

            if (goName == "DeckSlotCell")
            {
                // Read deck name from Text_Name child first (most reliable)
                string label = "";
                try
                {
                    Transform textNameTf = UIHelper.FindChildByName(btn.transform, "Text_Name");
                    if ((Object)(object)textNameTf != (Object)null)
                    {
                        var tmpText = ((Component)textNameTf).GetComponent<Il2CppTMPro.TMP_Text>();
                        if ((Object)(object)tmpText != (Object)null && !string.IsNullOrWhiteSpace(tmpText.text))
                            label = UIHelper.StripRichText(tmpText.text.Trim());
                    }
                }
                catch { }
                if (string.IsNullOrEmpty(label))
                    label = UIHelper.GetButtonLabel(btn);
                // Skip empty/placeholder deck slots
                if (string.IsNullOrEmpty(label) || label.Length < 2
                    || label.Equals("Deck name", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Deck Slot Cell", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("DeckSlotCell", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Deck Name\u200B", StringComparison.OrdinalIgnoreCase))
                    continue;
                _decks.Add(new TrayDeck { Name = label, Button = btn });
            }
            else if (goName.Contains("Edit")) _editButton = btn;
            else if (goName.Contains("Equip")) _equipButton = btn;
            else if (goName == "Close" || goName == "Esc" || goName == "btn_close") _closeButton = btn;
        }

        return _decks.Count > 0;
    }

    private void ProcessInput()
    {
        if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
        {
            MoveFocus(-1);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
        {
            MoveFocus(1);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
        {
            // Read deck details
            if (_focusIndex >= 0 && _focusIndex < _decks.Count)
                ScreenReader.Say("Deck: " + _decks[_focusIndex].Name + ". Enter to select and equip, E to edit.");
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
        {
            ActivateFocused();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.E))
        {
            EditFocusedDeck();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsKeyDown(SDLInput.Key.Escape) ||
                 SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
        {
            Close();
        }
    }

    private void MoveFocus(int dir)
    {
        if (_decks.Count == 0) return;
        _focusIndex = (_focusIndex + dir + _decks.Count) % _decks.Count;
        AnnounceDeck();
    }

    private void AnnounceDeck()
    {
        if (_focusIndex < 0 || _focusIndex >= _decks.Count) return;
        string name = _decks[_focusIndex].Name;
        if (string.IsNullOrEmpty(name) || name == "Deck Slot Cell" || name == "DeckSlotCell")
            name = "Empty Deck Slot";
        ScreenReader.Say("Deck: " + name + ", " + (_focusIndex + 1) + " of " + _decks.Count);
    }

    private void ActivateFocused()
    {
        if (_focusIndex < 0 || _focusIndex >= _decks.Count) return;
        
        ScreenReader.Say("Selecting " + _decks[_focusIndex].Name);
        UIHelper.ClickButton(_decks[_focusIndex].Button);
        
        // After selecting, we almost always want to Equip
        if (_equipButton != null && _equipButton.gameObject.activeInHierarchy)
        {
            ScreenReader.SayQueued("Equipping...");
            UIHelper.ClickButton(_equipButton);
            Close();
        }
    }

    private void EditFocusedDeck()
    {
        if (_focusIndex >= 0 && _focusIndex < _decks.Count)
        {
            // First select the deck
            UIHelper.ClickButton(_decks[_focusIndex].Button);
        }

        if (_editButton != null && _editButton.gameObject.activeInHierarchy)
        {
            ScreenReader.Say("Editing " + (_focusIndex >= 0 && _focusIndex < _decks.Count ? _decks[_focusIndex].Name : "deck"));
            UIHelper.ClickButton(_editButton);
        }
        else
        {
            ScreenReader.Say("Edit button not available.");
        }
    }

    public void Close()
    {
        if (_closeButton != null) UIHelper.ClickButton(_closeButton);
        else UIHelper.SimulateKeyPress(SDLInput.Key.Escape);
        _isActive = false;
    }

    public void AnnounceContext()
    {
        ScreenReader.Say("Deck Selection Tray. " + _decks.Count + " decks available. Enter to select, E to edit.");
        AnnounceDeck();
    }

    public void Reset()
    {
        _isActive = false;
        _decks.Clear();
        _focusIndex = -1;
    }
}
