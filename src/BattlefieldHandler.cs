using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using Il2CppApp.Game.UI.Button;
using Il2CppCubeUnity.App.Game;
using Il2CppCubeUnity.App.Game.SpeechBubble;
using Il2CppCubeUnity.App.Tutorials;
using DefaultTutorialState = Il2CppCubeUnity.App.Game.DefaultScenarioTutorialBehavior.DefaultTutorialState;
using Il2CppCubeUnity.App.View;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSecondDinner.CubeRendering.Card;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Manages in-game accessibility features during a match.
/// Handles hand/location navigation, card playing, and opponent state announcements.
/// </summary>
public class BattlefieldHandler : IScreenNavigator
{
    private enum FocusArea
    {
        Hand,
        Locations
    }

    private enum PlayState
    {
        Browsing,
        CardSelected
    }

    private struct POINT
    {
        public int X;
        public int Y;
    }

    private FocusArea _area = FocusArea.Hand;
    private PlayState _playState = PlayState.Browsing;
    private int _handIndex = 0;
    private int _locationIndex = 0;
    private int _detailLevel = 0; // 0=name, 1+=deeper details

    private readonly List<CardView> _handCards = new List<CardView>();
    private readonly List<LocationView> _locations = new List<LocationView>();

    // Key hold repeater for fast navigation (hold arrow → auto-repeat after 0.5s)
    private readonly KeyHoldRepeater _holdRepeater = new KeyHoldRepeater();

    private GameInputManager _gim;
    private HandZoneController _handZone;
    private CardView _selectedCard;

    private string _lastTutorialText = "";
    private string _lastInstructionText = "";
    private HashSet<string> _announcedTooltips = new HashSet<string>();
    private readonly Dictionary<int, string> _trackedTutorialTexts = new Dictionary<int, string>();
    private int _lastHandCount = -1;
    private bool _tapToContinueAnnounced = false;
    private bool _tutorialTextsInitialized = false;
    private float _tutorialInitStartTime = 0f;
    private string _lastTapScanState = "";

    private static bool _harmonyPatched = false;
    private static bool _stepMapHooked = false;
    private static readonly Queue<string> _pendingTooltipTexts = new Queue<string>();
    private static readonly Queue<string> _pendingInstructionTexts = new Queue<string>();

    private float _lastScanTime = 0f;
    private bool _wasInGame = false;

    // In-game popup/dialog state
    private bool _inPopup = false;
    private readonly List<Button> _popupButtons = new List<Button>();
    private int _popupFocusIndex = -1;
    private string _lastPopupText = "";

    // Location card tracking for opponent announcements
    // Track board card EntityIds we've already announced
    private readonly HashSet<int> _knownBoardCardEntityIds = new HashSet<int>();
    // Track all EntityIds we've seen in our hand (our cards)
    private readonly HashSet<int> _ourCardEntityIds = new HashSet<int>();
    private readonly Queue<string> _pendingOpponentAnnouncements = new Queue<string>();

    // Rollback detection: after a "successful" play, check if the card returns
    private string _lastPlayedCardName = null;
    private int _lastPlayedEntityId = -1;
    private int _handCountAfterPlay = -1;
    private float _rollbackConfirmStartTime = 0f; // Time when rollback tracking started
    private float _playVerifyTime = 0f; // When to verify a mouse-drag play actually worked
    private int _playVerifyExpectedCount = -1; // Expected hand count if play succeeded

    // Opponent card detection: don't announce before the game has started properly
    private bool _gameReadyForOpponentDetection = false;
    private int _turnChangeCount = 0;
    private readonly TurnAnnounceGate _turnGate = new TurnAnnounceGate();
    private float _opponentScanCooldownUntil = 0f; // Block scanning briefly after turn changes (animations)

    // Card draw tracking: EntityIds of cards that were in hand last scan
    private readonly HashSet<int> _previousHandEntityIds = new HashSet<int>();

    // Deferred drawn cards: stored for on-demand announcement via D key
    private string _deferredDrawnCardsMessage = null;

    // Opponent name retry: name is often empty at game start
    private bool _opponentNameAnnounced = false;

    // Turn phase tracking: detect "your turn" vs "waiting for opponent"
    private bool _isPlayerTurn = false;
    private bool _turnPhaseAnnounced = false;
    private float _lastTurnPhaseCheck = 0f;

    // Turn timer warning: announce once when timer is running low
    private bool _timerWarningAnnounced = false;
    private TurnTimer _turnTimer;
    private bool _turnTimerWarningFired = false; // Via TurnTimer component (not tooltip)
    private bool _turnTimerUrgentFired = false;
    private float _lastTimerCheck = 0f;

    // Retreat confirmation: require double-press R within timeout
    private bool _retreatPending = false;
    private float _retreatPendingTime = 0f;
    private const float RetreatConfirmTimeout = 3f;

    private float _forcePopupScanAfter = 0f;
    private const float ScanInterval = 0.25f;
    private const uint MOUSEEVENTF_MOVE = 1u;
    private const uint MOUSEEVENTF_LEFTDOWN = 2u;
    private const uint MOUSEEVENTF_LEFTUP = 4u;

    private bool _active = false;
    public bool IsActive => _active;

    public string NavigatorId => "Battlefield";

    public int Priority => 900;

