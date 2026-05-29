using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Il2CppTMPro;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppCubeUnity.App.Game;
using Il2CppCubeUnity.App.Login;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Handles the initial login, Terms of Service, and age gate screens.
/// Active before the main Navigator appears. Scans for all interactive elements
/// (Buttons and Toggles) and provides keyboard/gamepad navigation.
/// </summary>
public class LoginHandler : IScreenNavigator
{
    private enum ElementType { Button, Toggle, InputField }

    private struct InteractiveElement
    {
        public ElementType Type;
        public Button Button;
        public Toggle Toggle;
        public TMP_InputField InputField;
        public string Label;
    }

    private readonly List<InteractiveElement> _elements = new List<InteractiveElement>();
    private readonly List<string> _screenTexts = new List<string>();

    private int _focusIndex = -1;
    private int _textReadIndex = -1;
    private bool _active = false;
    private bool _announced = false;
    private float _lastScanTime = 0f;
    private int _lastElementHash = 0;
    private float _inputBlockUntil = 0f;
    private float _lastNavCheck = 0f;
    private bool _navVisible = false;
    private GameObject _scanRoot = null;

    // Text input mode
    private bool _textInputMode = false;
    private TMP_InputField _activeInputField = null;
    private string _lastInputText = "";

    private const float ScanInterval = 0.8f;

    public string NavigatorId => "Login";
    public int Priority => 1000;
    public bool IsActive => _active;

    public void Update()
    {
        // Cache Navigator visibility check (expensive FindObjectOfType)
        if (Time.time - _lastNavCheck >= 1f)
        {
            _lastNavCheck = Time.time;
            _navVisible = CheckNavigatorVisible();
        }

        // Don't run if the main Navigator is visible — hand off to MainMenuHandler
        if (_navVisible)
        {
            if (_active)
            {
                _active = false;
                DebugLogger.Log(LogCategory.State, "LoginHandler", "Navigator appeared, deactivating");
            }
            return;
        }

        // Periodic scan for login screen elements
        if (Time.time - _lastScanTime >= ScanInterval)
        {
            _lastScanTime = Time.time;
            ScanForElements();
        }

        if (!_active || _elements.Count == 0)
            return;

        // Text input mode: user is typing in a field
        if (_textInputMode)
        {
            ProcessTextInput();
            return;
        }

        ProcessInput();
    }

    public void AnnounceContext()
    {
        AnnouncementService.Instance.Announce(Loc.Get("login_help"), AnnouncementPriority.High);
        if (_elements.Count == 0)
        {
            AnnouncementService.Instance.Announce(Loc.Get("login_no_elements"));
            return;
        }

        if (_focusIndex >= 0 && _focusIndex < _elements.Count)
        {
            var elem = _elements[_focusIndex];
            string stateInfo = GetElementStateInfo(elem);
            AnnouncementService.Instance.Announce(Loc.Get("login_focused", elem.Label + stateInfo, _focusIndex + 1, _elements.Count));
        }
    }

    public void Deactivate()
    {
        _active = false;
        _announced = false;
        _textInputMode = false;
        _activeInputField = null;
        _lastInputText = "";
    }

    public void OnSceneChanged(string sceneName)
    {
        _elements.Clear();
        _screenTexts.Clear();
        _focusIndex = -1;
        _textReadIndex = -1;
        _active = false;
        _announced = false;
        _lastElementHash = 0;
        _textInputMode = false;
        _activeInputField = null;
        _lastInputText = "";
        _scanRoot = null;
        // Force fresh Navigator check on next Update after scene change
        _lastNavCheck = 0f;
        _navVisible = false;
        _lastScanTime = 0f;
    }

