using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppCubeUnity.App.View;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSecondDinner.CubeRendering.Card;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Handles the Deck Editor screen.
/// Detects CollectionDeckTrayView or DeckListView as the active deck editor.
/// Provides two areas: Deck Cards (current deck) and Collection Cards (available to add).
/// Cards are added/removed by clicking their buttons via mouse simulation.
/// </summary>
public class DeckBuilderHandler : IScreenNavigator
{
    private enum Area
    {
        DeckCards,
        CollectionCards
    }

    private Area _currentArea = Area.DeckCards;

    // Deck cards (the 12 card slots in the current deck)
    private readonly List<DeckCardEntry> _deckCards = new List<DeckCardEntry>();
    private int _deckCardIndex = 0;

    // Collection cards (browsable cards to add)
    private readonly List<DeckCardEntry> _collectionCards = new List<DeckCardEntry>();
    private int _collectionIndex = 0;

    private string _deckName = "";
    private bool _isActive = false;
    private bool _entryAnnounced = false;
    private readonly KeyHoldRepeater _holdRepeater = new KeyHoldRepeater();
    private bool _forceRescan = false;
    private float _lastScanTime = 0f;
    private float _lastCollectionScanTime = 0f;
    private const float ScanInterval = 0.8f;
    private const float CollectionScanInterval = 1.5f;
    private GameObject _editorRoot = null;

    private bool _requestedCollection = false;
    private bool _activated = false;
    private int _detailLevel = 0; // 0=name, 1=cost, 2=power, 3=ability (battlefield pattern)

    /// <summary>Called by MainMenuHandler to activate the deck builder and set starting area.</summary>
    public void Activate(bool startInCollection)
    {
        _activated = true;
        _requestedCollection = startInCollection;
        _entryAnnounced = false;
        _forceRescan = true;
        _lastScanTime = 0f;
        _lastCollectionScanTime = 0f;
        _editorRoot = null;
        DebugLogger.Log(LogCategory.Handler, "DeckBuilderHandler",
            $"Activated, startInCollection={startInCollection}");
    }

    /// <summary>Called by MainMenuHandler to set which area the deck builder should start in.</summary>
    public void RequestStartArea(bool startInCollection)
    {
        _requestedCollection = startInCollection;
    }

    private class DeckCardEntry
    {
        public string Name = "Unknown";
        public int Cost = -1;
        public int Power = -1;
        public string Ability = "";
        public GameObject GameObject;
        public Button Button;
        public CardRenderer Renderer;
    }

    public string NavigatorId => "DeckBuilder";
    public int Priority => 0; // Managed directly by MainMenuHandler, not by NavigatorManager priority
    public bool IsActive => _isActive;

    public void Update()
    {
        if (!_activated)
        {
            if (_isActive) { _isActive = false; Deactivate(); }
            return;
        }

        if (!ScanForDeckEditor())
        {
            if (_isActive)
            {
                _isActive = false;
                _activated = false;
                Deactivate();
            }
            return;
        }

        _isActive = true;
        if (!_entryAnnounced)
        {
            _entryAnnounced = true;
            // Apply requested starting area
            if (_requestedCollection)
            {
                _currentArea = Area.CollectionCards;
                ScanCollectionCards();
                AnnouncementService.Instance.Announce(
                    Loc.Get("deck_builder_entry", _deckName, _deckCards.Count) + " " +
                    Loc.Get("deck_builder_area_collection", _collectionCards.Count), AnnouncementPriority.High);
            }
            else
            {
                _currentArea = Area.DeckCards;
                AnnouncementService.Instance.Announce(
                    Loc.Get("deck_builder_entry", _deckName, _deckCards.Count), AnnouncementPriority.High);
            }
            _requestedCollection = false;
        }
        ProcessInput();
    }

    public void Deactivate()
    {
        _isActive = false;
        _activated = false;
        _entryAnnounced = false;
        _forceRescan = false;
        _currentArea = Area.DeckCards;
        _deckCardIndex = 0;
        _collectionIndex = 0;
        _holdRepeater.Reset();
    }

    public void OnSceneChanged(string sceneName)
    {
        _isActive = false;
        _activated = false;
        _entryAnnounced = false;
        _forceRescan = false;
        _currentArea = Area.DeckCards;
        _deckCards.Clear();
        _collectionCards.Clear();
        _deckCardIndex = 0;
        _collectionIndex = 0;
        _deckName = "";
        _editorRoot = null;
        _lastScanTime = 0f;
        _lastCollectionScanTime = 0f;
    }

