using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Specialized handler for Shop and Battle Pass screens.
/// Detects ShopSubSceneView or BattlePassView as the active screen.
/// Provides tab switching, section-grouped item navigation, and price reading.
/// </summary>
public class ShopHandler : IScreenNavigator
{
    private enum ShopScreen
    {
        None,
        Shop,
        BattlePass
    }

    private class ShopItem
    {
        public string Name;
        public string Price;
        public string ExtraInfo; // discount, timer, etc.
        public Button Button;
    }

    private class ShopSection
    {
        public string Name;
        public List<ShopItem> Items = new List<ShopItem>();
    }

    private readonly List<ShopSection> _sections = new List<ShopSection>();
    private int _sectionIndex = 0;
    private int _itemIndex = 0;
    private int _menuLevel = 0; // 0=Sections, 1=Items
    private ShopScreen _currentScreen = ShopScreen.None;
    private bool _isActive = false;
    private bool _activated = false;
    private bool _entryAnnounced = false;
    private float _lastScanTime = 0f;
    private readonly KeyHoldRepeater _holdRepeater = new KeyHoldRepeater();

    // Tab state (shop only)
    private int _tabIndex = 0;
    private readonly List<TabEntry> _tabs = new List<TabEntry>();

    private class TabEntry
    {
        public string Name;
        public Toggle Toggle;
    }

    public string NavigatorId => "Shop";
    public int Priority => 500;
    public bool IsActive => _isActive;

    /// <summary>
    /// Called by MainMenuHandler when user navigates to Shop or BattlePass.
    /// </summary>
    public void Activate()
    {
        _activated = true;
        _lastScanTime = 0f;
        _entryAnnounced = false;
    }

    public void Update()
    {
        if (!_activated || !ScanScreen())
        {
            _isActive = false;
            return;
        }

        _isActive = true;

        if (!_entryAnnounced)
        {
            _entryAnnounced = true;
            AnnounceEntry();
        }

        ProcessInput();
    }

    private bool ScanScreen()
    {
        if (Time.time - _lastScanTime < 1.5f) return _isActive;
        _lastScanTime = Time.time;

        // Try to find shop or battlepass root
        ShopScreen detected = DetectScreen();
        if (detected == ShopScreen.None) return false;

        if (detected != _currentScreen)
        {
            _currentScreen = detected;
            _entryAnnounced = false;
            _menuLevel = 0;
            _sectionIndex = 0;
            _itemIndex = 0;
        }

        if (_currentScreen == ShopScreen.Shop)
            return ScanShop();
        else
            return ScanBattlePass();
    }

    private ShopScreen DetectScreen()
    {
        try
        {
            // Check for ShopSubSceneView
            var shopView = Object.FindObjectOfType<Il2CppCubeUnity.App.Shop.ShopSubSceneView>();
            if (shopView != null && ((Component)shopView).gameObject.activeInHierarchy)
                return ShopScreen.Shop;

            // Check for BattlePassView
            var bpView = Object.FindObjectOfType<Il2CppCubeUnity.App.View.BattlePass.BattlePassView>();
            if (bpView != null && ((Component)bpView).gameObject.activeInHierarchy)
                return ShopScreen.BattlePass;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ShopHandler DetectScreen: " + ex.Message);
        }

        return ShopScreen.None;
    }

    private bool ScanShop()
    {
        try
        {
            var shopView = Object.FindObjectOfType<Il2CppCubeUnity.App.Shop.ShopSubSceneView>();
            if (shopView == null) return false;

            GameObject root = ((Component)shopView).gameObject;

            // Scan tabs
            _tabs.Clear();
            try
            {
                // Find toggle/tab buttons
                Il2CppArrayBase<Toggle> toggles = root.GetComponentsInChildren<Toggle>(true);
                foreach (var toggle in toggles)
                {
                    if (toggle == null || !((Component)toggle).gameObject.activeInHierarchy) continue;
                    string label = GetToggleLabel(toggle);
                    if (string.IsNullOrEmpty(label) || label.Length < 2) continue;
                    _tabs.Add(new TabEntry { Name = label, Toggle = toggle });
                }
            }
            catch { }

            // Scan shop items grouped by section
            ScanShopSections(root);

            return _sections.Count > 0 || _tabs.Count > 0;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ScanShop: " + ex.Message);
            return false;
        }
    }