    private void ScanForElements()
    {
        // Look for LoginView or LoginPopupView to know we're on the login screen
        bool hasLoginView = false;
        try
        {
            var loginView = UIHelper.FindComponent<LoginView>();
            if ((Object)(object)loginView != (Object)null
                && ((Component)loginView).gameObject.activeInHierarchy)
            {
                hasLoginView = true;
            }
        }
        catch { }

        if (!hasLoginView)
        {
            try
            {
                var popupView = UIHelper.FindComponent<LoginPopupView>();
                if ((Object)(object)popupView != (Object)null
                    && ((Component)popupView).gameObject.activeInHierarchy)
                {
                    hasLoginView = true;
                }
            }
            catch { }
        }

        // Also detect Terms of Service / consent / age gate screens
        // These appear as dialog widgets before the Navigator exists.
        // Must do a FRESH Navigator check here (not cached) to avoid false positives
        // when the Play scene has just loaded but the cache hasn't updated yet.
        bool hasConsentDialog = false;
        if (!hasLoginView)
        {
            try
            {
                // If a GameView is active, we're in a match — don't detect login dialogs
                // (the "So Close!" purchase popup in post-game is NOT a login screen)
                var gameView = UIHelper.FindComponent<GameView>();
                if ((Object)(object)gameView != (Object)null
                    && ((Component)gameView).gameObject.activeInHierarchy)
                {
                    // In game — skip consent dialog detection entirely
                }
                else
                {
                // Fresh Navigator check — the cached _navVisible can be stale after scene changes
                bool navVisibleNow = CheckNavigatorVisible();
                if (navVisibleNow)
                {
                    // Navigator is visible — this is NOT a login screen, bail out
                    _navVisible = true;
                    _lastNavCheck = Time.time;
                }
                else if (!navVisibleNow)
                {
                    // Only consider it a consent/login dialog if we find specific login-related UI,
                    // not just any Toggle (the play screen has toggles too).
                    // Look for: Canvas-Dialogs with active WidgetContainer, or LoginUI
                    GameObject loginUI = GameObject.Find("LoginUI(Clone)");
                    if (loginUI != null && loginUI.activeInHierarchy)
                    {
                        hasConsentDialog = true;
                    }

                    if (!hasConsentDialog)
                    {
                        // Check for a modal dialog overlay (consent, age gate, name entry)
                        GameObject dialogCanvas = GameObject.Find("Canvas-Dialogs");
                        if ((Object)(object)dialogCanvas != (Object)null)
                        {
                            Transform dialogPanel = dialogCanvas.transform.Find("DialogPanel");
                            if ((Object)(object)dialogPanel != (Object)null)
                            {
                                for (int i = 0; i < dialogPanel.childCount; i++)
                                {
                                    Transform child = dialogPanel.GetChild(i);
                                    if (child != null && child.gameObject.activeInHierarchy &&
                                        child.gameObject.name.Contains("WidgetContainer"))
                                    {
                                        hasConsentDialog = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (!hasConsentDialog)
                    {
                        // Check for toggles that are direct children of login-related containers
                        // (not random play screen toggles)
                        Il2CppArrayBase<Toggle> toggles = Object.FindObjectsOfType<Toggle>();
                        if (toggles != null)
                        {
                            for (int i = 0; i < toggles.Count; i++)
                            {
                                if ((Object)(object)toggles[i] == (Object)null) continue;
                                if (!((Component)toggles[i]).gameObject.activeInHierarchy) continue;
                                // Only count toggles that are under LoginUI or Canvas-Dialogs
                                string path = UIHelper.GetGameObjectPath(((Component)toggles[i]).gameObject);
                                if (path.Contains("LoginUI") || path.Contains("Canvas-Dialogs") ||
                                    path.Contains("ConsentView") || path.Contains("AgeGate"))
                                {
                                    hasConsentDialog = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                } // end else (not in game)
            }
            catch { }
        }

        if (!hasLoginView && !hasConsentDialog)
        {
            if (_active)
            {
                _active = false;
                _announced = false;
                _elements.Clear();
                _lastElementHash = 0;
                DebugLogger.Log(LogCategory.State, "LoginHandler", "Login screen no longer detected");
            }
            return;
        }

        // Scan for interactive elements ONLY under the detected login container
        // to avoid picking up play screen buttons in the background
        List<InteractiveElement> found = new List<InteractiveElement>();

        try
        {
            // Determine the scan root — only scan under login-related containers
            GameObject scanRoot = null;
            if (hasLoginView)
            {
                // Try LoginUI first, then fall back to LoginView's parent canvas
                scanRoot = GameObject.Find("LoginUI(Clone)");
                if (scanRoot == null)
                {
                    var loginView = UIHelper.FindComponent<LoginView>();
                    if ((Object)(object)loginView != (Object)null)
                        scanRoot = ((Component)loginView).gameObject;
                }
            }
            if (scanRoot == null && hasConsentDialog)
            {
                // Scan under Canvas-Dialogs (contains WidgetContainer modals)
                scanRoot = GameObject.Find("Canvas-Dialogs");
            }

            if (scanRoot == null)
            {
                DebugLogger.Log(LogCategory.Handler, "LoginHandler", "No scan root found");
                _active = false;
                return;
            }

            _scanRoot = scanRoot;
            DebugLogger.Log(LogCategory.Handler, "LoginHandler",
                "Scanning under: " + scanRoot.name);

            // Find buttons under scan root
            Il2CppArrayBase<Button> buttons = scanRoot.GetComponentsInChildren<Button>(false);
            if (buttons != null)
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    Button btn = buttons[i];
                    if ((Object)(object)btn == (Object)null) continue;
                    if (!((Component)btn).gameObject.activeInHierarchy) continue;
                    if (!((Selectable)btn).interactable) continue;

                    string label = UIHelper.GetButtonLabel(btn);
                    if (IsJunkElement(label)) continue;

                    found.Add(new InteractiveElement
                    {
                        Type = ElementType.Button,
                        Button = btn,
                        Label = label
                    });
                }
            }

            // Find toggles under scan root (e.g., "I agree" checkboxes)
            Il2CppArrayBase<Toggle> toggles = scanRoot.GetComponentsInChildren<Toggle>(false);
            if (toggles != null)
            {
                for (int i = 0; i < toggles.Count; i++)
                {
                    Toggle toggle = toggles[i];
                    if ((Object)(object)toggle == (Object)null) continue;
                    if (!((Component)toggle).gameObject.activeInHierarchy) continue;
                    if (!((Selectable)toggle).interactable) continue;

                    // Skip toggles that are part of a button (e.g., toggle inside a button)
                    if (((Component)toggle).GetComponentInParent<Button>() != null) continue;

                    string label = GetToggleLabel(toggle);
                    if (IsJunkElement(label)) continue;

                    found.Add(new InteractiveElement
                    {
                        Type = ElementType.Toggle,
                        Toggle = toggle,
                        Label = label
                    });
                }
            }

            // Find text input fields under scan root (e.g., "Enter your name")
            Il2CppArrayBase<TMP_InputField> inputFields = scanRoot.GetComponentsInChildren<TMP_InputField>(false);
            if (inputFields != null)
            {
                for (int i = 0; i < inputFields.Count; i++)
                {
                    TMP_InputField field = inputFields[i];
                    if ((Object)(object)field == (Object)null) continue;
                    if (!((Component)field).gameObject.activeInHierarchy) continue;
                    if (!((Selectable)field).interactable) continue;

                    string label = GetInputFieldLabel(field);

                    found.Add(new InteractiveElement
                    {
                        Type = ElementType.InputField,
                        InputField = field,
                        Label = label
                    });
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "LoginHandler", "Scan failed: " + ex.Message);
        }

        // Sort by vertical screen position (top to bottom) so elements follow visual layout
        found.Sort((a, b) =>
        {
            try
            {
                float yA = GetElementScreenY(a);
                float yB = GetElementScreenY(b);
                // Higher Y = higher on screen, so sort descending
                return yB.CompareTo(yA);
            }
            catch { return 0; }
        });

        // Check if elements changed
        int newHash = ComputeHash(found);
        if (newHash == _lastElementHash && _active)
            return; // No change

        _lastElementHash = newHash;
        _elements.Clear();
        _elements.AddRange(found);

        if (_elements.Count > 0)
        {
            _active = true;
            if (_focusIndex < 0 || _focusIndex >= _elements.Count)
                _focusIndex = 0;

            CollectScreenTexts();

            DebugLogger.Log(LogCategory.Handler, "LoginHandler",
                $"Found {_elements.Count} elements on login screen");

            if (!_announced)
            {
                _announced = true;
                AnnounceLoginScreen();
            }
            else
            {
                // Re-announce on element change (e.g., new dialog appeared)
                AnnounceLoginScreen();
            }
        }
        else
        {
            _active = false;
        }
    }

    private void AnnounceLoginScreen()
    {
        // Read screen text first
        if (_screenTexts.Count > 0)
        {
            string fullText = string.Join(". ", _screenTexts);
            // Limit length to avoid overwhelming the user
            if (fullText.Length > 500)
                fullText = fullText.Substring(0, 500) + "...";
            AnnouncementService.Instance.Announce(fullText, AnnouncementPriority.High);
        }

        // Then announce first focused element
        if (_focusIndex >= 0 && _focusIndex < _elements.Count)
        {
            var elem = _elements[_focusIndex];
            string stateInfo = GetElementStateInfo(elem);
            AnnouncementService.Instance.Announce(Loc.Get("login_element_focus",
                elem.Label + stateInfo, _focusIndex + 1, _elements.Count), AnnouncementPriority.Low);
        }
    }

    private void CollectScreenTexts()
    {
        _screenTexts.Clear();
        _textReadIndex = -1;

        try
        {
            if ((Object)(object)_scanRoot == (Object)null) return;
            Il2CppArrayBase<TMP_Text> texts = _scanRoot.GetComponentsInChildren<TMP_Text>(false);
            if (texts == null) return;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text tmp = texts[i];
                if ((Object)(object)tmp == (Object)null) continue;
                if (!((Component)tmp).gameObject.activeInHierarchy) continue;

                string raw = tmp.text;
                if (string.IsNullOrWhiteSpace(raw)) continue;

                string text = UIHelper.StripRichText(raw.Trim());
                if (text.Length < 3) continue;
                if (seen.Contains(text)) continue;

                // Skip loading/junk text
                if (text.Contains("{Missing", StringComparison.OrdinalIgnoreCase)) continue;
                if (text.Contains("<sprite", StringComparison.OrdinalIgnoreCase)) continue;
                if (text.Contains("img ", StringComparison.OrdinalIgnoreCase)) continue;

                // Skip if the text is just a label we already have as an element
                bool isElementLabel = false;
                foreach (var elem in _elements)
                {
                    if (text.Equals(elem.Label, StringComparison.OrdinalIgnoreCase))
                    {
                        isElementLabel = true;
                        break;
                    }
                }
                if (isElementLabel) continue;

                seen.Add(text);
                _screenTexts.Add(text);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "LoginHandler", "CollectScreenTexts failed: " + ex.Message);
        }
    }

    private void ProcessInput()
    {
        if (Time.time < _inputBlockUntil) return;
        if (_elements.Count == 0) return;

        // Left/Right: navigate elements
        if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
        {
            MoveFocus(-1);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
        {
            MoveFocus(1);
        }
        // Down: read next screen text
        else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
        {
            ReadNextScreenText();
        }
        // Up: read previous screen text
        else if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
        {
            ReadPreviousScreenText();
        }
        // Enter/Space: activate focused element
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsKeyDown(SDLInput.Key.Space)
            || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
        {
            ActivateFocused();
        }
    }

    private void MoveFocus(int direction)
    {
        CleanupDestroyedElements();
        if (_elements.Count == 0) return;

        _focusIndex += direction;
        if (_focusIndex >= _elements.Count) _focusIndex = 0;
        else if (_focusIndex < 0) _focusIndex = _elements.Count - 1;

        _textReadIndex = -1;

        var elem = _elements[_focusIndex];
        string stateInfo = GetElementStateInfo(elem);
        AnnouncementService.Instance.Announce(Loc.Get("login_element_focus",
            elem.Label + stateInfo, _focusIndex + 1, _elements.Count));
    }

    private void ActivateFocused()
    {
        CleanupDestroyedElements();
        if (_focusIndex < 0 || _focusIndex >= _elements.Count)
        {
            AnnouncementService.Instance.Announce(Loc.Get("dialog_no_focus"));
            return;
        }

        var elem = _elements[_focusIndex];

        switch (elem.Type)
        {
            case ElementType.Button:
                DebugLogger.LogInput("Enter", "Login: clicking " + elem.Label);
                AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("dialog_activating", elem.Label));
                if (!UIHelper.ClickButton(elem.Button))
                    UIHelper.SimulateMouseClick(((Component)elem.Button).gameObject);
                // Force rescan after action
                _lastElementHash = 0;
                _inputBlockUntil = Time.time + 0.5f;
                break;

            case ElementType.Toggle:
                DebugLogger.LogInput("Enter", "Login: toggling " + elem.Label);
                try
                {
                    elem.Toggle.isOn = !elem.Toggle.isOn;
                    string newState = elem.Toggle.isOn ? "checked" : "unchecked";
                    AnnouncementService.Instance.AnnounceInterrupt(elem.Label + ", " + newState);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "LoginHandler",
                        "Toggle failed, trying mouse: " + ex.Message);
                    UIHelper.SimulateMouseClick(((Component)elem.Toggle).gameObject);
                    AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("dialog_activating", elem.Label));
                }
                // Force rescan
                _lastElementHash = 0;
                _inputBlockUntil = Time.time + 0.3f;
                break;

            case ElementType.InputField:
                DebugLogger.LogInput("Enter", "Login: entering text field " + elem.Label);
                _activeInputField = elem.InputField;
                EnterTextInputMode(elem.Label);
                break;
        }
    }

    private void ReadNextScreenText()
    {
        if (_screenTexts.Count == 0)
        {
            AnnouncementService.Instance.Announce(Loc.Get("dialog_no_text"));
            return;
        }
        _textReadIndex++;
        if (_textReadIndex >= _screenTexts.Count)
        {
            _textReadIndex = _screenTexts.Count - 1;
            AnnouncementService.Instance.Announce(Loc.Get("dialog_end_of_text"));
            return;
        }
        AnnouncementService.Instance.Announce(Loc.Get("dialog_text_line",
            _screenTexts[_textReadIndex], _textReadIndex + 1, _screenTexts.Count));
    }

    private void ReadPreviousScreenText()
    {
        if (_screenTexts.Count == 0) return;
        _textReadIndex--;
        if (_textReadIndex < 0) _textReadIndex = 0;
        AnnouncementService.Instance.Announce(Loc.Get("dialog_text_line",
            _screenTexts[_textReadIndex], _textReadIndex + 1, _screenTexts.Count));
    }

    private string GetToggleLabel(Toggle toggle)
    {
        try
        {
            // Try reading text from children
            Il2CppArrayBase<TMP_Text> texts = ((Component)toggle).GetComponentsInChildren<TMP_Text>(false);
            if (texts != null)
            {
                string best = "";
                for (int i = 0; i < texts.Count; i++)
                {
                    TMP_Text tmp = texts[i];
                    if ((Object)(object)tmp == (Object)null) continue;
                    string text = UIHelper.StripRichText((tmp.text ?? "").Trim());
                    if (text.Length > best.Length)
                        best = text;
                }
                if (best.Length >= 2) return best;
            }

            // Try parent/sibling text
            Transform parent = ((Component)toggle).transform.parent;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform sibling = parent.GetChild(i);
                    if (sibling == ((Component)toggle).transform) continue;
                    TMP_Text tmp = sibling.GetComponentInChildren<TMP_Text>(false);
                    if ((Object)(object)tmp != (Object)null)
                    {
                        string text = UIHelper.StripRichText((tmp.text ?? "").Trim());
                        if (text.Length >= 3) return text;
                    }
                }

                // Try text on the parent level
                string allText = UIHelper.GetAllText(parent.gameObject);
                if (!string.IsNullOrEmpty(allText) && allText.Length >= 3)
                    return allText;
            }

            return UIHelper.CleanGameObjectName(((Object)((Component)toggle).gameObject).name);
        }
        catch
        {
            return "Checkbox";
        }
    }

    private string GetElementStateInfo(InteractiveElement elem)
    {
        if (elem.Type == ElementType.Toggle)
        {
            try
            {
                if ((Object)(object)elem.Toggle != (Object)null)
                    return elem.Toggle.isOn ? ", checked" : ", unchecked";
            }
            catch { }
        }
        else if (elem.Type == ElementType.InputField)
        {
            try
            {
                if ((Object)(object)elem.InputField != (Object)null)
                {
                    string text = elem.InputField.text;
                    if (!string.IsNullOrEmpty(text))
                        return ", current text: " + text + ", press Enter to edit";
                    return ", empty, press Enter to type";
                }
            }
            catch { }
        }
        return "";
    }

    private string GetInputFieldLabel(TMP_InputField field)
    {
        try
        {
            // Try placeholder text
            if (field.placeholder != null)
            {
                string placeholder = UIHelper.StripRichText(((TMP_Text)field.placeholder).text);
                if (!string.IsNullOrEmpty(placeholder) && placeholder.Length >= 2)
                    return placeholder;
            }
        }
        catch { }

        try
        {
            // Try sibling/parent text for context
            Transform parent = ((Component)field).transform.parent;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform sibling = parent.GetChild(i);
                    if (sibling == ((Component)field).transform) continue;
                    TMP_Text tmp = sibling.GetComponentInChildren<TMP_Text>(false);
                    if ((Object)(object)tmp != (Object)null)
                    {
                        string text = UIHelper.StripRichText((tmp.text ?? "").Trim());
                        if (text.Length >= 3) return text;
                    }
                }
            }
        }
        catch { }

        return UIHelper.CleanGameObjectName(((Object)((Component)field).gameObject).name);
    }

    private void EnterTextInputMode(string fieldName)
    {
        try
        {
            _activeInputField.ActivateInputField();
            _activeInputField.Select();
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "LoginHandler", "Failed to activate input field: " + ex.Message);
        }

        _textInputMode = true;
        _lastInputText = "";
        try { _lastInputText = _activeInputField.text ?? ""; } catch { }
        AnnouncementService.Instance.Announce(Loc.Get("dialog_editing", fieldName));
        DebugLogger.Log(LogCategory.Handler, "LoginHandler", "Entered text input mode: " + fieldName);
    }

    private void ProcessTextInput()
    {
        if ((Object)(object)_activeInputField == (Object)null)
        {
            _textInputMode = false;
            return;
        }

        // Enter exits text input mode
        if (SDLInput.IsKeyDown(SDLInput.Key.Return))
        {
            _textInputMode = false;
            string finalText = "";
            try { finalText = _activeInputField.text ?? ""; } catch { }
            try { _activeInputField.DeactivateInputField(); } catch { }

            if (string.IsNullOrEmpty(finalText))
                AnnouncementService.Instance.Announce(Loc.Get("dialog_done_editing_empty"));
            else
                AnnouncementService.Instance.Announce(Loc.Get("dialog_done_editing", finalText));

            DebugLogger.Log(LogCategory.Handler, "LoginHandler", "Exited text input mode. Text: " + finalText);
            _inputBlockUntil = Time.time + 0.3f;
            return;
        }

        // Escape also exits
        if (SDLInput.IsKeyDown(SDLInput.Key.Escape))
        {
            _textInputMode = false;
            try { _activeInputField.DeactivateInputField(); } catch { }
            AnnouncementService.Instance.Announce(Loc.Get("dialog_editing_cancelled"));
            _inputBlockUntil = Time.time + 0.3f;
            return;
        }

        // Track text changes and speak new characters
        try
        {
            string currentText = _activeInputField.text ?? "";
            if (currentText != _lastInputText)
            {
                if (currentText.Length > _lastInputText.Length)
                {
                    string added = currentText.Substring(_lastInputText.Length);
                    AnnouncementService.Instance.Announce(added);
                }
                else if (currentText.Length < _lastInputText.Length)
                {
                    AnnouncementService.Instance.Announce(Loc.Get("dialog_char_deleted"));
                }
                _lastInputText = currentText;
            }
        }
        catch { }
    }

    private bool CheckNavigatorVisible()
    {
        try
        {
            var nav = UIHelper.FindComponent<Il2CppCubeUnity.App.Navigator.Navigator>();
            if ((Object)(object)nav != (Object)null && nav.IsShown
                && ((Component)nav).gameObject.activeInHierarchy)
                return true;
        }
        catch { }
        return false;
    }

    private bool IsJunkElement(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return true;
        if (label.Length < 2) return true;
        // Common junk labels on login screens
        if (label.Equals("Background", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Equals("Background Panel", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Contains("Background", StringComparison.OrdinalIgnoreCase) && label.Contains("Panel", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Equals("BG", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Equals("Blocker", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Equals("Overlay", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Equals("Button", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Contains("Backing", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Contains("Glass", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Contains("Shadow", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Contains("catcher", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Contains("blocker", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void CleanupDestroyedElements()
    {
        for (int i = _elements.Count - 1; i >= 0; i--)
        {
            try
            {
                var elem = _elements[i];
                bool destroyed;
                switch (elem.Type)
                {
                    case ElementType.Button:
                        destroyed = (Object)(object)elem.Button == (Object)null || !((Component)elem.Button).gameObject.activeInHierarchy;
                        break;
                    case ElementType.Toggle:
                        destroyed = (Object)(object)elem.Toggle == (Object)null || !((Component)elem.Toggle).gameObject.activeInHierarchy;
                        break;
                    case ElementType.InputField:
                        destroyed = (Object)(object)elem.InputField == (Object)null || !((Component)elem.InputField).gameObject.activeInHierarchy;
                        break;
                    default:
                        destroyed = true;
                        break;
                }
                if (destroyed) _elements.RemoveAt(i);
            }
            catch
            {
                _elements.RemoveAt(i);
            }
        }
        if (_focusIndex >= _elements.Count)
            _focusIndex = (_elements.Count > 0) ? (_elements.Count - 1) : -1;
    }

    /// <summary>Returns the screen-space Y position of an element for sorting (higher = top of screen).</summary>
    private float GetElementScreenY(InteractiveElement elem)
    {
        try
        {
            Transform t;
            switch (elem.Type)
            {
                case ElementType.Button: t = ((Component)elem.Button).transform; break;
                case ElementType.Toggle: t = ((Component)elem.Toggle).transform; break;
                case ElementType.InputField: t = ((Component)elem.InputField).transform; break;
                default: return 0f;
            }
            RectTransform rt = t.GetComponent<RectTransform>();
            if ((Object)(object)rt != (Object)null)
                return rt.position.y;
            return t.position.y;
        }
        catch { return 0f; }
    }

    private int ComputeHash(List<InteractiveElement> elements)
    {
        if (elements.Count == 0) return 0;
        int hash = elements.Count;
        foreach (var elem in elements)
        {
            try
            {
                GameObject go;
                switch (elem.Type)
                {
                    case ElementType.Button: go = ((Component)elem.Button).gameObject; break;
                    case ElementType.Toggle: go = ((Component)elem.Toggle).gameObject; break;
                    case ElementType.InputField: go = ((Component)elem.InputField).gameObject; break;
                    default: continue;
                }
                hash = hash * 31 + ((Object)go).GetInstanceID();
            }
            catch { }
        }
        return hash;
    }
}
