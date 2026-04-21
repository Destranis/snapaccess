using System;
using System.Collections;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(SnapAccess.Main), "SnapAccess", "1.0.0", "Amethyst")]
[assembly: MelonGame("Second Dinner", "SNAP")]

namespace SnapAccess;

public class Main : MelonMod
{
    private bool _gameReady = false;

    public static bool DebugMode;

    private DialogHandler _dialogHandler;

    private MainMenuHandler _mainMenuHandler;

    private BattlefieldHandler _battlefieldHandler;

    public override void OnInitializeMelon()
    {
        DebugLogger.Initialize();
        ScreenReader.Initialize();
        SDLInput.Initialize();
        Loc.Initialize();
        InitializeHandlers();
        MelonCoroutines.Start(AnnounceStartupDelayed());
    }

    private void InitializeHandlers()
    {
        _dialogHandler = new DialogHandler();
        _mainMenuHandler = new MainMenuHandler(_dialogHandler);
        _battlefieldHandler = new BattlefieldHandler();
    }

    private IEnumerator AnnounceStartupDelayed()
    {
        yield return new WaitForSeconds(1f);
        ScreenReader.Say(Loc.Get("mod_loaded"));
    }

    public override void OnUpdate()
    {
        SDLInput.Update();
        if (!ProcessHotkeys() && CheckGameReady())
        {
            UpdateHandlers();
        }
    }

    private bool CheckGameReady()
    {
        return _gameReady;
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        MelonLogger.Msg("Scene loaded: " + sceneName);
        DebugLogger.LogState("Scene changed to: " + sceneName);
        if (!_gameReady)
        {
            _gameReady = true;
            MelonLogger.Msg("Game ready");
        }
        // Don't reset MainMenuHandler when entering the Game scene —
        // BattlefieldHandler takes priority and we don't want a duplicate "Play screen" announcement
        if (!sceneName.Equals("Game", StringComparison.OrdinalIgnoreCase))
        {
            _mainMenuHandler.Reset();
        }
        _battlefieldHandler.Reset();
    }

    public override void OnApplicationQuit()
    {
        SDLInput.Shutdown();
        ScreenReader.Shutdown();
    }

    private bool ProcessHotkeys()
    {
        if (SDLInput.IsKeyDown(SDLInput.Key.F12))
        {
            DebugMode = !DebugMode;
            string text = DebugMode ? Loc.Get("debug_enabled") : Loc.Get("debug_disabled");
            MelonLogger.Msg("Debug mode " + (DebugMode ? "enabled" : "disabled"));
            ScreenReader.Say(text);
            return true;
        }
        if (SDLInput.IsKeyDown(SDLInput.Key.F1))
        {
            DebugLogger.LogInput("F1", "Help");
            AnnounceHelp();
            return true;
        }
        if (SDLInput.IsKeyDown(SDLInput.Key.F2))
        {
            DebugLogger.LogInput("F2", "UI Dump");
            UIDumper.DumpFullState();
            ScreenReader.Say("UI state dumped to log.");
            return true;
        }
        return false;
    }

    private float _nextTutorialCheck = 0f;

    private void UpdateHandlers()
    {
        // Auto-dismiss tutorial overlays that block input for screen reader users
        DismissTutorialOverlays();

        if (_battlefieldHandler.Update())
        {
            return;
        }
        // MainMenuHandler returns true when it handles everything (Play screen)
        // Returns false when DialogHandler should handle sub-screen content
        if (_mainMenuHandler.Update())
        {
            return;
        }
        // DialogHandler handles non-Play sub-screens (News, Shop, Collection, etc.)
        if (_mainMenuHandler.InSubScreen)
        {
            _dialogHandler.Update();
        }
    }

    /// <summary>Disables tutorial overlays that capture input and block the screen.</summary>
    private void DismissTutorialOverlays()
    {
        if (Time.time < _nextTutorialCheck) return;
        _nextTutorialCheck = Time.time + 2f;

        try
        {
            GameObject tutContainer = GameObject.Find("TutorialContainer");
            if ((Object)(object)tutContainer != (Object)null && tutContainer.activeInHierarchy)
            {
                // Check for children that block input
                for (int i = 0; i < tutContainer.transform.childCount; i++)
                {
                    Transform child = tutContainer.transform.GetChild(i);
                    if ((Object)(object)child == (Object)null) continue;
                    if (!child.gameObject.activeInHierarchy) continue;
                    string childName = ((Object)child.gameObject).name;

                    // DirectToRecruitMissionsAndSeason, etc. — these are menu tutorials
                    if (childName.Contains("DirectTo", StringComparison.Ordinal) ||
                        childName.Contains("CaptureInput", StringComparison.Ordinal))
                    {
                        child.gameObject.SetActive(false);
                        DebugLogger.Log(LogCategory.Handler, "Main",
                            "Dismissed tutorial overlay: " + childName);
                    }
                }
            }

            // Also dismiss in-game StakesTutorial overlays
            GameObject stakesTut = GameObject.Find("StakesTutorial_1(Clone)");
            if ((Object)(object)stakesTut != (Object)null && stakesTut.activeInHierarchy)
            {
                stakesTut.SetActive(false);
                DebugLogger.Log(LogCategory.Handler, "Main", "Dismissed StakesTutorial overlay");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "Main", "DismissTutorialOverlays failed: " + ex.Message);
        }
    }

    private void AnnounceHelp()
    {
        if (_battlefieldHandler.IsActive)
        {
            _battlefieldHandler.AnnounceContext();
        }
        else if (_dialogHandler.HasActiveDialog)
        {
            _dialogHandler.AnnounceContext();
        }
        else if (_mainMenuHandler.IsActive)
        {
            _mainMenuHandler.AnnounceContext();
        }
        else
        {
            ScreenReader.Say(Loc.Get("help_text"));
        }
    }
}