    private bool ScanBattlePass()
    {
        try
        {
            var bpView = Object.FindObjectOfType<Il2CppCubeUnity.App.View.BattlePass.BattlePassView>();
            if (bpView == null) return false;

            GameObject root = ((Component)bpView).gameObject;
            ScanShopSections(root);

            return _sections.Count > 0;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ScanBattlePass: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Scans buttons under a root, grouping them into sections by parent container.
    /// Each section gets a name from the nearest header text.
    /// </summary>
    private void ScanShopSections(GameObject root)
    {
        _sections.Clear();

        try
        {
            Il2CppArrayBase<Button> buttons = root.GetComponentsInChildren<Button>(true);
            if (buttons == null || buttons.Length == 0) return;

            // Group buttons by their parent section container
            Dictionary<string, ShopSection> sectionMap = new Dictionary<string, ShopSection>();
            ShopSection uncategorized = new ShopSection { Name = Loc.Get("shop_items") };

            foreach (var btn in buttons)
            {
                if (btn == null || !((Component)btn).gameObject.activeInHierarchy) continue;
                if (!((Selectable)btn).interactable) continue;

                string goName = ((Component)btn).gameObject.name;

                // Skip junk buttons
                if (IsJunkShopButton(goName)) continue;

                ShopItem item = ReadShopItem(btn);
                if (item == null) continue;

                // Try to determine which section this belongs to
                string sectionName = FindSectionName(((Component)btn).transform);
                if (!string.IsNullOrEmpty(sectionName))
                {
                    if (!sectionMap.TryGetValue(sectionName, out var section))
                    {
                        section = new ShopSection { Name = sectionName };
                        sectionMap[sectionName] = section;
                    }
                    section.Items.Add(item);
                }
                else
                {
                    uncategorized.Items.Add(item);
                }
            }

            // Add sections in order
            foreach (var kvp in sectionMap)
            {
                if (kvp.Value.Items.Count > 0)
                    _sections.Add(kvp.Value);
            }
            if (uncategorized.Items.Count > 0)
                _sections.Add(uncategorized);

            // Clamp indices
            if (_sectionIndex >= _sections.Count) _sectionIndex = Math.Max(0, _sections.Count - 1);
            if (_menuLevel == 1 && _sectionIndex < _sections.Count)
            {
                if (_itemIndex >= _sections[_sectionIndex].Items.Count)
                    _itemIndex = Math.Max(0, _sections[_sectionIndex].Items.Count - 1);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ScanShopSections: " + ex.Message);
        }
    }

    private ShopItem ReadShopItem(Button btn)
    {
        try
        {
            Il2CppArrayBase<TMP_Text> texts = ((Component)btn).GetComponentsInChildren<TMP_Text>(true);
            if (texts == null || texts.Length == 0) return null;

            string name = "";
            string price = "";
            string extra = "";

            foreach (var t in texts)
            {
                if (t == null || !((Component)t).gameObject.activeInHierarchy) continue;
                string goName = ((Component)t).gameObject.name.ToLower();
                string val = UIHelper.StripRichText(t.text);
                if (string.IsNullOrWhiteSpace(val) || val.Length < 2) continue;

                // Classify text by GO name patterns
                if (goName.Contains("price") || goName.Contains("cost") || goName.Contains("currency"))
                    price = val;
                else if (goName.Contains("timer") || goName.Contains("countdown") || goName.Contains("time"))
                    extra = val;
                else if (goName.Contains("discount") || goName.Contains("sale") || goName.Contains("off"))
                    extra = val;
                else if (goName.Contains("title") || goName.Contains("name") || goName.Contains("header"))
                    name = val;
                else if (string.IsNullOrEmpty(name) && val.Length > 2 && !IsNumericOnly(val))
                    name = val; // First meaningful text becomes name
                else if (string.IsNullOrEmpty(price) && (val.Contains("$") || val.Contains("Gold") || val.Contains("Credits")))
                    price = val;
            }

            if (string.IsNullOrEmpty(name))
            {
                // Try button GO name as fallback
                name = UIHelper.CleanGameObjectName(((Component)btn).gameObject.name);
                if (string.IsNullOrEmpty(name) || name.Length < 3) return null;
            }

            return new ShopItem { Name = name, Price = price, ExtraInfo = extra, Button = btn };
        }
        catch { return null; }
    }

    /// <summary>Walks up hierarchy to find a section header name.</summary>
    private string FindSectionName(Transform itemTransform)
    {
        try
        {
            Transform current = itemTransform.parent;
            int depth = 0;
            while (current != null && depth < 6)
            {
                // Check if this container has a header text
                string containerName = current.gameObject.name;

                // Known section container patterns
                if (containerName.Contains("Vendor") || containerName.Contains("Section") ||
                    containerName.Contains("Panel") || containerName.Contains("Module"))
                {
                    // Look for a header TMP_Text in this container
                    for (int i = 0; i < current.childCount; i++)
                    {
                        Transform child = current.GetChild(i);
                        string childName = child.gameObject.name.ToLower();
                        if (childName.Contains("header") || childName.Contains("title") || childName.Contains("label"))
                        {
                            TMP_Text tmp = child.GetComponent<TMP_Text>();
                            if (tmp == null) tmp = child.GetComponentInChildren<TMP_Text>(false);
                            if (tmp != null)
                            {
                                string text = UIHelper.StripRichText(tmp.text);
                                if (!string.IsNullOrEmpty(text) && text.Length >= 2)
                                    return text;
                            }
                        }
                    }

                    // Use cleaned container name as fallback
                    string cleaned = UIHelper.CleanGameObjectName(containerName);
                    if (!string.IsNullOrEmpty(cleaned) && cleaned.Length >= 3)
                        return cleaned;
                }

                current = current.parent;
                depth++;
            }
        }
        catch { }
        return null;
    }

    private string GetToggleLabel(Toggle toggle)
    {
        try
        {
            Il2CppArrayBase<TMP_Text> texts = ((Component)toggle).GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in texts)
            {
                if (t == null) continue;
                string val = UIHelper.StripRichText(t.text);
                if (!string.IsNullOrEmpty(val) && val.Length >= 2)
                    return val;
            }
            return UIHelper.CleanGameObjectName(((Component)toggle).gameObject.name);
        }
        catch { return ""; }
    }

    private bool IsJunkShopButton(string goName)
    {
        if (string.IsNullOrEmpty(goName)) return true;
        return goName.Contains("Background", StringComparison.OrdinalIgnoreCase)
            || goName.Contains("Blocker", StringComparison.OrdinalIgnoreCase)
            || goName.Contains("Catcher", StringComparison.OrdinalIgnoreCase)
            || goName.Equals("Close", StringComparison.OrdinalIgnoreCase)
            || goName.Equals("btn_close", StringComparison.OrdinalIgnoreCase)
            || goName.Equals("btn_back", StringComparison.OrdinalIgnoreCase)
            || goName.Equals("Esc", StringComparison.OrdinalIgnoreCase)
            || goName.Equals("btn_hex_prp", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsNumericOnly(string text)
    {
        foreach (char c in text)
        {
            if (!char.IsDigit(c) && c != ',' && c != '.' && c != ' ' && c != '%' && c != '+' && c != '-')
                return false;
        }
        return true;
    }

    private void AnnounceEntry()
    {
        if (_currentScreen == ShopScreen.Shop)
        {
            int itemCount = 0;
            foreach (var s in _sections) itemCount += s.Items.Count;
            AnnouncementService.Instance.Announce(
                Loc.Get("shop_entered", _sections.Count, itemCount),
                AnnouncementPriority.High);
        }
        else
        {
            int itemCount = 0;
            foreach (var s in _sections) itemCount += s.Items.Count;
            AnnouncementService.Instance.Announce(
                Loc.Get("bp_entered", _sections.Count, itemCount),
                AnnouncementPriority.High);
        }

        if (_sections.Count > 0)
            AnnounceSection();
    }

    private void ProcessInput()
    {
        // Tab switching (shop only)
        if (_currentScreen == ShopScreen.Shop && _tabs.Count > 1 &&
            (SDLInput.IsKeyDown(SDLInput.Key.Tab) || SDLInput.IsButtonDown(SDLInput.GamepadButton.L1)))
        {
            SwitchTab();
            return;
        }

        if (_holdRepeater.Check(SDLInput.Key.Up, () => MovePrev())) { }
        else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp)) MovePrev();
        else if (_holdRepeater.Check(SDLInput.Key.Down, () => MoveNext())) { }
        else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown)) MoveNext();
        else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
            ReadDetails();
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
            ActivateFocused();
        else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsKeyDown(SDLInput.Key.Escape) ||
                 SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
            Back();
    }

