using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(SnapAccess.Main), "SnapAccess", "0.2.0", "Amethyst & Gemini")]
[assembly: MelonGame("Second Dinner", "SNAP")]

namespace SnapAccess;

/// <summary>
/// Entry point for the SnapAccess accessibility mod.
/// Coordinates initialization, input processing, and state-based handler execution.
/// </summary>
public class Main : MelonMod
{
    /// <summary>Global flag for verbose accessibility logging.</summary>
    public static bool DebugMode;

    private bool _gameReady = false;
    private List<IHandler> _handlers = new List<IHandler>();
    
    private BattlefieldHandler _battlefieldHandler;
    private MainMenuHandler _mainMenuHandler;
    private DialogHandler _dialogHandler;
    private DeckBuilderHandler _deckBuilderHandler;
    private FriendlyMatchHandler _friendlyMatchHandler;
    private PlayDeckTrayHandler _deckTrayHandler;
    private MissionsHandler _missionsHandler;

    /// <summary>
    /// Called by MelonLoader when the mod is first loaded.
    /// Initializes all core systems and handlers.
    /// </summary>
    public override void OnInitializeMelon()
    {
        DebugLogger.Initialize();
        ScreenReader.Initialize();
        SDLInput.Initialize();
        Loc.Initialize();
        
        InitializeHandlers();
        
        MelonCoroutines.Start(AnnounceStartupDelayed());
    }

    /// <summary>
    /// Instantiates and registers all feature handlers.
    /// Order of addition to _handlers determines default update priority.
    /// </summary>
    private void InitializeHandlers()
    {
        // Battlefield takes absolute priority if in a match
        _battlefieldHandler = new BattlefieldHandler();
        _handlers.Add(_battlefieldHandler);

        // Specialized sub-screen handlers
        _deckBuilderHandler = new DeckBuilderHandler();
        _handlers.Add(_deckBuilderHandler);

        _friendlyMatchHandler = new FriendlyMatchHandler();
        _handlers.Add(_friendlyMatchHandler);

        _deckTrayHandler = new PlayDeckTrayHandler();
        _handlers.Add(_deckTrayHandler);

        _missionsHandler = new MissionsHandler();
        _handlers.Add(_missionsHandler);

        // DialogHandler handles overlapping popups/modals
        _dialogHandler = new DialogHandler();
        
        // MainMenuHandler manages the main navigation hub
        _mainMenuHandler = new MainMenuHandler(_dialogHandler, _friendlyMatchHandler, _missionsHandler);
        _handlers.Add(_mainMenuHandler);
        
        _handlers.Add(_dialogHandler);
    }

    private IEnumerator AnnounceStartupDelayed()
    {
        // Wait for game to settle before announcing mod status
        yield return new WaitForSeconds(1.5f);
        ScreenReader.Say(Loc.Get("mod_loaded"));
    }

    /// <summary>
    /// Core update loop called every frame by the game engine.
    /// </summary>
    public override void OnUpdate()
    {
        // 1. Update global input state (SDL3 + Windows API)
        SDLInput.Update();
        
        // 2. Process global accessibility hotkeys (F1, F12, etc.)
        if (ProcessGlobalHotkeys()) return;

        // 3. Only run handlers if the game is initialized and ready
        if (CheckGameReady())
        {
            UpdateHandlers();
        }
    }

    /// <summary>
    /// Checks if the Unity scene is ready for interaction.
    /// </summary>
    private bool CheckGameReady()
    {
        return _gameReady;
    }

    /// <summary>
    /// Triggered whenever a new Unity scene is loaded.
    /// </summary>
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        MelonLogger.Msg($"Scene loaded: {sceneName}");
        DebugLogger.LogState($"Scene changed to: {sceneName}");
        
        if (!_gameReady)
        {
            _gameReady = true;
            MelonLogger.Msg("Game engine reported ready.");
        }

