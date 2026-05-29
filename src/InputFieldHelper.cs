using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Tracks focused input fields and announces character-by-character changes.
/// Adapted from AccessibleArena's InputFieldEditHelper pattern.
/// When an input field is focused, typed/deleted characters are announced,
/// and Left/Right arrow keys read the character at cursor position.
/// </summary>
public class InputFieldHelper
{
    private TMP_InputField _activeField;
    private string _prevText = "";
    private int _prevCaretPos = 0;
    private bool _wasActive = false;

    /// <summary>Whether an input field is currently being edited.</summary>
    public bool IsEditing => (Object)(object)_activeField != (Object)null && _activeField.isFocused;

    /// <summary>
    /// Call each frame. Detects focused input fields, tracks text changes,
    /// and handles Left/Right for character reading.
    /// Returns true if input was consumed (caller should skip other input handling).
    /// </summary>
    public bool Update()
    {
        // Find currently focused TMP_InputField
        TMP_InputField focused = FindFocusedInputField();

        if ((Object)(object)focused == (Object)null)
        {
            if (_wasActive)
            {
                // Just lost focus
                _wasActive = false;
                _activeField = null;
                _prevText = "";
                _prevCaretPos = 0;
            }
            return false;
        }

        // Field gained focus
        if (!_wasActive || _activeField != focused)
        {
            _activeField = focused;
            _wasActive = true;
            _prevText = focused.text ?? "";
            _prevCaretPos = focused.stringPosition;
            string fieldName = ((Object)((Component)focused).gameObject).name;
            DebugLogger.Log(LogCategory.Handler, "InputFieldHelper", $"Input field focused: {fieldName}");
            return false;
        }

        // Track text changes (typing/deleting)
        string currentText = focused.text ?? "";
        if (currentText != _prevText)
        {
            AnnounceTextChange(_prevText, currentText, focused.inputType);
            _prevText = currentText;
        }

        // Left/Right: announce character at cursor
        int caretPos = focused.stringPosition;
        if (caretPos != _prevCaretPos)
        {
            if (SDLInput.IsKeyHeld(SDLInput.Key.Left) || SDLInput.IsKeyHeld(SDLInput.Key.Right))
            {
                AnnounceCharAtCursor(currentText, caretPos, focused.inputType);
            }
            _prevCaretPos = caretPos;
        }

        return false; // Let the game handle the input field naturally
    }

    /// <summary>Announce what changed between old and new text.</summary>
    private void AnnounceTextChange(string oldText, string newText, TMP_InputField.InputType inputType)
    {
        bool isPassword = inputType == TMP_InputField.InputType.Password;

        if (newText.Length > oldText.Length)
        {
            // Character(s) added
            string added = newText.Substring(oldText.Length);
            if (added.Length == 1)
            {
                string charToAnnounce = isPassword ? "*" : added;
                AnnouncementService.Instance.Announce(charToAnnounce, AnnouncementPriority.Immediate);
            }
            else
            {
                // Pasted text
                AnnouncementService.Instance.Announce(isPassword ? Loc.Get("input_pasted") : added, AnnouncementPriority.Immediate);
            }
        }
        else if (newText.Length < oldText.Length)
        {
            // Character(s) deleted
            string deleted = oldText.Substring(newText.Length);
            if (deleted.Length == 1)
            {
                string charToAnnounce = isPassword ? "*" : Loc.Get("input_deleted", deleted);
                AnnouncementService.Instance.Announce(charToAnnounce, AnnouncementPriority.Immediate);
            }
            else
            {
                AnnouncementService.Instance.Announce(Loc.Get("input_cleared"), AnnouncementPriority.Immediate);
            }
        }
    }

    /// <summary>Announce the character at the current cursor position.</summary>
    private void AnnounceCharAtCursor(string text, int caretPos, TMP_InputField.InputType inputType)
    {
        if (string.IsNullOrEmpty(text) || caretPos < 0 || caretPos >= text.Length) return;
        bool isPassword = inputType == TMP_InputField.InputType.Password;
        string ch = isPassword ? "*" : text[caretPos].ToString();

        // Announce space as "space" for clarity
        if (ch == " ") ch = Loc.Get("input_space");
        AnnouncementService.Instance.Announce(ch, AnnouncementPriority.Immediate);
    }

    private TMP_InputField FindFocusedInputField()
    {
        try
        {
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<TMP_InputField> fields =
                Object.FindObjectsOfType<TMP_InputField>();
            if (fields == null) return null;
            for (int i = 0; i < fields.Count; i++)
            {
                TMP_InputField field = fields[i];
                if ((Object)(object)field == (Object)null) continue;
                if (!((Component)field).gameObject.activeInHierarchy) continue;
                if (field.isFocused) return field;
            }
        }
        catch { }
        return null;
    }
}