    private void SwitchTab()
    {
        if (_tabs.Count < 2) return;
        _tabIndex = (_tabIndex + 1) % _tabs.Count;
        var tab = _tabs[_tabIndex];

        try
        {
            if (tab.Toggle != null)
                tab.Toggle.isOn = true;
        }
        catch { }

        AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("shop_tab_switched", tab.Name));
        _lastScanTime = 0f; // Force rescan
        _menuLevel = 0;
        _sectionIndex = 0;
        _itemIndex = 0;
    }

    private void MovePrev()
    {
        if (_sections.Count == 0) return;
        if (_menuLevel == 0)
        {
            _sectionIndex = (_sectionIndex - 1 + _sections.Count) % _sections.Count;
            AnnounceSection();
        }
        else
        {
            var items = _sections[_sectionIndex].Items;
            _itemIndex = (_itemIndex - 1 + items.Count) % items.Count;
            AnnounceItem();
        }
    }

    private void MoveNext()
    {
        if (_sections.Count == 0) return;
        if (_menuLevel == 0)
        {
            _sectionIndex = (_sectionIndex + 1) % _sections.Count;
            AnnounceSection();
        }
        else
        {
            var items = _sections[_sectionIndex].Items;
            _itemIndex = (_itemIndex + 1) % items.Count;
            AnnounceItem();
        }
    }

    private void ReadDetails()
    {
        if (_menuLevel == 0)
        {
            if (_sectionIndex >= 0 && _sectionIndex < _sections.Count)
                AnnouncementService.Instance.Announce(
                    Loc.Get("shop_section_count", _sections[_sectionIndex].Items.Count));
        }
        else
        {
            // Read full item details
            AnnounceItemDetails();
        }
    }

    private void ActivateFocused()
    {
        if (_menuLevel == 0 && _sections.Count > 0)
        {
            _menuLevel = 1;
            _itemIndex = 0;
            AnnounceItem();
        }
        else if (_menuLevel == 1)
        {
            var items = _sections[_sectionIndex].Items;
            if (_itemIndex >= 0 && _itemIndex < items.Count)
            {
                var item = items[_itemIndex];
                AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("shop_activating", item.Name));
                if (item.Button != null)
                    UIHelper.ActivateButton(item.Button);
                // Force rescan — the shop view may still be active (item detail)
                // or may have closed (purchase dialog). Let ScanScreen detect the change.
                _lastScanTime = 0f;
            }
        }
    }

    private void Back()
    {
        if (_menuLevel == 1)
        {
            _menuLevel = 0;
            AnnounceSection();
        }
        else
        {
            _activated = false;
            _isActive = false;
        }
    }

    private void AnnounceSection()
    {
        if (_sectionIndex < 0 || _sectionIndex >= _sections.Count) return;
        var section = _sections[_sectionIndex];
        AnnouncementService.Instance.Announce(
            section.Name + ", " + section.Items.Count + " " + Loc.Get("shop_items_label") +
            ", " + (_sectionIndex + 1) + " " + Loc.Get("log_of") + " " + _sections.Count);
    }

    private void AnnounceItem()
    {
        if (_sectionIndex < 0 || _sectionIndex >= _sections.Count) return;
        var items = _sections[_sectionIndex].Items;
        if (_itemIndex < 0 || _itemIndex >= items.Count) return;

        var item = items[_itemIndex];
        string msg = item.Name;
        if (!string.IsNullOrEmpty(item.Price)) msg += ", " + item.Price;
        msg += ", " + (_itemIndex + 1) + " " + Loc.Get("log_of") + " " + items.Count;
        AnnouncementService.Instance.Announce(msg);
    }

    private void AnnounceItemDetails()
    {
        if (_sectionIndex < 0 || _sectionIndex >= _sections.Count) return;
        var items = _sections[_sectionIndex].Items;
        if (_itemIndex < 0 || _itemIndex >= items.Count) return;

        var item = items[_itemIndex];
        string msg = item.Name;
        if (!string.IsNullOrEmpty(item.Price)) msg += ". " + Loc.Get("shop_price") + ": " + item.Price;
        if (!string.IsNullOrEmpty(item.ExtraInfo)) msg += ". " + item.ExtraInfo;
        AnnouncementService.Instance.Announce(msg);
    }

    public void AnnounceContext()
    {
        if (_currentScreen == ShopScreen.Shop)
            AnnouncementService.Instance.Announce(Loc.Get("shop_help"), AnnouncementPriority.High);
        else
            AnnouncementService.Instance.Announce(Loc.Get("bp_help"), AnnouncementPriority.High);

        if (_menuLevel == 0 && _sections.Count > 0)
            AnnounceSection();
        else if (_menuLevel == 1)
            AnnounceItem();
    }

    public void Close()
    {
        _activated = false;
        _isActive = false;
    }

    public void Deactivate()
    {
        _isActive = false;
        _activated = false;
        _entryAnnounced = false;
        _holdRepeater.Reset();
    }

    public void OnSceneChanged(string sceneName)
    {
        _isActive = false;
        _activated = false;
        _entryAnnounced = false;
        _currentScreen = ShopScreen.None;
        _sections.Clear();
        _tabs.Clear();
        _sectionIndex = 0;
        _itemIndex = 0;
        _menuLevel = 0;
        _tabIndex = 0;
        _lastScanTime = 0f;
        _holdRepeater.Reset();
    }
}