        // Reset all handlers on scene transition to prevent stale UI state
        foreach (var handler in _handlers)
        {
            handler.Reset();
        }
    }

    public override void OnApplicationQuit()
    {
        SDLInput.Shutdown();
        ScreenReader.Shutdown();
    }

    /// <summary>
    /// Processes non-gameplay accessibility commands.
    /// </summary>
    /// <returns>True if a hotkey was handled.</returns>
    private bool ProcessGlobalHotkeys()
    {
        // F12: Toggle Accessibility Debugging
        if (SDLInput.IsKeyDown(SDLInput.Key.F12))
        {
            DebugMode = !DebugMode;
            string text = DebugMode ? Loc.Get("debug_enabled") : Loc.Get("debug_disabled");
            MelonLogger.Msg($"Debug mode {(DebugMode ? "enabled" : "disabled")}");
            ScreenReader.Say(text);
            return true;
        }

        // F1: Context-sensitive Help
        if (SDLInput.IsKeyDown(SDLInput.Key.F1))
        {
            DebugLogger.LogInput("F1", "Global Help");
            AnnounceHelp();
            return true;
        }

        // F2: Diagnostic UI Dump (Debug only)
        if (SDLInput.IsKeyDown(SDLInput.Key.F2))
        {
            DebugLogger.LogInput("F2", "UI Diagnostic Dump");
            UIDumper.DumpFullState();
            ScreenReader.Say("UI diagnostic data dumped to log file.");
            return true;
        }

        return false;
    }

    private float _nextTutorialCheck = 0f;

    private void UpdateHandlers()
    {
        DismissTutorialOverlays();

        // Each handler's Update() self-discovers its active state and returns false when inactive.
        // No IsActive guard needed — Update() handles that internally.

        // Priority 1: Battlefield (Active Match)
        if (_battlefieldHandler.Update()) return;

        // Priority 2: Specialized Tray/Modal Handlers
        if (_deckTrayHandler.Update()) return;
        if (_deckBuilderHandler.Update()) return;
        if (_missionsHandler.Update()) return;
        if (_friendlyMatchHandler.Update()) return;

        // Priority 3: Main Menu Navigation (Play Screen, Collection, etc.)
        if (_mainMenuHandler.Update()) return;

        // Priority 4: Generic Dialogs / Popups (The Catch-all)
        _dialogHandler.Update();
    }

    /// <summary>
    /// Identifies and disables tutorial overlays that capture mouse focus 
    /// but don't provide useful text to screen readers.
    /// </summary>
    private void DismissTutorialOverlays()
    {
        if (Time.time < _nextTutorialCheck) return;
        _nextTutorialCheck = Time.time + 2f; // Check every 2 seconds

        try
        {
            GameObject tutContainer = GameObject.Find("TutorialContainer");
            if (tutContainer != null && tutContainer.activeInHierarchy)
            {
                for (int i = 0; i < tutContainer.transform.childCount; i++)
                {
                    Transform child = tutContainer.transform.GetChild(i);
                    if (child == null || !child.gameObject.activeInHierarchy) continue;
                    
                    string childName = child.gameObject.name;
                    // Menu tutorials like "DirectToRecruitMissions" block the UI
                    if (childName.Contains("DirectTo") || childName.Contains("CaptureInput"))
                    {
                        child.gameObject.SetActive(false);
                        DebugLogger.Log(LogCategory.Handler, "Main", $"Dismissed blocking overlay: {childName}");
                    }
                }
            }

            // StakesTutorial is a common in-game overlay that we dismiss to allow direct board access
            GameObject stakesTut = GameObject.Find("StakesTutorial_1(Clone)");
            if (stakesTut != null && stakesTut.activeInHierarchy)
            {
                stakesTut.SetActive(false);
                DebugLogger.Log(LogCategory.Handler, "Main", "Dismissed in-game tutorial overlay");
            }

            // "Custom Card Unlocked" / "Let's make your custom card" tutorial banners block collection UI
            GameObject customCardTut = GameObject.Find("UnlockedCustomCardsTutorialLandscape");
            if (customCardTut != null && customCardTut.activeInHierarchy)
            {
                customCardTut.SetActive(false);
                DebugLogger.Log(LogCategory.Handler, "Main", "Dismissed custom card tutorial overlay");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "Main", $"DismissTutorialOverlays error: {ex.Message}");
        }
    }

    /// <summary>
    /// Orchestrates help announcements based on the currently active system.
    /// </summary>
    private void AnnounceHelp()
    {
        if (_battlefieldHandler.IsActive)
        {
            _battlefieldHandler.AnnounceContext();
        }
        else if (_mainMenuHandler.IsActive)
        {
            // If in a sub-screen, let DialogHandler or specific sub-browsers handle it
            if (_mainMenuHandler.InSubScreen && _dialogHandler.HasActiveDialog)
                _dialogHandler.AnnounceContext();
            else
                _mainMenuHandler.AnnounceContext();
        }
        else
        {
            ScreenReader.Say(Loc.Get("help_text"));
        }
    }
}
