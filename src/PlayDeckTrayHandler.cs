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
public class PlayDeckTrayHandler : IScreenNavigator
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
    private readonly KeyHoldRepeater _holdRepeater = new KeyHoldRepeater();

    public string NavigatorId => "PlayDeckTray";
    public int Priority => 700;
    public bool IsActive => _isActive;

    public void Update()
    {
        if (!ScanForTray())
        {
            _isActive = false;
            return;
        }

        _isActive = true;
        ProcessInput();
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

        Il2CppArrayBase<Button> buttons = root.GetComponentsInChildren<Button>(true);
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
        if (_holdRepeater.Check(SDLInput.Key.Up, () => MoveFocus(-1))) { }
        else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp)) MoveFocus(-1);
        else if (_holdRepeater.Check(SDLInput.Key.Down, () => MoveFocus(1))) { }
        else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown)) MoveFocus(1);
        else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
        {
            // Read deck details
            if (_focusIndex >= 0 && _focusIndex < _decks.Count)
                AnnouncementService.Instance.Announce(Loc.Get("pdt_deck_info", _decks[_focusIndex].Name));
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Home))
        {
            if (_decks.Count > 0) { _focusIndex = 0; AnnounceDeck(); }
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.End))
        {
            if (_decks.Count > 0) { _focusIndex = _decks.Count - 1; AnnounceDeck(); }
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
        {
            ActivateFocused();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.E) || SDLInput.IsButtonDown(SDLInput.GamepadButton.West))
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
        AnnouncementService.Instance.Announce(Loc.Get("pdt_deck_info", name) + ", " + (_focusIndex + 1) + " " + Loc.Get("log_of") + " " + _decks.Count);
    }

    private void ActivateFocused()
    {
        if (_focusIndex < 0 || _focusIndex >= _decks.Count) return;

        AnnouncementService.Instance.AnnounceInterrupt("Selecting " + _decks[_focusIndex].Name);
        UIHelper.ActivateButton(_decks[_focusIndex].Button);

        // After selecting, we almost always want to Equip
        if (_equipButton != null && _equipButton.gameObject.activeInHierarchy)
        {
            AnnouncementService.Instance.Announce("Equipping...", AnnouncementPriority.Low);
            UIHelper.ActivateButton(_equipButton);
            Close();
        }
    }

    private void EditFocusedDeck()
    {
        if (_focusIndex >= 0 && _focusIndex < _decks.Count)
        {
            // First select the deck
            UIHelper.ActivateButton(_decks[_focusIndex].Button);
        }

        if (_editButton != null && _editButton.gameObject.activeInHierarchy)
        {
            AnnouncementService.Instance.AnnounceInterrupt("Editing " + (_focusIndex >= 0 && _focusIndex < _decks.Count ? _decks[_focusIndex].Name : "deck"));
            UIHelper.ActivateButton(_editButton);
        }
        else
        {
            AnnouncementService.Instance.Announce("Edit button not available.");
        }
    }

    public void Close()
    {
        if (_closeButton != null) UIHelper.ActivateButton(_closeButton);
        else UIHelper.SimulateKeyPress(SDLInput.Key.Escape);
        _isActive = false;
    }

    public void AnnounceContext()
    {
        AnnouncementService.Instance.Announce(Loc.Get("pdt_help", _decks.Count.ToString()), AnnouncementPriority.High);
        if (_focusIndex >= 0 && _focusIndex < _decks.Count)
            AnnounceDeck();
    }

    public void Deactivate()
    {
        _isActive = false;
        _focusIndex = 0;
        _holdRepeater.Reset();
    }

    public void OnSceneChanged(string sceneName)
    {
        _isActive = false;
        _decks.Clear();
        _focusIndex = -1;
    }
}
