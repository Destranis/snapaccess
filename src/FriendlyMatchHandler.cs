using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Handles the Friendly Match (Battle Mode) screen.
/// Flat navigation: Left/Right between Create and Join.
/// Enter activates directly. No sub-menus.
/// </summary>
public class FriendlyMatchHandler : IHandler
{
    private enum FocusItem { Create, Join }

    private FocusItem _focus = FocusItem.Create;
    private bool _isActive = false;
    private bool _activated = false;
    private bool _waitingForCode = false; // True when user is typing a join code
    private float _lastScanTime = 0f;

    // UI Elements
    private GameObject _root;
    private Button _createButton;
    private Button _joinButton;
    private TMP_InputField _codeInputField;
    private Button _copyButton;
    private Button _shareButton;

    public bool IsActive => _isActive && _activated;

    /// <summary>Explicitly activate this handler (called from MainMenuHandler).</summary>
    public void Activate()
    {
        _activated = true;
        _lastScanTime = 0f;
        _focus = FocusItem.Create;
        _waitingForCode = false;
    }

    public bool Update()
    {
        if (!_activated) return false;

        if (!ScanForScreen())
        {
            _isActive = false;
            return false;
        }

        _isActive = true;
        ProcessInput();
        return true;
    }

    private bool ScanForScreen()
    {
        if (Time.time - _lastScanTime < 1.0f) return _isActive;
        _lastScanTime = Time.time;

        try
        {
            _root = GameObject.Find("BattleModeView")
                ?? GameObject.Find("FriendlyMatchView")
                ?? GameObject.Find("FriendlyBattleView")
                ?? GameObject.Find("BattleModeScreen(Clone)")
                ?? GameObject.Find("FriendlyBattleScreen(Clone)");

            // Search FloatingScreenStagingArea for battle-related screens
            if (_root == null)
            {
                GameObject floating = GameObject.Find("FloatingScreenStagingArea");
                if (floating != null)
                {
                    for (int i = 0; i < floating.transform.childCount; i++)
                    {
                        Transform child = floating.transform.GetChild(i);
                        if (child.gameObject.activeInHierarchy)
                        {
                            string childName = child.gameObject.name;
                            if (childName.Contains("Battle") || childName.Contains("Friendly"))
                            {
                                _root = child.gameObject;
                                break;
                            }
                        }
                    }
                }
            }

            // Last resort: look for create/join buttons anywhere
            if (_root == null)
            {
                Button[] allBtns = Object.FindObjectsOfType<Button>();
                foreach (var b in allBtns)
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;
                    string n = b.gameObject.name;
                    if (n == "btn_create" || n == "btn_join" || n == "btn_createMatch" || n == "btn_joinMatch"
                        || n == "CreateButton" || n == "JoinButton")
                    {
                        _root = b.transform.root.gameObject;
                        break;
                    }
                }
            }

            if (_root == null || !_root.activeInHierarchy) return false;

            _createButton = FindButton("btn_create", "Create");
            _joinButton = FindButton("btn_join", "Join");
            _codeInputField = _root.GetComponentInChildren<TMP_InputField>();
            _copyButton = FindButton("btn_copy", "Copy");
            _shareButton = FindButton("btn_share", "Share");

            DebugLogger.Log(LogCategory.Handler, "FriendlyMatch",
                $"Root={_root.name}, Create={_createButton != null}, Join={_joinButton != null}, " +
                $"Input={_codeInputField != null}, Copy={_copyButton != null}");

            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "FriendlyMatch", "Scan failed: " + ex.Message);
            return false;
        }
    }

    private void ProcessInput()
    {
        // When waiting for code input, only intercept Enter and Backspace
        if (_waitingForCode)
        {
            HandleCodeInput();
            return;
        }

        if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
        {
            _focus = FocusItem.Create;
            AnnounceFocus();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
        {
            _focus = FocusItem.Join;
            AnnounceFocus();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
        {
            ReadDetails();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
        {
            ActivateFocused();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
        {
            CloseView();
        }
    }

    private void HandleCodeInput()
    {
        if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
        {
            _waitingForCode = false;
            ScreenReader.Say("Cancelled. Back to Friendly Battle.");
            AnnounceFocus();
            return;
        }

        if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
        {
            // Read what was typed
            string code = "";
            if (_codeInputField != null)
            {
                try { code = _codeInputField.text; } catch { }
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                ScreenReader.Say("No code entered. Type or paste a code first, then press Enter.");
                return;
            }

            _waitingForCode = false;
            if (_joinButton != null)
            {
                ScreenReader.Say("Joining match with code " + code);
                UIHelper.ClickButtonWithFallback(_joinButton);
            }
            else
            {
                ScreenReader.Say("Join button not found.");
            }
            return;
        }

        // Down: read current input content
        if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
        {
            string code = "";
            if (_codeInputField != null)
            {
                try { code = _codeInputField.text; } catch { }
            }
            if (string.IsNullOrWhiteSpace(code))
                ScreenReader.Say("Code field is empty. Type or paste a code.");
            else
                ScreenReader.Say("Current code: " + code);
        }
        // All other keys pass through to the input field
    }

    private void AnnounceFocus()
    {
        string name = _focus switch
        {
            FocusItem.Create => "Create Match, 1 of 2",
            FocusItem.Join => "Join Match, 2 of 2",
            _ => ""
        };
        ScreenReader.Say(name);
    }

    private void ReadDetails()
    {
        if (_focus == FocusItem.Create)
        {
            // Check if a code was already generated
            string existingCode = ReadGeneratedCode();
            if (!string.IsNullOrEmpty(existingCode))
                ScreenReader.Say("Your match code: " + existingCode + ". Press Enter to copy.");
            else
                ScreenReader.Say("Press Enter to create a match and generate a code for your friend.");
        }
        else
        {
            string code = "";
            if (_codeInputField != null)
            {
                try { code = _codeInputField.text; } catch { }
            }
            if (!string.IsNullOrWhiteSpace(code))
                ScreenReader.Say("Code entered: " + code + ". Press Enter to join.");
            else
                ScreenReader.Say("Press Enter, then type or paste a match code from your friend.");
        }
    }

    private void ActivateFocused()
    {
        if (_focus == FocusItem.Create)
        {
            // Check if code already exists (already created)
            string existingCode = ReadGeneratedCode();
            if (!string.IsNullOrEmpty(existingCode))
            {
                // Copy to clipboard
                CopyToClipboard(existingCode);
                ScreenReader.Say("Code copied: " + existingCode);
            }
            else if (_createButton != null)
            {
                ScreenReader.Say("Creating match...");
                UIHelper.ClickButtonWithFallback(_createButton);
                // After creating, try to read the generated code
                MelonLoader.MelonCoroutines.Start(AnnounceGeneratedCodeDelayed());
            }
            else
            {
                ScreenReader.Say("Create button not found.");
            }
        }
        else // Join
        {
            // Focus the input field so user can type
            if (_codeInputField != null)
            {
                try
                {
                    _codeInputField.ActivateInputField();
                    _codeInputField.Select();
                }
                catch { }
                _waitingForCode = true;
                ScreenReader.Say("Type or paste match code, then press Enter to join. Backspace to cancel.");
            }
            else
            {
                ScreenReader.Say("Code input field not found.");
            }
        }
    }

    private string ReadGeneratedCode()
    {
        if (_root == null) return "";
        try
        {
            // Look for text elements that might contain the generated code
            TMP_Text[] texts = _root.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in texts)
            {
                if (t == null) continue;
                string goName = ((Object)((Component)t).gameObject).name;
                string val = UIHelper.StripRichText(t.text);
                if (string.IsNullOrEmpty(val) || val.Length < 4) continue;

                // Match code is typically displayed in elements named "code", "matchCode", "text_code", etc.
                if (goName.Contains("code", StringComparison.OrdinalIgnoreCase)
                    || goName.Contains("Code", StringComparison.OrdinalIgnoreCase))
                {
                    // Skip if it's a label like "Enter Code"
                    if (val.Contains("Enter") || val.Contains("enter") || val.Contains("Type"))
                        continue;
                    return val;
                }
            }

            // Fallback: check if the input field has a code value
            if (_codeInputField != null)
            {
                string inputVal = "";
                try { inputVal = _codeInputField.text; } catch { }
                if (!string.IsNullOrWhiteSpace(inputVal) && inputVal.Length >= 4)
                    return inputVal;
            }
        }
        catch { }
        return "";
    }

    private System.Collections.IEnumerator AnnounceGeneratedCodeDelayed()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            yield return new WaitForSeconds(0.5f);
            _lastScanTime = 0f; // Force rescan
            ScanForScreen();
            string code = ReadGeneratedCode();
            if (!string.IsNullOrEmpty(code))
            {
                CopyToClipboard(code);
                ScreenReader.Say("Match code: " + code + ". Copied to clipboard. Share with your friend.");
                yield break;
            }
        }
        ScreenReader.Say("Match created. Check screen for your code.");
    }

    private Button FindButton(string goNamePattern, string labelKeyword)
    {
        if (_root == null) return null;
        Button[] buttons = _root.GetComponentsInChildren<Button>(true);

        // First: exact name match
        foreach (var b in buttons)
        {
            if (b == null || !b.gameObject.activeInHierarchy) continue;
            if (b.gameObject.name.Contains(goNamePattern, StringComparison.OrdinalIgnoreCase))
                return b;
        }
        // Second: label text match
        foreach (var b in buttons)
        {
            if (b == null || !b.gameObject.activeInHierarchy) continue;
            string label = UIHelper.GetButtonLabel(b);
            if (!string.IsNullOrEmpty(label) && label.Contains(labelKeyword, StringComparison.OrdinalIgnoreCase))
                return b;
        }
        return null;
    }

    private void CloseView()
    {
        bool closed = false;
        try
        {
            // Find the Escape_BackButton under FloatingScreenContainer
            GameObject escBtn = GameObject.Find("Escape_BackButton");
            if (escBtn != null)
            {
                Button b = escBtn.GetComponentInChildren<Button>(true);
                if (b != null) { UIHelper.ClickButton(b); closed = true; }
            }
        }
        catch { }
        if (!closed) UIHelper.SimulateKeyPress(SDLInput.Key.Escape);
        _activated = false;
        _isActive = false;
        _waitingForCode = false;
        ScreenReader.Say("Closing Friendly Battle.");
    }

    public void AnnounceContext()
    {
        ScreenReader.Say("Friendly Battle. Left and Right to switch between Create and Join.");
        AnnounceFocus();
    }

    public void Reset()
    {
        _isActive = false;
        _activated = false;
        _waitingForCode = false;
    }

    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);

    private static void CopyToClipboard(string text)
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero)) return;
            EmptyClipboard();
            var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
            var hGlobal = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)bytes.Length);
            var ptr = GlobalLock(hGlobal);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            GlobalUnlock(hGlobal);
            SetClipboardData(13 /* CF_UNICODETEXT */, hGlobal);
            CloseClipboard();
        }
        catch { }
    }
}
