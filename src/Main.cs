using System;
using System.Collections;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(SnapAccess.Main), "SnapAccess", "0.6", "Amethyst & Gemini")]
[assembly: MelonGame("Second Dinner", "SNAP")]

namespace SnapAccess;

/// <summary>
/// Entry point for the SnapAccess accessibility mod.
/// Coordinates initialization, input processing, and navigator-based handler execution.
/// </summary>
public class Main : MelonMod
{
    /// <summary>Global flag for verbose accessibility logging.</summary>
    public static bool DebugMode;

    private bool _gameReady = false;
    private NavigatorManager _navigatorManager;
    private AnnouncementService _announcer;
    private GameLogNavigator _gameLog;
    private ModSettingsNavigator _modSettings;
    private InputFieldHelper _inputFieldHelper;
    private float _nextTutorialCheck = 0f;

    /// <summary>
    /// Called by MelonLoader when the mod is first loaded.
    /// Initializes all core systems and navigators.
    /// </summary>
    public override void OnInitializeMelon()
    {
        DebugLogger.Initialize();
        ScreenReader.Initialize();
        SDLInput.Initialize();
        Loc.Initialize();

        InitializeNavigators();

        MelonCoroutines.Start(AnnounceStartupDelayed());
    }

    /// <summary>
    /// Instantiates core services and registers all screen navigators.
    /// NavigatorManager handles priority-based activation and preemption.
    /// </summary>
    private void InitializeNavigators()
    {
        _announcer = new AnnouncementService();
        _navigatorManager = new NavigatorManager();
        _gameLog = new GameLogNavigator();
        _modSettings = new ModSettingsNavigator();
        _inputFieldHelper = new InputFieldHelper();

        _navigatorManager.Register(new LoginHandler());
        _navigatorManager.Register(new BattlefieldHandler());
        _navigatorManager.Register(new PlayDeckTrayHandler());
        _navigatorManager.Register(new DeckBuilderHandler());
        _navigatorManager.Register(new MissionsHandler());
        _navigatorManager.Register(new FriendlyMatchHandler());
        _navigatorManager.Register(new ShopHandler());
        _navigatorManager.Register(new MainMenuHandler());
        _navigatorManager.Register(new DialogHandler());
    }

    private IEnumerator AnnounceStartupDelayed()
    {
        yield return new WaitForSeconds(1.5f);
        _announcer.Announce(Loc.Get("mod_loaded"), AnnouncementPriority.High);
    }

    /// <summary>
    /// Core update loop called every frame by the game engine.
    /// </summary>
    public override void OnUpdate()
    {
        SDLInput.Update();

        // Input field helper tracks focused text fields for character announcements
        _inputFieldHelper.Update();

        if (ProcessGlobalHotkeys()) return;

        // Game log captures all input when active
        if (_modSettings.HandleInput()) return;
        if (_gameLog.HandleInput()) return;

        if (_gameReady)
        {
            DismissTutorialOverlays();
            _navigatorManager.Update();
        }
    }

    /// <summary>
    /// Triggered whenever a new Unity scene is loaded.
    /// </summary>
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        DebugLogger.LogState($"Scene changed to: {sceneName}");

        if (!_gameReady)
        {
            _gameReady = true;
            DebugLogger.LogState("Game engine reported ready.");
        }

        _navigatorManager.OnSceneChanged(sceneName);
    }

    public override void OnApplicationQuit()
    {
        SDLInput.Shutdown();
        ScreenReader.Shutdown();
    }

    /// <summary>
    /// Processes non-gameplay accessibility commands.
    /// </summary>
    private bool ProcessGlobalHotkeys()
    {
        // F12: Toggle Accessibility Debugging
        if (SDLInput.IsKeyDown(SDLInput.Key.F12))
        {
            DebugMode = !DebugMode;
            string text = DebugMode ? Loc.Get("debug_enabled") : Loc.Get("debug_disabled");
            MelonLogger.Msg($"Debug mode {(DebugMode ? "enabled" : "disabled")}");
            _announcer.AnnounceInterrupt(text);
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
            _announcer.AnnounceInterrupt("UI diagnostic data dumped to log file.");
            return true;
        }

        // F3: Repeat last announcement
        if (SDLInput.IsKeyDown(SDLInput.Key.F3))
        {
            _announcer.RepeatLast();
            return true;
        }

        // O: Open/close game log (announcement history)
        if (SDLInput.IsKeyDown(SDLInput.Key.O))
        {
            if (_gameLog.IsActive)
                _gameLog.Close();
            else
                _gameLog.Open();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Identifies and disables tutorial overlays that capture mouse focus.
    /// </summary>
    private void DismissTutorialOverlays()
    {
        if (Time.time < _nextTutorialCheck) return;
        _nextTutorialCheck = Time.time + 2f;

        try
        {
            GameObject tutContainer = GameObject.Find("TutorialContainer");
            if (tutContainer != null && tutContainer.activeInHierarchy)
            {
                for (int i = 0; i < tutContainer.transform.childCount; i++)
                {
                    Transform child = tutContainer.transform.GetChild(i);
                    if (child == null || !child.gameObject.activeInHierarchy) continue;

                    string name = child.gameObject.name;
                    // Only disable DirectTo arrows (visual highlights).
                    // CaptureInput overlays are the tap targets that TutorialGraphicRaycaster
                    // uses to detect "tap to continue" — disabling them breaks advancement
                    // and causes voice lines to overlap because tutorial steps race.
                    if (name.Contains("DirectTo"))
                    {
                        child.gameObject.SetActive(false);
                        DebugLogger.Log(LogCategory.Handler, "Main",
                            $"Dismissed blocking overlay: {name}");
                    }
                }
            }

            GameObject stakes = GameObject.Find("StakesTutorial_1(Clone)");
            if (stakes != null && stakes.activeInHierarchy)
            {
                stakes.SetActive(false);
                DebugLogger.Log(LogCategory.Handler, "Main", "Dismissed in-game tutorial overlay");
            }

            GameObject customCards = GameObject.Find("UnlockedCustomCardsTutorialLandscape");
            if (customCards != null && customCards.activeInHierarchy)
            {
                customCards.SetActive(false);
                DebugLogger.Log(LogCategory.Handler, "Main", "Dismissed custom card tutorial overlay");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "Main", $"DismissTutorialOverlays error: {ex.Message}");
        }
    }

    /// <summary>
    /// Orchestrates help announcements based on the currently active navigator.
    /// </summary>
    private void AnnounceHelp()
    {
        var active = _navigatorManager.ActiveNavigator;
        if (active != null)
        {
            active.AnnounceContext();
        }
        else
        {
            _announcer.Announce(Loc.Get("help_text"), AnnouncementPriority.High);
        }
    }

}