    public void Update()
    {
        bool shouldScan = UnityEngine.Time.time - _lastScanTime > ScanInterval;
        if (shouldScan)
        {
            Scan();
            _lastScanTime = UnityEngine.Time.time;
        }
        if ((Object)(object)_gim == (Object)null)
        {
            if (_wasInGame)
            {
                OnGameLeft();
                _wasInGame = false;
            }
            _active = false;
            return;
        }
        _active = true;
        // Only scan for popups when actually in a game
        // Also force scan shortly after Escape press to detect pause/retreat menu
        bool forcePopupScan = _forcePopupScanAfter > 0f && Time.time >= _forcePopupScanAfter;
        if (forcePopupScan) _forcePopupScanAfter = 0f;
        if (shouldScan || forcePopupScan) ScanForPopup();
        if (!_wasInGame)
        {
            OnGameEntered();
            _wasInGame = true;
        }
        // Check for game over (Collect Rewards / Next button)
        CheckGameOver();
        // Check turn phase (your turn vs waiting for opponent)
        if (!_gameOverAnnounced) CheckTurnPhase();
        // Check turn timer for warnings
        if (!_gameOverAnnounced) CheckTurnTimer();
        if (_inPopup)
        {
            ProcessPopupInput();
        }
        else if (_gameOverAnnounced)
        {
            // Game is over — handle reward collection and upgrade animations.
            // E: collect rewards / advance
            if (SDLInput.IsKeyDown(SDLInput.Key.E) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
            {
                TryEndTurn();
            }
            // Space or Enter: skip upgrade animation if active
            else if (SDLInput.IsKeyDown(SDLInput.Key.Space) || SDLInput.IsKeyDown(SDLInput.Key.Return))
            {
                TrySkipUpgradeAnimation();
            }
            // Scan for upgrade animation and announce what's happening
            CheckPostGameScreen();
        }
        else
        {
            ProcessInput();
        }
    }

    public void AnnounceContext()
    {
        if ((Object)(object)_gim == (Object)null)
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_not_in_game"), AnnouncementPriority.Normal);
            return;
        }
        if (!string.IsNullOrEmpty(_lastInstructionText))
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_tutorial_instruction", _lastInstructionText), AnnouncementPriority.Normal);
        }
        switch (_area)
        {
            case FocusArea.Hand:
                AnnounceCurrentCard();
                break;
            case FocusArea.Locations:
                AnnounceCurrentLocation();
                break;
        }
        AnnouncementService.Instance.Announce(Loc.Get("bf_help"), AnnouncementPriority.Low);
    }

    public void Deactivate()
    {
        _active = false;
        _holdRepeater.Reset();
        // Clear popup state but keep Harmony patches and tutorial state
        _inPopup = false;
        _popupButtons.Clear();
        _popupFocusIndex = -1;
        _lastPopupText = "";
        _forcePopupScanAfter = 0f;
    }

    public void OnSceneChanged(string sceneName)
    {
        _active = false;
        _gim = null;
        _handZone = null;
        _handCards.Clear();
        _locations.Clear();
        _handIndex = 0;
        _locationIndex = 0;
        _selectedCard = null;
        _playState = PlayState.Browsing;
        _lastTutorialText = "";
        _lastInstructionText = "";
        _announcedTooltips.Clear();
        _trackedTutorialTexts.Clear();
        _lastHandCount = -1;
        _tapToContinueAnnounced = false;
        _tutorialTextsInitialized = false;
        _tutorialInitStartTime = 0f;

        _wasInGame = false;
        _inPopup = false;
        _popupButtons.Clear();
        _popupFocusIndex = -1;
        _lastPopupText = "";
        _forcePopupScanAfter = 0f;
        _knownBoardCardEntityIds.Clear();
        _ourCardEntityIds.Clear();
        _previousHandEntityIds.Clear();
        _pendingOpponentAnnouncements.Clear();
        _deferredDrawnCardsMessage = null;
        _lastPlayedCardName = null;
        _lastPlayedEntityId = -1;
        _handCountAfterPlay = -1;
        _rollbackConfirmStartTime = 0f;
        _playVerifyTime = 0f;
        _playVerifyExpectedCount = -1;
        _gameReadyForOpponentDetection = false;
        _opponentScanCooldownUntil = 0f;
        _turnChangeCount = 0;
        _turnGate.Reset();
        _gameOverAnnounced = false;
        _gameOverCheckTime = 0f;
        _lastPostGameCheck = 0f;
        _lastPostGameAnnouncement = "";
        _opponentNameAnnounced = false;
        _retreatPending = false;
        _isPlayerTurn = false;
        _turnPhaseAnnounced = false;
        _lastTurnPhaseCheck = 0f;
        _timerWarningAnnounced = false;
        _turnTimer = null;
        _turnTimerWarningFired = false;
        _turnTimerUrgentFired = false;
        _endTurnGuardPending = false;
    }

    private void Scan()
    {
        try
        {
            _gim = UIHelper.FindComponent<GameInputManager>();
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "GameInputManager search failed: " + ex.Message);
            _gim = null;
        }
        if ((Object)(object)_gim == (Object)null) return;

        if (!_stepMapHooked)
        {
            SetupStepMapHooks();
        }
        // Auto-dismiss "Never Seen Before!" card encounter popups — they block all input
        DismissNewCardEncounterPopup();
        ScanHandCards();
        ScanLocations();
        ScanLocationCards();
        ScanTutorialTooltip();

        // Announce opponent card plays — batch into one message to avoid interrupting
        // Defer during turn-start cooldown so player hears turn info first and can act immediately
        if (!ModSettings.Instance.OpponentAnnouncements)
            _pendingOpponentAnnouncements.Clear();
        else if (_pendingOpponentAnnouncements.Count > 0 && Time.time > _opponentScanCooldownUntil)
        {
            StringBuilder opponentMsg = new StringBuilder();
            while (_pendingOpponentAnnouncements.Count > 0)
            {
                if (opponentMsg.Length > 0) opponentMsg.Append(". ");
                opponentMsg.Append(_pendingOpponentAnnouncements.Dequeue());
            }
            // Use Low priority so it doesn't cut off current speech
            AnnouncementService.Instance.Announce(opponentMsg.ToString(), AnnouncementPriority.Low);
        }
    }

    /// <summary>Auto-dismiss "Never Seen Before!" card encounter popups that block all game input.</summary>
    private void DismissNewCardEncounterPopup()
    {
        try
        {
            // Find VfxDefaultNewCardEncountered(Clone) GameObjects
            Il2CppArrayBase<Canvas> canvases = Object.FindObjectsOfType<Canvas>();
            if (canvases == null) return;

            for (int i = 0; i < canvases.Count; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || !((Component)canvas).gameObject.activeInHierarchy) continue;

                // Check if this canvas is a child of VfxDefaultNewCardEncountered
                Transform t = ((Component)canvas).transform;
                bool isNewCardPopup = false;
                while ((Object)(object)t != (Object)null)
                {
                    string pName = ((Object)t.gameObject).name;
                    if (pName.Contains("VfxDefaultNewCardEncountered", StringComparison.Ordinal))
                    {
                        isNewCardPopup = true;
                        break;
                    }
                    t = t.parent;
                }
                if (!isNewCardPopup) continue;

                // Found it — click BackgroundCloseButton to dismiss
                Il2CppArrayBase<Button> btns = ((Component)canvas).gameObject.GetComponentsInChildren<Button>(false);
                if (btns == null) continue;

                for (int j = 0; j < btns.Count; j++)
                {
                    Button btn = btns[j];
                    if (btn == null) continue;
                    string goName = ((Object)((Component)btn).gameObject).name;
                    if (goName.Contains("BackgroundClose", StringComparison.OrdinalIgnoreCase) ||
                        goName.Contains("Header_CardDetails", StringComparison.OrdinalIgnoreCase))
                    {
                        UIHelper.ClickButton(btn);
                        DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                            "Auto-dismissed 'Never Seen Before' popup via " + goName);
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                "DismissNewCardEncounterPopup failed: " + ex.Message);
        }
    }


    private void ScanHandCards()
    {
        _handCards.Clear();
        try
        {
            if ((Object)(object)_handZone == (Object)null)
            {
                _handZone = UIHelper.FindComponent<HandZoneController>();
                if ((Object)(object)_handZone != (Object)null)
                {
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "HandZoneController found");
                }
            }
            Il2CppArrayBase<CardView> cards = Object.FindObjectsOfType<CardView>();
            if (cards == null || cards.Count == 0)
            {
                Il2CppArrayBase<CardRenderer> renderers = Object.FindObjectsOfType<CardRenderer>();
                if (renderers != null)
                {
                    for (int i = 0; i < renderers.Count; i++)
                    {
                        CardRenderer cr = renderers[i];
                        if ((Object)(object)cr == (Object)null || !((Component)cr).gameObject.activeInHierarchy) continue;
                        CardView cv = ((Il2CppObjectBase)cr).TryCast<CardView>();
                        if ((Object)(object)cv != (Object)null && IsInHandZone(cv))
                        {
                            _handCards.Add(cv);
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < cards.Count; i++)
                {
                    CardView cv = cards[i];
                    if ((Object)(object)cv != (Object)null && ((Component)cv).gameObject.activeInHierarchy && IsInHandZone(cv))
                    {
                        _handCards.Add(cv);
                    }
                }
            }
            if (_handCards.Count > 0)
            {
                _handCards.Sort((CardView a, CardView b) => ((Component)a).transform.position.x.CompareTo(((Component)b).transform.position.x));
            }
            if (_handIndex >= _handCards.Count)
            {
                _handIndex = (_handCards.Count > 0) ? (_handCards.Count - 1) : 0;
            }
            // Track our card EntityIds
            for (int ci = 0; ci < _handCards.Count; ci++)
            {
                try { _ourCardEntityIds.Add(_handCards[ci].EntityId); } catch { }
            }
            // Rollback detection: if the hand grew back after a "successful" play, the play was rejected
            if (_lastPlayedCardName != null && _handCountAfterPlay >= 0)
            {
                if (_handCards.Count > _handCountAfterPlay)
                {
                    // Card came back — play was rolled back by the game
                    AnnouncementService.Instance.Announce(Loc.Get("bf_play_rolled_back", _lastPlayedCardName), AnnouncementPriority.Immediate);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                        $"Play rolled back: {_lastPlayedCardName} returned to hand (hand={_handCards.Count} > expected={_handCountAfterPlay})");
                    _lastPlayedCardName = null;
                    _lastPlayedEntityId = -1;
                    _handCountAfterPlay = -1;
                    _rollbackConfirmStartTime = 0f;
                    // Announce tutorial hints explaining why the play was rejected
                    AnnounceActiveTutorialHints();
                }
                else
                {
                    // Hand hasn't grown — but wait before confirming
                    // The game may roll back the play with a delay
                    if (Time.time - _rollbackConfirmStartTime >= 6.0f)
                    {
                        // Card stayed on board long enough — play succeeded
                        DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                            $"Play confirmed after {Time.time - _rollbackConfirmStartTime:F1}s: {_lastPlayedCardName}");
                        _lastPlayedCardName = null;
                        _lastPlayedEntityId = -1;
                        _handCountAfterPlay = -1;
                        _rollbackConfirmStartTime = 0f;
                    }
                }
            }
            // Delayed play verification: if mouse drag was used, check hand count after delay
            if (_playVerifyTime > 0f && Time.time >= _playVerifyTime)
            {
                if (_handCards.Count > _playVerifyExpectedCount && _lastPlayedCardName != null)
                {
                    // Card is still in hand — the play failed silently
                    AnnouncementService.Instance.Announce(Loc.Get("bf_play_failed_silent", _lastPlayedCardName), AnnouncementPriority.Immediate);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                        $"Play verification failed: {_lastPlayedCardName} still in hand (expected {_playVerifyExpectedCount}, got {_handCards.Count})");
                    _lastPlayedCardName = null;
                    _lastPlayedEntityId = -1;
                    _handCountAfterPlay = -1;
                    _rollbackConfirmStartTime = 0f;
                }
                _playVerifyTime = 0f;
                _playVerifyExpectedCount = -1;
            }
            // Log hand contents when count changes for debugging
            if (_handCards.Count != _lastHandCount)
            {
                StringBuilder cardNames = new StringBuilder();
                for (int ci = 0; ci < _handCards.Count; ci++)
                {
                    if (ci > 0) cardNames.Append(", ");
                    try { cardNames.Append(GetCardName(_handCards[ci])); }
                    catch { cardNames.Append("?"); }
                }
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                    $"Hand changed: {_handCards.Count} cards [{cardNames}]");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "ScanHandCards failed: " + ex.Message);
        }
    }

    /// <summary>Stores names of newly drawn cards for on-demand announcement via D key.</summary>
    private void StoreDrawnCards()
    {
        try
        {
            List<string> newCardNames = new List<string>();
            for (int i = 0; i < _handCards.Count; i++)
            {
                int entityId = _handCards[i].EntityId;
                if (!_previousHandEntityIds.Contains(entityId))
                {
                    string name = GetCardName(_handCards[i]);
                    if (!string.IsNullOrEmpty(name))
                        newCardNames.Add(name);
                }
            }
            if (newCardNames.Count > 0)
            {
                _deferredDrawnCardsMessage = Loc.Get("bf_card_drawn", string.Join(", ", newCardNames));
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                    "Deferred draw: " + string.Join(", ", newCardNames));
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "StoreDrawnCards failed: " + ex.Message);
        }
    }

    /// <summary>Announces stored drawn cards on demand (D key).</summary>
    private void AnnounceStoredDrawnCards()
    {
        if (!string.IsNullOrEmpty(_deferredDrawnCardsMessage))
        {
            AnnouncementService.Instance.Announce(_deferredDrawnCardsMessage, AnnouncementPriority.Immediate);
        }
        else
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_no_new_draws"), AnnouncementPriority.Normal);
        }
    }

    private bool IsInHandZone(CardView card)
    {
        try
        {
            // Primary: check if the card's parent transform hierarchy contains "Hand"
            // This is far more reliable than viewport-position heuristics
            Transform t = ((Component)card).transform;
            for (int depth = 0; depth < 6 && t != null; depth++)
            {
                string name = ((Object)t.gameObject).name;
                if (name.Contains("Hand", StringComparison.OrdinalIgnoreCase))
                    return true;
                // If parent contains "Board", "Location", "Zone" — it's on the board, not in hand
                if (name.Contains("Board", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Location", StringComparison.OrdinalIgnoreCase))
                    return false;
                t = t.parent;
            }

            // Fallback: viewport-based heuristic (wider check)
            Camera main = Camera.main;
            if ((Object)(object)main == (Object)null) return false;
            Vector3 vp = main.WorldToViewportPoint(((Component)card).transform.position);
            if (vp.z <= 0f) return false;
            if (vp.x < -0.1f || vp.x > 1.1f) return false;
            if (vp.y < 0.25f && vp.y > -0.1f) return true;
        }
        catch { }
        return false;
    }

    /// <summary>Checks if a CardView is inside an object pool (not actually on the board).</summary>
    private bool IsInObjectPool(CardView card)
    {
        Transform t = ((Component)card).transform;
        for (int depth = 0; depth < 8 && t != null; depth++)
        {
            string name = ((Object)t.gameObject).name;
            if (name.Contains("ObjectPool", StringComparison.Ordinal) ||
                name.Contains("CardPool", StringComparison.Ordinal))
                return true;
            t = t.parent;
        }
        return false;
    }

    private void ScanLocations()
    {
        _locations.Clear();
        try
        {
            Il2CppArrayBase<LocationView> locs = Object.FindObjectsOfType<LocationView>();
            if (locs == null) return;
            // Deduplicate by EntityId to avoid duplicate views for the same location
            HashSet<int> seenEntityIds = new HashSet<int>();
            for (int i = 0; i < locs.Count; i++)
            {
                LocationView lv = locs[i];
                if ((Object)(object)lv != (Object)null && ((Component)lv).gameObject.activeInHierarchy)
                {
                    int dedupId;
                    try
                    {
                        dedupId = lv.EntityId;
                    }
                    catch
                    {
                        dedupId = ((Object)((Component)lv).gameObject).GetInstanceID();
                    }
                    if (seenEntityIds.Add(dedupId))
                    {
                        _locations.Add(lv);
                    }
                }
            }
            _locations.Sort((LocationView a, LocationView b) => ((Component)a).transform.position.x.CompareTo(((Component)b).transform.position.x));
            if (_locationIndex >= _locations.Count)
            {
                _locationIndex = (_locations.Count > 0) ? (_locations.Count - 1) : 0;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "ScanLocations failed: " + ex.Message);
        }
    }

    /// <summary>Scan cards at each location to detect opponent plays.</summary>
    private void ScanLocationCards()
    {
        if (_locations.Count == 0) return;
        // Don't detect opponent cards until the game has properly started
        // (after the first turn change when cards are dealt)
        if (!_gameReadyForOpponentDetection) return;
        // Wait for reveal animations to finish after turn changes
        // Do NOT snapshot during cooldown — that would silently absorb opponent cards
        if (Time.time < _opponentScanCooldownUntil) return;
        try
        {
            // Find ALL CardViews in the scene — same approach as ScanHandCards
            Il2CppArrayBase<CardView> allCards = Object.FindObjectsOfType<CardView>();
            if (allCards == null || allCards.Count == 0) return;

            // Build location X positions for proximity matching
            List<(int entityId, string name, float x)> locPositions = new List<(int, string, float)>();
            for (int li = 0; li < _locations.Count; li++)
            {
                LocationView loc = _locations[li];
                if ((Object)(object)loc == (Object)null) continue;
                try
                {
                    locPositions.Add((loc.EntityId, GetLocationName(loc),
                        ((Component)loc).transform.position.x));
                }
                catch { }
            }
            if (locPositions.Count == 0) return;

            for (int i = 0; i < allCards.Count; i++)
            {
                CardView cv = allCards[i];
                if ((Object)(object)cv == (Object)null) continue;
                if (!((Component)cv).gameObject.activeInHierarchy) continue;

                // Skip cards in hand
                if (IsInHandZone(cv)) continue;

                // Skip cards in object pools (not actually on the board)
                // Check both the card's own name AND ancestor names, since pool cards
                // are at paths like ObjectPoolManager/ObjectPool_CardView/CardView[X]
                try
                {
                    if (IsInObjectPool(cv)) continue;
                }
                catch { }

                int entityId;
                try { entityId = cv.EntityId; } catch { continue; }

                // Skip invalid entities (pool cards, etc.)
                if (entityId <= 0) continue;

                // Skip if we already know about this board card
                if (_knownBoardCardEntityIds.Contains(entityId)) continue;

                // Skip if this is one of our cards (seen in our hand before)
                if (_ourCardEntityIds.Contains(entityId))
                {
                    _knownBoardCardEntityIds.Add(entityId);
                    continue;
                }

                // This is a new opponent card on the board — find nearest location
                _knownBoardCardEntityIds.Add(entityId);
                try
                {
                    // Try to get the card name from the Card data model first
                    string cardName = GetBoardCardName(cv);
                    if (string.IsNullOrEmpty(cardName) || cardName == "Card" ||
                        cardName == "Unknown card" || cardName.Contains("Card View")) continue;

                    float cardX = ((Component)cv).transform.position.x;
                    string nearestLocName = "unknown location";
                    float minDist = float.MaxValue;
                    for (int li = 0; li < locPositions.Count; li++)
                    {
                        float dist = Math.Abs(cardX - locPositions[li].x);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearestLocName = locPositions[li].name;
                        }
                    }

                    // Skip cards at unrevealed locations — they're still animating
                    if (nearestLocName.Contains("revealed", StringComparison.OrdinalIgnoreCase) ||
                        nearestLocName.Contains("next turn", StringComparison.OrdinalIgnoreCase))
                    {
                        // Remove from known so we can re-detect after reveal
                        _knownBoardCardEntityIds.Remove(entityId);
                        continue;
                    }

                    string cardInfo = GetCardInfo(cv);
                    string announcement = Loc.Get("bf_opponent_played", cardName);
                    if (!string.IsNullOrEmpty(cardInfo)) announcement += ", " + cardInfo;
                    announcement += " " + Loc.Get("bf_opponent_at_location", nearestLocName);
                    _pendingOpponentAnnouncements.Enqueue(announcement);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                        $"Opponent card: {cardName} (entity={entityId}) at {nearestLocName}");
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "ScanLocationCards failed: " + ex.Message);
        }
    }

    /// <summary>Snapshot all current board cards so they aren't announced as new opponent plays.</summary>
    private void SnapshotExistingBoardCards()
    {
        try
        {
            Il2CppArrayBase<CardView> allCards = Object.FindObjectsOfType<CardView>();
            if (allCards == null) return;
            for (int i = 0; i < allCards.Count; i++)
            {
                CardView cv = allCards[i];
                if ((Object)(object)cv == (Object)null) continue;
                if (!((Component)cv).gameObject.activeInHierarchy) continue;
                if (IsInHandZone(cv)) continue;
                try
                {
                    int entityId = cv.EntityId;
                    if (entityId > 0)
                        _knownBoardCardEntityIds.Add(entityId);
                }
                catch { }
            }
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                $"Snapshotted {_knownBoardCardEntityIds.Count} existing board cards");
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "SnapshotExistingBoardCards failed: " + ex.Message);
        }
    }

    private void ScanTutorialTooltip()
    {
        try
        {
            // Track hand count for detecting turn changes — skip if game is over
            if (!_gameOverAnnounced && _handCards.Count != _lastHandCount)
            {
                if (_lastHandCount >= 0)
                {
                    // Turn changed — only clear LocString cache so turn-specific instructions
                    // can re-announce. Do NOT clear _announcedTooltips — static tooltip texts
                    // (like "Cards cost Energy") should not be re-read every turn.
                    _lastLocString = "";
                    _turnChangeCount++;
                    _timerWarningAnnounced = false;
                    _turnTimerWarningFired = false;
                    _turnTimerUrgentFired = false;
                    _deferredDrawnCardsMessage = null;
                    // Cooldown: wait for card reveal animations to finish before scanning for opponent plays.
                    // Cards fly/animate for ~3-5 seconds during the reveal phase.
                    _opponentScanCooldownUntil = Time.time + 2.0f;
                    // Enable opponent detection after the first turn change (cards dealt and resolved)
                    if (!_gameReadyForOpponentDetection && _turnChangeCount >= 1)
                    {
                        _gameReadyForOpponentDetection = true;
                        // Snapshot all current board cards so we don't announce pre-existing ones
                        SnapshotExistingBoardCards();
                        DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                            "Opponent detection enabled after turn change");
                    }
                    // Retry opponent name if not yet announced
                    if (!_opponentNameAnnounced && _turnChangeCount >= 1)
                    {
                        string oppName = ReadOpponentName();
                        if (!string.IsNullOrEmpty(oppName))
                        {
                            _opponentNameAnnounced = true;
                            AnnouncementService.Instance.Announce($"Playing against {oppName}", AnnouncementPriority.High);
                            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                                "Opponent name (delayed): " + oppName);
                        }
                    }
                    // Auto-announce turn info when a new turn starts (after initial deal)
                    if (_turnChangeCount >= 2 && ModSettings.Instance.AutoTurnAnnounce)
                    {
                        AutoAnnounceTurnStart();
                    }
                    // Store drawn cards for on-demand announcement via D key (skip initial deal)
                    if (_gameReadyForOpponentDetection && _handCards.Count > _lastHandCount && _previousHandEntityIds.Count > 0)
                    {
                        StoreDrawnCards();
                    }
                }
                // Update previous hand EntityIds for next comparison
                _previousHandEntityIds.Clear();
                for (int ci = 0; ci < _handCards.Count; ci++)
                {
                    try { _previousHandEntityIds.Add(_handCards[ci].EntityId); } catch { }
                }
                _lastHandCount = _handCards.Count;
            }

            // Process pending instruction texts (from StepMap hooks, LocString, TapToContinue)
            while (_pendingInstructionTexts.Count > 0)
            {
                string rawInstr = _pendingInstructionTexts.Dequeue();
                if (IsFlavorText(rawInstr)) continue;
                string text = UIHelper.StripRichText(rawInstr);
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (_announcedTooltips.Add(text))
                {
                    _lastInstructionText = text;
                    _lastTutorialText = text;
                    if (ModSettings.Instance.TutorialMessages)
                    {
                        AnnouncementService.Instance.Announce(text, AnnouncementPriority.Low);
                        DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Instruction: " + text);
                    }
                }
            }

            // Process pending tooltip texts (from Harmony patches)
            while (_pendingTooltipTexts.Count > 0)
            {
                string rawText = _pendingTooltipTexts.Dequeue();
                // Filter flavor text before stripping tags (check <i> wrapping)
                if (IsFlavorText(rawText)) continue;
                // Strip rich text tags (e.g., <sprite name="icn_score">)
                string text = UIHelper.StripRichText(rawText);
                if (string.IsNullOrWhiteSpace(text)) continue;
                // Skip standalone number/fraction tooltips like "1 / 6" (duplicate of "turn 1/6")
                if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+\s*/\s*\d+$")) continue;
                // Skip pure numeric strings (credit counters, XP values, etc.)
                if (IsNumericOrCurrencyLabel(text)) continue;
                // Timer countdown tooltips (Timer: 5, Timer: 4, etc.)
                // Warn at 15 seconds (first warning) and again at 5 seconds (urgent)
                if (text.StartsWith("Timer:", StringComparison.OrdinalIgnoreCase))
                {
                    string numPart = text.Substring(6).Trim();
                    if (int.TryParse(numPart, out int seconds))
                    {
                        if (!_timerWarningAnnounced && seconds <= 15)
                        {
                            _timerWarningAnnounced = true;
                            AnnouncementService.Instance.Announce(seconds + " seconds left.", AnnouncementPriority.Immediate);
                        }
                        else if (_timerWarningAnnounced && seconds <= 5)
                        {
                            // Urgent warning — announce exact seconds
                            AnnouncementService.Instance.Announce(seconds + " seconds!", AnnouncementPriority.Immediate);
                        }
                    }
                    continue;
                }
                // Skip turn indicator tooltips (already available via T key)
                if (text.StartsWith("turn ", StringComparison.OrdinalIgnoreCase) && text.Contains("/")) continue;
                // Skip card cosmetic/rarity labels (Variant names, borders, finishes)
                if (IsCardCosmeticText(text)) continue;
                if (_announcedTooltips.Add(text))
                {
                    _lastTutorialText = text;
                    AnnouncementService.Instance.Announce(text, AnnouncementPriority.Low);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Tooltip: " + text);
                }
            }

            // Scan SpeechBubbleView instances (flavor text from characters)
            Il2CppArrayBase<SpeechBubbleView> bubbles = Object.FindObjectsOfType<SpeechBubbleView>();
            if (bubbles != null)
            {
                for (int i = 0; i < bubbles.Count; i++)
                {
                    SpeechBubbleView bubble = bubbles[i];
                    if ((Object)(object)bubble == (Object)null || !((Component)bubble).gameObject.activeInHierarchy) continue;

                    MeshRenderer meshRenderer = bubble._SpeechBubbleMeshRenderer;
                    if ((Object)(object)meshRenderer == (Object)null || !((Renderer)meshRenderer).enabled) continue;

                    TextMeshPro textMeshPro = bubble._TextMeshPro;
                    if ((Object)(object)textMeshPro == (Object)null || ((Graphic)textMeshPro).color.a < 0.1f) continue;

                    string rawBubbleText = ((TMP_Text)textMeshPro).text;
                    if (string.IsNullOrWhiteSpace(rawBubbleText) || rawBubbleText.Contains("{Missing Entry}")) continue;
                    // Filter flavor text (character quotes wrapped in <i> or quotes)
                    if (IsFlavorText(rawBubbleText)) continue;

                    string text = UIHelper.StripRichText(rawBubbleText.Trim());
                    if (text.Length >= 3 && _announcedTooltips.Add(text))
                    {
                        _lastTutorialText = text;
                        AnnouncementService.Instance.Announce(text, AnnouncementPriority.Low);
                        DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "SpeechBubble: " + text);
                    }
                }
            }

            // Scan VfxScenarioTutorialAction._TapToContinueText for instruction text
            ScanTapToContinueText();

            // Scan all visible TMP_Text under tutorial/tooltip parent objects
            ScanTutorialParentTexts();
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "ScanTutorialTooltip failed: " + ex.Message);
        }
    }

    private void ScanTapToContinueText()
    {
        try
        {
            VfxScenarioTutorialAction action = UIHelper.FindComponent<VfxScenarioTutorialAction>();
            if ((Object)(object)action == (Object)null)
            {
                _tapToContinueAnnounced = false;
                return;
            }

            // Check if tap-to-continue text is visible (tutorial is waiting for a tap)
            TextMeshProUGUI tapText = action._TapToContinueText;
            bool tapVisible = false;

            if ((Object)(object)tapText != (Object)null
                && ((Component)tapText).gameObject.activeInHierarchy
                && ((Graphic)tapText).color.a >= 0.1f)
            {
                tapVisible = true;
            }

            // Also check coroutine as fallback
            if (!tapVisible && (Object)(object)action._tapToContinueCoroutine != (Object)null)
            {
                tapVisible = true;
            }

            // Only log when state changes to reduce spam
            string scanState = $"text={tapVisible},co={(Object)(object)action._tapToContinueCoroutine != (Object)null},et={action._WaitStateCanEndTurn},drag={action._WaitStateCanDragCard}";
            if (scanState != _lastTapScanState)
            {
                _lastTapScanState = scanState;
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TapToContinue scan changed: " + scanState);
            }

            if (tapVisible && !_tapToContinueAnnounced)
            {
                _tapToContinueAnnounced = true;
                _lastInstructionText = Loc.Get("bf_tap_to_continue");
                AnnouncementService.Instance.Announce(Loc.Get("bf_tap_to_continue"), AnnouncementPriority.Low);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TapToContinue state detected");
            }
            else if (!tapVisible && _tapToContinueAnnounced)
            {
                _tapToContinueAnnounced = false;
            }

            // Also read the TapToContinueText content if it has readable text
            if ((Object)(object)tapText != (Object)null
                && ((Component)tapText).gameObject.activeInHierarchy
                && ((Graphic)tapText).color.a >= 0.1f)
            {
                string rawTapText = ((TMP_Text)tapText).text;
                if (!string.IsNullOrWhiteSpace(rawTapText) && !rawTapText.Contains("{Missing Entry}") && !IsFlavorText(rawTapText))
                {
                    string text = UIHelper.StripRichText(rawTapText.Trim());
                    if (text.Length >= 3 && _announcedTooltips.Add(text))
                    {
                        _lastInstructionText = text;
                        _lastTutorialText = text;
                        AnnouncementService.Instance.Announce(text, AnnouncementPriority.Low);
                        DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TapToContinueText: " + text);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "ScanTapToContinueText failed: " + ex.Message);
        }
    }

    private void ScanTutorialParentTexts()
    {
        try
        {
            Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
            if (texts == null) return;

            HashSet<int> seenIds = new HashSet<int>();

            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text tmp = texts[i];
                if ((Object)(object)tmp == (Object)null || !((Component)tmp).gameObject.activeInHierarchy) continue;
                if (((Graphic)tmp).color.a < 0.1f) continue;
                if (!IsUnderToolTip(tmp.transform)) continue;
                // Skip texts that are under SpeechBubble parents (already read by SpeechBubble scanner)
                if (IsUnderSpeechBubble(tmp.transform)) continue;

                string rawTutText = tmp.text;
                if (string.IsNullOrWhiteSpace(rawTutText) || rawTutText.Contains("{Missing Entry}")) continue;
                // Filter flavor text quotes
                if (IsFlavorText(rawTutText)) continue;

                string text = UIHelper.StripRichText(rawTutText.Trim());
                if (text.Length < 3) continue;

                int id = ((Object)tmp).GetInstanceID();
                seenIds.Add(id);

                // Check if this element's text changed since last scan
                if (_trackedTutorialTexts.TryGetValue(id, out string lastText))
                {
                    if (lastText == text) continue; // Same text, skip
                }

                bool isNew = !_trackedTutorialTexts.ContainsKey(id);
                _trackedTutorialTexts[id] = text;

                if (!_tutorialTextsInitialized)
                {
                    // First scan cycle - just record everything, don't announce
                    continue;
                }

                // New element or text changed on existing element - announce it
                // But skip if already announced via LocString/Instruction/Tooltip
                if (!_announcedTooltips.Add(text)) continue;
                _lastInstructionText = text;
                _lastTutorialText = text;
                AnnouncementService.Instance.Announce(text, AnnouncementPriority.Low);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TutorialText: " + text);
            }

            if (!_tutorialTextsInitialized && _trackedTutorialTexts.Count > 0)
            {
                if (_tutorialInitStartTime == 0f)
                {
                    _tutorialInitStartTime = Time.time;
                }
                // Wait for 3 seconds before considering initialized
                // This ensures all pre-loaded tutorial texts are captured silently
                if (Time.time - _tutorialInitStartTime >= 3.0f)
                {
                    _tutorialTextsInitialized = true;
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Tutorial texts initialized: {_trackedTutorialTexts.Count} elements tracked");
                }
            }

            // Clean up tracked texts for elements that are no longer visible
            List<int> toRemove = new List<int>();
            foreach (int id in _trackedTutorialTexts.Keys)
            {
                if (!seenIds.Contains(id))
                {
                    toRemove.Add(id);
                }
            }
            foreach (int id in toRemove)
            {
                _trackedTutorialTexts.Remove(id);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "ScanTutorialParentTexts failed: " + ex.Message);
        }
    }

    private bool IsUnderSpeechBubble(Transform t)
    {
        Transform parent = t.parent;
        int depth = 0;
        while ((Object)(object)parent != (Object)null && depth < 6)
        {
            string name = ((Object)((Component)parent).gameObject).name;
            if (name.Contains("SpeechBubble", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            parent = parent.parent;
            depth++;
        }
        return false;
    }

    private void SetupHarmonyPatches()
    {
        if (_harmonyPatched) return;
        _harmonyPatched = true;
        try
        {
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("com.snapaccess.tutorials");
            PatchMethod(harmony, typeof(TutorialTooltip), "Initialize", "OnTutorialTooltipInit");
            PatchMethod(harmony, typeof(TutorialSpeechBubble), "Initialize", "OnTutorialSpeechBubbleInit");
            PatchLocalizeStringEvent(harmony);

            // Patch CanDragCard on GameInputManager — the tutorial's CanDragCard returns
            // False even when _WaitStateCanDragCard=True due to step-specific logic.
            // Without this patch, cards can't be played on most turns.
            try
            {
                MethodInfo gimCanDrag = AccessTools.Method(typeof(GameInputManager), "CanDragCard", null, null);
                if (gimCanDrag != null)
                {
                    harmony.Patch((MethodBase)gimCanDrag,
                        new HarmonyMethod(typeof(BattlefieldHandler), nameof(PrefixCanDragCard)), null, null, null, null);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Patched GameInputManager.CanDragCard");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Warning("Failed to patch GIM.CanDragCard: " + ex.Message);
            }

            // Patch CanDragCard on VfxScenarioTutorialAction — tutorial-layer blocker
            try
            {
                MethodInfo tutCanDrag = AccessTools.Method(typeof(VfxScenarioTutorialAction), "CanDragCard", null, null);
                if (tutCanDrag != null)
                {
                    harmony.Patch((MethodBase)tutCanDrag,
                        new HarmonyMethod(typeof(BattlefieldHandler), nameof(PrefixTutorialCanDragCard)), null, null, null, null);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Patched VfxScenarioTutorialAction.CanDragCard");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Warning("Failed to patch VfxTutorial.CanDragCard: " + ex.Message);
            }

            // Patch CanZoomCard on VfxScenarioTutorialAction — allow zooming cards anytime
            try
            {
                MethodInfo tutCanZoom = AccessTools.Method(typeof(VfxScenarioTutorialAction), "CanZoomCard", null, null);
                if (tutCanZoom != null)
                {
                    harmony.Patch((MethodBase)tutCanZoom,
                        new HarmonyMethod(typeof(BattlefieldHandler), nameof(PrefixTutorialCanZoomCard)), null, null, null, null);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Patched VfxScenarioTutorialAction.CanZoomCard");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Warning("Failed to patch VfxTutorial.CanZoomCard: " + ex.Message);
            }

            MelonLogger.Msg("Tutorial Harmony patches applied");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("Failed to apply tutorial patches: " + ex.Message);
        }
    }

    /// <summary>Prefix patch: force GameInputManager.CanDragCard to return true.</summary>
    private static bool PrefixCanDragCard(ref bool __result)
    {
        __result = true;
        return false; // Skip original method
    }

    /// <summary>Prefix patch: force VfxScenarioTutorialAction.CanDragCard to return true.</summary>
    private static bool PrefixTutorialCanDragCard(ref bool __result)
    {
        __result = true;
        return false; // Skip original method
    }

    /// <summary>Prefix patch: force VfxScenarioTutorialAction.CanZoomCard to return true.</summary>
    private static bool PrefixTutorialCanZoomCard(ref bool __result)
    {
        __result = true;
        return false; // Skip original method
    }

    private void PatchMethod(HarmonyLib.Harmony harmony, System.Type targetType, string methodName, string postfixName)
    {
        try
        {
            MethodInfo method = AccessTools.Method(targetType, methodName, null, null);
            if (method != null)
            {
                harmony.Patch((MethodBase)method, null, new HarmonyMethod(typeof(BattlefieldHandler), postfixName, null), null, null, null);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Patched " + targetType.Name + "." + methodName);
            }
            else
            {
                DebugLogger.Warning("Method not found: " + targetType.Name + "." + methodName);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Warning($"Failed to patch {targetType.Name}.{methodName}: {ex.Message}");
        }
    }

    private static void OnTutorialTooltipInit(TutorialTooltip __instance)
    {
        MelonCoroutines.Start(ReadComponentTextDelayed((Component)(object)__instance));
    }

    private static void OnTutorialSpeechBubbleInit(TutorialSpeechBubble __instance)
    {
        MelonCoroutines.Start(ReadComponentTextDelayed((Component)(object)__instance));
    }

    private void PatchLocalizeStringEvent(HarmonyLib.Harmony harmony)
    {
        try
        {
            System.Type type = typeof(LocalizeStringEvent);
            List<MethodInfo> declaredMethods = AccessTools.GetDeclaredMethods(type);
            foreach (MethodInfo item in declaredMethods)
            {
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "LocalizeStringEvent method: " + item.Name);
            }
            MethodInfo refreshMethod = AccessTools.Method(type, "RefreshString", null, null);
            if (refreshMethod != null)
            {
                harmony.Patch((MethodBase)refreshMethod, null, new HarmonyMethod(typeof(BattlefieldHandler), "OnLocalizeStringRefresh", null), null, null, null);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Patched LocalizeStringEvent.RefreshString");
                return;
            }
            MethodInfo forceMethod = AccessTools.Method(type, "ForceUpdate", null, null);
            if (forceMethod != null)
            {
                harmony.Patch((MethodBase)forceMethod, null, new HarmonyMethod(typeof(BattlefieldHandler), "OnLocalizeStringRefresh", null), null, null, null);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Patched LocalizeStringEvent.ForceUpdate");
            }
            else
            {
                DebugLogger.Warning("Could not find RefreshString or ForceUpdate on LocalizeStringEvent");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Warning("Failed to patch LocalizeStringEvent: " + ex.Message);
        }
    }

    private static void OnLocalizeStringRefresh(LocalizeStringEvent __instance)
    {
        MelonCoroutines.Start(ReadComponentTextDelayed((Component)(object)__instance));
    }

    private void SetupStepMapHooks()
    {
        if (_stepMapHooked) return;
        try
        {
            var stepMap = DefaultTutorialState.StepMap;
            if (stepMap == null)
            {
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "StepMap is null - will retry next scan");
                return;
            }

            // ShowText (20)
            VfxScenarioTutorialStep.Type showTextType = (VfxScenarioTutorialStep.Type)20;
            if (stepMap.ContainsKey(showTextType))
            {
                System.Action<DefaultTutorialState, VfxScenarioTutorialStep> action = OnShowTextStep;
                stepMap[showTextType] = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<DefaultTutorialState, VfxScenarioTutorialStep>>((System.Delegate)action);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Hooked StepMap ShowText");
            }

            // ShowTooltip (18)
            VfxScenarioTutorialStep.Type showTooltipType = (VfxScenarioTutorialStep.Type)18;
            if (stepMap.ContainsKey(showTooltipType))
            {
                System.Action<DefaultTutorialState, VfxScenarioTutorialStep> action = OnShowTooltipStep;
                stepMap[showTooltipType] = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<DefaultTutorialState, VfxScenarioTutorialStep>>((System.Delegate)action);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Hooked StepMap ShowTooltip");
            }

            // ShowTapToContinue (7)
            VfxScenarioTutorialStep.Type showTapType = (VfxScenarioTutorialStep.Type)7;
            if (stepMap.ContainsKey(showTapType))
            {
                System.Action<DefaultTutorialState, VfxScenarioTutorialStep> action = OnShowTapToContinueStep;
                stepMap[showTapType] = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<DefaultTutorialState, VfxScenarioTutorialStep>>((System.Delegate)action);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Hooked StepMap ShowTapToContinue");
            }

            // ShowSpeechBubble (16)
            VfxScenarioTutorialStep.Type showBubbleType = (VfxScenarioTutorialStep.Type)16;
            if (stepMap.ContainsKey(showBubbleType))
            {
                System.Action<DefaultTutorialState, VfxScenarioTutorialStep> action = OnShowSpeechBubbleStep;
                stepMap[showBubbleType] = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<DefaultTutorialState, VfxScenarioTutorialStep>>((System.Delegate)action);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Hooked StepMap ShowSpeechBubble");
            }

            // ShowSpeechBubbleOnCard (17)
            VfxScenarioTutorialStep.Type showBubbleOnCardType = (VfxScenarioTutorialStep.Type)17;
            if (stepMap.ContainsKey(showBubbleOnCardType))
            {
                System.Action<DefaultTutorialState, VfxScenarioTutorialStep> action = OnShowSpeechBubbleOnCardStep;
                stepMap[showBubbleOnCardType] = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<DefaultTutorialState, VfxScenarioTutorialStep>>((System.Delegate)action);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Hooked StepMap ShowSpeechBubbleOnCard");
            }

            _stepMapHooked = true;
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "StepMap hooks applied");
        }
        catch (Exception ex)
        {
            DebugLogger.Warning("Failed to hook StepMap: " + ex.Message);
        }
    }

    private static void OnShowTextStep(DefaultTutorialState state, VfxScenarioTutorialStep step)
    {
        try
        {
            DefaultTutorialState.DoShowText(state, step);
        }
        catch (Exception ex)
        {
            DebugLogger.Warning("DoShowText call failed: " + ex.Message);
        }
        if ((Object)(object)(step?._TextObject) != (Object)null)
        {
            MelonCoroutines.Start(ReadTextObjectDelayed(step._TextObject));
        }
        ReadStepLocString(step);
    }

    private static void OnShowTooltipStep(DefaultTutorialState state, VfxScenarioTutorialStep step)
    {
        try
        {
            DefaultTutorialState.DoShowTooltip(state, step);
        }
        catch (Exception ex)
        {
            DebugLogger.Warning("DoShowTooltip call failed: " + ex.Message);
        }
        if ((Object)(object)(step?._Tooltip) != (Object)null)
        {
            MelonCoroutines.Start(ReadComponentTextDelayed((Component)(object)step._Tooltip));
        }
        ReadStepLocString(step);
    }

    private static void OnShowTapToContinueStep(DefaultTutorialState state, VfxScenarioTutorialStep step)
    {
        try
        {
            DefaultTutorialState.DoShowTapToContinue(state, step);
        }
        catch (Exception ex)
        {
            DebugLogger.Warning("DoShowTapToContinue call failed: " + ex.Message);
        }
        ReadStepLocString(step);
        // Also read TapToContinueText from VfxScenarioTutorialAction after a delay
        MelonCoroutines.Start(ReadTapToContinueDelayed());
    }

    private static void OnShowSpeechBubbleStep(DefaultTutorialState state, VfxScenarioTutorialStep step)
    {
        try
        {
            DefaultTutorialState.DoShowSpeechBubble(state, step);
        }
        catch (Exception ex)
        {
            DebugLogger.Warning("DoShowSpeechBubble call failed: " + ex.Message);
        }
        ReadStepLocString(step);
    }

    private static void OnShowSpeechBubbleOnCardStep(DefaultTutorialState state, VfxScenarioTutorialStep step)
    {
        try
        {
            DefaultTutorialState.DoShowSpeechBubbleOnCard(state, step);
        }
        catch (Exception ex)
        {
            DebugLogger.Warning("DoShowSpeechBubbleOnCard call failed: " + ex.Message);
        }
        ReadStepLocString(step);
    }

    private static void ReadStepLocString(VfxScenarioTutorialStep step)
    {
        if (step == null) return;
        try
        {
            var locString = step._LocString;
            if (locString == null) return;
            MelonCoroutines.Start(ReadLocStringDelayed(locString));
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "ReadStepLocString failed: " + ex.Message);
        }
    }

    private static string _lastLocString = "";

    private static IEnumerator ReadLocStringDelayed(UnityEngine.Localization.LocalizedString locString)
    {
        yield return (object)new WaitForSeconds(0.3f);
        if (locString == null) yield break;
        try
        {
            string result = locString.GetLocalizedString();
            if (!string.IsNullOrWhiteSpace(result) && !result.Contains("{Missing Entry}"))
            {
                result = UIHelper.StripRichText(result.Trim());
                if (result.Length >= 3 && result != _lastLocString)
                {
                    _lastLocString = result;
                    _pendingInstructionTexts.Enqueue(result);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "LocString: " + result);
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "ReadLocStringDelayed failed: " + ex.Message);
        }
    }

    private static IEnumerator ReadTapToContinueDelayed()
    {
        yield return (object)new WaitForSeconds(0.5f);
        try
        {
            VfxScenarioTutorialAction action = UIHelper.FindComponent<VfxScenarioTutorialAction>();
            if ((Object)(object)action == (Object)null) yield break;

            TextMeshProUGUI tapText = action._TapToContinueText;
            if ((Object)(object)tapText == (Object)null) yield break;
            if (!((Component)tapText).gameObject.activeInHierarchy) yield break;

            TMP_Text tmpText = ((Il2CppObjectBase)tapText).TryCast<TMP_Text>();
            if ((Object)(object)tmpText != (Object)null)
            {
                EnqueueInstructionText(tmpText);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "ReadTapToContinueDelayed failed: " + ex.Message);
        }
    }

    private static IEnumerator ReadComponentTextDelayed(Component source)
    {
        yield return (object)new WaitForSeconds(0.3f);
        if ((Object)(object)source == (Object)null || !source.gameObject.activeInHierarchy) yield break;

        Il2CppArrayBase<TMP_Text> texts = source.GetComponentsInChildren<TMP_Text>();
        if (texts == null) yield break;

        for (int i = 0; i < texts.Count; i++)
        {
            TMP_Text tmp = texts[i];
            if ((Object)(object)tmp != (Object)null && ((Graphic)tmp).color.a >= 0.1f)
            {
                EnqueueText(tmp);
            }
        }
    }

    private static IEnumerator ReadTextObjectDelayed(MaskableGraphic textObj)
    {
        yield return (object)new WaitForSeconds(0.3f);
        if ((Object)(object)textObj == (Object)null || !((Component)textObj).gameObject.activeInHierarchy) yield break;

        TMP_Text tmp = ((Il2CppObjectBase)textObj).TryCast<TMP_Text>();
        if ((Object)(object)tmp != (Object)null)
        {
            EnqueueInstructionText(tmp);
            yield break;
        }

        Il2CppArrayBase<TMP_Text> texts = ((Component)textObj).GetComponentsInChildren<TMP_Text>();
        if (texts == null) yield break;

        for (int i = 0; i < texts.Count; i++)
        {
            tmp = texts[i];
            if ((Object)(object)tmp != (Object)null && ((Graphic)tmp).color.a >= 0.1f)
            {
                EnqueueInstructionText(tmp);
            }
        }
    }

    private static void EnqueueText(TMP_Text tmp)
    {
        if ((Object)(object)tmp == (Object)null) return;
        string text = tmp.text;
        if (string.IsNullOrWhiteSpace(text) || text.Contains("{Missing Entry}")) return;
        text = UIHelper.StripRichText(text.Trim());
        if (text.Length >= 3)
        {
            _pendingTooltipTexts.Enqueue(text);
        }
    }

    private static void EnqueueInstructionText(TMP_Text tmp)
    {
        if ((Object)(object)tmp == (Object)null) return;
        string text = tmp.text;
        if (string.IsNullOrWhiteSpace(text) || text.Contains("{Missing Entry}")) return;
        text = UIHelper.StripRichText(text.Trim());
        if (text.Length >= 3)
        {
            _pendingInstructionTexts.Enqueue(text);
        }
    }

    private void ProcessInput()
    {
        // C: Focus hand — browse cards with left/right
        if (SDLInput.IsKeyDown(SDLInput.Key.C))
        {
            FocusHand();
        }
        // B: Focus locations — browse with left/right, or play if card selected
        else if (SDLInput.IsKeyDown(SDLInput.Key.B))
        {
            FocusLocations();
        }
        // Left/Right: Navigate within current area (hold to repeat)
        else if (_holdRepeater.Check(SDLInput.Key.Left, () => Navigate(-1))
            || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
        {
            if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft)) Navigate(-1);
        }
        else if (_holdRepeater.Check(SDLInput.Key.Right, () => Navigate(1))
            || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
        {
            if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight)) Navigate(1);
        }
        // Home/End: jump to first/last item
        else if (SDLInput.IsKeyDown(SDLInput.Key.Home))
        {
            NavigateToIndex(0);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.End))
        {
            NavigateToEnd();
        }
        // Down: Inspect/zoom current item (full details)
        else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
        {
            InspectCurrent();
        }
        // Up: Back to name-only browsing (unzoom)
        else if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
        {
            UnInspectCurrent();
        }
        // Enter: Select card or play to location
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
        {
            OnConfirm();
        }
        // Escape: Cancel selection or open pause menu
        else if (SDLInput.IsKeyDown(SDLInput.Key.Escape) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
        {
            if (_playState == PlayState.CardSelected)
            {
                OnCancel();
            }
            else
            {
                TryOpenEscapeMenu();
                // Force a popup scan shortly after opening escape menu so it gets detected
                _forcePopupScanAfter = Time.time + 0.3f;
            }
        }
        // Space: Advance tutorial
        else if (SDLInput.IsKeyDown(SDLInput.Key.Space) || SDLInput.IsButtonDown(SDLInput.GamepadButton.West))
        {
            TryAdvanceTutorial();
        }
        // E: End turn
        else if (SDLInput.IsKeyDown(SDLInput.Key.E) || SDLInput.IsButtonDown(SDLInput.GamepadButton.Start))
        {
            TryEndTurn();
        }
        // I: Tutorial instruction
        else if (SDLInput.IsKeyDown(SDLInput.Key.I) || SDLInput.IsButtonDown(SDLInput.GamepadButton.North))
        {
            AnnounceTutorialInstruction();
        }
        // A: Energy
        else if (SDLInput.IsKeyDown(SDLInput.Key.A) || SDLInput.IsButtonDown(SDLInput.GamepadButton.LeftShoulder))
        {
            AnnounceEnergy();
        }
        // T: Turn info (turn number / total + cube stake)
        else if (SDLInput.IsKeyDown(SDLInput.Key.T))
        {
            AnnounceTurnInfo();
        }
        // W: Timer (time remaining)
        else if (SDLInput.IsKeyDown(SDLInput.Key.W))
        {
            AnnounceTimeRemaining();
        }
        // G: Snap (double the cube stakes)
        else if (SDLInput.IsKeyDown(SDLInput.Key.G))
        {
            TrySnap();
        }
        // R: Retreat (fold — leave the match early)
        else if (SDLInput.IsKeyDown(SDLInput.Key.R))
        {
            TryRetreat();
        }
        // 1, 2, 3: Quick-play current/selected card to location 1, 2, or 3
        else if (SDLInput.IsKeyDown(SDLInput.Key.Num1))
        {
            QuickPlayToLocation(0);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Num2))
        {
            QuickPlayToLocation(1);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Num3))
        {
            QuickPlayToLocation(2);
        }
        // D: Announce drawn cards (deferred from turn start)
        else if (SDLInput.IsKeyDown(SDLInput.Key.D))
        {
            AnnounceStoredDrawnCards();
        }
        // S: Silence all speech immediately
        else if (SDLInput.IsKeyDown(SDLInput.Key.S))
        {
            AnnouncementService.Instance.Silence();
        }
        // Debug: F2 dump text, F3 dump tooltips, F4 dump API methods/fields
        else if (Main.DebugMode && SDLInput.IsKeyDown(SDLInput.Key.F2))
        {
            DumpAllText();
        }
        else if (Main.DebugMode && SDLInput.IsKeyDown(SDLInput.Key.F3))
        {
            DumpTooltipDiagnostics();
        }
        else if (Main.DebugMode && SDLInput.IsKeyDown(SDLInput.Key.F4))
        {
            DumpApiReflection();
        }
    }

    private void ScanForPopup()
    {
        // Look for active popup/dialog/modal GameObjects with buttons
        List<Button> popupBtns = new List<Button>();
        try
        {
            Il2CppArrayBase<Canvas> canvases = Object.FindObjectsOfType<Canvas>();
            if (canvases == null) { ExitPopup(); return; }

            for (int i = 0; i < canvases.Count; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || !((Component)canvas).gameObject.activeInHierarchy) continue;
                string name = ((Object)((Component)canvas).gameObject).name;
                // Look for popup/dialog/modal canvases
                // Skip FullscreenModals — it's always present with shop offers
                if (name.Contains("FullscreenModal", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Also check full path for VFX canvases (canvas name is "Canvas" but parent is "VfxCardUpgrade...")
                bool isVfxCanvas = false;
                if (_gameOverAnnounced)
                {
                    try
                    {
                        Transform t = ((Component)canvas).transform;
                        while ((Object)(object)t != (Object)null)
                        {
                            string pName = ((Object)t.gameObject).name;
                            if (pName.Contains("Vfx", StringComparison.OrdinalIgnoreCase))
                            {
                                isVfxCanvas = true;
                                break;
                            }
                            t = t.parent;
                        }
                    }
                    catch { }
                }

                if (name.Contains("Popup", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Dialog", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Retreat", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Leave", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Settings", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Pause", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Menu", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Modal", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Overlay", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("GameResult", StringComparison.OrdinalIgnoreCase) ||
                    (name.Contains("Reward", StringComparison.OrdinalIgnoreCase) && _gameOverAnnounced) ||
                    isVfxCanvas)
                {
                    Il2CppArrayBase<Button> btns = ((Component)canvas).gameObject.GetComponentsInChildren<Button>(false);
                    if (btns != null)
                    {
                        for (int j = 0; j < btns.Count; j++)
                        {
                            Button btn = btns[j];
                            if (btn != null && ((Selectable)btn).interactable && ((Component)btn).gameObject.activeInHierarchy)
                            {
                                // Filter junk buttons by label
                                string label = UIHelper.GetButtonLabel(btn);
                                if (IsJunkPopupButton(label, ((Object)((Component)btn).gameObject).name))
                                    continue;
                                popupBtns.Add(btn);
                            }
                        }
                    }
                    if (popupBtns.Count > 0)
                    {
                        DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Popup detected: {name} with {popupBtns.Count} button(s)");
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "ScanForPopup failed: " + ex.Message);
        }

        if (popupBtns.Count > 0)
        {
            if (!_inPopup)
            {
                EnterPopup(popupBtns);
            }
            else
            {
                // Update button list
                _popupButtons.Clear();
                _popupButtons.AddRange(popupBtns);
                if (_popupFocusIndex >= _popupButtons.Count)
                    _popupFocusIndex = 0;
            }
        }
        else if (_inPopup)
        {
            ExitPopup();
        }
    }

    private void EnterPopup(List<Button> buttons)
    {
        _inPopup = true;
        _popupButtons.Clear();
        _popupButtons.AddRange(buttons);
        _popupFocusIndex = 0;

        // Read popup text — walk up from the first button until we find meaningful text
        string popupText = "";
        try
        {
            Transform parent = ((Component)buttons[0]).transform;
            // Walk up to 8 parent levels, stopping when we find substantial text
            for (int i = 0; i < 8; i++)
            {
                if (parent.parent == null) break;
                parent = parent.parent;
                string candidateText = UIHelper.GetAllText(((Component)parent).gameObject);
                if (!string.IsNullOrEmpty(candidateText) && candidateText.Length > popupText.Length)
                {
                    popupText = candidateText;
                    // Stop if we have a good amount of text (not just button labels)
                    if (popupText.Length > 50) break;
                }
            }
        }
        catch { }

        // Filter popup text to remove cosmetic/junk parts
        if (!string.IsNullOrEmpty(popupText))
            popupText = FilterPopupText(popupText);

        if (!string.IsNullOrEmpty(popupText) && popupText != _lastPopupText)
        {
            _lastPopupText = popupText;
            if (popupText.Length > 300) popupText = popupText.Substring(0, 300) + "...";
            AnnouncementService.Instance.Announce(popupText, AnnouncementPriority.Normal);
        }

        string btnLabel = GetPopupButtonLabel(buttons[0]);
        AnnouncementService.Instance.Announce(Loc.Get("dialog_button_focus", btnLabel, 1, buttons.Count), AnnouncementPriority.Normal);
        DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Entered popup with {buttons.Count} button(s)");
    }

    private void ExitPopup()
    {
        if (_inPopup)
        {
            _inPopup = false;
            _popupButtons.Clear();
            _popupFocusIndex = -1;
            _lastPopupText = "";
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Exited popup");
        }
    }

    /// <summary>Filters popup text to remove cosmetic junk (card upgrade details, numbers, etc.).</summary>
    private static string FilterPopupText(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string[] parts = raw.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries);
        List<string> filtered = new List<string>();
        foreach (string part in parts)
        {
            string p = part.Trim();
            if (p.Length < 2) continue;
            // Skip pure numbers
            if (int.TryParse(p.Replace(",", ""), out _)) continue;
            // Skip numeric patterns like "3 / 7", "1 / 10"
            if (System.Text.RegularExpressions.Regex.IsMatch(p, @"^\d+\s*/\s*\d+$")) continue;
            // Skip cosmetic/upgrade junk
            if (p.Equals("Cancel", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Equals("Card Information", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Equals("Equipped Cosmetics", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Equals("Base Card", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Equals("Base Finish", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Equals("No Flare", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.StartsWith("Series ", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Contains("Border", StringComparison.OrdinalIgnoreCase) && p.Length < 30) continue;
            if (p.Contains("Finish", StringComparison.OrdinalIgnoreCase) && p.Contains("Flare", StringComparison.OrdinalIgnoreCase)) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(p, @"^\+\d+$")) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(p, @"^\d+/\d+\s*XP$")) continue;
            if (p.Equals("{Missing Entry}", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Contains("MAX", StringComparison.Ordinal) && p.Length < 5) continue;
            filtered.Add(p);
        }
        return string.Join(". ", filtered);
    }

    private static readonly HashSet<string> _junkPopupLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Tooltip View Container", "Glass Backing", "Background", "BG",
        "Right Bracket", "Left Bracket", "Shadow", "Glow", "Gradient",
        "Mask", "Frame", "Border", "Divider", "Spacer", "Blocker",
        "Click Catcher", "ClickCatcher", "Touch Blocker", "TouchBlocker",
        "Overlay", "Underlay", "btn hex prp", "Button",
        "Card Button", "Avatar View", "btn tooltip",
        "Button  Background Close", "Background Button",
    };

    private static Dictionary<string, string> _popupLabelOverrides;

    private static Dictionary<string, string> GetPopupLabelOverrides()
    {
        if (_popupLabelOverrides != null) return _popupLabelOverrides;
        _popupLabelOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "btn upgrade", Loc.Get("bf_btn_upgrade") },
            { "btn_upgrade", Loc.Get("bf_btn_upgrade") },
            { "btn_next", Loc.Get("bf_btn_next") },
            { "btn_collect", Loc.Get("bf_btn_collect") },
            { "btn_claim", Loc.Get("bf_btn_claim") },
            { "btn_ok", Loc.Get("bf_btn_ok") },
            { "btn_confirm", Loc.Get("bf_btn_confirm") },
            { "btn_cancel", Loc.Get("bf_btn_cancel") },
            { "btn_close", Loc.Get("bf_btn_close") },
            { "btn_back", Loc.Get("bf_btn_back") },
            { "btn_retreat", Loc.Get("bf_btn_retreat") },
            { "btn_stay", Loc.Get("bf_btn_stay") },
            { "btn_resume", Loc.Get("bf_btn_resume") },
            { "btn_hex_blu", Loc.Get("bf_btn_ok") },
            { "Button_BackgroundClose", Loc.Get("bf_btn_close") },
            { "Esc", Loc.Get("bf_btn_close") },
        };
        return _popupLabelOverrides;
    }

    // GO names that should always be filtered from popups
    private static readonly HashSet<string> _junkPopupGoNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Button_BackgroundClose", "BackgroundButton", "AvatarView",
        "btn_Nameplate", "btn_tooltip", "container_credits", "container_gold",
        "StakesButton", "StakesView(Clone)",
    };

    private static bool IsJunkPopupButton(string label, string goName)
    {
        // If the GO name has a known override, this is a real button — never filter it
        if (GetPopupLabelOverrides().ContainsKey(goName))
            return false;

        if (string.IsNullOrWhiteSpace(label) || _junkPopupLabels.Contains(label))
            return true;
        if (_junkPopupLabels.Contains(goName) || _junkPopupGoNames.Contains(goName))
            return true;
        // Filter partial matches
        if (label.Contains("Parallelogram", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Backing", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("blocker", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("catcher", StringComparison.OrdinalIgnoreCase) ||
            label.StartsWith("img ", StringComparison.OrdinalIgnoreCase))
            return true;
        // Filter labels that are just numbers/currency (e.g., "1,075", "50")
        if (IsNumericOrCurrencyLabel(label))
            return true;
        return false;
    }

    private static bool IsNumericOrCurrencyLabel(string label)
    {
        // Strip commas and check if the result is purely numeric
        string stripped = label.Replace(",", "").Replace(".", "").Trim();
        if (stripped.Length > 0 && stripped.Length <= 10)
        {
            bool allDigits = true;
            for (int i = 0; i < stripped.Length; i++)
            {
                if (!char.IsDigit(stripped[i])) { allDigits = false; break; }
            }
            if (allDigits) return true;
        }
        // Also filter "X/Y XP" patterns
        if (label.Contains("XP", StringComparison.OrdinalIgnoreCase) && label.Contains("/"))
            return true;
        return false;
    }

    /// <summary>Get a cleaned label for popup buttons, applying overrides for common game button names.</summary>
    private static string GetPopupButtonLabel(Button btn)
    {
        string label = UIHelper.GetButtonLabel(btn);
        string goName = ((Object)((Component)btn).gameObject).name;

        // Check override by label text first (e.g., "Esc" → "Close")
        if (GetPopupLabelOverrides().TryGetValue(label, out string labelOverride))
            return labelOverride;

        // If the button has real, non-numeric text content (not just a cleaned GO name), prefer it
        // This lets "Nice!" show instead of being overridden to "OK" for btn_hex_blu
        // But numeric labels like "25" (upgrade cost) should still use the GO name override
        string cleanedGoName = UIHelper.CleanGameObjectName(goName);
        if (label.Length >= 2 && !label.Equals(cleanedGoName, StringComparison.OrdinalIgnoreCase)
            && !_junkPopupLabels.Contains(label) && !IsNumericOrCurrencyLabel(label))
            return label;

        // Fall back to GO name override
        if (GetPopupLabelOverrides().TryGetValue(goName, out string goOverride))
            return goOverride;

        return label;
    }

    private void ProcessPopupInput()
    {
        if (_popupButtons.Count == 0)
        {
            ExitPopup();
            return;
        }

        // Clean up destroyed buttons
        _popupButtons.RemoveAll(btn => {
            try { return btn == null || !((Component)btn).gameObject.activeInHierarchy; }
            catch { return true; }
        });
        if (_popupButtons.Count == 0) { ExitPopup(); return; }
        if (_popupFocusIndex >= _popupButtons.Count) _popupFocusIndex = 0;

        if (SDLInput.IsKeyDown(SDLInput.Key.Tab) || SDLInput.IsKeyDown(SDLInput.Key.Right)
            || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight) || SDLInput.IsButtonDown(SDLInput.GamepadButton.R1))
        {
            _popupFocusIndex = (_popupFocusIndex + 1) % _popupButtons.Count;
            string label = GetPopupButtonLabel(_popupButtons[_popupFocusIndex]);
            AnnouncementService.Instance.Announce(Loc.Get("dialog_button_focus", label, _popupFocusIndex + 1, _popupButtons.Count), AnnouncementPriority.Normal);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Left)
            || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft) || SDLInput.IsButtonDown(SDLInput.GamepadButton.L1))
        {
            _popupFocusIndex = (_popupFocusIndex - 1 + _popupButtons.Count) % _popupButtons.Count;
            string label = GetPopupButtonLabel(_popupButtons[_popupFocusIndex]);
            AnnouncementService.Instance.Announce(Loc.Get("dialog_button_focus", label, _popupFocusIndex + 1, _popupButtons.Count), AnnouncementPriority.Normal);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsKeyDown(SDLInput.Key.Space)
            || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
        {
            if (_popupFocusIndex >= 0 && _popupFocusIndex < _popupButtons.Count)
            {
                string label = GetPopupButtonLabel(_popupButtons[_popupFocusIndex]);
                AnnouncementService.Instance.Announce(Loc.Get("dialog_activating", label), AnnouncementPriority.Normal);
                ClickPopupButton(_popupButtons[_popupFocusIndex]);
            }
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
        {
            // Read popup text again
            if (!string.IsNullOrEmpty(_lastPopupText))
                AnnouncementService.Instance.Announce(_lastPopupText, AnnouncementPriority.Normal);
        }
        else if (_gameOverAnnounced && (SDLInput.IsKeyDown(SDLInput.Key.E) || SDLInput.IsButtonDown(SDLInput.GamepadButton.Start)))
        {
            // Game over: E exits the results screen by collecting rewards
            ExitPopup();
            TryCollectRewards();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Escape) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
        {
            // Try to close popup — look for close/cancel/back button
            for (int i = 0; i < _popupButtons.Count; i++)
            {
                string label = GetPopupButtonLabel(_popupButtons[i]).ToLower();
                if (label.Contains("close") || label.Contains("cancel") || label.Contains("back") || label.Contains("resume") || label.Contains("x"))
                {
                    ClickPopupButton(_popupButtons[i]);
                    ExitPopup();
                    return;
                }
            }
            // If no obvious close button, click the last button (often cancel/close)
            ClickPopupButton(_popupButtons[_popupButtons.Count - 1]);
            ExitPopup();
        }
    }

    /// <summary>Click a popup button — try onClick first, fall back to mouse simulation.</summary>
    private void ClickPopupButton(Button btn)
    {
        if ((Object)(object)btn == (Object)null) return;
        // Try onClick.Invoke first (most reliable for standard UI buttons)
        if (UIHelper.ClickButton(btn)) return;
        // Fallback to mouse simulation
        SimulateClickOnButton((Component)btn);
    }

    private void FocusHand()
    {
        UnZoomCurrentCard();
        _area = FocusArea.Hand;
        _detailLevel = 0;
        if (_playState == PlayState.CardSelected)
        {
            OnCancel();
        }
        if (_handCards.Count > 0)
        {
            AnnounceCurrentCard();
        }
        else
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_no_cards"), AnnouncementPriority.Normal);
        }
    }

    private void FocusLocations()
    {
        UnZoomCurrentCard();
        _area = FocusArea.Locations;
        _detailLevel = 0;
        if (_playState == PlayState.CardSelected)
        {
            string cardName = GetCardName(_selectedCard);
            AnnouncementService.Instance.Announce(Loc.Get("bf_choose_location", cardName), AnnouncementPriority.Normal);
        }
        if (_locations.Count > 0)
        {
            AnnounceCurrentLocation();
        }
        else
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_no_locations"), AnnouncementPriority.Normal);
        }
    }

    private void Navigate(int direction)
    {
        switch (_area)
        {
            case FocusArea.Hand:
                NavigateHand(direction);
                break;
            case FocusArea.Locations:
                NavigateLocations(direction);
                break;
        }
    }

    private void NavigateToIndex(int index)
    {
        _detailLevel = 0;
        switch (_area)
        {
            case FocusArea.Hand:
                if (_handCards.Count == 0) return;
                UnZoomCurrentCard();
                _handIndex = Math.Clamp(index, 0, _handCards.Count - 1);
                AnnounceCurrentCard();
                break;
            case FocusArea.Locations:
                if (_locations.Count == 0) return;
                _locationIndex = Math.Clamp(index, 0, _locations.Count - 1);
                AnnounceCurrentLocation();
                break;
        }
    }

    private void NavigateToEnd()
    {
        switch (_area)
        {
            case FocusArea.Hand:
                NavigateToIndex(_handCards.Count - 1);
                break;
            case FocusArea.Locations:
                NavigateToIndex(_locations.Count - 1);
                break;
        }
    }

    private void NavigateHand(int direction)
    {
        UnZoomCurrentCard();
        _detailLevel = 0;
        if (_handCards.Count == 0)
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_no_cards"), AnnouncementPriority.Normal);
            return;
        }
        _handIndex += direction;
        if (_handIndex >= _handCards.Count) _handIndex = 0;
        if (_handIndex < 0) _handIndex = _handCards.Count - 1;
        AnnounceCurrentCard();
    }

    private void NavigateLocations(int direction)
    {
        _detailLevel = 0;
        if (_locations.Count == 0)
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_no_locations"), AnnouncementPriority.Normal);
            return;
        }
        _locationIndex += direction;
        if (_locationIndex >= _locations.Count) _locationIndex = 0;
        if (_locationIndex < 0) _locationIndex = _locations.Count - 1;
        AnnounceCurrentLocation();
    }

    /// <summary>Down arrow — drill deeper into current item details.</summary>
    private void InspectCurrent()
    {
        _detailLevel++;
        switch (_area)
        {
            case FocusArea.Hand:
                InspectCardAtLevel();
                break;
            case FocusArea.Locations:
                InspectLocationAtLevel();
                break;
        }
    }

    /// <summary>Up arrow — go back one detail level.</summary>
    private void UnInspectCurrent()
    {
        if (_detailLevel > 0)
        {
            _detailLevel--;
            switch (_area)
            {
                case FocusArea.Hand:
                    if (_detailLevel == 0)
                    {
                        UnZoomCurrentCard();
                        AnnounceCurrentCard();
                    }
                    else
                    {
                        InspectCardAtLevel();
                    }
                    break;
                case FocusArea.Locations:
                    if (_detailLevel == 0)
                        AnnounceCurrentLocation();
                    else
                        InspectLocationAtLevel();
                    break;
            }
        }
        else
        {
            // Already at top level — re-announce name
            switch (_area)
            {
                case FocusArea.Hand:
                    AnnounceCurrentCard();
                    break;
                case FocusArea.Locations:
                    AnnounceCurrentLocation();
                    break;
            }
        }
    }

    /// <summary>Card detail levels: 1=cost, 2=power, 3=ability. Also triggers zoom on first down.</summary>
    private void InspectCardAtLevel()
    {
        if (_handCards.Count == 0 || _handIndex >= _handCards.Count) return;
        CardView card = _handCards[_handIndex];
        if ((Object)(object)card == (Object)null) return;

        // Trigger game zoom on first detail press — fires OnCardZoom on tutorial
        if (_detailLevel == 1)
        {
            try
            {
                if ((Object)(object)_gim != (Object)null)
                {
                    bool zoomed = _gim.ZoomCard(card);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"ZoomCard={zoomed}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "ZoomCard failed: " + ex.Message);
            }
        }

        switch (_detailLevel)
        {
            case 1: // Cost
                try
                {
                    CardValueView costView = ((CardRenderer)card)._CostValueView;
                    if ((Object)(object)costView != (Object)null)
                        AnnouncementService.Instance.Announce(Loc.Get("bf_detail_cost", costView.Value.ToString()), AnnouncementPriority.Normal);
                    else
                        AnnouncementService.Instance.Announce(Loc.Get("bf_detail_cost_unknown"), AnnouncementPriority.Normal);
                }
                catch { AnnouncementService.Instance.Announce(Loc.Get("bf_detail_cost_unknown"), AnnouncementPriority.Normal); }
                break;
            case 2: // Power
                try
                {
                    CardValueView powerView = ((CardRenderer)card)._PowerValueView;
                    if ((Object)(object)powerView != (Object)null)
                        AnnouncementService.Instance.Announce(Loc.Get("bf_detail_power", powerView.Value.ToString()), AnnouncementPriority.Normal);
                    else
                        AnnouncementService.Instance.Announce(Loc.Get("bf_detail_power_unknown"), AnnouncementPriority.Normal);
                }
                catch { AnnouncementService.Instance.Announce(Loc.Get("bf_detail_power_unknown"), AnnouncementPriority.Normal); }
                break;
            case 3: // Ability
                string ability = GetCardAbilityText(card);
                if (!string.IsNullOrEmpty(ability))
                    AnnouncementService.Instance.Announce(ability, AnnouncementPriority.Normal);
                else
                    AnnouncementService.Instance.Announce(Loc.Get("bf_detail_no_ability"), AnnouncementPriority.Normal);
                break;
            default:
                // Beyond available details — cap at max
                _detailLevel = 3;
                string ab = GetCardAbilityText(card);
                if (!string.IsNullOrEmpty(ab))
                    AnnouncementService.Instance.Announce(ab, AnnouncementPriority.Normal);
                else
                    AnnouncementService.Instance.Announce(Loc.Get("bf_detail_no_ability"), AnnouncementPriority.Normal);
                break;
        }
    }

    /// <summary>Location detail levels: 1=description/ability, 2=power scores, 3=cards at location.</summary>
    private void InspectLocationAtLevel()
    {
        if (_locations.Count == 0 || _locationIndex >= _locations.Count) return;
        LocationView loc = _locations[_locationIndex];
        if ((Object)(object)loc == (Object)null) return;

        switch (_detailLevel)
        {
            case 1: // Description/ability
                string desc = GetLocationDescription(loc);
                if (!string.IsNullOrEmpty(desc))
                    AnnouncementService.Instance.Announce(desc, AnnouncementPriority.Normal);
                else
                    AnnouncementService.Instance.Announce(Loc.Get("bf_detail_no_description"), AnnouncementPriority.Normal);
                break;
            case 2: // Power scores
                string playerPower = GetPowerFromTransform(loc._LocationFriendlyPower);
                string opponentPower = GetPowerFromTransform(loc._LocationEnemyPower);
                AnnouncementService.Instance.Announce(Loc.Get("bf_power_score", playerPower ?? "0", opponentPower ?? "0"), AnnouncementPriority.Normal);
                break;
            case 3: // Cards at this location
                AnnounceCardsAtLocation(loc);
                break;
            default:
                _detailLevel = 3;
                AnnounceCardsAtLocation(loc);
                break;
        }
    }

    /// <summary>Lists all cards (yours and opponent's) at a given location.</summary>
    private void AnnounceCardsAtLocation(LocationView loc)
    {
        try
        {
            float locX = ((Component)loc).transform.position.x;
            Il2CppArrayBase<CardView> allCards = Object.FindObjectsOfType<CardView>();
            if (allCards == null || allCards.Count == 0)
            {
                AnnouncementService.Instance.Announce(Loc.Get("bf_detail_no_cards_here"), AnnouncementPriority.Normal);
                return;
            }

            List<string> yourCards = new List<string>();
            List<string> opponentCards = new List<string>();

            // Determine which location X range this card belongs to
            // Use proximity matching like ScanLocationCards
            for (int i = 0; i < allCards.Count; i++)
            {
                CardView cv = allCards[i];
                if ((Object)(object)cv == (Object)null) continue;
                if (!((Component)cv).gameObject.activeInHierarchy) continue;
                if (IsInHandZone(cv)) continue;

                // Skip pool/inactive cards
                try
                {
                    string goName = ((Object)((Component)cv).gameObject).name;
                    if (goName != null && goName.Contains("Pool")) continue;
                }
                catch { }

                int entityId;
                try { entityId = cv.EntityId; } catch { continue; }
                if (entityId <= 0) continue;

                // Check if this card is nearest to the current location
                float cardX = ((Component)cv).transform.position.x;
                int nearestLocIdx = -1;
                float minDist = float.MaxValue;
                for (int li = 0; li < _locations.Count; li++)
                {
                    LocationView otherLoc = _locations[li];
                    if ((Object)(object)otherLoc == (Object)null) continue;
                    float dist = Math.Abs(cardX - ((Component)otherLoc).transform.position.x);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearestLocIdx = li;
                    }
                }

                if (nearestLocIdx != _locationIndex) continue;

                string cardName = GetBoardCardName(cv);
                if (string.IsNullOrEmpty(cardName) || cardName == "Card" ||
                    cardName == "Unknown card" || cardName.Contains("Card View")) continue;

                string cardInfo = GetCardInfo(cv);
                string entry = string.IsNullOrEmpty(cardInfo) ? cardName : $"{cardName}, {cardInfo}";

                if (_ourCardEntityIds.Contains(entityId))
                    yourCards.Add(entry);
                else
                    opponentCards.Add(entry);
            }

            StringBuilder sb = new StringBuilder();
            if (yourCards.Count > 0)
                sb.Append(Loc.Get("bf_your_cards_at_loc", string.Join(", ", yourCards)) + " ");
            else
                sb.Append(Loc.Get("bf_no_your_cards_at_loc") + " ");
            if (opponentCards.Count > 0)
                sb.Append(Loc.Get("bf_opponent_cards_at_loc", string.Join(", ", opponentCards)));
            else
                sb.Append(Loc.Get("bf_no_opponent_cards_at_loc"));

            AnnouncementService.Instance.Announce(sb.ToString(), AnnouncementPriority.Normal);
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "AnnounceCardsAtLocation failed: " + ex.Message);
            AnnouncementService.Instance.Announce(Loc.Get("bf_detail_no_cards_here"), AnnouncementPriority.Normal);
        }
    }

    /// <summary>Un-zoom the current card if zoomed.</summary>
    private void UnZoomCurrentCard()
    {
        try
        {
            if ((Object)(object)_gim == (Object)null) return;
            if (_handCards.Count == 0 || _handIndex >= _handCards.Count) return;
            CardView card = _handCards[_handIndex];
            if ((Object)(object)card == (Object)null) return;
            if (_gim.IsZoomingCard(card))
            {
                _gim.UnZoomCard(card);
            }
        }
        catch { }
    }

    private void OnConfirm()
    {
        switch (_area)
        {
            case FocusArea.Hand:
                SelectCard();
                break;
            case FocusArea.Locations:
                if (_playState == PlayState.CardSelected)
                {
                    PlayCardToLocation();
                }
                else
                {
                    // No card selected — pressing Enter on location just shows details
                    InspectCurrent();
                }
                break;
        }
    }

    private void OnCancel()
    {
        if (_playState == PlayState.CardSelected)
        {
            string cardName = GetCardName(_selectedCard);
            _selectedCard = null;
            _playState = PlayState.Browsing;
            AnnouncementService.Instance.Announce(Loc.Get("bf_card_deselected", cardName), AnnouncementPriority.Immediate);
        }
        // If no card selected, do nothing — let the game handle Escape naturally
        // (pause menu, concede dialog, etc.)
    }

    private void SelectCard()
    {
        if (_handCards.Count == 0 || _handIndex >= _handCards.Count) return;

        CardView card = _handCards[_handIndex];
        if ((Object)(object)card == (Object)null) return;

        string cardName = GetCardName(card);

        // Face-down card: click to reveal instead of selecting for play
        if (cardName == "Unknown card" || cardName == "Card")
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_card_revealing"), AnnouncementPriority.Immediate);
            UIHelper.SimulateMouseClick(((Component)card).gameObject);
            DebugLogger.LogInput("Enter", "Revealing face-down card");
            return;
        }
        if (_playState == PlayState.CardSelected && (Object)(object)_selectedCard == (Object)(object)card)
        {
            _selectedCard = null;
            _playState = PlayState.Browsing;
            AnnouncementService.Instance.Announce(Loc.Get("bf_card_deselected", cardName), AnnouncementPriority.Immediate);
        }
        else
        {
            _selectedCard = card;
            _playState = PlayState.CardSelected;
            AnnouncementService.Instance.Announce(Loc.Get("bf_card_selected", cardName), AnnouncementPriority.Immediate);
            DebugLogger.LogInput("Enter", "Selected card: " + cardName);
        }
    }

    private void PlayCardToLocation()
    {
        if ((Object)(object)_selectedCard == (Object)null || _locations.Count == 0 || _locationIndex >= _locations.Count) return;

        LocationView loc = _locations[_locationIndex];
        if ((Object)(object)loc == (Object)null) return;

        string cardName = GetCardName(_selectedCard);
        string locationName = GetLocationName(loc);
        try
        {
            bool success = false;

            // Refresh GIM reference
            try
            {
                GameInputManager freshGim = UIHelper.FindComponent<GameInputManager>();
                if ((Object)(object)freshGim != (Object)null)
                    _gim = freshGim;
            }
            catch { }

            // --- Pre-play: force all known blockers ---
            ForceGameInputFlags();
            ForceWaitStateFlags();

            // Clean up any lingering drag state from previous attempts
            CleanupDragState(_selectedCard);

            // Log diagnostics
            LogPrePlayDiagnostics(_selectedCard);

            // Primary approach: StartDragCard + UpdateDragCard + DropCard
            if ((Object)(object)_gim != (Object)null)
            {
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Trying drag: {cardName} -> {locationName}");
                bool dragging = _gim.StartDragCard(_selectedCard);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"StartDragCard={dragging}");
                if (dragging)
                {
                    try { _gim.UpdateDragCard(_selectedCard, (Object)(object)loc); }
                    catch (Exception udEx) { DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"UpdateDragCard failed: {udEx.Message}"); }
                    success = _gim.DropCard(_selectedCard, (Object)(object)loc);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"DropCard={success}");
                    if (!success)
                    {
                        try { _gim.StopDragCard(_selectedCard); } catch { }
                    }
                }
                else
                {
                    // StartDragCard failed — game doesn't allow dragging this card
                    // This means the tutorial or game rules restrict this card
                    AnnouncementService.Instance.Announce(Loc.Get("bf_card_restricted", cardName), AnnouncementPriority.Immediate);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                        $"Card restricted by game: {cardName}");
                    // Announce any active tutorial hints explaining why
                    AnnounceActiveTutorialHints();
                    _selectedCard = null;
                    _playState = PlayState.Browsing;
                    _area = FocusArea.Hand;
                    return;
                }
            }

            // Fallback 1: StageCardToLocation via controllers
            if (!success)
            {
                CleanupDragState(_selectedCard);
                success = TryStageViaControllers(_selectedCard, loc, cardName, locationName);
            }

            // Check if location is full before trying fallbacks
            if (!success)
            {
                int cardCount = CountPlayerCardsAtLocation(_locationIndex);
                if (cardCount >= 4)
                {
                    AnnouncementService.Instance.Announce(Loc.Get("bf_location_full"), AnnouncementPriority.Immediate);
                    _selectedCard = null;
                    _playState = PlayState.Browsing;
                    _area = FocusArea.Hand;
                    return;
                }
            }

            // Fallback 2: Mouse drag simulation (physically drag card to location)
            if (!success)
            {
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Trying mouse drag: {cardName} -> {locationName}");
                SimulateDragCardToLocation(_selectedCard, loc);
                // Mouse drag is fire-and-forget — we'll verify below
                success = true;
            }

            if (success)
            {
                AnnouncementService.Instance.Announce(Loc.Get("bf_card_played", cardName, locationName), AnnouncementPriority.Immediate);
                // Track for rollback detection
                try { _lastPlayedEntityId = _selectedCard.EntityId; } catch { }
                _lastPlayedCardName = cardName;
                int handBefore = _handCards.Count;
                ScanHandCards();
                _handCountAfterPlay = _handCards.Count;
                _rollbackConfirmStartTime = Time.time;
                // If hand count didn't change after scan, the play likely failed
                if (_handCards.Count >= handBefore)
                {
                    // Schedule a delayed check — the card might still be animating
                    _playVerifyTime = Time.time + 1.5f;
                    _playVerifyExpectedCount = handBefore - 1;
                }
                ScanLocations();
            }
            else
            {
                AnnouncementService.Instance.Announce(Loc.Get("bf_play_failed", cardName, locationName), AnnouncementPriority.Immediate);
                // Announce tutorial hints explaining why the play failed
                AnnounceActiveTutorialHints();
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Msg("[BF] PlayCardToLocation failed: " + ex.Message);
            AnnouncementService.Instance.Announce(Loc.Get("bf_play_error"), AnnouncementPriority.Immediate);
            // Ensure drag is cleaned up on error
            try { if ((Object)(object)_gim != (Object)null) _gim.StopDragCard(_selectedCard); } catch { }
        }
        _selectedCard = null;
        _playState = PlayState.Browsing;
        _area = FocusArea.Hand;
    }

    /// <summary>
    /// Quick-play: plays the current or selected card directly to the given location index (0, 1, 2).
    /// If no card is selected, auto-selects the currently focused hand card first.
    /// Reduces card play from 6-8 key presses to 1-2.
    /// </summary>
    // Quick-play two-stage: first press previews location, second press plays
    private int _quickPlayPreviewIdx = -1;
    private float _quickPlayPreviewTime = 0f;
    private const float QuickPlayConfirmTimeout = 3f;

    private void QuickPlayToLocation(int locationIdx)
    {
        if (_locations.Count == 0 || locationIdx >= _locations.Count)
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_no_locations"), AnnouncementPriority.Immediate);
            return;
        }

        // Stage 1: Preview — announce location name, power scores
        if (_quickPlayPreviewIdx != locationIdx || Time.time - _quickPlayPreviewTime > QuickPlayConfirmTimeout)
        {
            _quickPlayPreviewIdx = locationIdx;
            _quickPlayPreviewTime = Time.time;

            LocationView loc = _locations[locationIdx];
            string locName = GetLocationName(loc);
            string playerPower = GetPowerFromTransform(loc._LocationFriendlyPower) ?? "0";
            string opponentPower = GetPowerFromTransform(loc._LocationEnemyPower) ?? "0";
            int slots = CountPlayerCardsAtLocation(locationIdx);

            string msg = Loc.Get("bf_quickplay_preview", locName, playerPower, opponentPower, slots.ToString());
            AnnouncementService.Instance.Announce(msg, AnnouncementPriority.High);
            return;
        }

        // Stage 2: Confirm — play the card
        _quickPlayPreviewIdx = -1;

        // If no card selected, auto-select the currently focused hand card
        if (_playState != PlayState.CardSelected)
        {
            if (_area != FocusArea.Hand || _handCards.Count == 0 || _handIndex >= _handCards.Count)
            {
                AnnouncementService.Instance.Announce(Loc.Get("bf_no_cards"), AnnouncementPriority.Immediate);
                return;
            }
            CardView card = _handCards[_handIndex];
            if ((Object)(object)card == (Object)null) return;
            string name = GetCardName(card);
            // Face-down card: reveal instead
            if (name == "Unknown card" || name == "Card")
            {
                AnnouncementService.Instance.Announce(Loc.Get("bf_card_revealing"), AnnouncementPriority.Immediate);
                UIHelper.SimulateMouseClick(((Component)card).gameObject);
                return;
            }
            _selectedCard = card;
            _playState = PlayState.CardSelected;
        }

        // Set the target location and delegate to existing play logic
        _locationIndex = locationIdx;
        PlayCardToLocation();
    }

    private void ForceGameInputFlags()
    {
        try
        {
            if ((Object)(object)_gim == (Object)null) return;
            var gimType = _gim.GetType();
            var isInGameProp = gimType.GetProperty("_isInGameInput",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (isInGameProp != null)
            {
                bool isInGame = (bool)isInGameProp.GetValue(_gim);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Pre-play: _isInGameInput={isInGame}");
                if (!isInGame)
                {
                    isInGameProp.SetValue(_gim, true);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Forced _isInGameInput=true");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "ForceGameInputFlags error: " + ex.Message);
        }
    }

    private void ForceWaitStateFlags()
    {
        try
        {
            VfxScenarioTutorialAction tutAction = UIHelper.FindComponent<VfxScenarioTutorialAction>();
            if (tutAction == null) return;
            var tutType = tutAction.GetType();

            // Force _WaitStateCanDragCard=true
            var waitDragProp = tutType.GetProperty("_WaitStateCanDragCard",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (waitDragProp != null)
            {
                bool canDrag = (bool)waitDragProp.GetValue(tutAction);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Pre-play: _WaitStateCanDragCard={canDrag}");
                if (!canDrag)
                {
                    waitDragProp.SetValue(tutAction, true);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Forced _WaitStateCanDragCard=true");
                }
            }

            // Force _WaitStateCanEndTurn=true
            var waitEndProp = tutType.GetProperty("_WaitStateCanEndTurn",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (waitEndProp != null)
            {
                bool canEnd = (bool)waitEndProp.GetValue(tutAction);
                if (!canEnd)
                {
                    waitEndProp.SetValue(tutAction, true);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Forced _WaitStateCanEndTurn=true");
                }
            }

            // Force _ShowEndTurnButton=true
            var showEndBtnProp = tutType.GetProperty("_ShowEndTurnButton",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (showEndBtnProp != null)
            {
                bool showBtn = (bool)showEndBtnProp.GetValue(tutAction);
                if (!showBtn)
                {
                    showEndBtnProp.SetValue(tutAction, true);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Forced _ShowEndTurnButton=true");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "ForceWaitStateFlags error: " + ex.Message);
        }
    }

    private void CleanupDragState(CardView card)
    {
        if ((Object)(object)_gim == (Object)null || (Object)(object)card == (Object)null) return;
        try
        {
            if (_gim.IsDraggingCard(card))
            {
                _gim.StopDragCard(card);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Cleaned up lingering drag state");
            }
        }
        catch { }
    }

    private void LogPrePlayDiagnostics(CardView card)
    {
        try
        {
            if ((Object)(object)_gim != (Object)null)
            {
                bool canDrag = _gim.CanDragCard(card);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Pre-play: CanDragCard={canDrag}");

                var vfxBlockedMethod = _gim.GetType().GetMethod("InputVfxBlocked",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (vfxBlockedMethod != null)
                {
                    bool vfxBlocked = (bool)vfxBlockedMethod.Invoke(_gim, null);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Pre-play: InputVfxBlocked={vfxBlocked}");
                }

                bool isDragging = _gim.IsDraggingCard(card);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Pre-play: IsDraggingCard={isDragging}");
            }
        }
        catch { }

        try
        {
            int entityId = card.EntityId;
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Pre-play: entityId={entityId}");
        }
        catch { }
    }

    private bool TryStageViaControllers(CardView card, LocationView loc, string cardName, string locationName)
    {
        try
        {
            GameView gameView = GameView.Get();
            if ((Object)(object)gameView == (Object)null) return false;

            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Trying StageCardToLocation: {cardName} -> {locationName}");
            int cardEntityId = card.EntityId;
            int locEntityId = loc.EntityId;

            CardController cardCtrl = null;
            try
            {
                var entityControllers = gameView.EntityControllers;
                if (entityControllers != null)
                {
                    EntityController entityCtrl;
                    if (entityControllers.TryGetValue(cardEntityId, out entityCtrl) && entityCtrl != null)
                        cardCtrl = entityCtrl.TryCast<CardController>();
                }
            }
            catch { }

            LocationController locCtrl = null;
            try
            {
                GameObject locCtrlGO = loc._LocationControllerGameObject;
                if ((Object)(object)locCtrlGO != (Object)null)
                    locCtrl = locCtrlGO.GetComponent<LocationController>();
            }
            catch { }
            if (locCtrl == null)
            {
                try
                {
                    var entityControllers = gameView.EntityControllers;
                    if (entityControllers != null)
                    {
                        EntityController entityCtrl;
                        if (entityControllers.TryGetValue(locEntityId, out entityCtrl) && entityCtrl != null)
                            locCtrl = entityCtrl.TryCast<LocationController>();
                    }
                }
                catch { }
            }

            if (cardCtrl != null && locCtrl != null)
            {
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                    $"StageCardToLocation: cardEntity={cardEntityId}, locEntity={locEntityId}");
                bool result = gameView.StageCardToLocation(cardCtrl, locCtrl);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"StageCardToLocation={result}");
                return result;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "StageViaControllers error: " + ex.Message);
        }
        return false;
    }

    private void SimulateDragCardToLocation(CardView card, LocationView location)
    {
        try
        {
            Camera main = Camera.main;
            if ((Object)(object)main == (Object)null) return;

            System.IntPtr hwnd = GetForegroundWindow();

            // Get card screen position
            Vector3 cardScreen = main.WorldToScreenPoint(((Component)card).transform.position);
            POINT cardPt = new POINT { X = (int)cardScreen.x, Y = Screen.height - (int)cardScreen.y };
            ClientToScreen(hwnd, ref cardPt);

            // Get location screen position
            Vector3 locScreen = main.WorldToScreenPoint(((Component)location).transform.position);
            POINT locPt = new POINT { X = (int)locScreen.x, Y = Screen.height - (int)locScreen.y };
            ClientToScreen(hwnd, ref locPt);

            // Simulate drag: move to card, press down, move to midpoint, move to location, release
            SetCursorPos(cardPt.X, cardPt.Y);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0u, System.IntPtr.Zero);

            // Move through midpoint to simulate real drag motion
            int midX = (cardPt.X + locPt.X) / 2;
            int midY = (cardPt.Y + locPt.Y) / 2;
            SetCursorPos(midX, midY);
            mouse_event(MOUSEEVENTF_MOVE, 0, 0, 0u, System.IntPtr.Zero);

            SetCursorPos(locPt.X, locPt.Y);
            mouse_event(MOUSEEVENTF_MOVE, 0, 0, 0u, System.IntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0u, System.IntPtr.Zero);

            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                $"Mouse drag: ({cardPt.X},{cardPt.Y}) -> ({locPt.X},{locPt.Y})");
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "SimulateDragCardToLocation failed: " + ex.Message);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, System.IntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(System.IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern System.IntPtr GetForegroundWindow();

    /// <summary>Simulates a mouse click on a UI component's screen position. Returns true on success.</summary>
    private bool SimulateClickOnButton(Component component)
    {
        try
        {
            int sx, sy;

            RectTransform rt = component.GetComponent<RectTransform>();
            if ((Object)(object)rt != (Object)null)
            {
                // Use RectTransformUtility for correct screen coordinates
                // Get the root canvas to determine the correct camera
                Camera cam = null;
                Canvas canvas = component.GetComponentInParent<Canvas>();
                if ((Object)(object)canvas != (Object)null)
                {
                    Canvas root = canvas.rootCanvas;
                    if ((Object)(object)root != (Object)null && root.renderMode != RenderMode.ScreenSpaceOverlay)
                        cam = root.worldCamera;
                }
                // WorldToScreenPoint handles both overlay (cam=null) and camera canvases
                Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, rt.position);
                sx = (int)screenPos.x;
                sy = Screen.height - (int)screenPos.y;
            }
            else
            {
                // Non-UI component: use Camera.main
                Camera cam = Camera.main;
                if ((Object)(object)cam == (Object)null) return false;
                Vector3 btnScreen = cam.WorldToScreenPoint(component.transform.position);
                sx = (int)btnScreen.x;
                sy = Screen.height - (int)btnScreen.y;
            }

            // Bounds check: skip if coordinates are outside visible screen area
            if (sx < 0 || sx > Screen.width || sy < 0 || sy > Screen.height)
            {
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                    $"SimulateClick SKIPPED {((Object)component.gameObject).name}: coords ({sx},{sy}) outside screen ({Screen.width}x{Screen.height})");
                return false;
            }

            System.IntPtr hwnd = GetForegroundWindow();
            POINT pt = new POINT { X = sx, Y = sy };
            ClientToScreen(hwnd, ref pt);
            SetCursorPos(pt.X, pt.Y);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0u, System.IntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0u, System.IntPtr.Zero);
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                $"SimulateClick on {((Object)component.gameObject).name} at screen=({sx},{sy}) client=({pt.X},{pt.Y})");
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "SimulateClickOnButton failed: " + ex.Message);
            return false;
        }
    }

    private void TryOpenEscapeMenu()
    {
        // First try to leave the game scene (for post-game/victory state)
        if (TryLeaveGame()) return;

        try
        {
            if ((Object)(object)_gim != (Object)null)
            {
                _gim.OnBackButton();
                DebugLogger.LogInput("Escape", "OnBackButton via GameInputManager");
                return;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "OnBackButton failed: " + ex.Message);
        }

        // Fallback: simulate Escape key press
        try
        {
            keybd_event(0x1B, 0, 0, System.IntPtr.Zero);
            keybd_event(0x1B, 0, 2, System.IntPtr.Zero);
            DebugLogger.LogInput("Escape", "Simulated Escape keypress");
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Escape simulation failed: " + ex.Message);
        }
    }

    /// <summary>Try to leave the game scene — for post-game/victory/defeat states.
    /// Only attempts if the game appears to be over (no cards in hand, end of game).</summary>
    private bool TryLeaveGame()
    {
        // Only try if the hand is empty (game is likely over)
        if (_handCards.Count > 0) return false;

        try
        {
            // Force _WaitStateCanLeaveGameScene=true on the tutorial
            VfxScenarioTutorialAction tutAction = UIHelper.FindComponent<VfxScenarioTutorialAction>();
            if ((Object)(object)tutAction != (Object)null)
            {
                var leaveProp = tutAction.GetType().GetProperty("_WaitStateCanLeaveGameScene",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (leaveProp != null)
                {
                    leaveProp.SetValue(tutAction, true);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Forced _WaitStateCanLeaveGameScene=true");
                }

                bool left = tutAction.TryLeaveGameScene();
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"TryLeaveGameScene={left}");
                if (left)
                {
                    AnnouncementService.Instance.Announce(Loc.Get("bf_leaving_game"), AnnouncementPriority.Immediate);
                    return true;
                }
            }

            // Try GameViewController.OnLeaveGame
            try
            {
                GameViewControllerProvider provider = UIHelper.FindComponent<GameViewControllerProvider>();
                if ((Object)(object)provider != (Object)null)
                {
                    GameViewController gvc = provider.GameViewController;
                    if ((Object)(object)gvc != (Object)null)
                    {
                        gvc.OnLeaveGame();
                        DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Called GVC.OnLeaveGame");
                        AnnouncementService.Instance.Announce(Loc.Get("bf_leaving_game"), AnnouncementPriority.Immediate);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "GVC.OnLeaveGame failed: " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TryLeaveGame failed: " + ex.Message);
        }
        return false;
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, System.IntPtr dwExtraInfo);

    private void TryAdvanceTutorial()
    {
        try
        {
            VfxScenarioTutorialAction action = UIHelper.FindComponent<VfxScenarioTutorialAction>();
            int clickX, clickY;

            // Try to click on the TapToContinueText element position if available
            if ((Object)(object)action != (Object)null)
            {
                TextMeshProUGUI tapText = action._TapToContinueText;
                if ((Object)(object)tapText != (Object)null && ((Component)tapText).gameObject.activeInHierarchy)
                {
                    Camera main = Camera.main;
                    if ((Object)(object)main != (Object)null)
                    {
                        Vector3 tapScreen = main.WorldToScreenPoint(((Component)tapText).transform.position);
                        clickX = (int)tapScreen.x;
                        clickY = Screen.height - (int)tapScreen.y;
                        DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Clicking TapToContinue at ({clickX},{clickY})");

                        System.IntPtr hwnd = GetForegroundWindow();
                        POINT pt = new POINT { X = clickX, Y = clickY };
                        ClientToScreen(hwnd, ref pt);
                        SetCursorPos(pt.X, pt.Y);
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0u, System.IntPtr.Zero);
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0u, System.IntPtr.Zero);
                        _tapToContinueAnnounced = false;
                        AnnouncementService.Instance.Announce(Loc.Get("bf_tutorial_advance"), AnnouncementPriority.Immediate);
                        DebugLogger.LogInput("Space", "Tutorial click at TapToContinue element");
                        return;
                    }
                }
            }

            // Fallback: click center of screen
            clickX = Screen.width / 2;
            clickY = Screen.height / 2;
            System.IntPtr hwnd2 = GetForegroundWindow();
            POINT pt2 = new POINT { X = clickX, Y = clickY };
            ClientToScreen(hwnd2, ref pt2);
            SetCursorPos(pt2.X, pt2.Y);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0u, System.IntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0u, System.IntPtr.Zero);
            _tapToContinueAnnounced = false;
            AnnouncementService.Instance.Announce(Loc.Get("bf_tutorial_advance"), AnnouncementPriority.Immediate);
            DebugLogger.LogInput("Space", "Tutorial click at center");
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TryAdvanceTutorial failed: " + ex.Message);
            AnnouncementService.Instance.Announce(Loc.Get("bf_no_tutorial"), AnnouncementPriority.Normal);
        }
    }

    // End turn guard: warn if playable cards remain
    private bool _endTurnGuardPending = false;
    private float _endTurnGuardTime = 0f;

    private void TryEndTurn()
    {
        // Game over: clicking E collects rewards / exits
        if (_gameOverAnnounced)
        {
            TryCollectRewards();
            return;
        }

        // Phase skip guard: warn if there are playable cards (cost <= available energy)
        if (!_endTurnGuardPending)
        {
            int playableCount = CountPlayableCards();
            if (playableCount > 0)
            {
                _endTurnGuardPending = true;
                _endTurnGuardTime = Time.time;
                AnnouncementService.Instance.Announce(Loc.Get("bf_end_turn_guard", playableCount.ToString()), AnnouncementPriority.Immediate);
                return;
            }
        }
        else
        {
            // Guard was pending — if pressed again within 3 seconds, proceed
            if (Time.time - _endTurnGuardTime > 3f)
            {
                // Expired — re-check
                _endTurnGuardPending = false;
                TryEndTurn();
                return;
            }
            _endTurnGuardPending = false;
        }

        try
        {
            // Force _WaitStateCanEndTurn=true so tutorial allows ending turns
            try
            {
                VfxScenarioTutorialAction tutAction = UIHelper.FindComponent<VfxScenarioTutorialAction>();
                if (tutAction != null)
                {
                    var endTurnProp = tutAction.GetType().GetProperty("_WaitStateCanEndTurn",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (endTurnProp != null)
                    {
                        bool canEnd = (bool)endTurnProp.GetValue(tutAction);
                        if (!canEnd)
                        {
                            endTurnProp.SetValue(tutAction, true);
                            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Forced _WaitStateCanEndTurn=true");
                        }
                    }
                }
            }
            catch { }

            // Primary approach: click EndTurnSDButton via mouse simulation + API call together
            // Mouse simulation handles cases where SendEndTurnRequest is ignored during animations
            // SendEndTurnRequest handles cases where mouse coordinates are off
            Button sdBtn = FindEndTurnSDButton();
            bool clicked = false;
            if ((Object)(object)sdBtn != (Object)null)
            {
                clicked = SimulateClickOnButton((Component)sdBtn);
                DebugLogger.LogInput("E", "End Turn mouse simulation: " + (clicked ? "sent" : "failed"));
            }

            // Also call SendEndTurnRequest as belt-and-suspenders
            GameView gameView = GameView.Get();
            if ((Object)(object)gameView != (Object)null)
            {
                gameView.SendEndTurnRequest(false);
                DebugLogger.LogInput("E", "End Turn via SendEndTurnRequest");
            }

            if (clicked || (Object)(object)gameView != (Object)null)
            {
                AnnouncementService.Instance.Announce(Loc.Get("bf_end_turn"), AnnouncementPriority.Immediate);
                _isPlayerTurn = false;
                _turnPhaseAnnounced = false;
            }
            else
            {
                AnnouncementService.Instance.Announce(Loc.Get("bf_no_end_turn"), AnnouncementPriority.Immediate);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TryEndTurn failed: " + ex.Message);
            AnnouncementService.Instance.Announce(Loc.Get("bf_no_end_turn"), AnnouncementPriority.Immediate);
        }
    }

    /// <summary>Clicks the end turn button to collect rewards and exit the game.</summary>
    private void TryCollectRewards()
    {
        try
        {
            // Find EndTurnSDButton by name and click it directly
            Button sdBtn = FindEndTurnSDButton();
            if ((Object)(object)sdBtn != (Object)null)
            {
                if (UIHelper.ClickButton(sdBtn))
                {
                    AnnouncementService.Instance.Announce(Loc.Get("bf_leaving_game"), AnnouncementPriority.Immediate);
                    DebugLogger.LogInput("E", "Collect Rewards / Exit via direct button click");
                }
                else
                {
                    AnnouncementService.Instance.Announce(Loc.Get("bf_no_end_turn"), AnnouncementPriority.Immediate);
                }
            }
            else
            {
                // Fallback: try clicking the EndTurnButtonView's own Button component
                EndTurnButtonView endBtn = UIHelper.FindComponent<EndTurnButtonView>();
                if ((Object)(object)endBtn != (Object)null)
                {
                    Button fallbackBtn = ((Component)endBtn).GetComponentInChildren<Button>(true);
                    if ((Object)(object)fallbackBtn != (Object)null && UIHelper.ClickButton(fallbackBtn))
                    {
                        AnnouncementService.Instance.Announce(Loc.Get("bf_leaving_game"), AnnouncementPriority.Immediate);
                        DebugLogger.LogInput("E", "Collect Rewards / Exit via EndTurnButtonView fallback");
                    }
                    else
                    {
                        AnnouncementService.Instance.Announce(Loc.Get("bf_no_end_turn"), AnnouncementPriority.Immediate);
                    }
                }
                else
                {
                    AnnouncementService.Instance.Announce(Loc.Get("bf_no_end_turn"), AnnouncementPriority.Immediate);
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TryCollectRewards failed: " + ex.Message);
        }
    }

    private float _lastPostGameCheck = 0f;
    private string _lastPostGameAnnouncement = "";

    /// <summary>Check for post-game upgrade/reward screens and announce them.</summary>
    private void CheckPostGameScreen()
    {
        if (Time.time - _lastPostGameCheck < 2f) return;
        _lastPostGameCheck = Time.time;

        try
        {
            // Check for upgrade animation (VfxUpgradeCardFlow skip button)
            Il2CppArrayBase<Button> buttons = Object.FindObjectsOfType<Button>();
            if (buttons == null) return;

            for (int i = 0; i < buttons.Count; i++)
            {
                Button btn = buttons[i];
                if ((Object)(object)btn == (Object)null) continue;
                if (!((Component)btn).gameObject.activeInHierarchy) continue;
                string goName = ((Object)((Component)btn).gameObject).name;

                // Detect upgrade skip or continue buttons
                if (goName.Contains("Skip", StringComparison.OrdinalIgnoreCase) ||
                    goName.Contains("Continue", StringComparison.OrdinalIgnoreCase) ||
                    goName.Contains("Claim", StringComparison.OrdinalIgnoreCase) ||
                    goName.Contains("Collect", StringComparison.OrdinalIgnoreCase))
                {
                    string label = UIHelper.GetButtonLabel(btn);
                    if (string.IsNullOrEmpty(label)) label = goName;
                    // Filter out pure numeric labels (collection level counters like "33")
                    // and labels that are just "Level XX" — those are level-up indicators, not actions
                    string labelTrimmed = label.Trim();
                    if (int.TryParse(labelTrimmed, out _)) continue;
                    if (labelTrimmed.StartsWith("Level", StringComparison.OrdinalIgnoreCase)) continue;
                    // Skip very short labels that aren't meaningful action text
                    if (labelTrimmed.Length < 3) continue;
                    string announcement = $"Press Space to {label}";
                    if (announcement != _lastPostGameAnnouncement)
                    {
                        _lastPostGameAnnouncement = announcement;
                        // Read any visible text on screen (reward info)
                        string screenText = ReadPostGameText();
                        if (!string.IsNullOrEmpty(screenText))
                            AnnouncementService.Instance.Announce(screenText, AnnouncementPriority.Normal);
                        AnnouncementService.Instance.Announce(announcement, AnnouncementPriority.Low);
                    }
                    return;
                }
            }
        }
        catch { }
    }

    /// <summary>Read visible text during post-game screens (rewards, upgrade info).</summary>
    private string ReadPostGameText()
    {
        try
        {
            // Look for text in EndGameCanvas or Rewards canvas
            GameObject endGame = GameObject.Find("EndGameCanvas");
            if (endGame != null && endGame.activeInHierarchy)
            {
                string text = UIHelper.GetAllText(endGame);
                if (!string.IsNullOrEmpty(text) && text.Length > 3)
                    return text.Length > 300 ? text.Substring(0, 300) : text;
            }

            GameObject rewards = GameObject.Find("Canvas-Rewards");
            if (rewards != null && rewards.activeInHierarchy)
            {
                string text = UIHelper.GetAllText(rewards);
                if (!string.IsNullOrEmpty(text) && text.Length > 3)
                    return text.Length > 300 ? text.Substring(0, 300) : text;
            }
        }
        catch { }
        return "";
    }

    /// <summary>Try to skip upgrade animation by clicking skip/continue buttons.</summary>
    private void TrySkipUpgradeAnimation()
    {
        try
        {
            Il2CppArrayBase<Button> buttons = Object.FindObjectsOfType<Button>();
            if (buttons == null) return;

            for (int i = 0; i < buttons.Count; i++)
            {
                Button btn = buttons[i];
                if ((Object)(object)btn == (Object)null) continue;
                if (!((Component)btn).gameObject.activeInHierarchy) continue;
                string goName = ((Object)((Component)btn).gameObject).name;

                // Skip btn_collectionscore (shows collection level "36", not actionable)
                if (goName.Equals("btn_collectionscore", StringComparison.OrdinalIgnoreCase)) continue;

                if (goName.Contains("Skip", StringComparison.OrdinalIgnoreCase) ||
                    goName.Contains("Continue", StringComparison.OrdinalIgnoreCase) ||
                    goName.Contains("Claim", StringComparison.OrdinalIgnoreCase) ||
                    goName.Contains("Collect", StringComparison.OrdinalIgnoreCase) ||
                    goName.Contains("Next", StringComparison.OrdinalIgnoreCase))
                {
                    string label = UIHelper.GetButtonLabel(btn);
                    // Skip pure numeric labels (collection level counters)
                    if (!string.IsNullOrEmpty(label) && int.TryParse(label.Trim(), out _)) continue;
                    if (UIHelper.ClickButtonWithFallback(btn))
                    {
                        AnnouncementService.Instance.Announce(label ?? "Skipped", AnnouncementPriority.Normal);
                        DebugLogger.LogInput("Space", "Post-game skip: " + goName);
                        _lastPostGameAnnouncement = "";
                        return;
                    }
                }
            }

            // Fallback: click center of screen to skip (common in animations)
            UIHelper.SimulateMouseClickAtCenter();
            AnnouncementService.Instance.Announce("Skipping.", AnnouncementPriority.Normal);
        }
        catch { }
    }

    /// <summary>Finds the EndTurnSDButton by searching active buttons.</summary>
    private Button FindEndTurnSDButton()
    {
        try
        {
            // Try exact name first
            Il2CppArrayBase<Button> buttons = Object.FindObjectsOfType<Button>();
            if (buttons == null) return null;
            Button fallback = null;
            for (int i = 0; i < buttons.Count; i++)
            {
                Button btn = buttons[i];
                if ((Object)(object)btn == (Object)null) continue;
                if (!((Component)btn).gameObject.activeInHierarchy) continue;
                string goName = ((Object)((Component)btn).gameObject).name;
                if (goName == "EndTurnSDButton")
                    return btn;
                // Broader match for renamed/alternate end turn buttons
                if (goName.Contains("EndTurn", StringComparison.OrdinalIgnoreCase) ||
                    goName.Contains("End_Turn", StringComparison.OrdinalIgnoreCase))
                    fallback = btn;
            }
            if (fallback != null) return fallback;

            // Last resort: try EndTurnButtonView component
            EndTurnButtonView endBtnView = UIHelper.FindComponent<EndTurnButtonView>();
            if ((Object)(object)endBtnView != (Object)null)
            {
                Button viewBtn = ((Component)endBtnView).GetComponentInChildren<Button>(true);
                if ((Object)(object)viewBtn != (Object)null && ((Component)viewBtn).gameObject.activeInHierarchy)
                    return viewBtn;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "FindEndTurnSDButton failed: " + ex.Message);
        }
        return null;
    }

    /// <summary>Announce card name and position only — down arrow for full details.</summary>
    private void AnnounceCurrentCard()
    {
        if (_handCards.Count == 0 || _handIndex >= _handCards.Count) return;
        CardView card = _handCards[_handIndex];
        if ((Object)(object)card == (Object)null) return;

        string cardName = GetCardName(card);

        // If we can't read the card name, it might be face-down
        if (cardName == "Unknown card" || cardName == "Card")
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_card_face_down", _handIndex + 1, _handCards.Count), AnnouncementPriority.Normal);
            return;
        }
        string costStr = "";
        try
        {
            CardValueView costView = ((CardRenderer)card)._CostValueView;
            if ((Object)(object)costView != (Object)null)
                costStr = costView.Value.ToString();
        }
        catch { }
        string msg;
        if (ModSettings.Instance.PositionCounts)
            msg = Loc.Get("bf_card_brief", cardName, costStr, _handIndex + 1, _handCards.Count);
        else
            msg = cardName + (string.IsNullOrEmpty(costStr) ? "" : ", " + Loc.Get("deck_builder_cost", costStr));

        // VerboseCardInfo: append ability text automatically
        if (ModSettings.Instance.VerboseCardInfo)
        {
            string ability = GetCardAbilityText(card);
            if (!string.IsNullOrEmpty(ability)) msg += ". " + ability;
        }

        AnnouncementService.Instance.Announce(msg, AnnouncementPriority.Normal);
    }

    /// <summary>Announce location name, position, and card slots — warns if full.</summary>
    private void AnnounceCurrentLocation()
    {
        if (_locations.Count == 0 || _locationIndex >= _locations.Count) return;
        LocationView loc = _locations[_locationIndex];
        if ((Object)(object)loc == (Object)null) return;

        string locationName = GetLocationName(loc);
        int playerCardCount = CountPlayerCardsAtLocation(_locationIndex);

        // Build restriction prefix when a card is selected
        string prefix = "";
        if (_playState == PlayState.CardSelected)
        {
            string restriction = GetLocationRestriction(loc, playerCardCount);
            if (!string.IsNullOrEmpty(restriction))
                prefix = restriction + ". ";
        }

        string msg = ModSettings.Instance.PositionCounts
            ? Loc.Get("bf_location_brief", locationName, _locationIndex + 1, _locations.Count)
            : locationName;

        // Append power scores
        try
        {
            string playerPower = GetPowerFromTransform(loc._LocationFriendlyPower);
            string opponentPower = GetPowerFromTransform(loc._LocationEnemyPower);
            if (!string.IsNullOrEmpty(playerPower) || !string.IsNullOrEmpty(opponentPower))
                msg += ", " + Loc.Get("bf_power_score", playerPower ?? "0", opponentPower ?? "0");
        }
        catch { }

        // Append restriction AFTER location name so name is always read first
        if (!string.IsNullOrEmpty(prefix))
            msg += ". " + prefix.TrimEnd(' ', '.');

        // Show slot count when selecting a target
        if (playerCardCount >= 4)
        {
            if (string.IsNullOrEmpty(prefix)) // Don't duplicate "Full" if already in prefix
                msg += ". " + Loc.Get("bf_location_full");
        }
        else if (playerCardCount > 0 && _playState == PlayState.CardSelected)
            msg += ". " + Loc.Get("bf_slots_used", playerCardCount);

        AnnouncementService.Instance.Announce(msg, AnnouncementPriority.Normal);
    }

    /// <summary>
    /// Returns a human-readable restriction reason if the selected card cannot be played
    /// at this location, or empty string if playable.
    /// </summary>
    private string GetLocationRestriction(LocationView loc, int playerCardCount)
    {
        // Full location
        if (playerCardCount >= 4)
            return Loc.Get("bf_location_full");

        // Not enough energy for selected card
        if ((Object)(object)_selectedCard != (Object)null)
        {
            try
            {
                CardValueView costView = ((CardRenderer)_selectedCard)._CostValueView;
                if ((Object)(object)costView != (Object)null)
                {
                    int cardCost = costView.Value;
                    int currentEnergy = GetCurrentEnergy();
                    if (currentEnergy >= 0 && cardCost > currentEnergy)
                        return $"Not enough energy, need {cardCost}, have {currentEnergy}";
                }
            }
            catch { }
        }

        // Check location description for card-specific restrictions
        try
        {
            string desc = GetLocationDescription(loc);
            if (string.IsNullOrEmpty(desc))
            {
                // Fallback: read "Location Description Text" from location hierarchy
                Transform locT = ((Component)loc).transform;
                Il2CppArrayBase<TMP_Text> texts = locT.GetComponentsInChildren<TMP_Text>(true);
                if (texts != null)
                {
                    for (int ti = 0; ti < texts.Count; ti++)
                    {
                        TMP_Text tmp = texts[ti];
                        if ((Object)(object)tmp == (Object)null) continue;
                        string goName = ((Object)((Component)tmp).gameObject).name;
                        if (!goName.Contains("Description", StringComparison.OrdinalIgnoreCase)) continue;
                        string text = UIHelper.StripRichText(tmp.text);
                        if (!string.IsNullOrEmpty(text) && text.Length > 3 && !text.Contains("{Missing"))
                        {
                            desc = text;
                            break;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(desc))
            {
                string descLower = desc.ToLower();
                // Common location restriction patterns
                if (descLower.Contains("can't play") || descLower.Contains("cannot play") ||
                    descLower.Contains("no cards can") || descLower.Contains("cards can't be") ||
                    descLower.Contains("can't be played"))
                {
                    // Check if it's a cost restriction and whether our card matches
                    if ((Object)(object)_selectedCard != (Object)null)
                    {
                        try
                        {
                            CardValueView costView = ((CardRenderer)_selectedCard)._CostValueView;
                            if ((Object)(object)costView != (Object)null)
                            {
                                int cardCost = costView.Value;
                                // Parse "cost X or less/more" patterns
                                if (CheckCostRestriction(descLower, cardCost))
                                    return desc;
                            }
                            else
                            {
                                return desc;
                            }
                        }
                        catch { return desc; }
                    }
                    else
                    {
                        return desc;
                    }
                }
            }
        }
        catch { }

        return "";
    }

    /// <summary>Checks if a card cost matches a restriction like "cost X or less can't be played".</summary>
    private bool CheckCostRestriction(string descLower, int cardCost)
    {
        // "cards that cost 3 or less" — extract number and check
        int idx = descLower.IndexOf("cost ");
        if (idx >= 0)
        {
            idx += 5;
            string numStr = "";
            while (idx < descLower.Length && char.IsDigit(descLower[idx]))
            {
                numStr += descLower[idx];
                idx++;
            }
            if (int.TryParse(numStr, out int threshold))
            {
                if (descLower.Contains("or less") && cardCost <= threshold)
                    return true;
                if (descLower.Contains("or more") && cardCost >= threshold)
                    return true;
                if (!descLower.Contains("or less") && !descLower.Contains("or more") && cardCost == threshold)
                    return true;
            }
        }
        // If we can't parse, just show the restriction for safety
        return true;
    }

    /// <summary>Returns the current energy value, or -1 if unavailable.</summary>
    private int GetCurrentEnergy()
    {
        try
        {
            EnergyView ev = UIHelper.FindComponent<EnergyView>();
            if ((Object)(object)ev == (Object)null) return -1;
            string text = UIHelper.GetAllText(((Component)ev).gameObject);
            if (string.IsNullOrEmpty(text)) return -1;
            // Energy text is like "3. 6" or "3/6" — first number is current
            string cleaned = text.Replace("/", ".").Trim();
            int dotIdx = cleaned.IndexOf('.');
            if (dotIdx > 0)
            {
                string currentStr = cleaned.Substring(0, dotIdx).Trim();
                if (int.TryParse(currentStr, out int val))
                    return val;
            }
            // Single number
            if (int.TryParse(cleaned, out int single))
                return single;
        }
        catch { }
        return -1;
    }

    /// <summary>Count player cards at a specific location index.</summary>
    private int CountPlayerCardsAtLocation(int locIdx)
    {
        if (locIdx < 0 || locIdx >= _locations.Count) return 0;
        try
        {
            LocationView targetLoc = _locations[locIdx];
            if ((Object)(object)targetLoc == (Object)null) return 0;
            float locX = ((Component)targetLoc).transform.position.x;

            Il2CppArrayBase<CardView> allCards = Object.FindObjectsOfType<CardView>();
            if (allCards == null) return 0;

            int count = 0;
            for (int i = 0; i < allCards.Count; i++)
            {
                CardView cv = allCards[i];
                if ((Object)(object)cv == (Object)null) continue;
                if (!((Component)cv).gameObject.activeInHierarchy) continue;
                if (IsInHandZone(cv)) continue;
                try { if (IsInObjectPool(cv)) continue; } catch { }

                int entityId;
                try { entityId = cv.EntityId; } catch { continue; }
                if (entityId <= 0) continue;

                // Only count our cards
                if (!_ourCardEntityIds.Contains(entityId)) continue;

                // Check proximity to this location
                float cardX = ((Component)cv).transform.position.x;
                int nearestLocIdx = -1;
                float minDist = float.MaxValue;
                for (int li = 0; li < _locations.Count; li++)
                {
                    LocationView otherLoc = _locations[li];
                    if ((Object)(object)otherLoc == (Object)null) continue;
                    float dist = Math.Abs(cardX - ((Component)otherLoc).transform.position.x);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearestLocIdx = li;
                    }
                }
                if (nearestLocIdx == locIdx) count++;
            }
            return count;
        }
        catch { return 0; }
    }

    private void AnnounceGameInfo()
    {
        List<string> parts = new List<string>();
        string energy = GetEnergyText();
        if (!string.IsNullOrEmpty(energy))
        {
            parts.Add($"Energy {energy}");
        }
        parts.Add(Loc.Get("bf_hand_count", _handCards.Count));
        parts.Add(Loc.Get("bf_location_count", _locations.Count));
        if (!string.IsNullOrEmpty(_lastInstructionText))
        {
            parts.Add(Loc.Get("bf_tutorial_instruction", _lastInstructionText));
        }
        AnnouncementService.Instance.Announce(string.Join(". ", parts), AnnouncementPriority.Normal);
    }

    private void AnnounceEnergy()
    {
        string energy = GetEnergyText();
        if (!string.IsNullOrEmpty(energy))
        {
            AnnouncementService.Instance.Announce($"Energy {energy}", AnnouncementPriority.Normal);
        }
        else
        {
            AnnouncementService.Instance.Announce("Energy not available", AnnouncementPriority.Normal);
        }
    }

    private void AnnounceTurnInfo()
    {
        try
        {
            // Find TurnCountText_Active or TurnCountText_Inactive TMP_Text elements
            Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
            if (texts == null) { AnnouncementService.Instance.Announce(Loc.Get("bf_turn_not_available"), AnnouncementPriority.Normal); return; }

            string turnText = null;
            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text t = texts[i];
                if ((Object)(object)t == (Object)null) continue;
                if (!((Component)t).gameObject.activeInHierarchy) continue;

                string goName = ((Object)((Component)t).gameObject).name;
                if (goName.Contains("TurnCountText", StringComparison.OrdinalIgnoreCase))
                {
                    string val = t.text;
                    if (!string.IsNullOrEmpty(val) && val.Contains("/"))
                    {
                        turnText = val.Trim();
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(turnText))
            {
                string cubeStake = GetCubeStakeValue();
                string cubeMsg = !string.IsNullOrEmpty(cubeStake) ? " " + Loc.Get("bf_cube_stake", cubeStake) : "";

                if (turnText == "FINAL")
                {
                    // Final turn — announce with energy and cube stake
                    string energy = GetEnergyText();
                    string msg = Loc.Get("bf_final_turn");
                    if (!string.IsNullOrEmpty(energy)) msg += " Energy " + energy + ".";
                    msg += cubeMsg;
                    AnnouncementService.Instance.Announce(msg, AnnouncementPriority.Normal);
                }
                else
                {
                    // turnText is like "3 / 6" — parse into "Turn 3 of 6"
                    string[] parts = turnText.Split('/');
                    if (parts.Length == 2)
                    {
                        string current = parts[0].Trim();
                        string total = parts[1].Trim();
                        AnnouncementService.Instance.Announce(Loc.Get("bf_turn_info", current, total) + cubeMsg, AnnouncementPriority.Normal);
                    }
                    else
                    {
                        AnnouncementService.Instance.Announce(Loc.Get("bf_turn_info_raw", turnText) + cubeMsg, AnnouncementPriority.Normal);
                    }
                }
            }
            else
            {
                AnnouncementService.Instance.Announce(Loc.Get("bf_turn_not_available"), AnnouncementPriority.Normal);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "AnnounceTurnInfo failed: " + ex.Message);
            AnnouncementService.Instance.Announce(Loc.Get("bf_turn_not_available"), AnnouncementPriority.Normal);
        }
    }

    /// <summary>Count cards in hand that could be played (cost <= available energy).</summary>
    private int CountPlayableCards()
    {
        try
        {
            string energyText = GetEnergyText();
            if (string.IsNullOrEmpty(energyText)) return 0;
            // Energy text is like "3/6" — we want the first number (available)
            string[] parts = energyText.Split('/');
            if (parts.Length == 0) return 0;
            if (!int.TryParse(parts[0].Trim(), out int available)) return 0;
            if (available <= 0) return 0;

            int count = 0;
            for (int i = 0; i < _handCards.Count; i++)
            {
                CardView card = _handCards[i];
                if ((Object)(object)card == (Object)null) continue;
                try
                {
                    CardValueView costView = ((CardRenderer)card)._CostValueView;
                    if ((Object)(object)costView != (Object)null && costView.Value <= available)
                        count++;
                }
                catch { }
            }
            return count;
        }
        catch { return 0; }
    }

    /// <summary>Find or cache the TurnTimer component.</summary>
    private TurnTimer FindTurnTimer()
    {
        if ((Object)(object)_turnTimer != (Object)null) return _turnTimer;
        try { _turnTimer = Object.FindObjectOfType<TurnTimer>(); }
        catch { }
        return _turnTimer;
    }

    /// <summary>Checks TurnTimer component for warning thresholds and announces time running low.</summary>
    private void CheckTurnTimer()
    {
        if (Time.time - _lastTimerCheck < 0.5f) return;
        _lastTimerCheck = Time.time;

        TurnTimer timer = FindTurnTimer();
        if ((Object)(object)timer == (Object)null) return;

        try
        {
            if (!timer._Active_k__BackingField || timer._noTimer) return;
            float remaining = timer.GetRemainingTime();

            // Warning at 15 seconds
            if (!_turnTimerWarningFired && remaining <= 15f && remaining > 0f)
            {
                _turnTimerWarningFired = true;
                int secs = (int)remaining;
                AnnouncementService.Instance.Announce(Loc.Get("bf_timer_warning", secs.ToString()), AnnouncementPriority.Immediate);
            }
            // Urgent at 5 seconds
            else if (!_turnTimerUrgentFired && remaining <= 5f && remaining > 0f)
            {
                _turnTimerUrgentFired = true;
                int secs = (int)remaining;
                AnnouncementService.Instance.Announce(Loc.Get("bf_timer_urgent", secs.ToString()), AnnouncementPriority.Critical);
            }

            // Reset when timer goes back up (new turn started)
            if (remaining > 20f)
            {
                _turnTimerWarningFired = false;
                _turnTimerUrgentFired = false;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "CheckTurnTimer failed: " + ex.Message);
        }
    }

    /// <summary>W key: announce time remaining from TurnTimer component.</summary>
    private void AnnounceTimeRemaining()
    {
        TurnTimer timer = FindTurnTimer();
        if ((Object)(object)timer == (Object)null || !timer._Active_k__BackingField || timer._noTimer)
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_timer_not_active"), AnnouncementPriority.Normal);
            return;
        }
        try
        {
            float remaining = timer.GetRemainingTime();
            int secs = (int)remaining;
            AnnouncementService.Instance.Announce(Loc.Get("bf_timer_remaining", secs.ToString()), AnnouncementPriority.High);
        }
        catch
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_timer_not_active"), AnnouncementPriority.Normal);
        }
    }

    /// <summary>Auto-announces turn number, energy, and "your turn" when a new turn starts.</summary>
    private void AutoAnnounceTurnStart()
    {
        try
        {
            string turnText = GetTurnText();
            string energy = GetEnergyText();

            // Ultra-concise: "Turn 3, energy 3, go." — minimize speech time so user can act fast
            string msg;
            if (turnText == "FINAL")
            {
                if (!_turnGate.ShouldAnnounce("FINAL")) return;
                msg = Loc.Get("bf_turn_start_final", energy ?? "?");
            }
            else if (turnText != null)
            {
                // Suppress re-announcing the same turn when the hand count changed
                // because a card was played rather than because the turn advanced.
                if (!_turnGate.ShouldAnnounce(turnText)) return;
                msg = Loc.Get("bf_turn_start", turnText, energy ?? "?");
            }
            else
                msg = Loc.Get("bf_your_turn");

            // Immediate priority: interrupts any lingering speech so user hears this first
            AnnouncementService.Instance.Announce(msg, AnnouncementPriority.Immediate);
            _turnPhaseAnnounced = false; // Reset so we can announce "waiting" after they end turn
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "AutoAnnounceTurnStart failed: " + ex.Message);
        }
    }

    /// <summary>Returns the current turn number (e.g. "3"), the sentinel "FINAL", or null. Does not announce.</summary>
    private string GetTurnText()
    {
        try
        {
            Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
            if (texts == null) return null;
            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text t = texts[i];
                if ((Object)(object)t == (Object)null) continue;
                if (!((Component)t).gameObject.activeInHierarchy) continue;
                string goName = ((Object)((Component)t).gameObject).name;
                if (goName.Contains("TurnCountText", StringComparison.OrdinalIgnoreCase))
                {
                    string val = t.text;
                    if (string.IsNullOrEmpty(val)) continue;
                    // The label is TMP rich text such as "<size=490>2</size> / 6";
                    // TurnTextParser strips the markup before splitting on '/' so the
                    // closing "</size>" tag's slash can't corrupt the turn number.
                    string current = TurnTextParser.ParseCurrent(val);
                    if (current != null) return current;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Checks end turn button text to detect turn phase and announce changes.</summary>
    private void CheckTurnPhase()
    {
        if (UnityEngine.Time.time - _lastTurnPhaseCheck < 0.35f) return;
        _lastTurnPhaseCheck = UnityEngine.Time.time;

        try
        {
            EndTurnButtonView endBtn = UIHelper.FindComponent<EndTurnButtonView>();
            if ((Object)(object)endBtn == (Object)null) return;

            Il2CppArrayBase<TMP_Text> texts = ((Component)endBtn).GetComponentsInChildren<TMP_Text>(false);
            if (texts == null) return;

            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text tmp = texts[i];
                if ((Object)(object)tmp == (Object)null || !((Component)tmp).gameObject.activeInHierarchy) continue;
                string text = tmp.text;
                if (string.IsNullOrWhiteSpace(text)) continue;
                text = UIHelper.StripRichText(text.Trim());

                bool isWaiting = text.Contains("Wait", StringComparison.OrdinalIgnoreCase);
                bool isEndTurn = text.Contains("End Turn", StringComparison.OrdinalIgnoreCase) ||
                                 text.Contains("End", StringComparison.OrdinalIgnoreCase);

                if (isWaiting && _isPlayerTurn)
                {
                    _isPlayerTurn = false;
                    if (!_turnPhaseAnnounced)
                    {
                        _turnPhaseAnnounced = true;
                        AnnouncementService.Instance.Announce(Loc.Get("bf_waiting_for_opponent"), AnnouncementPriority.Immediate);
                    }
                    return;
                }
                else if (isEndTurn && !isWaiting && !_isPlayerTurn && _turnChangeCount >= 1)
                {
                    _isPlayerTurn = true;
                    _turnPhaseAnnounced = false;
                    return;
                }
            }
        }
        catch { }
    }

    private void TrySnap()
    {
        try
        {
            // Find the StakesButton by searching for a Button inside a GO named "StakesButton"
            Button stakesBtn = FindButtonByGameObjectName("StakesButton");
            if ((Object)(object)stakesBtn != (Object)null)
            {
                // Read current cube value before snapping
                string cubeValue = GetCubeStakeValue();
                // Use mouse simulation — onClick.Invoke doesn't trigger the snap mechanic
                if (SimulateClickOnButton((Component)stakesBtn))
                {
                    AnnouncementService.Instance.Announce(Loc.Get("bf_snapped"), AnnouncementPriority.Immediate);
                    DebugLogger.LogInput("G", "Snap via mouse simulation — stakes were " + cubeValue);
                }
                else
                {
                    AnnouncementService.Instance.Announce(Loc.Get("bf_snap_no_button"), AnnouncementPriority.Normal);
                }
            }
            else
            {
                // Maybe snap isn't available (already snapped, or game state doesn't allow it)
                string cubeValue = GetCubeStakeValue();
                if (!string.IsNullOrEmpty(cubeValue))
                {
                    AnnouncementService.Instance.Announce(Loc.Get("bf_snap_not_available", cubeValue), AnnouncementPriority.Normal);
                }
                else
                {
                    AnnouncementService.Instance.Announce(Loc.Get("bf_snap_no_button"), AnnouncementPriority.Normal);
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TrySnap failed: " + ex.Message);
            AnnouncementService.Instance.Announce(Loc.Get("bf_snap_no_button"), AnnouncementPriority.Normal);
        }
    }

    private void TryRetreat()
    {
        try
        {
            // Find the RetreatSDButton
            Button retreatBtn = FindButtonByGameObjectName("RetreatSDButton");
            if ((Object)(object)retreatBtn == (Object)null)
            {
                AnnouncementService.Instance.Announce(Loc.Get("bf_retreat_no_button"), AnnouncementPriority.Immediate);
                return;
            }

            string cubeValue = GetCubeStakeValue();

            // First press: warn and wait for confirmation
            if (!_retreatPending || (UnityEngine.Time.time - _retreatPendingTime > RetreatConfirmTimeout))
            {
                _retreatPending = true;
                _retreatPendingTime = UnityEngine.Time.time;
                AnnouncementService.Instance.Announce(Loc.Get("bf_retreat_confirm", cubeValue), AnnouncementPriority.Immediate);
                DebugLogger.LogInput("R", "Retreat confirmation requested — stakes " + cubeValue);
                return;
            }

            // Second press within timeout: actually retreat
            _retreatPending = false;
            if (SimulateClickOnButton((Component)retreatBtn))
            {
                AnnouncementService.Instance.Announce(Loc.Get("bf_retreat_initiated", cubeValue), AnnouncementPriority.Critical);
                DebugLogger.LogInput("R", "Retreat confirmed via mouse simulation — stakes " + cubeValue);
            }
            else
            {
                AnnouncementService.Instance.Announce(Loc.Get("bf_retreat_no_button"), AnnouncementPriority.Immediate);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TryRetreat failed: " + ex.Message);
            AnnouncementService.Instance.Announce(Loc.Get("bf_retreat_no_button"), AnnouncementPriority.Immediate);
        }
    }

    private string GetCubeStakeValue()
    {
        try
        {
            Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
            if (texts == null) return "";
            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text t = texts[i];
                if ((Object)(object)t == (Object)null) continue;
                if (!((Component)t).gameObject.activeInHierarchy) continue;
                string goName = ((Object)((Component)t).gameObject).name;
                if (goName.Contains("Cube Value", StringComparison.OrdinalIgnoreCase) ||
                    goName.Contains("StakeValue", StringComparison.OrdinalIgnoreCase) ||
                    goName.Contains("CubeValue", StringComparison.OrdinalIgnoreCase))
                {
                    string val = t.text;
                    if (!string.IsNullOrEmpty(val))
                        return UIHelper.StripRichText(val.Trim());
                }
            }
        }
        catch { }
        return "";
    }

    private Button FindButtonByGameObjectName(string targetName)
    {
        try
        {
            Il2CppArrayBase<Button> buttons = Object.FindObjectsOfType<Button>();
            if (buttons == null) return null;
            for (int i = 0; i < buttons.Count; i++)
            {
                Button btn = buttons[i];
                if ((Object)(object)btn == (Object)null) continue;
                if (!((Component)btn).gameObject.activeInHierarchy) continue;
                string goName = ((Object)((Component)btn).gameObject).name;
                if (goName == targetName)
                    return btn;
            }
        }
        catch { }
        return null;
    }

    // Generic tutorial texts to ignore when announcing hints (always loaded, not contextual)
    private static readonly HashSet<string> _genericTutorialTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Highest Power wins!",
        "Gain more Energy each turn.",
        "Cards cost Energy.",
        "Tilt cards around to see awesome effects!",
        "Most cards have special abilities!",
        "Each card adds Power",
        "Win 2 out of 3 Locations!",
    };

    /// <summary>Check if a text is a generic tutorial teaching text (not a contextual hint).</summary>
    private static bool IsGenericTutorialText(string text)
    {
        if (_genericTutorialTexts.Contains(text)) return true;
        // Filter very short texts and turn counters
        if (text.Length < 8) return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+\s*/\s*\d+$")) return true;
        if (text.StartsWith("turn ", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Scan visible tutorial/tooltip texts and announce contextual hints (not generic teaching).</summary>
    private void AnnounceActiveTutorialHints()
    {
        if (!ModSettings.Instance.TutorialMessages) return;
        try
        {
            List<string> hints = new List<string>();

            // Gather texts from TMP_Text under Tooltip/Tutorial parents
            Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
            if (texts != null)
            {
                for (int i = 0; i < texts.Count; i++)
                {
                    TMP_Text tmp = texts[i];
                    if ((Object)(object)tmp == (Object)null || !((Component)tmp).gameObject.activeInHierarchy) continue;
                    if (((Graphic)tmp).color.a < 0.1f) continue;
                    if (!IsUnderToolTip(tmp.transform)) continue;
                    if (IsUnderSpeechBubble(tmp.transform)) continue;

                    string text = tmp.text;
                    if (string.IsNullOrWhiteSpace(text) || text.Contains("{Missing Entry}")) continue;
                    text = UIHelper.StripRichText(text.Trim());
                    if (text.Length < 3) continue;
                    if (IsGenericTutorialText(text)) continue;
                    if (!hints.Contains(text)) hints.Add(text);
                }
            }

            if (hints.Count > 0)
            {
                // Announce at most 3 hints to avoid overwhelming
                int maxHints = Math.Min(hints.Count, 3);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < maxHints; i++)
                {
                    if (sb.Length > 0) sb.Append(". ");
                    sb.Append(hints[i]);
                }
                string combined = Loc.Get("bf_tutorial_hints", sb.ToString());
                AnnouncementService.Instance.Announce(combined, AnnouncementPriority.Low);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                    $"Tutorial hints announced: {maxHints} of {hints.Count}");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "AnnounceActiveTutorialHints failed: " + ex.Message);
        }
    }

    private void AnnounceTutorialInstruction()
    {
        if (!string.IsNullOrEmpty(_lastInstructionText))
        {
            AnnouncementService.Instance.Announce(_lastInstructionText, AnnouncementPriority.Normal);
        }
        else
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_no_tutorial"), AnnouncementPriority.Normal);
        }
    }

    private bool _gameOverAnnounced = false;
    private float _gameOverCheckTime = 0f;

    private void OnGameEntered()
    {
        SetupHarmonyPatches();
        SetupStepMapHooks();
        DebugLogger.Log(LogCategory.State, "BattlefieldHandler", "Game entered");
        // AccessStateManager removed — state is now tracked by NavigatorManager
        _area = FocusArea.Hand;
        _playState = PlayState.Browsing;
        _handIndex = 0;
        _locationIndex = 0;
        _gameOverAnnounced = false;
        _gameOverCheckTime = 0f;

        _opponentNameAnnounced = false;
        _isPlayerTurn = false;
        _turnPhaseAnnounced = false;
        _lastTurnPhaseCheck = 0f;

        // Read opponent name and announce game start
        string opponentName = ReadOpponentName();
        string playerName = ReadPlayerName();
        if (!string.IsNullOrEmpty(opponentName))
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_game_entered_vs", opponentName), AnnouncementPriority.High);
            _opponentNameAnnounced = true;
        }
        else
        {
            AnnouncementService.Instance.Announce(Loc.Get("bf_game_entered"), AnnouncementPriority.High);
        }
        if (!string.IsNullOrEmpty(playerName))
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Player: " + playerName + " vs " + opponentName);
        }
    }

    /// <summary>Reads the opponent player name from EnemyPlayerNameText.</summary>
    private string ReadOpponentName()
    {
        try
        {
            Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
            if (texts == null) return "";
            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text tmp = texts[i];
                if ((Object)(object)tmp == (Object)null || !((Component)tmp).gameObject.activeInHierarchy) continue;
                string goName = ((Object)((Component)tmp).gameObject).name;
                if (goName == "EnemyPlayerNameText" || goName.Contains("EnemyPlayer"))
                {
                    string text = tmp.text;
                    if (!string.IsNullOrWhiteSpace(text))
                        return UIHelper.StripRichText(text.Trim());
                }
            }
        }
        catch { }
        return "";
    }

    /// <summary>Reads the local player name.</summary>
    private string ReadPlayerName()
    {
        try
        {
            Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
            if (texts == null) return "";
            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text tmp = texts[i];
                if ((Object)(object)tmp == (Object)null || !((Component)tmp).gameObject.activeInHierarchy) continue;
                string goName = ((Object)((Component)tmp).gameObject).name;
                if (goName == "LocalPlayerNameText" || goName.Contains("LocalPlayer"))
                {
                    string text = tmp.text;
                    if (!string.IsNullOrWhiteSpace(text))
                        return UIHelper.StripRichText(text.Trim());
                }
            }
        }
        catch { }
        return "";
    }

    /// <summary>Checks the end turn button text for game-over states like "Collect Rewards".</summary>
    private void CheckGameOver()
    {
        if (_gameOverAnnounced) return;
        if (UnityEngine.Time.time - _gameOverCheckTime < 2f) return;
        _gameOverCheckTime = UnityEngine.Time.time;

        try
        {
            EndTurnButtonView endBtn = UIHelper.FindComponent<EndTurnButtonView>();
            if ((Object)(object)endBtn == (Object)null) return;

            // Look for text on the end turn button
            Il2CppArrayBase<TMP_Text> texts = ((Component)endBtn).GetComponentsInChildren<TMP_Text>(false);
            if (texts == null) return;
            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text tmp = texts[i];
                if ((Object)(object)tmp == (Object)null || !((Component)tmp).gameObject.activeInHierarchy) continue;
                string text = tmp.text;
                if (string.IsNullOrWhiteSpace(text)) continue;
                text = UIHelper.StripRichText(text.Trim());

                if (text.Contains("Collect", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Reward", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Next", StringComparison.OrdinalIgnoreCase))
                {
                    _gameOverAnnounced = true;
                    // Read the game result from location scores
                    string result = ReadGameResult();
                    AnnouncementService.Instance.Announce(Loc.Get("bf_game_over", result), AnnouncementPriority.Critical);
                    AnnouncementService.Instance.Announce(Loc.Get("bf_game_over_instructions"), AnnouncementPriority.Immediate);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Game over detected: " + text + " Result: " + result);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "CheckGameOver failed: " + ex.Message);
        }
    }

    /// <summary>Reads location scores to determine win/lose/draw with detailed breakdown.</summary>
    private string ReadGameResult()
    {
        int playerWins = 0;
        int opponentWins = 0;
        List<string> locationResults = new List<string>();
        try
        {
            if (_locations == null || _locations.Count == 0) return "";
            for (int i = 0; i < _locations.Count; i++)
            {
                LocationView loc = _locations[i];
                if ((Object)(object)loc == (Object)null) continue;
                try
                {
                    Transform fp = loc._LocationFriendlyPower;
                    Transform ep = loc._LocationEnemyPower;
                    if ((Object)(object)fp == (Object)null || (Object)(object)ep == (Object)null) continue;
                    string fpText = UIHelper.GetTextInChildren(((Component)fp).gameObject);
                    string epText = UIHelper.GetTextInChildren(((Component)ep).gameObject);
                    string locName = GetLocationName(loc);
                    if (int.TryParse(fpText, out int fpVal) && int.TryParse(epText, out int epVal))
                    {
                        if (fpVal > epVal) playerWins++;
                        else if (epVal > fpVal) opponentWins++;
                        if (!string.IsNullOrEmpty(locName))
                            locationResults.Add(Loc.Get("bf_result_location", locName, fpVal.ToString(), epVal.ToString()));
                    }
                }
                catch { }
            }
        }
        catch { }

        string outcome;
        if (playerWins > opponentWins) outcome = Loc.Get("bf_result_win");
        else if (opponentWins > playerWins) outcome = Loc.Get("bf_result_lose");
        else outcome = Loc.Get("bf_result_draw");

        // Include cube stake
        string cubeStake = GetCubeStakeValue();
        if (!string.IsNullOrEmpty(cubeStake))
            outcome += " " + Loc.Get("bf_result_cubes", cubeStake);

        // Include per-location breakdown
        if (locationResults.Count > 0)
            outcome += " " + string.Join(". ", locationResults);

        return outcome;
    }

    private void OnGameLeft()
    {
        DebugLogger.Log(LogCategory.State, "BattlefieldHandler", "Game left");
        // AccessStateManager removed — state is now tracked by NavigatorManager
        _handZone = null;
        _handCards.Clear();
        _locations.Clear();
        _selectedCard = null;
        _playState = PlayState.Browsing;
        _lastTutorialText = "";
        _lastInstructionText = "";
        _announcedTooltips.Clear();
        _trackedTutorialTexts.Clear();
        _lastHandCount = -1;
        _tapToContinueAnnounced = false;
        _tutorialTextsInitialized = false;
        _tutorialInitStartTime = 0f;

        _knownBoardCardEntityIds.Clear();
        _ourCardEntityIds.Clear();
        _pendingOpponentAnnouncements.Clear();
        _lastPlayedCardName = null;
        _lastPlayedEntityId = -1;
        _handCountAfterPlay = -1;
        _rollbackConfirmStartTime = 0f;
        _playVerifyTime = 0f;
        _playVerifyExpectedCount = -1;
        _gameReadyForOpponentDetection = false;
        _turnChangeCount = 0;
        _turnGate.Reset();
        _opponentNameAnnounced = false;
    }

    private string GetCardName(CardView card)
    {
        if ((Object)(object)card == (Object)null) return "Unknown card";
        try
        {
            string cardName = ((CardRenderer)card).CardName;
            if (!string.IsNullOrEmpty(cardName))
            {
                return UIHelper.StripRichText(cardName);
            }
        }
        catch { }
        try
        {
            string name = ((Object)((Component)card).gameObject).name;
            if (!string.IsNullOrEmpty(name))
            {
                string cleaned = UIHelper.CleanGameObjectName(name);
                if (!string.IsNullOrEmpty(cleaned) && !cleaned.Equals("Card", StringComparison.OrdinalIgnoreCase))
                {
                    return cleaned;
                }
            }
        }
        catch { }
        return "Card";
    }

    /// <summary>Get card name for board cards — uses CardDefId and CardModel for more reliable names.</summary>
    private string GetBoardCardName(CardView card)
    {
        if ((Object)(object)card == (Object)null) return null;
        // Try CardDefId first — most reliable for board cards
        try
        {
            var cardDefId = card.CardDefId;
            if (cardDefId != null)
            {
                string defIdStr = cardDefId.ToString();
                if (!string.IsNullOrEmpty(defIdStr) && defIdStr != "0" && !defIdStr.Contains("Missing"))
                    return UIHelper.StripRichText(defIdStr);
            }
        }
        catch { }
        // Try CardRenderer.CardName
        try
        {
            string cardName = ((CardRenderer)card).CardName;
            if (!string.IsNullOrEmpty(cardName) && !cardName.Contains("Card View"))
                return UIHelper.StripRichText(cardName);
        }
        catch { }
        // Try game object name
        try
        {
            string goName = ((Object)((Component)card).gameObject).name;
            if (!string.IsNullOrEmpty(goName) && !goName.Contains("CardView") && !goName.Contains("Pool"))
            {
                string cleaned = UIHelper.CleanGameObjectName(goName);
                if (!string.IsNullOrEmpty(cleaned) && cleaned != "Card") return cleaned;
            }
        }
        catch { }
        return null;
    }

    private string GetCardInfo(CardView card)
    {
        if ((Object)(object)card == (Object)null) return "";
        List<string> parts = new List<string>();
        try
        {
            CardValueView costView = ((CardRenderer)card)._CostValueView;
            if ((Object)(object)costView != (Object)null)
            {
                parts.Add($"cost {costView.Value}");
            }
        }
        catch { }
        try
        {
            CardValueView powerView = ((CardRenderer)card)._PowerValueView;
            if ((Object)(object)powerView != (Object)null)
            {
                parts.Add($"power {powerView.Value}");
            }
        }
        catch { }
        return string.Join(", ", parts);
    }

    /// <summary>Get the card's ability/description text — tries CardRenderer._AbilityText, then child TMP_Text search.</summary>
    private string GetCardAbilityText(CardView card)
    {
        if ((Object)(object)card == (Object)null) return "";

        // Try 1: Find "Ability Text" TMP_Text under AbilityTextRoot/CardAbilityText hierarchy
        // This is the game's standard path for ability text display
        try
        {
            Transform cardTransform = ((Component)card).transform;
            // Search for AbilityTextRoot first, then find "Ability Text" inside it
            Transform abilityRoot = UIHelper.FindChildByName(cardTransform, "AbilityTextRoot")
                ?? UIHelper.FindChildByName(cardTransform, "CardAbilityText");
            if ((Object)(object)abilityRoot != (Object)null)
            {
                Il2CppArrayBase<TMP_Text> abilityTexts = ((Component)abilityRoot).GetComponentsInChildren<TMP_Text>(true);
                if (abilityTexts != null)
                {
                    for (int i = 0; i < abilityTexts.Count; i++)
                    {
                        TMP_Text tmp = abilityTexts[i];
                        if ((Object)(object)tmp == (Object)null) continue;
                        string text = tmp.text;
                        if (string.IsNullOrWhiteSpace(text) || text.Contains("Missing Entry")) continue;
                        string cleaned = UIHelper.StripRichText(text.Trim());
                        if (cleaned.Length > 3)
                        {
                            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Card ability from AbilityTextRoot: {cleaned}");
                            return cleaned;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"AbilityTextRoot search failed: {ex.Message}");
        }

        // Try 2: Search all child TMP_Text for any named "Ability Text"
        try
        {
            Il2CppArrayBase<TMP_Text> texts = ((Component)card).GetComponentsInChildren<TMP_Text>(true);
            if (texts != null)
            {
                string cardName = GetCardName(card);
                for (int i = 0; i < texts.Count; i++)
                {
                    TMP_Text tmp = texts[i];
                    if ((Object)(object)tmp == (Object)null) continue;
                    string goName = ((Object)((Component)tmp).gameObject).name;
                    if (!goName.Contains("Ability", StringComparison.OrdinalIgnoreCase)) continue;
                    string text = tmp.text;
                    if (string.IsNullOrWhiteSpace(text) || text.Contains("Missing Entry")) continue;
                    if (IsFlavorText(text)) continue;
                    string cleaned = UIHelper.StripRichText(text.Trim());
                    if (cleaned.Length > 3 && cleaned != cardName)
                    {
                        DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Card ability from child '{goName}': {cleaned}");
                        return cleaned;
                    }
                }
            }
        }
        catch { }

        // Try 3: Call UpdateAbilityText() on CardView to force localization, then retry
        try
        {
            card.UpdateAbilityText();
            Transform cardTransform = ((Component)card).transform;
            Transform abilityRoot = UIHelper.FindChildByName(cardTransform, "AbilityTextRoot");
            if ((Object)(object)abilityRoot != (Object)null)
            {
                Il2CppArrayBase<TMP_Text> abilityTexts = ((Component)abilityRoot).GetComponentsInChildren<TMP_Text>(true);
                if (abilityTexts != null)
                {
                    for (int i = 0; i < abilityTexts.Count; i++)
                    {
                        TMP_Text tmp = abilityTexts[i];
                        if ((Object)(object)tmp == (Object)null) continue;
                        string text = tmp.text;
                        if (string.IsNullOrWhiteSpace(text) || text.Contains("Missing Entry")) continue;
                        string cleaned = UIHelper.StripRichText(text.Trim());
                        if (cleaned.Length > 3) return cleaned;
                    }
                }
            }
        }
        catch { }
        return "";
    }

    /// <summary>Check if raw card text is flavor text (italic quoted text, not a real ability).</summary>
    /// <summary>Returns true if the text is a card cosmetic label (variant, border, rarity, finish).</summary>
    private static bool IsCardCosmeticText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        // Exact rarity/border matches
        if (text.Equals("Common", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("Uncommon", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("Rare", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("Epic", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("Legendary", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("Infinity", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("Ultra", StringComparison.OrdinalIgnoreCase)) return true;
        // Patterns: "X Border", "X Finish", "X Flare", "Base Card", "Series N"
        if (text.EndsWith(" Border", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.EndsWith(" Finish", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.EndsWith(" Flare", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("Variant", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("Base Card", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("Series ", StringComparison.OrdinalIgnoreCase)) return true;
        // Artist credit lines (e.g., "PANDART STUDIO Variant")
        if (text.Contains("STUDIO", StringComparison.OrdinalIgnoreCase) || text.Contains("STUDIOS", StringComparison.OrdinalIgnoreCase)) return true;
        // "Equipped Cosmetics", "Card Information", cosmetic detail labels
        if (text.Equals("Equipped Cosmetics", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("Card Information", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("+ No Flare", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("Base Finish", StringComparison.OrdinalIgnoreCase)) return true;
        // "0/5 XP" style upgrade progress
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+/\d+\s*XP$")) return true;
        return false;
    }

    private static bool IsFlavorText(string rawText)
    {
        if (string.IsNullOrEmpty(rawText)) return false;
        string trimmed = rawText.Trim();
        // Flavor text is wrapped in <i>...</i> tags
        if (trimmed.StartsWith("<i>", StringComparison.OrdinalIgnoreCase))
            return true;
        // After stripping tags, flavor text starts and ends with quotes
        string stripped = UIHelper.StripRichText(trimmed);
        if (stripped.Length > 2 && stripped.StartsWith("\"") && stripped.EndsWith("\""))
            return true;
        // Also catch smart quotes
        if (stripped.Length > 2 && (stripped[0] == '\u201C' || stripped[0] == '\u2018')
            && (stripped[stripped.Length - 1] == '\u201D' || stripped[stripped.Length - 1] == '\u2019'))
            return true;
        return false;
    }

    /// <summary>Extract text from an object that may be a string, TMP_Text, or TextMeshPro component.</summary>
    private static string ExtractTextFromObject(object val)
    {
        if (val == null) return null;
        // Direct string
        if (val is string str) return str;
        // TMP_Text or TextMeshPro — read the .text property
        try
        {
            if (val is TMP_Text tmpText && (Object)(object)tmpText != (Object)null)
                return tmpText.text;
        }
        catch { }
        // Try as Il2Cpp object with a text property via Reflection
        try
        {
            var textProp = val.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            if (textProp != null)
            {
                object textVal = textProp.GetValue(val);
                return textVal?.ToString();
            }
        }
        catch { }
        return null;
    }

    private string GetEnergyText()
    {
        try
        {
            EnergyView ev = UIHelper.FindComponent<EnergyView>();
            if ((Object)(object)ev == (Object)null) return "";
            string text = UIHelper.GetAllText(((Component)ev).gameObject);
            if (!string.IsNullOrEmpty(text))
            {
                return text.Replace(". ", "/");
            }
        }
        catch { }
        return "";
    }

    private string GetLocationName(LocationView location)
    {
        if ((Object)(object)location == (Object)null) return "Unknown location";
        try
        {
            Transform nameChild = UIHelper.FindChildByName(((Component)location).transform, "Location Name Text");
            if ((Object)(object)nameChild != (Object)null)
            {
                string text = UIHelper.GetText(((Component)nameChild).gameObject);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            string childText = UIHelper.GetTextInChildren(((Component)location).gameObject);
            if (!string.IsNullOrEmpty(childText)) return childText;

            string goName = ((Object)((Component)location).gameObject).name;
            if (!string.IsNullOrEmpty(goName)) return UIHelper.CleanGameObjectName(goName);
            return "Location";
        }
        catch
        {
            return "Location";
        }
    }

    private string GetLocationInfo(LocationView location, string locationName = "")
    {
        if ((Object)(object)location == (Object)null) return "";
        try
        {
            List<string> parts = new List<string>();

            // Get location description/ability — skip if it matches the location name
            string desc = GetLocationDescription(location);
            if (!string.IsNullOrEmpty(desc) && desc != locationName)
            {
                parts.Add(desc);
            }

            // Get player and opponent power
            string playerPower = GetPowerFromTransform(location._LocationFriendlyPower);
            string opponentPower = GetPowerFromTransform(location._LocationEnemyPower);
            if (!string.IsNullOrEmpty(playerPower) || !string.IsNullOrEmpty(opponentPower))
            {
                parts.Add($"you {playerPower ?? "0"}, opponent {opponentPower ?? "0"}");
            }

            return string.Join(". ", parts);
        }
        catch
        {
            return "";
        }
    }

    private string GetLocationDescription(LocationView location)
    {
        if ((Object)(object)location == (Object)null) return "";
        try
        {
            // Try to get description from LocalizeDescriptionEvent
            LocalizeStringEvent descEvent = location._LocalizeDescriptionEvent;
            if ((Object)(object)descEvent != (Object)null)
            {
                string desc = descEvent.StringReference?.GetLocalizedString();
                if (!string.IsNullOrEmpty(desc) && !desc.Contains("{Missing Entry}"))
                {
                    return UIHelper.StripRichText(desc.Trim());
                }
            }
        }
        catch { }
        return "";
    }

    private string GetPowerFromTransform(Transform powerTransform)
    {
        if ((Object)(object)powerTransform == (Object)null) return null;
        try
        {
            string text = UIHelper.GetTextInChildren(((Component)powerTransform).gameObject);
            if (!string.IsNullOrEmpty(text))
            {
                return text.Trim();
            }
        }
        catch { }
        return null;
    }

    private bool IsUnderToolTip(Transform t)
    {
        Transform parent = t.parent;
        int depth = 0;
        while ((Object)(object)parent != (Object)null && depth < 6)
        {
            string name = ((Object)((Component)parent).gameObject).name;
            if (name.Contains("ExplicitToolTip", StringComparison.OrdinalIgnoreCase)
                || name.Contains("_Tooltip", StringComparison.OrdinalIgnoreCase)
                || name.Contains("SpeechBubble", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Tutorial", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            parent = parent.parent;
            depth++;
        }
        return false;
    }

    private void DumpAllText()
    {
        try
        {
            Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
            if (texts == null)
            {
                MelonLogger.Msg("[DUMP] No TMP_Text found");
                return;
            }
            int count = 0;
            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text tmp = texts[i];
                if ((Object)(object)tmp == (Object)null || !((Component)tmp).gameObject.activeInHierarchy) continue;
                string text = tmp.text;
                if (string.IsNullOrWhiteSpace(text)) continue;
                text = UIHelper.StripRichText(text.Trim());
                if (text.Length < 3) continue;

                string path = ((Object)((Component)tmp).gameObject).name;
                Transform parent = tmp.transform.parent;
                int depth = 0;
                while ((Object)(object)parent != (Object)null && depth < 5)
                {
                    path = ((Object)((Component)parent).gameObject).name + "/" + path;
                    parent = parent.parent;
                    depth++;
                }
                MelonLogger.Msg($"[DUMP] [{count}] \"{text}\" @ {path}");
                count++;
            }
            MelonLogger.Msg($"[DUMP] Total: {count} text objects");
            AnnouncementService.Instance.Announce($"Dumped {count} text objects to log", AnnouncementPriority.Normal);
        }
        catch (Exception ex)
        {
            MelonLogger.Msg("[DUMP] Failed: " + ex.Message);
        }
    }

    private void DumpApiReflection()
    {
        try
        {
            MelonLogger.Msg("=== API REFLECTION DUMP (F4) ===");
            AnnouncementService.Instance.Announce("Dumping API reflection to log", AnnouncementPriority.Normal);

            // Dump GameInputManager
            DumpTypeInfo(typeof(GameInputManager), "GameInputManager", _gim);

            // Dump GameViewController
            try
            {
                GameViewControllerProvider provider = UIHelper.FindComponent<GameViewControllerProvider>();
                GameViewController gvc = null;
                if ((Object)(object)provider != (Object)null)
                    gvc = provider.GameViewController;
                DumpTypeInfo(typeof(GameViewController), "GameViewController", gvc);
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[API] GameViewController lookup failed: {ex.Message}");
            }

            // Dump GameView
            try
            {
                GameView gameView = GameView.Get();
                DumpTypeInfo(typeof(GameView), "GameView", gameView);
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[API] GameView lookup failed: {ex.Message}");
            }

            // Dump CardView
            DumpTypeInfo(typeof(CardView), "CardView", null);

            // Dump LocationView
            DumpTypeInfo(typeof(LocationView), "LocationView", null);

            // Also dump VfxScenarioTutorialAction for tutorial state fields
            try
            {
                VfxScenarioTutorialAction action = UIHelper.FindComponent<VfxScenarioTutorialAction>();
                DumpTypeInfo(typeof(VfxScenarioTutorialAction), "VfxScenarioTutorialAction", action);
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[API] VfxScenarioTutorialAction lookup failed: {ex.Message}");
            }

            MelonLogger.Msg("=== END API REFLECTION DUMP ===");
            AnnouncementService.Instance.Announce("API dump complete. Check log.", AnnouncementPriority.Normal);
        }
        catch (Exception ex)
        {
            MelonLogger.Msg("[API] DumpApiReflection failed: " + ex.Message);
            AnnouncementService.Instance.Announce("API dump failed", AnnouncementPriority.Normal);
        }
    }

    private void DumpTypeInfo(System.Type type, string label, object instance)
    {
        try
        {
            MelonLogger.Msg($"--- {label} ---");

            // Methods
            List<MethodInfo> methods = AccessTools.GetDeclaredMethods(type);
            MelonLogger.Msg($"[API] {label}: {methods.Count} methods");
            foreach (MethodInfo m in methods)
            {
                try
                {
                    string paramStr = "";
                    ParameterInfo[] parms = m.GetParameters();
                    if (parms.Length > 0)
                    {
                        List<string> ps = new List<string>();
                        foreach (ParameterInfo p in parms)
                            ps.Add($"{p.ParameterType.Name} {p.Name}");
                        paramStr = string.Join(", ", ps);
                    }
                    MelonLogger.Msg($"[API]   M: {m.ReturnType.Name} {m.Name}({paramStr})");
                }
                catch
                {
                    MelonLogger.Msg($"[API]   M: {m.Name} (params error)");
                }
            }

            // Fields
            List<FieldInfo> fields = AccessTools.GetDeclaredFields(type);
            MelonLogger.Msg($"[API] {label}: {fields.Count} fields");
            foreach (FieldInfo f in fields)
            {
                try
                {
                    string valStr = "";
                    if (instance != null && !f.IsStatic)
                    {
                        try
                        {
                            object val = f.GetValue(instance);
                            valStr = $" = {val ?? "null"}";
                        }
                        catch { valStr = " = <error>"; }
                    }
                    else if (f.IsStatic)
                    {
                        try
                        {
                            object val = f.GetValue(null);
                            valStr = $" = {val ?? "null"}";
                        }
                        catch { valStr = " = <error>"; }
                    }
                    MelonLogger.Msg($"[API]   F: {f.FieldType.Name} {f.Name}{valStr}");
                }
                catch
                {
                    MelonLogger.Msg($"[API]   F: {f.Name} (type error)");
                }
            }

            // Properties
            PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            MelonLogger.Msg($"[API] {label}: {props.Length} properties");
            foreach (PropertyInfo p in props)
            {
                try
                {
                    string valStr = "";
                    if (instance != null && p.CanRead && p.GetIndexParameters().Length == 0)
                    {
                        try
                        {
                            object val = p.GetValue(instance);
                            valStr = $" = {val ?? "null"}";
                        }
                        catch { valStr = " = <error>"; }
                    }
                    MelonLogger.Msg($"[API]   P: {p.PropertyType.Name} {p.Name}{valStr}");
                }
                catch
                {
                    MelonLogger.Msg($"[API]   P: {p.Name} (type error)");
                }
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Msg($"[API] DumpTypeInfo({label}) failed: {ex.Message}");
        }
    }

    private void DumpTooltipDiagnostics()
    {
        try
        {
            Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
            if (texts == null)
            {
                MelonLogger.Msg("[TIP] No TMP_Text found");
                return;
            }
            Camera main = Camera.main;
            int count = 0;
            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text tmp = texts[i];
                if ((Object)(object)tmp == (Object)null || !((Component)tmp).gameObject.activeInHierarchy || !IsUnderToolTip(tmp.transform)) continue;
                string text = tmp.text;
                if (string.IsNullOrWhiteSpace(text)) continue;
                text = UIHelper.StripRichText(text.Trim());
                if (text.Length < 3) continue;

                StringBuilder sb = new StringBuilder();
                sb.Append($"[TIP] [{count}] \"{text}\"");
                sb.Append($" | textAlpha={((Graphic)tmp).color.a:F2}");

                Transform t = tmp.transform;
                int depth = 0;
                while ((Object)(object)t != (Object)null && depth < 8)
                {
                    CanvasGroup cg = ((Component)t).GetComponent<CanvasGroup>();
                    Vector3 scale = t.localScale;
                    string cgInfo = ((Object)(object)cg != (Object)null) ? $"cg={cg.alpha:F2}" : "no-cg";
                    string scaleInfo = $"s=({scale.x:F1},{scale.y:F1})";
                    sb.Append($" | {((Object)((Component)t).gameObject).name}[{cgInfo},{scaleInfo}]");
                    t = t.parent;
                    depth++;
                }
                if ((Object)(object)main != (Object)null)
                {
                    Vector3 vp = main.WorldToViewportPoint(tmp.transform.position);
                    sb.Append($" | vp=({vp.x:F2},{vp.y:F2},{vp.z:F1})");
                }
                MelonLogger.Msg(sb.ToString());
                count++;
            }
            MelonLogger.Msg($"[TIP] Total: {count} tooltip texts");
            AnnouncementService.Instance.Announce($"Dumped {count} tooltip diagnostics to log", AnnouncementPriority.Normal);
        }
        catch (Exception ex)
        {
            MelonLogger.Msg("[TIP] Failed: " + ex.Message);
        }
    }
}
