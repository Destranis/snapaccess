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

public class BattlefieldHandler
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
    private int _tutorialInitScanCount = 0;
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
    private int _rollbackConfirmCycles = 0; // Wait several cycles before confirming success

    // Opponent card detection: don't announce before the game has started properly
    private bool _gameReadyForOpponentDetection = false;
    private int _turnChangeCount = 0;

    // Card draw tracking: EntityIds of cards that were in hand last scan
    private readonly HashSet<int> _previousHandEntityIds = new HashSet<int>();

    // Opponent name retry: name is often empty at game start
    private bool _opponentNameAnnounced = false;

    // Retreat confirmation: require double-press R within timeout
    private bool _retreatPending = false;
    private float _retreatPendingTime = 0f;
    private const float RetreatConfirmTimeout = 3f;

    private const float ScanInterval = 0.5f;
    private const uint MOUSEEVENTF_MOVE = 1u;
    private const uint MOUSEEVENTF_LEFTDOWN = 2u;
    private const uint MOUSEEVENTF_LEFTUP = 4u;

    public bool IsActive => (Object)(object)_gim != (Object)null;

    public bool Update()
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
            return false;
        }
        // Only scan for popups when actually in a game
        if (shouldScan) ScanForPopup();
        if (!_wasInGame)
        {
            OnGameEntered();
            _wasInGame = true;
        }
        // Check for game over (Collect Rewards / Next button)
        CheckGameOver();
        if (_inPopup)
        {
            ProcessPopupInput();
        }
        else if (_gameOverAnnounced)
        {
            // Game is over — don't process normal input (hand/locations).
            // Just wait for the next popup in the results flow.
            // Only allow E key to try collecting rewards again.
            if (SDLInput.IsKeyDown(SDLInput.Key.E))
            {
                TryEndTurn();
            }
        }
        else
        {
            ProcessInput();
        }
        return true;
    }

    public void AnnounceContext()
    {
        if ((Object)(object)_gim == (Object)null)
        {
            ScreenReader.Say(Loc.Get("bf_not_in_game"));
            return;
        }
        if (!string.IsNullOrEmpty(_lastInstructionText))
        {
            ScreenReader.Say(Loc.Get("bf_tutorial_instruction", _lastInstructionText));
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
        ScreenReader.SayQueued(Loc.Get("bf_help"));
    }

    public void Reset()
    {
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
        _tutorialInitScanCount = 0;

        _wasInGame = false;
        _inPopup = false;
        _popupButtons.Clear();
        _popupFocusIndex = -1;
        _lastPopupText = "";
        _knownBoardCardEntityIds.Clear();
        _ourCardEntityIds.Clear();
        _previousHandEntityIds.Clear();
        _pendingOpponentAnnouncements.Clear();
        _lastPlayedCardName = null;
        _lastPlayedEntityId = -1;
        _handCountAfterPlay = -1;
        _rollbackConfirmCycles = 0;
        _gameReadyForOpponentDetection = false;
        _turnChangeCount = 0;
        _gameOverAnnounced = false;
        _gameOverCheckTime = 0f;
        _opponentNameAnnounced = false;
        _retreatPending = false;
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
        if (_pendingOpponentAnnouncements.Count > 0)
        {
            StringBuilder opponentMsg = new StringBuilder();
            while (_pendingOpponentAnnouncements.Count > 0)
            {
                if (opponentMsg.Length > 0) opponentMsg.Append(". ");
                opponentMsg.Append(_pendingOpponentAnnouncements.Dequeue());
            }
            // Use SayQueued so it doesn't cut off current speech
            ScreenReader.SayQueued(opponentMsg.ToString());
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
                    ScreenReader.Say(Loc.Get("bf_play_rolled_back", _lastPlayedCardName));
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                        $"Play rolled back: {_lastPlayedCardName} returned to hand (hand={_handCards.Count} > expected={_handCountAfterPlay})");
                    _lastPlayedCardName = null;
                    _lastPlayedEntityId = -1;
                    _handCountAfterPlay = -1;
                    _rollbackConfirmCycles = 0;
                    // Announce tutorial hints explaining why the play was rejected
                    AnnounceActiveTutorialHints();
                }
                else
                {
                    // Hand hasn't grown — but wait several cycles before confirming
                    // The game may roll back the play with a delay
                    _rollbackConfirmCycles++;
                    if (_rollbackConfirmCycles >= 12) // ~6 seconds at 0.5s scan interval
                    {
                        // Card stayed on board long enough — play succeeded
                        DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                            $"Play confirmed after {_rollbackConfirmCycles} cycles: {_lastPlayedCardName}");
                        _lastPlayedCardName = null;
                        _lastPlayedEntityId = -1;
                        _handCountAfterPlay = -1;
                        _rollbackConfirmCycles = 0;
                    }
                }
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

    /// <summary>Announces names of newly drawn cards by comparing current hand EntityIds with previous scan.</summary>
    private void AnnounceDrawnCards()
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
                string msg = Loc.Get("bf_card_drawn", string.Join(", ", newCardNames));
                ScreenReader.SayQueued(msg);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                    "Drew: " + string.Join(", ", newCardNames));
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "AnnounceDrawnCards failed: " + ex.Message);
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
                try
                {
                    string goName = ((Object)((Component)cv).gameObject).name;
                    if (goName != null && (goName.Contains("ObjectPool") || goName.Contains("CardPool"))) continue;
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

                    string cardInfo = GetCardInfo(cv);
                    string announcement = $"Opponent played {cardName}";
                    if (!string.IsNullOrEmpty(cardInfo)) announcement += $", {cardInfo}";
                    announcement += $" to {nearestLocName}";
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
            // Track hand count for detecting turn changes
            if (_handCards.Count != _lastHandCount)
            {
                if (_lastHandCount >= 0)
                {
                    // Turn changed — only clear LocString cache so turn-specific instructions
                    // can re-announce. Do NOT clear _announcedTooltips — static tooltip texts
                    // (like "Cards cost Energy") should not be re-read every turn.
                    _lastLocString = "";
                    _turnChangeCount++;
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
                            ScreenReader.SayQueued($"Playing against {oppName}");
                            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler",
                                "Opponent name (delayed): " + oppName);
                        }
                    }
                    // Announce newly drawn cards (skip initial deal — only after turn 1+)
                    if (_gameReadyForOpponentDetection && _handCards.Count > _lastHandCount && _previousHandEntityIds.Count > 0)
                    {
                        AnnounceDrawnCards();
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
                    ScreenReader.SayQueued(text);
                    DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "Instruction: " + text);
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
                // Skip timer countdown tooltips (Timer: 5, Timer: 4, etc.) — noisy and game auto-ends turn
                if (text.StartsWith("Timer:", StringComparison.OrdinalIgnoreCase)) continue;
                // Skip turn indicator tooltips (already available via T key)
                if (text.StartsWith("turn ", StringComparison.OrdinalIgnoreCase) && text.Contains("/")) continue;
                if (_announcedTooltips.Add(text))
                {
                    _lastTutorialText = text;
                    ScreenReader.SayQueued(text);
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
                        ScreenReader.SayQueued(text);
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
                ScreenReader.SayQueued(Loc.Get("bf_tap_to_continue"));
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
                        ScreenReader.SayQueued(text);
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
                ScreenReader.SayQueued(text);
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TutorialText: " + text);
            }

            if (!_tutorialTextsInitialized && _trackedTutorialTexts.Count > 0)
            {
                _tutorialInitScanCount++;
                // Wait for 6 scan cycles (~3 seconds) before considering initialized
                // This ensures all pre-loaded tutorial texts are captured silently
                if (_tutorialInitScanCount >= 6)
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
        // Left/Right: Navigate within current area
        else if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
        {
            Navigate(-1);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
        {
            Navigate(1);
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
        // T: Turn info (turn number / total)
        else if (SDLInput.IsKeyDown(SDLInput.Key.T))
        {
            AnnounceTurnInfo();
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

        // Read popup text from parent of first button
        string popupText = "";
        try
        {
            Transform parent = ((Component)buttons[0]).transform;
            for (int i = 0; i < 4; i++)
            {
                if (parent.parent == null) break;
                parent = parent.parent;
            }
            popupText = UIHelper.GetAllText(((Component)parent).gameObject);
        }
        catch { }

        if (!string.IsNullOrEmpty(popupText) && popupText != _lastPopupText)
        {
            _lastPopupText = popupText;
            if (popupText.Length > 300) popupText = popupText.Substring(0, 300) + "...";
            ScreenReader.Say(popupText);
        }

        string btnLabel = GetPopupButtonLabel(buttons[0]);
        ScreenReader.SayQueued(Loc.Get("dialog_button_focus", btnLabel, 1, buttons.Count));
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

    private static readonly Dictionary<string, string> _popupLabelOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "btn upgrade", "Upgrade" },
        { "btn_upgrade", "Upgrade" },
        { "btn_next", "Next" },
        { "btn_collect", "Collect" },
        { "btn_claim", "Claim" },
        { "btn_ok", "OK" },
        { "btn_confirm", "Confirm" },
        { "btn_cancel", "Cancel" },
        { "btn_close", "Close" },
        { "btn_back", "Back" },
        { "btn_retreat", "Retreat" },
        { "btn_stay", "Stay" },
        { "btn_resume", "Resume" },
        { "btn_hex_blu", "OK" },
        { "Button_BackgroundClose", "Close" },
        { "Esc", "Close" },
    };

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
        if (_popupLabelOverrides.ContainsKey(goName))
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
        if (_popupLabelOverrides.TryGetValue(label, out string labelOverride))
            return labelOverride;

        // If the button has real, non-numeric text content (not just a cleaned GO name), prefer it
        // This lets "Nice!" show instead of being overridden to "OK" for btn_hex_blu
        // But numeric labels like "25" (upgrade cost) should still use the GO name override
        string cleanedGoName = UIHelper.CleanGameObjectName(goName);
        if (label.Length >= 2 && !label.Equals(cleanedGoName, StringComparison.OrdinalIgnoreCase)
            && !_junkPopupLabels.Contains(label) && !IsNumericOrCurrencyLabel(label))
            return label;

        // Fall back to GO name override
        if (_popupLabelOverrides.TryGetValue(goName, out string goOverride))
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

        if (SDLInput.IsKeyDown(SDLInput.Key.Tab) || SDLInput.IsKeyDown(SDLInput.Key.Right))
        {
            _popupFocusIndex = (_popupFocusIndex + 1) % _popupButtons.Count;
            string label = GetPopupButtonLabel(_popupButtons[_popupFocusIndex]);
            ScreenReader.Say(Loc.Get("dialog_button_focus", label, _popupFocusIndex + 1, _popupButtons.Count));
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Left))
        {
            _popupFocusIndex = (_popupFocusIndex - 1 + _popupButtons.Count) % _popupButtons.Count;
            string label = GetPopupButtonLabel(_popupButtons[_popupFocusIndex]);
            ScreenReader.Say(Loc.Get("dialog_button_focus", label, _popupFocusIndex + 1, _popupButtons.Count));
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsKeyDown(SDLInput.Key.Space))
        {
            if (_popupFocusIndex >= 0 && _popupFocusIndex < _popupButtons.Count)
            {
                string label = GetPopupButtonLabel(_popupButtons[_popupFocusIndex]);
                ScreenReader.Say(Loc.Get("dialog_activating", label));
                ClickPopupButton(_popupButtons[_popupFocusIndex]);
            }
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Escape))
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
            ScreenReader.Say(Loc.Get("bf_no_cards"));
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
            ScreenReader.Say(Loc.Get("bf_choose_location", cardName));
        }
        if (_locations.Count > 0)
        {
            AnnounceCurrentLocation();
        }
        else
        {
            ScreenReader.Say(Loc.Get("bf_no_locations"));
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

    private void NavigateHand(int direction)
    {
        UnZoomCurrentCard();
        _detailLevel = 0;
        if (_handCards.Count == 0)
        {
            ScreenReader.Say(Loc.Get("bf_no_cards"));
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
            ScreenReader.Say(Loc.Get("bf_no_locations"));
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
                        ScreenReader.Say($"cost {costView.Value}");
                    else
                        ScreenReader.Say("cost unknown");
                }
                catch { ScreenReader.Say("cost unknown"); }
                break;
            case 2: // Power
                try
                {
                    CardValueView powerView = ((CardRenderer)card)._PowerValueView;
                    if ((Object)(object)powerView != (Object)null)
                        ScreenReader.Say($"power {powerView.Value}");
                    else
                        ScreenReader.Say("power unknown");
                }
                catch { ScreenReader.Say("power unknown"); }
                break;
            case 3: // Ability
                string ability = GetCardAbilityText(card);
                if (!string.IsNullOrEmpty(ability))
                    ScreenReader.Say(ability);
                else
                    ScreenReader.Say("no ability");
                break;
            default:
                // Beyond available details — cap at max
                _detailLevel = 3;
                string ab = GetCardAbilityText(card);
                if (!string.IsNullOrEmpty(ab))
                    ScreenReader.Say(ab);
                else
                    ScreenReader.Say("no ability");
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
                    ScreenReader.Say(desc);
                else
                    ScreenReader.Say("no description");
                break;
            case 2: // Power scores
                string playerPower = GetPowerFromTransform(loc._LocationFriendlyPower);
                string opponentPower = GetPowerFromTransform(loc._LocationEnemyPower);
                ScreenReader.Say($"you {playerPower ?? "0"}, opponent {opponentPower ?? "0"}");
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
                ScreenReader.Say("no cards here");
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
            {
                sb.Append($"Your cards: {string.Join(", ", yourCards)}. ");
            }
            else
            {
                sb.Append("No cards from you. ");
            }
            if (opponentCards.Count > 0)
            {
                sb.Append($"Opponent cards: {string.Join(", ", opponentCards)}.");
            }
            else
            {
                sb.Append("No opponent cards.");
            }

            ScreenReader.Say(sb.ToString());
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "AnnounceCardsAtLocation failed: " + ex.Message);
            ScreenReader.Say("no cards here");
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
            ScreenReader.Say(Loc.Get("bf_card_deselected", cardName));
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
        if (_playState == PlayState.CardSelected && (Object)(object)_selectedCard == (Object)(object)card)
        {
            _selectedCard = null;
            _playState = PlayState.Browsing;
            ScreenReader.Say(Loc.Get("bf_card_deselected", cardName));
        }
        else
        {
            _selectedCard = card;
            _playState = PlayState.CardSelected;
            ScreenReader.Say(Loc.Get("bf_card_selected", cardName));
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
                    ScreenReader.Say(Loc.Get("bf_card_restricted", cardName));
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

            // Fallback 2: Mouse drag simulation (physically drag card to location)
            if (!success)
            {
                DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Trying mouse drag: {cardName} -> {locationName}");
                SimulateDragCardToLocation(_selectedCard, loc);
                // Mouse drag is fire-and-forget; assume it worked and let rollback detection catch failures
                success = true;
            }

            if (success)
            {
                ScreenReader.Say(Loc.Get("bf_card_played", cardName, locationName));
                // Track for rollback detection
                try { _lastPlayedEntityId = _selectedCard.EntityId; } catch { }
                _lastPlayedCardName = cardName;
                ScanHandCards();
                _handCountAfterPlay = _handCards.Count;
                ScanLocations();
            }
            else
            {
                ScreenReader.Say(Loc.Get("bf_play_failed", cardName, locationName));
                // Announce tutorial hints explaining why the play failed
                AnnounceActiveTutorialHints();
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Msg("[BF] PlayCardToLocation failed: " + ex.Message);
            ScreenReader.Say(Loc.Get("bf_play_error"));
            // Ensure drag is cleaned up on error
            try { if ((Object)(object)_gim != (Object)null) _gim.StopDragCard(_selectedCard); } catch { }
        }
        _selectedCard = null;
        _playState = PlayState.Browsing;
        _area = FocusArea.Hand;
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
                    ScreenReader.Say(Loc.Get("bf_leaving_game"));
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
                        ScreenReader.Say(Loc.Get("bf_leaving_game"));
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
                        ScreenReader.Say(Loc.Get("bf_tutorial_advance"));
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
            ScreenReader.Say(Loc.Get("bf_tutorial_advance"));
            DebugLogger.LogInput("Space", "Tutorial click at center");
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TryAdvanceTutorial failed: " + ex.Message);
            ScreenReader.Say(Loc.Get("bf_no_tutorial"));
        }
    }

    private void TryEndTurn()
    {
        // Game over: clicking E collects rewards / exits
        if (_gameOverAnnounced)
        {
            TryCollectRewards();
            return;
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
                ScreenReader.Say(Loc.Get("bf_end_turn"));
            }
            else
            {
                ScreenReader.Say(Loc.Get("bf_no_end_turn"));
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TryEndTurn failed: " + ex.Message);
            ScreenReader.Say(Loc.Get("bf_no_end_turn"));
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
                    ScreenReader.Say(Loc.Get("bf_leaving_game"));
                    DebugLogger.LogInput("E", "Collect Rewards / Exit via direct button click");
                }
                else
                {
                    ScreenReader.Say(Loc.Get("bf_no_end_turn"));
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
                        ScreenReader.Say(Loc.Get("bf_leaving_game"));
                        DebugLogger.LogInput("E", "Collect Rewards / Exit via EndTurnButtonView fallback");
                    }
                    else
                    {
                        ScreenReader.Say(Loc.Get("bf_no_end_turn"));
                    }
                }
                else
                {
                    ScreenReader.Say(Loc.Get("bf_no_end_turn"));
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TryCollectRewards failed: " + ex.Message);
        }
    }

    /// <summary>Finds the EndTurnSDButton by searching active buttons.</summary>
    private Button FindEndTurnSDButton()
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
                if (goName == "EndTurnSDButton")
                    return btn;
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
        ScreenReader.Say(Loc.Get("bf_card_brief", cardName, _handIndex + 1, _handCards.Count));
    }

    /// <summary>Announce location name and position only — down arrow for full details.</summary>
    private void AnnounceCurrentLocation()
    {
        if (_locations.Count == 0 || _locationIndex >= _locations.Count) return;
        LocationView loc = _locations[_locationIndex];
        if ((Object)(object)loc == (Object)null) return;

        string locationName = GetLocationName(loc);
        ScreenReader.Say(Loc.Get("bf_location_brief", locationName, _locationIndex + 1, _locations.Count));
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
        ScreenReader.Say(string.Join(". ", parts));
    }

    private void AnnounceEnergy()
    {
        string energy = GetEnergyText();
        if (!string.IsNullOrEmpty(energy))
        {
            ScreenReader.Say($"Energy {energy}");
        }
        else
        {
            ScreenReader.Say("Energy not available");
        }
    }

    private void AnnounceTurnInfo()
    {
        try
        {
            // Find TurnCountText_Active or TurnCountText_Inactive TMP_Text elements
            Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
            if (texts == null) { ScreenReader.Say(Loc.Get("bf_turn_not_available")); return; }

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
                // turnText is like "3 / 6" — parse into "Turn 3 of 6"
                string[] parts = turnText.Split('/');
                if (parts.Length == 2)
                {
                    string current = parts[0].Trim();
                    string total = parts[1].Trim();
                    ScreenReader.Say(Loc.Get("bf_turn_info", current, total));
                }
                else
                {
                    ScreenReader.Say(Loc.Get("bf_turn_info_raw", turnText));
                }
            }
            else
            {
                ScreenReader.Say(Loc.Get("bf_turn_not_available"));
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "AnnounceTurnInfo failed: " + ex.Message);
            ScreenReader.Say(Loc.Get("bf_turn_not_available"));
        }
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
                    ScreenReader.Say(Loc.Get("bf_snapped"));
                    DebugLogger.LogInput("G", "Snap via mouse simulation — stakes were " + cubeValue);
                }
                else
                {
                    ScreenReader.Say(Loc.Get("bf_snap_no_button"));
                }
            }
            else
            {
                // Maybe snap isn't available (already snapped, or game state doesn't allow it)
                string cubeValue = GetCubeStakeValue();
                if (!string.IsNullOrEmpty(cubeValue))
                {
                    ScreenReader.Say(Loc.Get("bf_snap_not_available", cubeValue));
                }
                else
                {
                    ScreenReader.Say(Loc.Get("bf_snap_no_button"));
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TrySnap failed: " + ex.Message);
            ScreenReader.Say(Loc.Get("bf_snap_no_button"));
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
                ScreenReader.Say(Loc.Get("bf_retreat_no_button"));
                return;
            }

            string cubeValue = GetCubeStakeValue();

            // First press: warn and wait for confirmation
            if (!_retreatPending || (UnityEngine.Time.time - _retreatPendingTime > RetreatConfirmTimeout))
            {
                _retreatPending = true;
                _retreatPendingTime = UnityEngine.Time.time;
                ScreenReader.Say(Loc.Get("bf_retreat_confirm", cubeValue));
                DebugLogger.LogInput("R", "Retreat confirmation requested — stakes " + cubeValue);
                return;
            }

            // Second press within timeout: actually retreat
            _retreatPending = false;
            if (SimulateClickOnButton((Component)retreatBtn))
            {
                ScreenReader.Say(Loc.Get("bf_retreat_initiated", cubeValue));
                DebugLogger.LogInput("R", "Retreat confirmed via mouse simulation — stakes " + cubeValue);
            }
            else
            {
                ScreenReader.Say(Loc.Get("bf_retreat_no_button"));
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", "TryRetreat failed: " + ex.Message);
            ScreenReader.Say(Loc.Get("bf_retreat_no_button"));
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
                ScreenReader.SayQueued(combined);
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
            ScreenReader.Say(_lastInstructionText);
        }
        else
        {
            ScreenReader.Say(Loc.Get("bf_no_tutorial"));
        }
    }

    private bool _gameOverAnnounced = false;
    private float _gameOverCheckTime = 0f;

    private void OnGameEntered()
    {
        SetupHarmonyPatches();
        SetupStepMapHooks();
        DebugLogger.Log(LogCategory.State, "BattlefieldHandler", "Game entered");
        AccessStateManager.TryEnter(AccessStateManager.State.Gameplay);
        _area = FocusArea.Hand;
        _playState = PlayState.Browsing;
        _handIndex = 0;
        _locationIndex = 0;
        _gameOverAnnounced = false;
        _gameOverCheckTime = 0f;

        _opponentNameAnnounced = false;

        // Read opponent name and announce game start
        string opponentName = ReadOpponentName();
        string playerName = ReadPlayerName();
        if (!string.IsNullOrEmpty(opponentName))
        {
            ScreenReader.Say(Loc.Get("bf_game_entered_vs", opponentName));
            _opponentNameAnnounced = true;
        }
        else
        {
            ScreenReader.Say(Loc.Get("bf_game_entered"));
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
                    ScreenReader.Say(Loc.Get("bf_game_over", result));
                    ScreenReader.SayQueued(Loc.Get("bf_game_over_instructions"));
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

    /// <summary>Reads location scores to determine win/lose/draw.</summary>
    private string ReadGameResult()
    {
        int playerWins = 0;
        int opponentWins = 0;
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
                    if (int.TryParse(fpText, out int fpVal) && int.TryParse(epText, out int epVal))
                    {
                        if (fpVal > epVal) playerWins++;
                        else if (epVal > fpVal) opponentWins++;
                    }
                }
                catch { }
            }
        }
        catch { }

        if (playerWins > opponentWins) return Loc.Get("bf_result_win");
        if (opponentWins > playerWins) return Loc.Get("bf_result_lose");
        return Loc.Get("bf_result_draw");
    }

    private void OnGameLeft()
    {
        DebugLogger.Log(LogCategory.State, "BattlefieldHandler", "Game left");
        AccessStateManager.Exit(AccessStateManager.State.Gameplay);
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
        _tutorialInitScanCount = 0;

        _knownBoardCardEntityIds.Clear();
        _ourCardEntityIds.Clear();
        _pendingOpponentAnnouncements.Clear();
        _lastPlayedCardName = null;
        _lastPlayedEntityId = -1;
        _handCountAfterPlay = -1;
        _rollbackConfirmCycles = 0;
        _gameReadyForOpponentDetection = false;
        _turnChangeCount = 0;
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

        // Try 1: Direct access to CardRenderer._AbilityText (TMP_Text field, like _CostValueView/_PowerValueView)
        try
        {
            CardRenderer renderer = (CardRenderer)card;
            TMP_Text abilityTmp = renderer._AbilityText;
            if ((Object)(object)abilityTmp != (Object)null)
            {
                string raw = abilityTmp.text;
                if (!string.IsNullOrWhiteSpace(raw) && !raw.Contains("Missing Entry"))
                {
                    if (!IsFlavorText(raw))
                    {
                        string cleaned = UIHelper.StripRichText(raw.Trim());
                        if (cleaned.Length > 3)
                        {
                            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"Card ability from _AbilityText: {cleaned}");
                            return cleaned;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "BattlefieldHandler", $"_AbilityText access failed: {ex.Message}");
        }

        // Try 2: Search child TMP_Text named "Ability Text" under CardAbilityText hierarchy
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
                    // Prefer "Ability Text" GO specifically
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
                // Fallback: any non-trivial child text that isn't cost/power/name
                for (int i = 0; i < texts.Count; i++)
                {
                    TMP_Text tmp = texts[i];
                    if ((Object)(object)tmp == (Object)null) continue;
                    if (!((Component)tmp).gameObject.activeInHierarchy) continue;
                    string text = tmp.text;
                    if (string.IsNullOrWhiteSpace(text) || text.Contains("Missing Entry")) continue;
                    if (IsFlavorText(text)) continue;
                    string cleaned = UIHelper.StripRichText(text.Trim());
                    if (cleaned.Length < 5) continue;
                    if (cleaned == cardName) continue;
                    if (int.TryParse(cleaned, out _)) continue;
                    return cleaned;
                }
            }
        }
        catch { }
        return "";
    }

    /// <summary>Check if raw card text is flavor text (italic quoted text, not a real ability).</summary>
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
            ScreenReader.Say($"Dumped {count} text objects to log");
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
            ScreenReader.Say("Dumping API reflection to log");

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
            ScreenReader.Say("API dump complete. Check log.");
        }
        catch (Exception ex)
        {
            MelonLogger.Msg("[API] DumpApiReflection failed: " + ex.Message);
            ScreenReader.Say("API dump failed");
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
            ScreenReader.Say($"Dumped {count} tooltip diagnostics to log");
        }
        catch (Exception ex)
        {
            MelonLogger.Msg("[TIP] Failed: " + ex.Message);
        }
    }
}