    /// <summary>
    /// Detects whether the deck editor is open by looking for Grid_CardSlots
    /// (the container of DeckCardSlotCell children in the landscape deck editor).
    /// Avoids Il2Cpp generic component lookups that can silently fail.
    /// </summary>
    private bool ScanForDeckEditor()
    {
        if (!_forceRescan && Time.time - _lastScanTime < ScanInterval) return _isActive;
        _lastScanTime = Time.time;

        try
        {
            // Strategy 1: Find Grid_CardSlots directly (proven path from UI dump)
            GameObject gridSlots = GameObject.Find("Grid_CardSlots");
            if (gridSlots != null && gridSlots.activeInHierarchy)
            {
                _editorRoot = gridSlots;
                _forceRescan = false;
                DebugLogger.Log(LogCategory.Handler, "DeckBuilderHandler",
                    "Found Grid_CardSlots directly");
                ScanDeckCards();
                return true;
            }

            // Strategy 2: Find DeckCardSlotLIst (parent of Grid_CardSlots)
            GameObject slotList = GameObject.Find("DeckCardSlotLIst");
            if (slotList != null && slotList.activeInHierarchy)
            {
                // Try to find Grid_CardSlots as child
                Transform grid = UIHelper.FindChildByName(slotList.transform, "Grid_CardSlots");
                if (grid != null)
                {
                    _editorRoot = grid.gameObject;
                    _forceRescan = false;
                    DebugLogger.Log(LogCategory.Handler, "DeckBuilderHandler",
                        "Found Grid_CardSlots via DeckCardSlotLIst");
                    ScanDeckCards();
                    return true;
                }
                // Use DeckCardSlotLIst itself
                _editorRoot = slotList;
                _forceRescan = false;
                ScanDeckCards();
                return true;
            }

            // Strategy 3: Find DeckEditSection container
            GameObject deckEditSection = GameObject.Find("DeckEditSection");
            if (deckEditSection == null) deckEditSection = GameObject.Find("DeckEditSectionContainer");
            if (deckEditSection != null && deckEditSection.activeInHierarchy)
            {
                // Search for Grid_CardSlots within it
                Transform grid = UIHelper.FindChildByName(deckEditSection.transform, "Grid_CardSlots");
                if (grid != null)
                {
                    _editorRoot = grid.gameObject;
                    _forceRescan = false;
                    DebugLogger.Log(LogCategory.Handler, "DeckBuilderHandler",
                        "Found Grid_CardSlots under DeckEditSection");
                    ScanDeckCards();
                    return true;
                }
                // Use DeckEditSection as root even without Grid_CardSlots
                _editorRoot = deckEditSection;
                _forceRescan = false;
                ScanDeckCards();
                if (_deckCards.Count > 0) return true;
            }

            // Keep _forceRescan true if we haven't found anything yet,
            // so next frame tries again immediately
            DebugLogger.Log(LogCategory.Handler, "DeckBuilderHandler",
                "ScanForDeckEditor: no deck editor found");
            return false;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ScanForDeckEditor failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Scans deck cards from DeckCardSlotCell buttons under the editor root,
    /// with fallback to DeckListCardSlotView components.
    /// </summary>
    private void ScanDeckCards()
    {
        try
        {
            _deckCards.Clear();
            _deckName = "";

            // Read deck name from TMP_Text elements near the editor root
            if (_editorRoot != null)
            {
                _deckName = ReadDeckName(_editorRoot);
            }

            HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Strategy 1: Find DeckCardSlotCell buttons under editor root
            // _editorRoot may BE Grid_CardSlots itself, or a parent containing it
            if (_editorRoot != null)
            {
                Transform gridSlots = _editorRoot.transform;
                // If _editorRoot is not Grid_CardSlots itself, search for it
                if (!_editorRoot.name.Contains("Grid_CardSlots"))
                {
                    Transform found = UIHelper.FindChildByName(_editorRoot.transform, "Grid_CardSlots");
                    if (found == null) found = UIHelper.FindChildByName(_editorRoot.transform, "DeckCardSlotLIst");
                    if (found != null) gridSlots = found;
                }

                for (int i = 0; i < gridSlots.childCount; i++)
                {
                    try
                    {
                        Transform slotCell = gridSlots.GetChild(i);
                        if (slotCell == null || !slotCell.gameObject.activeInHierarchy) continue;
                        if (!slotCell.name.Contains("DeckCardSlotCell")) continue;

                        // Find CardRenderer in this slot
                        CardRenderer cr = slotCell.GetComponentInChildren<CardRenderer>(false);
                        if (cr == null) continue;

                        DeckCardEntry entry = new DeckCardEntry();
                        entry.Renderer = cr;
                        entry.GameObject = slotCell.gameObject;
                        // The clickable button is the LandscapeCollectionCardView
                        entry.Button = slotCell.GetComponentInChildren<Button>(false);
                        ReadCardRendererData(entry);

                        if (entry.Name == "Unknown" || entry.Name == "Card") continue;
                        if (seenNames.Contains(entry.Name)) continue;
                        seenNames.Add(entry.Name);
                        _deckCards.Add(entry);
                    }
                    catch { }
                }
            }

            // Strategy 2: DeckListCardSlotView components (fallback for older UI)
            if (_deckCards.Count == 0)
            {
                Il2CppArrayBase<DeckListCardSlotView> slotViews = Object.FindObjectsOfType<DeckListCardSlotView>();
                if (slotViews != null)
                {
                    for (int i = 0; i < slotViews.Length; i++)
                    {
                        try
                        {
                            DeckListCardSlotView slot = slotViews[i];
                            if (slot == null || !((Component)slot).gameObject.activeInHierarchy) continue;

                            DeckCardEntry entry = new DeckCardEntry();
                            entry.GameObject = ((Component)slot).gameObject;
                            entry.Button = ((Component)slot).GetComponentInChildren<Button>();

                            // Try CardRenderer
                            CardRenderer cr = ((Component)slot).GetComponentInChildren<CardRenderer>();
                            if (cr != null)
                            {
                                entry.Renderer = cr;
                                ReadCardRendererData(entry);
                            }

                            if (entry.Name == "Unknown" || entry.Name == "Card") continue;
                            if (seenNames.Contains(entry.Name)) continue;
                            seenNames.Add(entry.Name);
                            _deckCards.Add(entry);
                        }
                        catch { }
                    }
                }
            }

            // Strategy 3: CardRenderers under deck containers
            if (_deckCards.Count == 0)
                ScanDeckCardsFallback();

            // Sort by name for consistent browsing
            _deckCards.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            // Clamp index
            if (_deckCardIndex >= _deckCards.Count) _deckCardIndex = Math.Max(0, _deckCards.Count - 1);

            DebugLogger.Log(LogCategory.Handler, "DeckBuilderHandler",
                $"Scanned deck: '{_deckName}', {_deckCards.Count} cards");
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ScanDeckCards failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Fallback scanning: find CardRenderers under a DeckList-like container.
    /// </summary>
    private void ScanDeckCardsFallback()
    {
        try
        {
            Il2CppArrayBase<CardRenderer> renderers = Object.FindObjectsOfType<CardRenderer>();
            if (renderers == null) return;

            HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < renderers.Length; i++)
            {
                try
                {
                    CardRenderer cr = renderers[i];
                    if (cr == null || !((Component)cr).gameObject.activeInHierarchy) continue;

                    string path = UIHelper.GetGameObjectPath(((Component)cr).gameObject);
                    // Only include cards that are in a deck-related container
                    if (!path.Contains("DeckList") && !path.Contains("DeckSlot") && !path.Contains("DeckCard"))
                        continue;

                    DeckCardEntry entry = new DeckCardEntry();
                    entry.Renderer = cr;
                    entry.GameObject = ((Component)cr).gameObject;
                    entry.Button = ((Component)cr).GetComponentInParent<Button>();
                    ReadCardRendererData(entry);

                    if (entry.Name == "Unknown" || entry.Name == "Card") continue;
                    if (seenNames.Contains(entry.Name)) continue;
                    seenNames.Add(entry.Name);

                    _deckCards.Add(entry);
                }
                catch { }
            }

            _deckCards.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (_deckCardIndex >= _deckCards.Count) _deckCardIndex = Math.Max(0, _deckCards.Count - 1);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ScanDeckCardsFallback failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Scans collection cards visible on screen. These are the cards
    /// available for adding to the deck, shown in the collection grid.
    /// </summary>
    private void ScanCollectionCards()
    {
        if (Time.time - _lastCollectionScanTime < CollectionScanInterval) return;
        _lastCollectionScanTime = Time.time;

        try
        {
            _collectionCards.Clear();

            // Collection cards are CardRenderer or CardView instances NOT in the deck list
            Il2CppArrayBase<CardRenderer> renderers = Object.FindObjectsOfType<CardRenderer>();
            if (renderers == null) return;

            HashSet<string> deckCardNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dc in _deckCards)
                deckCardNames.Add(dc.Name);

            HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<DeckCardEntry> unsorted = new List<DeckCardEntry>();

            for (int i = 0; i < renderers.Length; i++)
            {
                try
                {
                    CardRenderer cr = renderers[i];
                    if (cr == null || !((Component)cr).gameObject.activeInHierarchy) continue;

                    string path = UIHelper.GetGameObjectPath(((Component)cr).gameObject);
                    // Exclude deck list cards (they are in the deck area, not collection)
                    if (path.Contains("DeckList") || path.Contains("DeckSlot") ||
                        path.Contains("DeckCard") || path.Contains("Grid_CardSlots") ||
                        path.Contains("ObjectPool")) continue;

                    DeckCardEntry entry = new DeckCardEntry();
                    entry.Renderer = cr;
                    entry.GameObject = ((Component)cr).gameObject;
                    entry.Button = ((Component)cr).GetComponentInParent<Button>();
                    ReadCardRendererData(entry);

                    if (entry.Name == "Unknown" || entry.Name == "Card") continue;
                    if (seenNames.Contains(entry.Name)) continue;
                    seenNames.Add(entry.Name);

                    unsorted.Add(entry);
                }
                catch { }
            }

            // Also scan for buttons with CardRenderer children in collection area
            Il2CppArrayBase<Button> buttons = Object.FindObjectsOfType<Button>();
            if (buttons != null)
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    try
                    {
                        Button btn = buttons[i];
                        if (btn == null || !((Component)btn).gameObject.activeInHierarchy) continue;

                        string path = UIHelper.GetGameObjectPath(((Component)btn).gameObject);
                        if (path.Contains("DeckList") || path.Contains("DeckSlot") ||
                            path.Contains("DeckCard") || path.Contains("Grid_CardSlots") ||
                            path.Contains("ObjectPool")) continue;
                        if (!path.Contains("Collection") && !path.Contains("Card")) continue;

                        CardRenderer cr = ((Component)btn).GetComponentInChildren<CardRenderer>();
                        if (cr == null) continue;

                        DeckCardEntry entry = new DeckCardEntry();
                        entry.Renderer = cr;
                        entry.GameObject = ((Component)btn).gameObject;
                        entry.Button = btn;
                        ReadCardRendererData(entry);

                        if (entry.Name == "Unknown" || entry.Name == "Card") continue;
                        if (seenNames.Contains(entry.Name)) continue;
                        seenNames.Add(entry.Name);

                        unsorted.Add(entry);
                    }
                    catch { }
                }
            }

            // Sort alphabetically
            unsorted.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _collectionCards.AddRange(unsorted);

            if (_collectionIndex >= _collectionCards.Count)
                _collectionIndex = Math.Max(0, _collectionCards.Count - 1);

            DebugLogger.Log(LogCategory.Handler, "DeckBuilderHandler",
                $"Collection scan: {_collectionCards.Count} cards");
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ScanCollectionCards failed: " + ex.Message);
        }
    }

    /// <summary>Reads name, cost, power from a CardRenderer into a DeckCardEntry.</summary>
    private void ReadCardRendererData(DeckCardEntry entry)
    {
        if (entry.Renderer == null) return;
        try
        {
            string name = entry.Renderer.CardName;
            if (!string.IsNullOrEmpty(name))
                entry.Name = UIHelper.StripRichText(name);
        }
        catch { }

        try
        {
            CardValueView costView = entry.Renderer._CostValueView;
            if (costView != null) entry.Cost = costView.Value;
        }
        catch { }

        try
        {
            CardValueView powerView = entry.Renderer._PowerValueView;
            if (powerView != null) entry.Power = powerView.Value;
        }
        catch { }

        // Read ability text from AbilityTextRoot child hierarchy
        try
        {
            Transform cardTransform = ((Component)entry.Renderer).transform;
            Transform abilityRoot = UIHelper.FindChildByName(cardTransform, "AbilityTextRoot")
                ?? UIHelper.FindChildByName(cardTransform, "CardAbilityText");
            if (abilityRoot != null)
            {
                Il2CppArrayBase<TMP_Text> abilityTexts = ((Component)abilityRoot).GetComponentsInChildren<TMP_Text>(true);
                if (abilityTexts != null)
                {
                    for (int i = 0; i < abilityTexts.Count; i++)
                    {
                        TMP_Text tmp = abilityTexts[i];
                        if (tmp == null) continue;
                        string text = tmp.text;
                        if (string.IsNullOrWhiteSpace(text) || text.Contains("Missing Entry")) continue;
                        string cleaned = UIHelper.StripRichText(text.Trim());
                        if (cleaned.Length > 3)
                        {
                            entry.Ability = cleaned;
                            break;
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void ProcessInput()
    {
        // Area switching: Tab or Up/Down to toggle between Deck Cards and Collection
        if (SDLInput.IsKeyDown(SDLInput.Key.Tab) || SDLInput.IsButtonDown(SDLInput.GamepadButton.L1))
        {
            _currentArea = _currentArea == Area.DeckCards ? Area.CollectionCards : Area.DeckCards;
            _detailLevel = 0;
            if (_currentArea == Area.CollectionCards)
            {
                ScanCollectionCards();
                AnnouncementService.Instance.Announce(Loc.Get("deck_builder_area_collection", _collectionCards.Count));
            }
            else
            {
                AnnouncementService.Instance.Announce(Loc.Get("deck_builder_area_deck", _deckCards.Count));
            }
            return;
        }

        // Backspace: close editor
        if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
        {
            TryCloseEditor();
            return;
        }

        // I: Announce deck info summary
        if (SDLInput.IsKeyDown(SDLInput.Key.I) || SDLInput.IsButtonDown(SDLInput.GamepadButton.North))
        {
            string info = Loc.Get("deck_builder_info", _deckName, _deckCards.Count);
            AnnouncementService.Instance.Announce(info);
            return;
        }

        // S: Save deck
        if (SDLInput.IsKeyDown(SDLInput.Key.S))
        {
            TrySaveDeck();
            return;
        }

        // H: Help
        if (SDLInput.IsKeyDown(SDLInput.Key.H))
        {
            AnnounceContext();
            return;
        }

        if (_currentArea == Area.DeckCards)
            HandleDeckCardsInput();
        else
            HandleCollectionInput();
    }

    private void HandleDeckCardsInput()
    {
        if (_deckCards.Count == 0)
        {
            if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsKeyDown(SDLInput.Key.Right)
                || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft)
                || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
            {
                AnnouncementService.Instance.Announce(Loc.Get("deck_builder_deck_empty"));
            }
            return;
        }

        if (_holdRepeater.Check(SDLInput.Key.Left, () => { _deckCardIndex = (_deckCardIndex - 1 + _deckCards.Count) % _deckCards.Count; _detailLevel = 0; AnnounceDeckCard(); })) { }
        else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
        {
            _deckCardIndex = (_deckCardIndex - 1 + _deckCards.Count) % _deckCards.Count;
            _detailLevel = 0;
            AnnounceDeckCard();
        }
        else if (_holdRepeater.Check(SDLInput.Key.Right, () => { _deckCardIndex = (_deckCardIndex + 1) % _deckCards.Count; _detailLevel = 0; AnnounceDeckCard(); })) { }
        else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
        {
            _deckCardIndex = (_deckCardIndex + 1) % _deckCards.Count;
            _detailLevel = 0;
            AnnounceDeckCard();
        }
        else if (TryLetterJumpDeck()) { _detailLevel = 0; }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Home))
        {
            _deckCardIndex = 0; _detailLevel = 0; AnnounceDeckCard();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.End))
        {
            _deckCardIndex = _deckCards.Count - 1; _detailLevel = 0; AnnounceDeckCard();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
        {
            _detailLevel++;
            InspectCurrentCard(_deckCards, _deckCardIndex);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
        {
            if (_detailLevel > 0)
            {
                _detailLevel--;
                if (_detailLevel == 0)
                    AnnounceDeckCard();
                else
                    InspectCurrentCard(_deckCards, _deckCardIndex);
            }
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
        {
            RemoveCard(_deckCardIndex);
        }
    }

    private void HandleCollectionInput()
    {
        // Rescan if needed
        ScanCollectionCards();

        if (_collectionCards.Count == 0)
        {
            if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsKeyDown(SDLInput.Key.Right)
                || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft)
                || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
            {
                AnnouncementService.Instance.Announce(Loc.Get("deck_builder_collection_empty"));
            }
            return;
        }

        if (_holdRepeater.Check(SDLInput.Key.Left, () => { _collectionIndex = (_collectionIndex - 1 + _collectionCards.Count) % _collectionCards.Count; _detailLevel = 0; AnnounceCollectionCard(); })) { }
        else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
        {
            _collectionIndex = (_collectionIndex - 1 + _collectionCards.Count) % _collectionCards.Count;
            _detailLevel = 0;
            AnnounceCollectionCard();
        }
        else if (_holdRepeater.Check(SDLInput.Key.Right, () => { _collectionIndex = (_collectionIndex + 1) % _collectionCards.Count; _detailLevel = 0; AnnounceCollectionCard(); })) { }
        else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
        {
            _collectionIndex = (_collectionIndex + 1) % _collectionCards.Count;
            _detailLevel = 0;
            AnnounceCollectionCard();
        }
        else if (TryLetterJumpCollection()) { _detailLevel = 0; }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Home))
        {
            _collectionIndex = 0; _detailLevel = 0; AnnounceCollectionCard();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.End))
        {
            if (_collectionCards.Count > 0) { _collectionIndex = _collectionCards.Count - 1; _detailLevel = 0; AnnounceCollectionCard(); }
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
        {
            _detailLevel++;
            InspectCurrentCard(_collectionCards, _collectionIndex);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
        {
            if (_detailLevel > 0)
            {
                _detailLevel--;
                if (_detailLevel == 0)
                    AnnounceCollectionCard();
                else
                    InspectCurrentCard(_collectionCards, _collectionIndex);
            }
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
        {
            AddCard(_collectionIndex);
        }
    }

    private void AnnounceDeckCard()
    {
        if (_deckCardIndex < 0 || _deckCardIndex >= _deckCards.Count) return;
        var card = _deckCards[_deckCardIndex];
        string costPower = FormatCostPower(card);
        string msg = card.Name + costPower + ", " + (_deckCardIndex + 1) + " of " + _deckCards.Count;
        AnnouncementService.Instance.Announce(msg);
    }

    private void AnnounceDeckCardDetails()
    {
        if (_deckCardIndex < 0 || _deckCardIndex >= _deckCards.Count) return;
        var card = _deckCards[_deckCardIndex];
        string details = FormatCardDetails(card);
        AnnouncementService.Instance.Announce(details + ". " + Loc.Get("deck_builder_remove_hint"));
    }

    private void AnnounceCollectionCard()
    {
        if (_collectionIndex < 0 || _collectionIndex >= _collectionCards.Count) return;
        var card = _collectionCards[_collectionIndex];
        string costPower = FormatCostPower(card);
        // Check if already in deck
        bool inDeck = false;
        foreach (var dc in _deckCards)
            if (dc.Name.Equals(card.Name, StringComparison.OrdinalIgnoreCase)) { inDeck = true; break; }
        string deckStatus = inDeck ? " (" + Loc.Get("deck_builder_in_deck") + ")" : "";
        string msg = card.Name + costPower + deckStatus + ", " + (_collectionIndex + 1) + " of " + _collectionCards.Count;
        AnnouncementService.Instance.Announce(msg);
    }

    private void AnnounceCollectionCardDetails()
    {
        if (_collectionIndex < 0 || _collectionIndex >= _collectionCards.Count) return;
        var card = _collectionCards[_collectionIndex];
        string details = FormatCardDetails(card);
        AnnouncementService.Instance.Announce(details + ". " + Loc.Get("deck_builder_add_hint"));
    }

    /// <summary>Card detail levels matching battlefield: 1=cost, 2=power, 3=ability.</summary>
    private void InspectCurrentCard(List<DeckCardEntry> cards, int index)
    {
        if (index < 0 || index >= cards.Count) return;
        var card = cards[index];

        switch (_detailLevel)
        {
            case 1: // Cost
                if (card.Cost >= 0)
                    AnnouncementService.Instance.Announce(Loc.Get("bf_detail_cost", card.Cost.ToString()));
                else
                    AnnouncementService.Instance.Announce(Loc.Get("bf_detail_cost_unknown"));
                break;
            case 2: // Power
                if (card.Power >= 0)
                    AnnouncementService.Instance.Announce(Loc.Get("bf_detail_power", card.Power.ToString()));
                else
                    AnnouncementService.Instance.Announce(Loc.Get("bf_detail_power_unknown"));
                break;
            case 3: // Ability
                if (!string.IsNullOrEmpty(card.Ability))
                    AnnouncementService.Instance.Announce(card.Ability);
                else
                    AnnouncementService.Instance.Announce(Loc.Get("bf_detail_no_ability"));
                break;
            default:
                _detailLevel = 3;
                if (!string.IsNullOrEmpty(card.Ability))
                    AnnouncementService.Instance.Announce(card.Ability);
                else
                    AnnouncementService.Instance.Announce(Loc.Get("bf_detail_no_ability"));
                break;
        }
    }

    /// <summary>Brief cost/power for browse announcements.</summary>
    private string FormatCostPower(DeckCardEntry card)
    {
        string result = "";
        if (card.Cost >= 0) result += ", " + card.Cost + " cost";
        if (card.Power >= 0) result += ", " + card.Power + " power";
        return result;
    }

    private string FormatCardDetails(DeckCardEntry card)
    {
        string details = card.Name;
        if (card.Cost >= 0) details += ", " + Loc.Get("deck_builder_cost", card.Cost);
        if (card.Power >= 0) details += ", " + Loc.Get("deck_builder_power", card.Power);
        if (!string.IsNullOrEmpty(card.Ability)) details += ". " + card.Ability;
        return details;
    }

    /// <summary>
    /// Removes a card from the deck by clicking it via SDButton.Click().
    /// In Marvel Snap's deck editor, clicking a card in the deck removes it.
    /// </summary>
    private void RemoveCard(int index)
    {
        if (index < 0 || index >= _deckCards.Count) return;
        var card = _deckCards[index];

        try
        {
            bool clicked = false;

            // SDButton.Click() — Snap's native button activation
            if (card.GameObject != null)
                clicked = UIHelper.ActivateButton(card.GameObject);

            // Fallback: try the button directly
            if (!clicked && card.Button != null)
                clicked = UIHelper.ActivateButton(card.Button);

            if (clicked)
            {
                AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("deck_builder_removed", card.Name));
                AnnouncementService.Instance.Announce(
                    Loc.Get("deck_builder_card_count", _deckCards.Count - 1), AnnouncementPriority.Low);
                DebugLogger.Log(LogCategory.Handler, "DeckBuilderHandler", $"Removed card: {card.Name}");
            }
            else
            {
                AnnouncementService.Instance.Announce(Loc.Get("deck_builder_remove_failed", card.Name));
            }

            // Force immediate rescan after modification
            _forceRescan = true;
            _lastScanTime = 0f;
        }
        catch (Exception ex)
        {
            AnnouncementService.Instance.Announce(Loc.Get("deck_builder_remove_failed", card.Name));
            DebugLogger.Error($"RemoveCard failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a card from the collection to the deck by clicking it via SDButton.Click().
    /// In Marvel Snap's deck editor, clicking a collection card adds it.
    /// </summary>
    private void AddCard(int index)
    {
        if (index < 0 || index >= _collectionCards.Count) return;

        if (_deckCards.Count >= 12)
        {
            AnnouncementService.Instance.Announce(Loc.Get("deck_builder_deck_full"));
            return;
        }

        var card = _collectionCards[index];

        try
        {
            bool clicked = false;

            // SDButton.Click() — Snap's native button activation
            if (card.GameObject != null)
                clicked = UIHelper.ActivateButton(card.GameObject);

            // Fallback: try the button directly
            if (!clicked && card.Button != null)
                clicked = UIHelper.ActivateButton(card.Button);

            if (clicked)
            {
                AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("deck_builder_added", card.Name));
                AnnouncementService.Instance.Announce(
                    Loc.Get("deck_builder_card_count", _deckCards.Count + 1), AnnouncementPriority.Low);
                DebugLogger.Log(LogCategory.Handler, "DeckBuilderHandler", $"Added card: {card.Name}");
            }
            else
            {
                AnnouncementService.Instance.Announce(Loc.Get("deck_builder_add_failed", card.Name));
            }

            // Force immediate rescan after modification
            _forceRescan = true;
            _lastScanTime = 0f;
            _lastCollectionScanTime = 0f;
        }
        catch (Exception ex)
        {
            AnnouncementService.Instance.Announce(Loc.Get("deck_builder_add_failed", card.Name));
            DebugLogger.Error($"AddCard failed: {ex.Message}");
        }
    }

    /// <summary>Tries to save the deck by finding and clicking a Save button.</summary>
    private void TrySaveDeck()
    {
        try
        {
            // Look for save/done buttons
            Button saveBtn = FindButtonByNames("btn_save", "SaveButton", "btn_done", "DoneButton", "ConfirmButton");
            if (saveBtn != null)
            {
                UIHelper.ActivateButton(saveBtn);
                AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("deck_builder_saving"));
                DebugLogger.Log(LogCategory.Handler, "DeckBuilderHandler", "Save button clicked");
                return;
            }

            // Fallback: look for button with "Save" or "Done" text
            Il2CppArrayBase<Button> buttons = Object.FindObjectsOfType<Button>();
            if (buttons != null)
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    try
                    {
                        Button btn = buttons[i];
                        if (btn == null || !((Component)btn).gameObject.activeInHierarchy) continue;
                        string label = UIHelper.GetButtonLabel(btn);
                        if (label != null && (label.Contains("Save", StringComparison.OrdinalIgnoreCase)
                            || label.Contains("Done", StringComparison.OrdinalIgnoreCase)
                            || label.Contains("Confirm", StringComparison.OrdinalIgnoreCase)))
                        {
                            UIHelper.ActivateButton(btn);
                            AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("deck_builder_saving"));
                            return;
                        }
                    }
                    catch { }
                }
            }

            AnnouncementService.Instance.Announce(Loc.Get("deck_builder_no_save"));
        }
        catch (Exception ex)
        {
            AnnouncementService.Instance.Announce(Loc.Get("deck_builder_no_save"));
            DebugLogger.Error($"TrySaveDeck failed: {ex.Message}");
        }
    }

    /// <summary>Tries to close the deck editor by deactivating and returning to MainMenuHandler.</summary>
    private void TryCloseEditor()
    {
        _activated = false;
        _isActive = false;
        AnnouncementService.Instance.Announce(Loc.Get("deck_builder_back"));
        DebugLogger.Log(LogCategory.Handler, "DeckBuilderHandler", "Closed, returning to collection");
    }

    /// <summary>Finds a button by checking multiple possible GameObject names.</summary>
    private Button FindButtonByNames(params string[] names)
    {
        foreach (string name in names)
        {
            try
            {
                GameObject go = GameObject.Find(name);
                if (go != null && go.activeInHierarchy)
                {
                    Button btn = go.GetComponent<Button>();
                    if (btn != null) return btn;
                }
            }
            catch { }
        }
        return null;
    }

    private string ReadDeckName(GameObject root)
    {
        try
        {
            // Search the DeckEditSection hierarchy for deck name
            // root may be Grid_CardSlots — walk up to find the broader context
            GameObject searchRoot = root;
            Transform parent = root.transform;
            while (parent != null)
            {
                if (parent.name.Contains("DeckEditSection") || parent.name.Contains("CollectionDeckDetailsView"))
                {
                    searchRoot = parent.gameObject;
                    break;
                }
                parent = parent.parent;
            }

            // Look for a Text_Name or DeckName TMP element
            Transform nameTransform = UIHelper.FindChildByName(searchRoot.transform, "Text_Name")
                ?? UIHelper.FindChildByName(searchRoot.transform, "DeckName")
                ?? UIHelper.FindChildByName(searchRoot.transform, "Text_DeckName");

            if (nameTransform != null)
            {
                TMP_Text text = nameTransform.GetComponent<TMP_Text>();
                if (text != null)
                {
                    string t = UIHelper.StripRichText(text.text);
                    if (!string.IsNullOrEmpty(t) && t.Length > 1) return t;
                }
            }

            // Also try finding it via global search
            GameObject nameGo = GameObject.Find("Text_Name");
            if (nameGo == null) nameGo = GameObject.Find("DeckName");
            if (nameGo != null)
            {
                TMP_Text text = nameGo.GetComponent<TMP_Text>();
                if (text != null)
                {
                    string t = UIHelper.StripRichText(text.text);
                    if (!string.IsNullOrEmpty(t) && t.Length > 1) return t;
                }
            }
        }
        catch { }
        return Loc.Get("deck_builder_unnamed");
    }

    public void AnnounceContext()
    {
        string deckInfo = Loc.Get("deck_builder_context", _deckName, _deckCards.Count);
        string areaInfo = _currentArea == Area.DeckCards
            ? Loc.Get("deck_builder_area_deck", _deckCards.Count)
            : Loc.Get("deck_builder_area_collection", _collectionCards.Count);
        AnnouncementService.Instance.Announce(deckInfo + " " + areaInfo + " " + Loc.Get("deck_builder_help"), AnnouncementPriority.High);
    }

    /// <summary>Checks if a letter key (A-Z) was pressed and jumps to first collection card starting with that letter.</summary>
    private bool TryLetterJumpCollection()
    {
        if (_collectionCards.Count == 0) return false;
        return TryLetterJump(_collectionCards, ref _collectionIndex, c => c.Name, AnnounceCollectionCard);
    }

    /// <summary>Checks if a letter key (A-Z) was pressed and jumps to first deck card starting with that letter.</summary>
    private bool TryLetterJumpDeck()
    {
        if (_deckCards.Count == 0) return false;
        return TryLetterJump(_deckCards, ref _deckCardIndex, c => c.Name, AnnounceDeckCard);
    }

    /// <summary>Generic letter jump: press A-Z to jump to first item starting with that letter.</summary>
    private bool TryLetterJump<T>(List<T> items, ref int index, Func<T, string> getName, Action announce)
    {
        // Check each letter key A-Z (SDL key codes: A=65..Z=90)
        for (int i = 0; i < 26; i++)
        {
            SDLInput.Key key = (SDLInput.Key)(65 + i); // A through Z
            if (!SDLInput.IsKeyDown(key)) continue;

            char letter = (char)('A' + i);
            // Find first item starting with this letter (from current position forward, wrapping)
            for (int j = 0; j < items.Count; j++)
            {
                int candidateIdx = (index + 1 + j) % items.Count;
                string name = getName(items[candidateIdx]);
                if (!string.IsNullOrEmpty(name) && char.ToUpper(name[0]) == letter)
                {
                    index = candidateIdx;
                    announce();
                    return true;
                }
            }
            // No card found for this letter
            AnnouncementService.Instance.Announce(Loc.Get("deck_builder_no_letter", letter.ToString()));
            return true;
        }
        return false;
    }
}
