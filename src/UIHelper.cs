using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using Il2CppSecondDinner.CubeRendering.Card;
using Object = UnityEngine.Object;

namespace SnapAccess;

public static class UIHelper
{
    private static readonly Regex _richTextRegex = new Regex("<\\/?[a-zA-Z][a-zA-Z0-9]*(?:[\\s=][^>]*)?>", RegexOptions.Compiled);

    public static T FindComponent<T>() where T : Object
    {
        try
        {
            return Object.FindObjectOfType<T>();
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Game, "UIHelper", "FindComponent<" + typeof(T).Name + "> failed: " + ex.Message);
            return default(T);
        }
    }

    public static string StripRichText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = text.Replace("\\n", " ").Replace("\n", " ").Replace("\r", " ");
        return _richTextRegex.Replace(text, "").Trim();
    }

    public static string StripTags(string text)
    {
        return StripRichText(text);
    }

    public static string GetText(GameObject go)
    {
        if ((Object)(object)go == (Object)null) return "";
        try
        {
            TMP_Text component = go.GetComponent<TMP_Text>();
            if ((Object)(object)component != (Object)null && !string.IsNullOrWhiteSpace(component.text))
            {
                return StripRichText(component.text.Trim());
            }
            return "";
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Game, "UIHelper", "GetText failed: " + ex.Message);
            return "";
        }
    }

    public static string GetTextInChildren(GameObject go)
    {
        if ((Object)(object)go == (Object)null) return "";
        try
        {
            TMP_Text componentInChildren = go.GetComponentInChildren<TMP_Text>(false);
            if ((Object)(object)componentInChildren != (Object)null && !string.IsNullOrWhiteSpace(componentInChildren.text))
            {
                return StripRichText(componentInChildren.text.Trim());
            }
            return "";
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Game, "UIHelper", "GetTextInChildren failed: " + ex.Message);
            return "";
        }
    }

    public static string GetAllText(GameObject go)
    {
        if ((Object)(object)go == (Object)null) return "";
        try
        {
            Il2CppArrayBase<TMP_Text> texts = go.GetComponentsInChildren<TMP_Text>(false);
            if (texts == null || texts.Count == 0) return "";

            List<string> parts = new List<string>();
            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text tmp = texts[i];
                if ((Object)(object)tmp != (Object)null && !string.IsNullOrWhiteSpace(tmp.text))
                {
                    string cleaned = StripRichText(tmp.text.Trim());
                    if (cleaned.Length > 0)
                    {
                        parts.Add(cleaned);
                    }
                }
            }
            return string.Join(". ", parts);
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Game, "UIHelper", "GetAllText failed: " + ex.Message);
            return "";
        }
    }

    public static List<Button> FindAllButtons()
    {
        List<Button> list = new List<Button>();
        try
        {
            Il2CppArrayBase<Button> buttons = Object.FindObjectsOfType<Button>();
            if (buttons == null) return list;

            for (int i = 0; i < buttons.Count; i++)
            {
                Button btn = buttons[i];
                if ((Object)(object)btn != (Object)null && ((Selectable)btn).interactable && ((Component)btn).gameObject.activeInHierarchy)
                {
                    list.Add(btn);
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Game, "UIHelper", "FindAllButtons failed: " + ex.Message);
        }
        return list;
    }

    public static List<Button> FindButtonsUnder(GameObject parent)
    {
        List<Button> list = new List<Button>();
        if ((Object)(object)parent == (Object)null) return list;
        try
        {
            Il2CppArrayBase<Button> buttons = parent.GetComponentsInChildren<Button>(false);
            if (buttons != null)
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    Button btn = buttons[i];
                    if ((Object)(object)btn != (Object)null && ((Selectable)btn).interactable && ((Component)btn).gameObject.activeInHierarchy)
                    {
                        list.Add(btn);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Game, "UIHelper", "FindButtonsUnder failed: " + ex.Message);
        }
        return list;
    }

    public static bool ClickButton(Button button)
    {
        if ((Object)(object)button == (Object)null) return false;
        try
        {
            ((UnityEvent)button.onClick).Invoke();
            DebugLogger.Log(LogCategory.Handler, "UIHelper", "Clicked button: " + ((Object)((Component)button).gameObject).name);
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "UIHelper", "ClickButton failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>Click a button using onClick first, then mouse simulation as fallback.</summary>
    public static bool ClickButtonWithFallback(Button button)
    {
        if ((Object)(object)button == (Object)null) return false;
        // Try onClick first
        if (ClickButton(button)) return true;
        // Fallback: mouse simulation
        return SimulateMouseClick(((Component)button).gameObject);
    }

    /// <summary>Send a pointer click and submit event through Unity's EventSystem. Works even if the button is off-screen.</summary>
    public static bool SendPointerClick(GameObject go)
    {
        if ((Object)(object)go == (Object)null) return false;
        try
        {
            var eventData = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left
            };
            // Try both pointer click and submit — buttons may use either handler
            ExecuteEvents.Execute(go, eventData, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.Execute(go, eventData, ExecuteEvents.submitHandler);
            DebugLogger.Log(LogCategory.Handler, "UIHelper",
                "SendPointerClick on " + ((Object)go).name);
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "UIHelper", "SendPointerClick failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>Simulate a mouse click on a GameObject's screen position using P/Invoke.</summary>
    public static bool SimulateMouseClick(GameObject go)
    {
        if ((Object)(object)go == (Object)null) return false;
        try
        {
            int sx, sy;
            RectTransform rt = go.GetComponent<RectTransform>();
            if ((Object)(object)rt != (Object)null)
            {
                Camera cam = null;
                Canvas canvas = go.GetComponentInParent<Canvas>();
                if ((Object)(object)canvas != (Object)null)
                {
                    Canvas root = canvas.rootCanvas;
                    if ((Object)(object)root != (Object)null && root.renderMode != RenderMode.ScreenSpaceOverlay)
                        cam = root.worldCamera;
                }
                Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, rt.position);
                sx = (int)screenPos.x;
                sy = Screen.height - (int)screenPos.y;
            }
            else
            {
                Camera cam = Camera.main;
                if ((Object)(object)cam == (Object)null) return false;
                Vector3 sp = cam.WorldToScreenPoint(go.transform.position);
                sx = (int)sp.x;
                sy = Screen.height - (int)sp.y;
            }
            // Bounds check: skip if coordinates are outside visible screen area
            if (sx < 0 || sx > Screen.width || sy < 0 || sy > Screen.height)
            {
                DebugLogger.Log(LogCategory.Handler, "UIHelper",
                    $"SimulateMouseClick SKIPPED {((Object)go).name}: coords ({sx},{sy}) outside screen ({Screen.width}x{Screen.height})");
                return false;
            }

            System.IntPtr hwnd = GetForegroundWindow();
            POINT pt;
            pt.X = sx;
            pt.Y = sy;
            ClientToScreen(hwnd, ref pt);
            SetCursorPos(pt.X, pt.Y);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0u, System.IntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0u, System.IntPtr.Zero);
            DebugLogger.Log(LogCategory.Handler, "UIHelper",
                $"SimulateMouseClick on {((Object)go).name} at screen=({sx},{sy}) client=({pt.X},{pt.Y})");
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "UIHelper", "SimulateMouseClick failed: " + ex.Message);
            return false;
        }
    }

    private struct POINT { public int X; public int Y; }
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, System.IntPtr dwExtraInfo);
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(System.IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")]
    private static extern System.IntPtr GetForegroundWindow();

    public static string GetButtonLabel(Button button)
    {
        if ((Object)(object)button == (Object)null) return "";
        try
        {
            string goName = ((Object)((Component)button).gameObject).name;

            // "Next Entity Button" / "Previous Entity Button" → simple labels
            if (goName.Equals("Next Entity Button", StringComparison.Ordinal))
                return "Next";
            if (goName.Equals("Previous Entity Button", StringComparison.Ordinal))
                return "Previous";

            // "btn next" / "btn prev" → simple labels
            if (goName.Equals("btn next", StringComparison.OrdinalIgnoreCase))
                return "Next";
            if (goName.Equals("btn prev", StringComparison.OrdinalIgnoreCase))
                return "Previous";

            // "btn tooltip" → try to read text from parent or sibling hierarchy
            if (goName.Equals("btn tooltip", StringComparison.OrdinalIgnoreCase))
            {
                string parentText = GetTextFromParentOrSiblings(((Component)button).transform);
                if (!string.IsNullOrEmpty(parentText))
                    return parentText;
                return "Tooltip";
            }

            // "container_credits" / "container_gold" → read amount from child text
            if (goName.StartsWith("container_credits", StringComparison.OrdinalIgnoreCase))
            {
                string amount = GetTextInChildren(((Component)button).gameObject);
                return !string.IsNullOrEmpty(amount) ? amount + " Credits" : "Credits";
            }
            if (goName.StartsWith("container_gold", StringComparison.OrdinalIgnoreCase))
            {
                string amount = GetTextInChildren(((Component)button).gameObject);
                return !string.IsNullOrEmpty(amount) ? amount + " Gold" : "Gold";
            }

            // "Premade Deck Details View Card Container XX" → read card name from children
            if (goName.StartsWith("Premade Deck Details View Card Container", StringComparison.Ordinal))
            {
                string cardText = GetAllText(((Component)button).gameObject);
                if (!string.IsNullOrEmpty(cardText))
                    return cardText;
            }

            TMP_Text tmp = ((Component)button).GetComponentInChildren<TMP_Text>(false);
            if ((Object)(object)tmp != (Object)null && !string.IsNullOrWhiteSpace(tmp.text))
            {
                string cleaned = StripRichText(tmp.text.Trim());
                // Skip broken localization entries — fall through to CardRenderer
                if (!cleaned.Contains("{Missing") && cleaned.Length >= 2)
                    return cleaned;
            }

            // Try CardRenderer.CardName for card buttons (collection, deck builder, etc.)
            try
            {
                var cardRenderer = ((Component)button).GetComponentInChildren<Il2CppSecondDinner.CubeRendering.Card.CardRenderer>(true);
                if (cardRenderer != null)
                {
                    string cardName = cardRenderer.CardName;
                    if (!string.IsNullOrEmpty(cardName) && cardName.Length >= 2)
                        return cardName;
                }
            }
            catch { }

            // "ClaimButton" with no text — try to read reward info from parent/siblings
            if (goName.Equals("ClaimButton", StringComparison.Ordinal))
            {
                string rewardText = GetTextFromParentOrSiblings(((Component)button).transform);
                if (!string.IsNullOrEmpty(rewardText))
                    return "Claim: " + rewardText;
                return "Claim";
            }

            return CleanGameObjectName(goName);
        }
        catch
        {
            try
            {
                return ((Object)((Component)button).gameObject).name ?? "Button";
            }
            catch
            {
                return "Button";
            }
        }
    }

    /// <summary>Reads text from parent or sibling GameObjects for context when a button itself has no label.</summary>
    private static string GetTextFromParentOrSiblings(Transform btnTransform)
    {
        if (btnTransform == null) return "";
        try
        {
            Transform parent = btnTransform.parent;
            if (parent == null) return "";

            // Check siblings for TMP_Text
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform sibling = parent.GetChild(i);
                if ((Object)(object)sibling == (Object)(object)btnTransform) continue;
                if (!sibling.gameObject.activeInHierarchy) continue;
                string text = GetText(sibling.gameObject);
                if (!string.IsNullOrEmpty(text))
                    return text;
            }

            // Check parent's text components
            string parentText = GetAllText(parent.gameObject);
            if (!string.IsNullOrEmpty(parentText))
                return parentText;
        }
        catch { }
        return "";
    }

    public static string CleanGameObjectName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unknown";

        int cloneIdx = name.IndexOf("(Clone)", StringComparison.Ordinal);
        if (cloneIdx > 0)
        {
            name = name.Substring(0, cloneIdx).Trim();
        }
        if (name.EndsWith(")"))
        {
            int parenIdx = name.LastIndexOf(" (", StringComparison.Ordinal);
            if (parenIdx > 0)
            {
                name = name.Substring(0, parenIdx).Trim();
            }
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
            {
                sb.Append(' ');
            }
            if (c == '_')
            {
                sb.Append(' ');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Trim();
    }

    public static Transform FindChildByName(Transform parent, string name)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (((Object)((Component)child).gameObject).name == name)
            {
                return child;
            }
            Transform found = FindChildByName(child, name);
            if ((Object)(object)found != (Object)null)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>Finds a Button component in a child with the given name.</summary>
    public static Button FindButtonInChildren(Transform parent, string name)
    {
        Transform child = FindChildByName(parent, name);
        if ((Object)(object)child == (Object)null) return null;
        return ((Component)child).GetComponent<Button>();
    }

    public static string GetGameObjectPath(GameObject obj)
    {
        if ((Object)(object)obj == (Object)null) return "";
        string path = "/" + ((Object)obj).name;
        while (obj.transform.parent != null)
        {
            obj = ((Component)obj.transform.parent).gameObject;
            path = "/" + ((Object)obj).name + path;
        }
        return path;
    }

    /// <summary>Simulates a keyboard press using Windows API.</summary>
    public static void SimulateKeyPress(SDLInput.Key key)
    {
        byte vk = (byte)key;
        keybd_event(vk, 0, 0, 0); // Key Down
        keybd_event(vk, 0, 2, 0); // Key Up
        DebugLogger.Log(LogCategory.Handler, "UIHelper", $"SimulateKeyPress: {key}");
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);
}
