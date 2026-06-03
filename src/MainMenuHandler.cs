using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Il2CppCubeUnity.App.Collection;
using Il2CppCubeUnity.App.Navigator;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSecondDinner.CubeRendering.Card;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Manages navigation within the main menu hub.
/// </summary>
public class MainMenuHandler : IScreenNavigator
{
	private class MenuEntry
	{
		public string LocKey;
		public Action<Navigator> Activate;
	}

	public string NavigatorId => "MainMenu";
	public int Priority => 400;

	private Navigator _navigator;
	private int _focusIndex = -1;
	private bool _wasShown = false;
	private bool _inSubScreen = false;
	private float _inputBlockUntil = 0f;
	private bool _active = false;
	private readonly KeyHoldRepeater _holdRepeater = new KeyHoldRepeater();

	// --- Modal overlay tracking ---
	private bool _modalOverlayWasActive = false;
	private bool _modalOverlayCache = false;
	private float _lastModalCheck = 0f;
	private const float ModalCheckInterval = 0.3f;

	// --- Play screen state ---
	private Button _playButton;
	private Button _deckLeftButton;
	private Button _deckRightButton;
	private string _deckName = "";
	private bool _onPlayScreen = false;

	// --- Collection browsing state ---
	private bool _browsingCollection = false;
	private int _collectionLevel = 0; // 0 = categories, 1 = items inside category
	private readonly List<CollectionCard> _collectionCards = new List<CollectionCard>();
	private int _collectionIndex = 0;
	private int _collectionDetailLevel = 0;
	private readonly List<string> _collectionSections = new List<string>();
	private int _collectionSectionIndex = 0;
	private bool _deckActionMode = false;
	private CollectionDeckSlotView _activeDeckSlot = null;
	private Button _activeDeckButton = null;
	private bool _deckSubmenuMode = false;
	private int _deckSubmenuIndex = 0;
	private string _deckSubmenuName = "";
	private static readonly string[] _deckSubmenuItems = { "deck_sub_add", "deck_sub_view", "deck_sub_copy", "deck_sub_delete", "deck_sub_back" };
	private bool _deckBuilderActive = false;
	private DeckBuilderHandler _deckBuilderHandler = null;
	private int _collectionTabIndex = 0; // 0 = Cards, 1 = Albums
	private static readonly string[] _collectionTabNames = { "Cards", "Albums" };
	private static readonly string[] _collectionTabGoNames = { "Tab_Cards", "Tab_Albums" };

	// --- Game Modes state ---
	private bool _inGameModes = false;
	private readonly List<GameModeEntry> _gameModeEntries = new List<GameModeEntry>();
	private int _gameModeIndex = 0;

	private class GameModeEntry
	{
		public string Name;
		public bool IsLocked;
		public string LockReason;
		public Button Button; // null if locked (no Button component)
		public GameObject GameObject;
	}

	private class CollectionCard
	{
		public string Name;
		public Button Button;
		public CardRenderer Renderer;
		public bool IsDeckSlot;
	}

	// --- Reward browsing state ---
	private bool _browsingRewards = false;
	private bool _browsingRewardDetails = false;
	private readonly List<RewardEvent> _rewardEvents = new List<RewardEvent>();
	private readonly List<RewardDay> _rewardDays = new List<RewardDay>();
	private int _rewardIndex = 0;
	private int _rewardDayIndex = 0;

	private class RewardEvent
	{
		public string EventName;
		public string NextRewardDay;
		public string NextRewardCountdown; // empty = ready now
		public string FinalRewardDay;
		public string EventEndTime;
		public string ExtraInfo; // card name, boosters, etc.
		public Button SeeAllButton;
	}

	private class RewardDay
	{
		public string Day;
		public string Reward; // "155 Credits", "Series 3 Pack", card name, title name
		public string Countdown; // empty = claimable now or already claimed
		public bool IsClaimable; // label starts with "Claim"
		public Button ClaimButton;
	}

	// --- Start button state ---
	private string _startButtonLabel = "Start Game";

	private readonly List<MenuEntry> _entries = new List<MenuEntry>();

	public MainMenuHandler()
	{
		InitializeEntries();
	}

	private void InitializeEntries()
	{
		_entries.Add(new MenuEntry { LocKey = "menu_news", Activate = n => n.OnNewsButton() });
		_entries.Add(new MenuEntry { LocKey = "menu_shop", Activate = n => n.OnShopButton() });
		_entries.Add(new MenuEntry { LocKey = "menu_play", Activate = n => n.OnPlayButton() });
		_entries.Add(new MenuEntry { LocKey = "menu_collection", Activate = n => n.OnCollectionButton() });
		_entries.Add(new MenuEntry { LocKey = "menu_game_modes", Activate = n => n.OnGameModesButton() });
		_entries.Add(new MenuEntry { LocKey = "menu_clan", Activate = n => {
            Button b = FindButtonByName("Btn_Clans");
            if (b != null)
            {
                AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_opening_alliances"));
                UIHelper.ActivateButton(((Component)b).gameObject);
                _inSubScreen = true;
                NavigatorManager.Instance.GetNavigator<DialogHandler>()?.ResetWithDelay(0.8f);
            }
        } });
		_entries.Add(new MenuEntry { LocKey = "menu_settings", Activate = n => {
            OpenGameSettings();
        } });
	}

	/// <summary>
	/// Opens the game's native settings popup via SettingsButtonView.OnSettingsClicked().
	/// Uses reflection to avoid direct Il2Cpp type dependencies that can cause native crashes.
	/// After the popup loads (async via Addressables), deactivates so DialogHandler takes over.
	/// </summary>
	private void OpenGameSettings()
	{
		try
		{
			GameObject settingsGo = GameObject.Find("Button_Settings");
			if ((Object)(object)settingsGo == (Object)null || !settingsGo.activeInHierarchy)
			{
				AnnouncementService.Instance.Announce(Loc.Get("settings_not_found"));
				return;
			}

			// Find SettingsButtonView component via reflection
			Component settingsView = null;
			var components = settingsGo.GetComponents<Component>();
			if (components != null)
			{
				for (int i = 0; i < components.Count; i++)
				{
					var comp = components[i];
					if ((Object)(object)comp == (Object)null) continue;
					if (comp.GetType().Name.Contains("SettingsButtonView"))
					{
						settingsView = comp;
						break;
					}
				}
			}

			if ((Object)(object)settingsView == (Object)null)
			{
				UIHelper.SimulateMouseClick(settingsGo);
				AnnouncementService.Instance.Announce(Loc.Get("settings_opening"), AnnouncementPriority.High);
				_inSubScreen = true;
				return;
			}

			// Call OnSettingsClicked via reflection
			MethodInfo clickMethod = settingsView.GetType().GetMethod("OnSettingsClicked",
				BindingFlags.Public | BindingFlags.Instance);
			if (clickMethod != null)
			{
				clickMethod.Invoke(settingsView, null);
				AnnouncementService.Instance.Announce(Loc.Get("settings_opening"), AnnouncementPriority.High);
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Settings opened via OnSettingsClicked()");

				// Deactivate so DialogHandler (priority 200) picks up the settings popup.
				// The popup loads asynchronously via Addressables, so we wait briefly.
				MelonCoroutines.Start(WaitForSettingsPopup());
			}
			else
			{
				UIHelper.SimulateMouseClick(settingsGo);
				AnnouncementService.Instance.Announce(Loc.Get("settings_opening"), AnnouncementPriority.High);
				_inSubScreen = true;
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", $"OpenGameSettings error: {ex.Message}");
			AnnouncementService.Instance.Announce(Loc.Get("settings_not_found"));
		}
	}

	/// <summary>
	/// Waits for the settings popup to appear, then deactivates MainMenuHandler
	/// so DialogHandler can scan and navigate the settings controls.
	/// </summary>
	private IEnumerator WaitForSettingsPopup()
	{
		// Wait up to 3 seconds for the popup to load via Addressables
		float timeout = Time.time + 3f;
		while (Time.time < timeout)
		{
			yield return null;
			// Look for the settings popup — SettingsController is on the root of the popup
			Il2CppArrayBase<Slider> sliders = Object.FindObjectsOfType<Slider>();
			if (sliders != null && sliders.Count >= 3)
			{
				// Settings popup has at least 4 audio sliders — if we find 3+ sliders, it's ready
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
					$"Settings popup detected ({sliders.Count} sliders)");
				_active = false;
				yield break;
			}
		}
		DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Settings popup did not appear within timeout");
	}

	public bool IsActive => _active;

	public bool InSubScreen => _inSubScreen && !_onPlayScreen && !_browsingCollection && !_inGameModes;

	public void Update()
	{
		if (!FindNavigator())
		{
			_active = false;
			return;
		}

		bool visible = IsNavigatorVisible();
		if (visible && !_wasShown) OnNavigatorShown();
		else if (!visible && _wasShown) OnNavigatorHidden();
		_wasShown = visible;

		if (!visible)
		{
			_active = false;
			return;
		}

		bool consumed = ProcessInput();

		// When in a sub-screen (News, Shop, Clan) that DialogHandler should handle,
		// deactivate so DialogHandler can take over via NavigatorManager priority.
		if (InSubScreen && !consumed)
		{
			_active = false;
			return;
		}

		// Otherwise MainMenuHandler owns the screen.
		_active = true;
	}

	/// <summary>
	/// Checks whether a popup, dialog, or modal overlay is active on top of the current screen.
	/// Covers Canvas-Dialogs (WidgetContainer modals), Canvas-FullscreenModals, Canvas-Rewards,
	/// and any other high-order overlay canvas that contains interactive content.
	/// </summary>
	private bool HasActiveModalOverlay()
	{
		try
		{
			// Check Canvas-Dialogs for WidgetContainer modals (e.g. "Enter a Name", consent)
			if (HasActiveChildContent("Canvas-Dialogs", "DialogPanel", "WidgetContainer"))
				return true;

			// Check Canvas-FullscreenModals for fullscreen popups (e.g. season pass, bundles)
			if (HasActiveCanvasContent("Canvas-FullscreenModals"))
				return true;

			// Check Canvas-Rewards for reward claim popups
			if (HasActiveCanvasContent("Canvas-Rewards"))
				return true;

			// Check Canvas-DownloadModals for download/update prompts
			if (HasActiveCanvasContent("Canvas-DownloadModals"))
				return true;

			// Check FloatingScreenContainer for overlay screens (login rewards, etc.)
			// These appear inside SubSceneController and overlay the play screen.
			// Skip this check when we're already browsing rewards — those screens
			// live in FloatingScreenContainer and MainMenuHandler handles them directly.
			if (!_browsingRewards && !_browsingRewardDetails && HasFloatingScreenOverlay())
				return true;
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
				$"HasActiveModalOverlay error: {ex.Message}");
		}
		return false;
	}

	/// <summary>
	/// Checks if a specific parent/child pattern has active content (buttons or text).
	/// Used for structured overlays like Canvas-Dialogs/DialogPanel/WidgetContainer.
	/// </summary>
	private bool HasActiveChildContent(string canvasName, string panelName, string childPattern)
	{
		GameObject canvas = GameObject.Find(canvasName);
		if ((Object)(object)canvas == (Object)null) return false;

		Transform panel = canvas.transform.Find(panelName);
		if ((Object)(object)panel == (Object)null) return false;

		for (int i = 0; i < panel.childCount; i++)
		{
			Transform child = panel.GetChild(i);
			if (child != null && child.gameObject.activeInHierarchy &&
			    child.gameObject.name.Contains(childPattern))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Checks if an overlay canvas has active buttons or meaningful content,
	/// indicating a popup the user needs to interact with.
	/// </summary>
	private bool HasActiveCanvasContent(string canvasName)
	{
		GameObject canvas = GameObject.Find(canvasName);
		if ((Object)(object)canvas == (Object)null) return false;
		if (!canvas.activeInHierarchy) return false;

		Il2CppArrayBase<Button> buttons = canvas.GetComponentsInChildren<Button>(false);
		if (buttons != null && buttons.Count > 0)
			return true;
		return false;
	}

	/// <summary>
	/// Checks if a floating screen overlay (login rewards, card upgrade, etc.) is active.
	/// These live inside SubSceneController/FloatingScreenContainer and overlay the play screen.
	/// </summary>
	private bool HasFloatingScreenOverlay()
	{
		try
		{
			GameObject subScene = GameObject.Find("SubSceneController");
			if ((Object)(object)subScene == (Object)null) return false;

			Transform floating = subScene.transform.Find("FloatingScreenContainer");
			if ((Object)(object)floating == (Object)null || !floating.gameObject.activeInHierarchy) return false;

			// Check if any child of FloatingScreenStagingArea/ScreenAnchor has active content
			Transform staging = floating.Find("FloatingScreenStagingArea");
			if ((Object)(object)staging == (Object)null) return false;

			Transform anchor = staging.Find("ScreenAnchor");
			if ((Object)(object)anchor == (Object)null) return false;

			for (int i = 0; i < anchor.childCount; i++)
			{
				Transform child = anchor.GetChild(i);
				if (child != null && child.gameObject.activeInHierarchy)
				{
					// Found an active floating screen
					Il2CppArrayBase<Button> buttons = child.gameObject.GetComponentsInChildren<Button>(false);
					if (buttons != null && buttons.Count > 0)
						return true;
				}
			}
		}
		catch { }
		return false;
	}

	/// <summary>Returns true if input was consumed and no other handler should process it.</summary>
	private bool ProcessInput()
	{
		if (Time.time < _inputBlockUntil) return false;

		// Modal dialog overlay check — popups/dialogs appear on top of the current screen
		// and must be handled by DialogHandler instead of the current menu state.
		// Throttled to avoid expensive FindObjectsOfType every frame.
		if (Time.time - _lastModalCheck >= ModalCheckInterval)
		{
			_lastModalCheck = Time.time;
			_modalOverlayCache = HasActiveModalOverlay();
		}
		bool modalActive = _modalOverlayCache;
		if (modalActive)
		{
			if (!_modalOverlayWasActive)
			{
				// Modal just appeared — delay the scan to let dialog animation finish
				// (buttons may be non-interactable during animation)
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Modal overlay appeared");
				NavigatorManager.Instance.GetNavigator<DialogHandler>()?.ResetWithDelay(0.6f);
				_modalOverlayWasActive = true;
			}
			var dialogHandler = NavigatorManager.Instance.GetNavigator<DialogHandler>();
			dialogHandler?.Update();
			return true;
		}
		else if (_modalOverlayWasActive)
		{
			// Modal just closed — reset DialogHandler and restore normal menu handling
			var dialogHandler = NavigatorManager.Instance.GetNavigator<DialogHandler>();
			dialogHandler?.OnSceneChanged(NavigatorManager.Instance.CurrentScene ?? "");
			_modalOverlayWasActive = false;
		}

		// Deck builder sub-handler: when active, delegate all input to it
		if (_deckBuilderActive)
		{
			if (_deckBuilderHandler == null)
				_deckBuilderHandler = NavigatorManager.Instance.GetNavigator<DeckBuilderHandler>();
			if (_deckBuilderHandler != null)
			{
				_deckBuilderHandler.Update();
				// Check if DeckBuilder deactivated itself (Backspace pressed inside it)
				if (!_deckBuilderHandler.IsActive)
				{
					_deckBuilderActive = false;
					// Resume collection browsing — announce current deck
					AnnouncementService.Instance.Announce(Loc.Get("deck_builder_back"));
					if (_collectionCards.Count > 0 && _collectionIndex >= 0 && _collectionIndex < _collectionCards.Count)
						AnnounceCollectionCard();
				}
			}
			else
			{
				_deckBuilderActive = false;
			}
			return true;
		}

		// Backspace always exits current level — checked first so it works everywhere
		if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
		{
			HandleBackCommand();
			return true;
		}

		if (_browsingRewardDetails) { ProcessRewardDetailsInput(); return true; }
		if (_browsingRewards) { ProcessRewardsInput(); return true; }
		if (_browsingCollection) { ProcessCollectionInput(); return true; }
		if (_inGameModes) { ProcessGameModesInput(); return true; }
		if (_onPlayScreen) { ProcessPlayInput(); return true; }
		if (_inSubScreen) return false; // Let DialogHandler handle arrows/Enter

		HandleMenuBarInput();
		return true; // Menu bar is active — consume input to prevent DialogHandler conflicts
	}

	private void HandleBackCommand()
	{
		if (_browsingRewardDetails)
		{
			_browsingRewardDetails = false;
			_rewardDays.Clear();
			// Click the back button on the reward detail screen
			TryClickRewardDetailBackButton();
			_browsingRewards = true;
			AnnouncementService.Instance.AnnounceInterrupt("Back to reward events.");
			AnnounceReward();
			return;
		}

		if (_browsingRewards)
		{
			_browsingRewards = false;
			AnnouncePlayCategory();
			return;
		}

		if (_browsingCollection)
		{
			if (_collectionLevel == 1)
			{
				// Back from items to category list
				_collectionLevel = 0;
				AnnouncementService.Instance.Announce(_collectionSections[_collectionSectionIndex] + ", " + (_collectionSectionIndex + 1) + " of " + _collectionSections.Count);
				return;
			}
			_browsingCollection = false;
			_deckActionMode = false;
			_deckSubmenuMode = false;
			_activeDeckSlot = null;
			_activeDeckButton = null;
			_inSubScreen = false;
			TryClickGameBackButton();
			AnnouncementService.Instance.AnnounceInterrupt("Exiting collection.");
			AnnounceFocused();
		}
		else if (_inGameModes)
		{
			_inGameModes = false;
			_inSubScreen = false;
			AnnouncementService.Instance.Announce(Loc.Get("menu_nav_focus"));
			AnnounceFocused();
		}
		else if (_inSubScreen)
		{
			TryClickGameBackButton();
			_inSubScreen = false;
			AnnouncementService.Instance.Announce(Loc.Get("menu_nav_focus"));
			AnnounceFocused();
		}
		else if (_onPlayScreen)
		{
			_onPlayScreen = false;
			AnnouncementService.Instance.Announce(Loc.Get("menu_nav_focus"));
			AnnounceFocused();
		}
	}

	private void HandleMenuBarInput()
	{
		if (_holdRepeater.Check(SDLInput.Key.Up, () => MoveFocus(-1))) { }
		else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp)) MoveFocus(-1);
		else if (_holdRepeater.Check(SDLInput.Key.Down, () => MoveFocus(1))) { }
		else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown)) MoveFocus(1);
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South)) ActivateFocused();
		else
		{
			for (int i = 0; i < _entries.Count && i < 6; i++)
			{
				if (SDLInput.IsKeyDown((SDLInput.Key)(49 + i))) { _focusIndex = i; ActivateFocused(); return; }
			}
		}
	}

	private void MoveFocus(int dir)
	{
		_focusIndex = (_focusIndex + dir + _entries.Count) % _entries.Count;
		AnnounceFocused();
	}

	private void AnnounceFocused()
	{
		if (_focusIndex < 0 || _focusIndex >= _entries.Count) return;
		var entry = _entries[_focusIndex];
		AnnouncementService.Instance.Announce(Loc.Get(entry.LocKey) + ", " + (_focusIndex + 1) + " of " + _entries.Count);
	}

	private void ActivateFocused()
	{
		if (_focusIndex < 0 || _focusIndex >= _entries.Count) return;
		var entry = _entries[_focusIndex];
		AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_activated", Loc.Get(entry.LocKey)));
		entry.Activate(_navigator);
		EnterCurrentSection();
	}

	private void EnterCurrentSection()
	{
		if (_focusIndex < 0 || _focusIndex >= _entries.Count) return;
		string key = _entries[_focusIndex].LocKey;
		if (key == "menu_play") EnterPlayScreen();
		else if (key == "menu_collection") EnterCollection();
		else if (key == "menu_game_modes") EnterGameModes();
		else
		{
			_inSubScreen = true;
			if (key == "menu_shop")
				NavigatorManager.Instance.GetNavigator<ShopHandler>()?.Activate();
			NavigatorManager.Instance.GetNavigator<DialogHandler>()?.ResetWithDelay(0.5f);
		}
	}

	// --- Play Screen ---

	private enum PlayMenuCategory { StartGame, SelectDeck, EditDeck, Missions, Rewards, GameInfo }
	private PlayMenuCategory _playCategory = PlayMenuCategory.StartGame;
	private const int _playMenuCount = 6;

	private void EnterPlayScreen()
	{
		_onPlayScreen = true;
		_inSubScreen = false;
		_playCategory = PlayMenuCategory.StartGame;
		ScanPlayScreen();
		AnnouncementService.Instance.Announce(Loc.Get("play_screen") + " Deck: " + _deckName, AnnouncementPriority.High);
		AnnouncePlayCategory();
	}

	private void ProcessPlayInput()
	{
		if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
		{
			_playCategory = (PlayMenuCategory)(((int)_playCategory - 1 + _playMenuCount) % _playMenuCount);
			AnnouncePlayCategory();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
		{
			_playCategory = (PlayMenuCategory)(((int)_playCategory + 1) % _playMenuCount);
			AnnouncePlayCategory();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			// Read details about current category
			AnnouncePlayCategoryDetails();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			ActivatePlayCategory();
		}
		// Backspace is handled centrally in ProcessInput -> HandleBackCommand
	}

	private void AnnouncePlayCategory()
	{
		string label = _playCategory switch
		{
			PlayMenuCategory.StartGame => _startButtonLabel,
			PlayMenuCategory.SelectDeck => Loc.Get("play_select_deck", _deckName),
			PlayMenuCategory.EditDeck => Loc.Get("play_edit_deck"),
			PlayMenuCategory.Missions => Loc.Get("play_missions"),
			PlayMenuCategory.Rewards => Loc.Get("play_rewards"),
			PlayMenuCategory.GameInfo => Loc.Get("play_game_info"),
			_ => "Unknown"
		};
		AnnouncementService.Instance.Announce(label + ", " + ((int)_playCategory + 1) + " of " + _playMenuCount);
	}

	private void AnnouncePlayCategoryDetails()
	{
		string detail = _playCategory switch
		{
			PlayMenuCategory.StartGame => Loc.Get("play_start_detail", _deckName),
			PlayMenuCategory.SelectDeck => Loc.Get("play_select_detail", _deckName),
			PlayMenuCategory.EditDeck => Loc.Get("play_edit_detail", _deckName),
			PlayMenuCategory.Missions => Loc.Get("play_missions_detail"),
			PlayMenuCategory.Rewards => Loc.Get("play_rewards_detail"),
			PlayMenuCategory.GameInfo => ReadRankText() + ". " + ReadSeasonText(),
			_ => ""
		};
		AnnouncementService.Instance.Announce(detail, AnnouncementPriority.Low);
	}

	private void ActivatePlayCategory()
	{
		// Re-scan play screen buttons in case they weren't loaded on first entry
		ScanPlayScreen();

		switch (_playCategory)
		{
			case PlayMenuCategory.StartGame:
				if (_playButton != null)
				{
					AnnouncementService.Instance.AnnounceInterrupt(_startButtonLabel);
					UIHelper.ActivateButton(((Component)_playButton).gameObject);
				}
				break;
			case PlayMenuCategory.SelectDeck:
				Button deckBtn = _deckLeftButton ?? _deckRightButton;
				if (deckBtn != null) UIHelper.ActivateButton(((Component)deckBtn).gameObject);
				else AnnouncementService.Instance.Announce(Loc.Get("menu_deck_not_available"));
				break;
			case PlayMenuCategory.EditDeck:
				TryOpenDeckEditor();
				break;
			case PlayMenuCategory.Missions:
				NavigatorManager.Instance.GetNavigator<MissionsHandler>()?.Activate();
				break;
			case PlayMenuCategory.Rewards:
				ScanRewards();
				if (_rewardEvents.Count > 0)
				{
					_browsingRewards = true;
					_rewardIndex = 0;
					AnnouncementService.Instance.Announce(Loc.Get("menu_rewards_count", _rewardEvents.Count), AnnouncementPriority.High);
					AnnounceReward();
				}
				else
				{
					AnnouncementService.Instance.Announce(Loc.Get("menu_no_rewards"));
				}
				break;
			case PlayMenuCategory.GameInfo:
				ReadPlayScreenInfo();
				break;
		}
	}

	private void ScanPlayScreen()
	{
		// Search under PlayScreenLandscape specifically to avoid picking up buttons from other screens
		GameObject playScreen = GameObject.Find("PlayScreenLandscape(Clone)");
		if (playScreen != null)
		{
			_playButton = FindButtonUnder(playScreen.transform, "btn_start") ?? FindButtonUnder(playScreen.transform, "PlayButton");
			_deckLeftButton = FindButtonUnder(playScreen.transform, "btn_left");
			_deckRightButton = FindButtonUnder(playScreen.transform, "btn_right");
		}
		else
		{
			_playButton = FindButtonByName("btn_start") ?? FindButtonByName("PlayButton");
			_deckLeftButton = FindButtonByName("btn_left");
			_deckRightButton = FindButtonByName("btn_right");
		}
		_deckName = ReadDeckNameFromUI();

		// Read the actual start button label (may say "Reconnect to Game" etc.)
		if (_playButton != null)
		{
			string label = UIHelper.GetButtonLabel(_playButton);
			if (!string.IsNullOrEmpty(label) && label.Length > 2)
				_startButtonLabel = label;
			else
				_startButtonLabel = "Start Game";
		}
		else
		{
			_startButtonLabel = "Start Game";
		}
	}

	private string ReadDeckNameFromUI()
	{
		// Primary: find the Text_Name TMP_Text (under Disk_Base/ReactiveDeckView)
		try
		{
			Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
			foreach (var t in texts)
			{
				if (t.gameObject.name == "Text_Name" && t.gameObject.activeInHierarchy)
				{
					string name = UIHelper.StripRichText(t.text);
					if (!string.IsNullOrEmpty(name) && name.Length > 1 && !name.Contains("{Missing"))
						return name;
				}
			}
		}
		catch { }
		// Fallback: try btn_left label
		if (_deckLeftButton != null)
		{
			string label = UIHelper.GetButtonLabel(_deckLeftButton);
			if (!string.IsNullOrEmpty(label) && label.Length > 2 && !char.IsDigit(label[0]) && !label.Contains("Rank")) return label;
		}
		return "Current Deck";
	}

	private void ReadPlayScreenInfo()
	{
		string rank = ReadRankText();
		string season = ReadSeasonText();
		AnnouncementService.Instance.Announce(rank + ". " + season, AnnouncementPriority.Low);
	}

	private string ReadRankText()
	{
		try
		{
			var parts = new List<string>();
			Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
			foreach (var t in texts)
			{
				if ((Object)(object)t == (Object)null || !t.gameObject.activeInHierarchy) continue;
				string goName = t.gameObject.name;
				if (goName == "Text_Rank") parts.Add(Loc.Get("play_rank", t.text.Trim()));
				else if (goName.Contains("TierName") || goName.Contains("Tier_Name"))
				{
					string tier = UIHelper.StripRichText(t.text.Trim());
					if (!string.IsNullOrEmpty(tier)) parts.Add(Loc.Get("play_tier", tier));
				}
				else if (goName.Contains("Trophies") && !goName.Contains("Meter"))
				{
					string troph = UIHelper.StripRichText(t.text.Trim());
					if (!string.IsNullOrEmpty(troph)) parts.Add(Loc.Get("play_trophies", troph));
				}
			}
			if (parts.Count > 0) return string.Join(", ", parts);
		}
		catch { }
		return Loc.Get("play_rank_unknown");
	}

	private string ReadSeasonText()
	{
		Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
		foreach (var t in texts)
		{
			if ((Object)(object)t == (Object)null || !t.gameObject.activeInHierarchy) continue;
			if (t.gameObject.name == "seasonpass_title") return Loc.Get("play_season", UIHelper.StripRichText(t.text.Trim()));
		}
		return "";
	}

	// --- Game Modes ---

	private void EnterGameModes()
	{
		_inGameModes = true;
		_inSubScreen = false;
		MelonCoroutines.Start(ScanGameModesDelayed());
	}

	private IEnumerator ScanGameModesDelayed()
	{
		// GameModeScreen needs time to load after clicking the button
		for (int attempt = 0; attempt < 6; attempt++)
		{
			yield return new WaitForSeconds(attempt == 0 ? 0.8f : 0.5f);
			ScanGameModes();
			if (_gameModeEntries.Count > 0) break;
		}
		if (_gameModeEntries.Count > 0)
		{
			_gameModeIndex = 0;
			AnnouncementService.Instance.Announce(Loc.Get("menu_game_modes_count", _gameModeEntries.Count), AnnouncementPriority.High);
			AnnounceGameMode();
		}
		else
		{
			AnnouncementService.Instance.Announce(Loc.Get("menu_no_game_modes"));
		}
	}

	private void ScanGameModes()
	{
		_gameModeEntries.Clear();
		try
		{
			// Game mode screen is at GameModeScreen(Clone)/MainCanvas/SafePanel/ModesScrollView/Root/Content
			GameObject gameModeScreen = GameObject.Find("GameModeScreen(Clone)");
			if (gameModeScreen == null) return;

			Transform content = UIHelper.FindChildByName(gameModeScreen.transform, "Content");
			if ((Object)(object)content == (Object)null) return;

			for (int i = 0; i < content.childCount; i++)
			{
				Transform child = content.GetChild(i);
				if (!child.gameObject.activeInHierarchy) continue;
				string goName = child.gameObject.name;

				// Read label from Text_Header child under Root or TitleAnchor
				string label = "";
				Transform textHeader = UIHelper.FindChildByName(child, "Text_Header");
				if ((Object)(object)textHeader != (Object)null)
				{
					var tmp = textHeader.GetComponent<TMP_Text>();
					if ((Object)(object)tmp != (Object)null)
						label = UIHelper.StripRichText(tmp.text);
				}
				if (string.IsNullOrEmpty(label))
					label = UIHelper.CleanGameObjectName(goName.Replace("btn_", ""));
				if (string.IsNullOrEmpty(label) || label.Length < 2) continue;

				// Check if locked (has LockedGroup child that is active)
				bool isLocked = false;
				string lockReason = "";
				Transform lockedGroup = UIHelper.FindChildByName(child, "LockedGroup");
				if ((Object)(object)lockedGroup != (Object)null)
				{
					// Check both activeInHierarchy and activeSelf — Il2Cpp can be inconsistent
					bool groupActive = false;
					try { groupActive = lockedGroup.gameObject.activeInHierarchy || lockedGroup.gameObject.activeSelf; } catch { groupActive = true; }
					if (groupActive)
					{
						isLocked = true;
						Transform contentText = UIHelper.FindChildByName(lockedGroup, "ContentText");
						if ((Object)(object)contentText != (Object)null)
						{
							var descTmp = contentText.GetComponent<TMP_Text>();
							if ((Object)(object)descTmp != (Object)null)
								lockReason = UIHelper.StripRichText(descTmp.text);
						}
					}
				}

				// Also check for tooltip_locked (alternative lock indicator)
				if (!isLocked)
				{
					Transform tooltipLocked = UIHelper.FindChildByName(child, "tooltip_locked");
					if ((Object)(object)tooltipLocked != (Object)null)
					{
						bool tooltipActive = false;
						try { tooltipActive = tooltipLocked.gameObject.activeInHierarchy || tooltipLocked.gameObject.activeSelf; } catch { tooltipActive = true; }
						if (tooltipActive)
						{
							isLocked = true;
							Transform descTf = UIHelper.FindChildByName(tooltipLocked, "text_description");
							if ((Object)(object)descTf != (Object)null)
							{
								var descTmp = descTf.GetComponent<TMP_Text>();
								if ((Object)(object)descTmp != (Object)null)
									lockReason = UIHelper.StripRichText(descTmp.text);
							}
						}
					}
				}

				// Fallback: scan all text children for lock-related keywords
				if (!isLocked)
				{
					Il2CppArrayBase<TMP_Text> allTexts = child.GetComponentsInChildren<TMP_Text>(true);
					foreach (var t in allTexts)
					{
						if (t == null) continue;
						string val = UIHelper.StripRichText(t.text);
						if (!string.IsNullOrEmpty(val) &&
							(val.Contains("Unlock") || val.Contains("unlock") || val.Contains("Locked") || val.Contains("locked")))
						{
							isLocked = true;
							lockReason = val;
							break;
						}
					}
				}

				// Try to get Button component (locked modes may not have one)
				Button btn = child.GetComponent<Button>();
				// If no button but not explicitly locked, check interactability
				if (btn != null && !((Selectable)btn).interactable)
					isLocked = true;

				_gameModeEntries.Add(new GameModeEntry
				{
					Name = label,
					IsLocked = isLocked,
					LockReason = lockReason,
					Button = btn,
					GameObject = child.gameObject
				});
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenu", "ScanGameModes failed: " + ex.Message);
		}
	}

	private void ProcessGameModesInput()
	{
		if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
		{
			if (_gameModeEntries.Count == 0) return;
			_gameModeIndex = (_gameModeIndex - 1 + _gameModeEntries.Count) % _gameModeEntries.Count;
			AnnounceGameMode();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
		{
			if (_gameModeEntries.Count == 0) return;
			_gameModeIndex = (_gameModeIndex + 1) % _gameModeEntries.Count;
			AnnounceGameMode();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			// Read details
			if (_gameModeIndex >= 0 && _gameModeIndex < _gameModeEntries.Count)
			{
				var mode = _gameModeEntries[_gameModeIndex];
				if (mode.IsLocked && !string.IsNullOrEmpty(mode.LockReason))
					AnnouncementService.Instance.Announce(mode.LockReason, AnnouncementPriority.Low);
				else if (mode.IsLocked)
					AnnouncementService.Instance.Announce(Loc.Get("menu_mode_locked"), AnnouncementPriority.Low);
				else
					AnnouncementService.Instance.Announce(mode.Name + ". Press Enter to open.", AnnouncementPriority.Low);
			}
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			if (_gameModeIndex >= 0 && _gameModeIndex < _gameModeEntries.Count)
			{
				var mode = _gameModeEntries[_gameModeIndex];
				if (mode.IsLocked)
				{
					AnnouncementService.Instance.Announce(Loc.Get("menu_locked_reason", string.IsNullOrEmpty(mode.LockReason) ? "" : mode.LockReason));
				}
				else if (mode.Button != null)
				{
					AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_activated", mode.Name));
					UIHelper.ActivateButton(mode.Button);
					_inGameModes = false;
					_inSubScreen = true;
					// Activate FriendlyMatchHandler in case this is the friendly battle mode
					NavigatorManager.Instance.GetNavigator<FriendlyMatchHandler>()?.Activate();
					NavigatorManager.Instance.GetNavigator<DialogHandler>()?.ResetWithDelay(0.8f);
				}
			}
		}
		// Backspace handled by HandleBackCommand
	}

	private void AnnounceGameMode()
	{
		if (_gameModeIndex < 0 || _gameModeIndex >= _gameModeEntries.Count) return;
		var mode = _gameModeEntries[_gameModeIndex];
		string locked = mode.IsLocked ? ", locked" : "";
		AnnouncementService.Instance.Announce(mode.Name + locked + ", " + (_gameModeIndex + 1) + " of " + _gameModeEntries.Count);
	}

	// --- Collection ---

	private void EnterCollection()
	{
		_browsingCollection = true;
		_inSubScreen = true;
		_collectionSectionIndex = 0;
		_collectionIndex = 0;
		_collectionLevel = 0;
		_collectionTabIndex = 0;
		MelonCoroutines.Start(ScanCollectionDelayed());
	}

	private IEnumerator ScanCollectionDelayed()
	{
		// Collection scene needs time to load — retry up to 5 times
		for (int attempt = 0; attempt < 5; attempt++)
		{
			yield return new WaitForSeconds(attempt == 0 ? 1.0f : 0.5f);

			// Dismiss "Custom Card Unlocked" tutorial overlay if present — it blocks collection
			bool dismissedTutorial = false;
			try
			{
				GameObject customCardTut = GameObject.Find("UnlockedCustomCardsTutorialLandscape");
				if (customCardTut != null && customCardTut.activeInHierarchy)
				{
					customCardTut.SetActive(false);
					DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Dismissed custom card tutorial in collection");
					dismissedTutorial = true;
				}
			}
			catch { }
			if (dismissedTutorial)
				yield return new WaitForSeconds(0.3f);

			ScanCollectionSections();
			if (_collectionSections.Count > 0) break;
		}

		if (_collectionSections.Count > 0)
		{
			_collectionLevel = 0;
			string tabName = _collectionTabNames[_collectionTabIndex];
			AnnouncementService.Instance.Announce(Loc.Get("menu_collection_tab", tabName, _collectionSections.Count), AnnouncementPriority.High);
			AnnouncementService.Instance.Announce(_collectionSections[_collectionSectionIndex] + ", 1 of " + _collectionSections.Count, AnnouncementPriority.Low);
		}
		else AnnouncementService.Instance.Announce(Loc.Get("menu_collection_empty"));
	}

	private void SwitchCollectionTab(int tabIdx)
	{
		string tabName = _collectionTabNames[tabIdx];
		string tabGoName = _collectionTabGoNames[tabIdx];

		// Find the tab button by its known GameObject name (Tab_Cards or Tab_Albums)
		bool switched = false;
		try
		{
			GameObject tabGo = GameObject.Find(tabGoName);
			if (tabGo != null)
			{
				UIHelper.SendPointerClick(tabGo);
				switched = true;
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", $"Clicked tab button: {tabGoName}");
			}
			else
			{
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", $"Tab button not found: {tabGoName}");
			}
		}
		catch (System.Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", $"SwitchCollectionTab exception: {ex.Message}");
		}

		if (switched)
		{
			_collectionSectionIndex = 0;
			_collectionIndex = 0;
			MelonCoroutines.Start(RescanCollectionAfterTabSwitch(tabName));
		}
		else
		{
			AnnouncementService.Instance.Announce(Loc.Get("menu_tab_switch_failed", tabName));
		}
	}

	private IEnumerator RescanCollectionAfterTabSwitch(string tabName)
	{
		yield return new WaitForSeconds(0.5f);
		ScanCollectionSections();
		if (_collectionSections.Count > 0)
		{
			AnnouncementService.Instance.Announce(tabName + " tab, " + _collectionSections.Count + " categories.", AnnouncementPriority.High);
			AnnouncementService.Instance.Announce(_collectionSections[0] + ", 1 of " + _collectionSections.Count, AnnouncementPriority.Low);
		}
		else
		{
			AnnouncementService.Instance.Announce(tabName + " tab.", AnnouncementPriority.High);
		}
	}

	private void ProcessCollectionInput()
	{
		// Deck submenu mode: navigable menu after pressing Enter on a deck
		if (_deckSubmenuMode)
		{
			ProcessDeckSubmenu();
			return;
		}

		// Legacy deck action mode (unused, kept for safety)
		if (_deckActionMode)
		{
			ProcessDeckAction();
			return;
		}

		// Level 0: Category navigation
		if (_collectionLevel == 0)
		{
			if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
			{
				if (_collectionSections.Count == 0) return;
				_collectionSectionIndex = (_collectionSectionIndex - 1 + _collectionSections.Count) % _collectionSections.Count;
				AnnouncementService.Instance.Announce(_collectionSections[_collectionSectionIndex] + ", " + (_collectionSectionIndex + 1) + " of " + _collectionSections.Count);
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
			{
				if (_collectionSections.Count == 0) return;
				_collectionSectionIndex = (_collectionSectionIndex + 1) % _collectionSections.Count;
				AnnouncementService.Instance.Announce(_collectionSections[_collectionSectionIndex] + ", " + (_collectionSectionIndex + 1) + " of " + _collectionSections.Count);
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
			{
				if (_collectionSections.Count == 0) return;
				// Preview item count
				ScanCollectionCardsInSection(_collectionSectionIndex);
				AnnouncementService.Instance.Announce(_collectionSections[_collectionSectionIndex] + ": " + _collectionCards.Count + " items.", AnnouncementPriority.Low);
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Home))
			{
				if (_collectionSections.Count > 0) { _collectionSectionIndex = 0; AnnouncementService.Instance.Announce(_collectionSections[0] + ", 1 of " + _collectionSections.Count); }
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.End))
			{
				if (_collectionSections.Count > 0) { _collectionSectionIndex = _collectionSections.Count - 1; AnnouncementService.Instance.Announce(_collectionSections[_collectionSectionIndex] + ", " + _collectionSections.Count + " of " + _collectionSections.Count); }
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.H))
			{
				AnnouncementService.Instance.Announce(Loc.Get("menu_collection_help"), AnnouncementPriority.High);
			}
			// I: Collection stats summary
			else if (SDLInput.IsKeyDown(SDLInput.Key.I) || SDLInput.IsButtonDown(SDLInput.GamepadButton.North))
			{
				AnnounceCollectionStats();
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Tab) || SDLInput.IsButtonDown(SDLInput.GamepadButton.L1) || SDLInput.IsButtonDown(SDLInput.GamepadButton.R1))
			{
				// Switch collection tab (Decks / Albums)
				_collectionTabIndex = (_collectionTabIndex + 1) % _collectionTabNames.Length;
				SwitchCollectionTab(_collectionTabIndex);
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
			{
				if (_collectionSections.Count == 0) return;
				// Enter category
				ScanCollectionCardsInSection(_collectionSectionIndex);
				_collectionIndex = 0;
				_collectionLevel = 1;
				if (_collectionCards.Count > 0)
				{
					AnnouncementService.Instance.Announce(_collectionSections[_collectionSectionIndex] + ", " + _collectionCards.Count + " items.", AnnouncementPriority.High);
					AnnouncementService.Instance.Announce(_collectionCards[0].Name + ", 1 of " + _collectionCards.Count, AnnouncementPriority.Low);
				}
				else
				{
					AnnouncementService.Instance.Announce(_collectionSections[_collectionSectionIndex] + " is empty.");
				}
			}
			// Backspace handled in HandleBackCommand
			return;
		}

		// Level 1: Items within category
		// Up: previous item
		if (_holdRepeater.Check(SDLInput.Key.Up, () => { if (_collectionCards.Count == 0) return; _collectionIndex = (_collectionIndex - 1 + _collectionCards.Count) % _collectionCards.Count; AnnounceCollectionCard(); })) { }
		else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
		{
			if (_collectionCards.Count == 0) return;
			_collectionIndex = (_collectionIndex - 1 + _collectionCards.Count) % _collectionCards.Count;
			AnnounceCollectionCard();
		}
		// Down: next item
		else if (_holdRepeater.Check(SDLInput.Key.Down, () => { if (_collectionCards.Count == 0) return; _collectionIndex = (_collectionIndex + 1) % _collectionCards.Count; AnnounceCollectionCard(); })) { }
		else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
		{
			if (_collectionCards.Count == 0) return;
			_collectionIndex = (_collectionIndex + 1) % _collectionCards.Count;
			AnnounceCollectionCard();
		}
		// Home/End: jump to first/last item
		else if (SDLInput.IsKeyDown(SDLInput.Key.Home))
		{
			if (_collectionCards.Count > 0) { _collectionIndex = 0; AnnounceCollectionCard(); }
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.End))
		{
			if (_collectionCards.Count > 0) { _collectionIndex = _collectionCards.Count - 1; AnnounceCollectionCard(); }
		}
		// A-Z: letter jump
		else if (TryLetterJumpCollection()) { }
		else if (SDLInput.IsKeyDown(SDLInput.Key.H))
		{
			AnnouncementService.Instance.Announce(Loc.Get("menu_collection_items_help"), AnnouncementPriority.High);
		}
		// Right: read card details (cost, power)
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			ReadCollectionCardInfo();
		}
		// Enter: activate card or enter deck action mode
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			if (_collectionIndex >= 0 && _collectionIndex < _collectionCards.Count)
			{
				var card = _collectionCards[_collectionIndex];
				if (card.IsDeckSlot && card.Name != "New Deck")
				{
					// Click the deck button to load it in the detail panel
					UIHelper.ActivateButton(card.Button);
					// Open deck submenu — AccessibleArena pattern: navigable menu of actions
					_deckSubmenuMode = true;
					_deckSubmenuIndex = 0;
					_deckSubmenuName = card.Name;
					_activeDeckSlot = FindDeckSlotViewFromButton(card.Button) ?? FindDeckSlotView();
					_activeDeckButton = card.Button;
					AnnouncementService.Instance.Announce(
						card.Name + ". " + Loc.Get("deck_sub_menu_intro"), AnnouncementPriority.High);
					AnnounceDeckSubmenuItem();
				}
				else if (card.IsDeckSlot && card.Name == "New Deck")
				{
					AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_creating_deck"));
					// MTGA pattern: invoke OnClick() directly on the empty slot's CollectionDeckSlotView
					CollectionDeckSlotView emptySlot = FindDeckSlotViewFromButton(card.Button);
					if (emptySlot != null)
					{
						emptySlot.OnClick();
						DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "New deck created via OnClick()");
					}
					else
					{
						// Fallback: full pointer event sequence
						UIHelper.ActivateButton(card.Button.gameObject);
						DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "New deck created via SimulatePointerClick fallback");
					}
				}
				else
				{
					AnnouncementService.Instance.AnnounceInterrupt("Activating " + card.Name);
					UIHelper.ActivateButton(card.Button);
				}
			}
		}
		// Backspace goes back to categories (handled in HandleBackCommand)
	}

	/// <summary>Find the deck action options panel (appears after clicking a deck).</summary>
	/// <summary>
	/// Finds the CollectionDeckSlotView for the currently focused deck.
	/// Walks up from the button's GameObject, then falls back to scanning all slots.
	/// </summary>
	private CollectionDeckSlotView FindDeckSlotView()
	{
		// Strategy 1: Walk up from the focused card's button to find CollectionDeckSlotView
		if (_collectionIndex >= 0 && _collectionIndex < _collectionCards.Count)
		{
			var card = _collectionCards[_collectionIndex];
			if (card.Button != null)
			{
				// Check button itself and parents up to 8 levels
				Transform current = ((Component)card.Button).transform;
				for (int depth = 0; depth < 8 && current != null; depth++)
				{
					var slotView = current.GetComponent<CollectionDeckSlotView>();
					if ((Object)(object)slotView != (Object)null)
						return slotView;
					current = current.parent;
				}
			}
		}

		// Strategy 2: Find all CollectionDeckSlotView instances and match by deck name
		Il2CppArrayBase<CollectionDeckSlotView> allSlots = Object.FindObjectsOfType<CollectionDeckSlotView>();
		if (allSlots != null && allSlots.Count > 0)
		{
			// If we have a deck name, try to match
			string targetName = (_collectionIndex >= 0 && _collectionIndex < _collectionCards.Count)
				? _collectionCards[_collectionIndex].Name : null;

			for (int i = 0; i < allSlots.Count; i++)
			{
				var slot = allSlots[i];
				if ((Object)(object)slot == (Object)null || !((Component)slot).gameObject.activeInHierarchy) continue;

				if (!string.IsNullOrEmpty(targetName))
				{
					try
					{
						var deck = slot.Deck;
						if (deck != null)
						{
							string deckName = deck.Name;
							if (!string.IsNullOrEmpty(deckName) &&
								deckName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
								return slot;
						}
					}
					catch { }
				}

				// If only one slot active, use it
				if (allSlots.Count == 1) return slot;
			}

			// Fallback: return first active slot with a deck
			for (int i = 0; i < allSlots.Count; i++)
			{
				var slot = allSlots[i];
				if ((Object)(object)slot == (Object)null || !((Component)slot).gameObject.activeInHierarchy) continue;
				try { if (slot.Deck != null) return slot; } catch { }
			}
		}

		return null;
	}

	/// <summary>
	/// Walks up the parent hierarchy from a button to find its CollectionDeckSlotView.
	/// Works for both populated deck slots and empty "New Deck" slots.
	/// </summary>
	private CollectionDeckSlotView FindDeckSlotViewFromButton(Button btn)
	{
		if ((Object)(object)btn == (Object)null) return null;
		Transform current = ((Component)btn).transform;
		for (int depth = 0; depth < 10 && current != null; depth++)
		{
			var slotView = current.GetComponent<CollectionDeckSlotView>();
			if ((Object)(object)slotView != (Object)null)
				return slotView;
			current = current.parent;
		}
		return null;
	}

	private GameObject FindDeckOptionsPanel()
	{
		// Try known names
		string[] names = { "DeckEditOptions", "DeckOptions", "DeckActionPanel", "DeckContextMenu" };
		foreach (string name in names)
		{
			GameObject go = GameObject.Find(name);
			if (go != null && go.activeInHierarchy) return go;
		}
		// Fallback: look for any active panel with edit/delete buttons
		Il2CppArrayBase<Button> buttons = Object.FindObjectsOfType<Button>();
		if (buttons != null)
		{
			for (int i = 0; i < buttons.Count; i++)
			{
				Button btn = buttons[i];
				if (btn == null || !((Component)btn).gameObject.activeInHierarchy) continue;
				string goName = ((Object)((Component)btn).gameObject).name;
				if (goName.Contains("edit", StringComparison.OrdinalIgnoreCase) ||
					goName.Contains("discard", StringComparison.OrdinalIgnoreCase) ||
					goName.Contains("delete", StringComparison.OrdinalIgnoreCase))
				{
					// Return the parent container
					Transform parent = ((Component)btn).transform.parent;
					if (parent != null) return ((Component)parent).gameObject;
				}
			}
		}
		return null;
	}

	/// <summary>Find a button by searching multiple name patterns.</summary>
	private Button FindDeckActionButton(GameObject panel, params string[] patterns)
	{
		if (panel == null) return null;
		foreach (string pattern in patterns)
		{
			Button btn = UIHelper.FindButtonInChildren(panel.transform, pattern);
			if (btn != null) return btn;
		}
		// Fallback: search all buttons in panel for label match
		Il2CppArrayBase<Button> buttons = panel.GetComponentsInChildren<Button>(true);
		if (buttons != null)
		{
			foreach (Button btn in buttons)
			{
				if (btn == null || !((Component)btn).gameObject.activeInHierarchy) continue;
				string label = UIHelper.GetButtonLabel(btn);
				foreach (string pattern in patterns)
				{
					if (label.Contains(pattern, StringComparison.OrdinalIgnoreCase))
						return btn;
				}
			}
		}
		return null;
	}

	/// <summary>
	/// Navigable deck submenu: Add Cards, View Deck Cards, Copy Code, Delete, Back.
	/// Following AccessibleArena pattern: menu-driven actions instead of key shortcuts.
	/// </summary>
	private void ProcessDeckSubmenu()
	{
		if (_holdRepeater.Check(SDLInput.Key.Up, () => { _deckSubmenuIndex = (_deckSubmenuIndex - 1 + _deckSubmenuItems.Length) % _deckSubmenuItems.Length; AnnounceDeckSubmenuItem(); })) { }
		else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
		{
			_deckSubmenuIndex = (_deckSubmenuIndex - 1 + _deckSubmenuItems.Length) % _deckSubmenuItems.Length;
			AnnounceDeckSubmenuItem();
		}
		else if (_holdRepeater.Check(SDLInput.Key.Down, () => { _deckSubmenuIndex = (_deckSubmenuIndex + 1) % _deckSubmenuItems.Length; AnnounceDeckSubmenuItem(); })) { }
		else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
		{
			_deckSubmenuIndex = (_deckSubmenuIndex + 1) % _deckSubmenuItems.Length;
			AnnounceDeckSubmenuItem();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Home))
		{
			_deckSubmenuIndex = 0;
			AnnounceDeckSubmenuItem();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.End))
		{
			_deckSubmenuIndex = _deckSubmenuItems.Length - 1;
			AnnounceDeckSubmenuItem();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			ExecuteDeckSubmenuAction(_deckSubmenuItems[_deckSubmenuIndex]);
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsKeyDown(SDLInput.Key.Escape) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
		{
			_deckSubmenuMode = false;
			ClearDeckAction();
			AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_cancelled"));
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.H))
		{
			AnnouncementService.Instance.Announce(Loc.Get("deck_sub_help"), AnnouncementPriority.High);
		}
	}

	private void AnnounceDeckSubmenuItem()
	{
		string itemKey = _deckSubmenuItems[_deckSubmenuIndex];
		string label = Loc.Get(itemKey);
		AnnouncementService.Instance.Announce(label + ", " + (_deckSubmenuIndex + 1) + " of " + _deckSubmenuItems.Length);
	}

	private void ExecuteDeckSubmenuAction(string itemKey)
	{
		switch (itemKey)
		{
			case "deck_sub_add":
				// Open deck editor in collection/add cards mode
				_deckSubmenuMode = false;
				OpenDeckForEditing(startInCollection: true);
				break;

			case "deck_sub_view":
				// Open deck editor in deck cards view
				_deckSubmenuMode = false;
				OpenDeckForEditing(startInCollection: false);
				break;

			case "deck_sub_copy":
				// Copy deck code
				_deckSubmenuMode = false;
				CopyDeckCode();
				break;

			case "deck_sub_delete":
				// Delete deck
				_deckSubmenuMode = false;
				DeleteDeck();
				break;

			case "deck_sub_back":
				_deckSubmenuMode = false;
				ClearDeckAction();
				AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_cancelled"));
				break;
		}
	}

	/// <summary>
	/// Opens the deck editor by activating DeckBuilderHandler directly.
	/// The deck edit panel is already visible in landscape collection view —
	/// no need to click anything. Just tell DeckBuilderHandler to take over.
	/// </summary>
	private void OpenDeckForEditing(bool startInCollection)
	{
		try
		{
			if (_deckBuilderHandler == null)
				_deckBuilderHandler = NavigatorManager.Instance.GetNavigator<DeckBuilderHandler>();
			if (_deckBuilderHandler != null)
			{
				_deckBuilderHandler.Activate(startInCollection);
				_deckBuilderActive = true;
				AnnouncementService.Instance.AnnounceInterrupt(
					startInCollection ? Loc.Get("deck_sub_opening_add") : Loc.Get("deck_sub_opening_view"));
				ClearDeckAction();
				return;
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "OpenDeckForEditing failed: " + ex.Message);
		}
		ClearDeckAction();
		AnnouncementService.Instance.Announce(Loc.Get("menu_edit_not_available"));
	}

	private void CopyDeckCode()
	{
		try
		{
			// Try context menu approach first
			CollectionDeckSlotView slotView = ResolveActiveDeckSlot();
			if (slotView != null)
			{
				slotView.OnClick();
				ClearDeckAction();
				MelonCoroutines.Start(FindAndClickCopyButton());
				return;
			}

			// Fallback: click button to open context, then find copy
			if ((Object)(object)_activeDeckButton != (Object)null)
			{
				UIHelper.SimulateMouseClick(((Component)_activeDeckButton).gameObject);
				ClearDeckAction();
				MelonCoroutines.Start(FindAndClickCopyButton());
				return;
			}
		}
		catch { }
		ClearDeckAction();
		AnnouncementService.Instance.Announce(Loc.Get("menu_copy_not_available"));
	}

	private void DeleteDeck()
	{
		try
		{
			int countBefore = _collectionCards.Count;
			string deckName = (_collectionIndex >= 0 && _collectionIndex < _collectionCards.Count)
				? _collectionCards[_collectionIndex].Name : "deck";

			CollectionDeckSlotView slotView = ResolveActiveDeckSlot();
			if (slotView != null)
			{
				slotView.OnClick();
				ClearDeckAction();
				// Click btn_discard, then let DialogHandler handle the confirm dialog
				MelonCoroutines.Start(FindAndClickDeleteButton(countBefore, deckName));
				return;
			}

			if ((Object)(object)_activeDeckButton != (Object)null)
			{
				UIHelper.SimulateMouseClick(((Component)_activeDeckButton).gameObject);
				ClearDeckAction();
				MelonCoroutines.Start(FindAndClickDeleteButton(countBefore, deckName));
				return;
			}
		}
		catch { }
		ClearDeckAction();
		AnnouncementService.Instance.Announce(Loc.Get("menu_delete_not_available"));
	}

	private void ProcessDeckAction()
	{
		if (SDLInput.IsKeyDown(SDLInput.Key.E))
		{
			_deckActionMode = false;
			try
			{
				// Strategy 1: Use stored slot → EnterEditModeForClonedDeck()
				CollectionDeckSlotView slotView = ResolveActiveDeckSlot();
				if (slotView != null)
				{
					slotView.EnterEditModeForClonedDeck();
					AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_editing_deck"));
					DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Edit via EnterEditModeForClonedDeck()");
					ClearDeckAction();
					return;
				}

				// Strategy 2: Click the deck button directly (simulates what a sighted user does)
				if ((Object)(object)_activeDeckButton != (Object)null)
				{
					UIHelper.SimulateMouseClick(((Component)_activeDeckButton).gameObject);
					AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_editing_deck"));
					DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Edit via button SimulateMouseClick");
					ClearDeckAction();
					return;
				}

				// Strategy 3: Click the current collection card button
				if (_collectionIndex >= 0 && _collectionIndex < _collectionCards.Count)
				{
					var card = _collectionCards[_collectionIndex];
					if (card.Button != null)
					{
						UIHelper.SimulateMouseClick(((Component)card.Button).gameObject);
						AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_editing_deck"));
						DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Edit via collection card button");
						ClearDeckAction();
						return;
					}
				}

				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Edit: no slot or button found");
			}
			catch (Exception ex)
			{
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Edit failed: " + ex.Message);
			}
			ClearDeckAction();
			AnnouncementService.Instance.Announce(Loc.Get("menu_edit_not_available"));
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.D))
		{
			_deckActionMode = false;
			try
			{
				// Open context menu for delete: try slot OnClick, fallback to button click
				bool opened = false;
				CollectionDeckSlotView slotView = ResolveActiveDeckSlot();
				if (slotView != null)
				{
					slotView.OnClick();
					opened = true;
				}
				else if ((Object)(object)_activeDeckButton != (Object)null)
				{
					UIHelper.SimulateMouseClick(((Component)_activeDeckButton).gameObject);
					opened = true;
				}
				if (opened)
				{
					int countBefore = _collectionCards.Count;
					string deckName = (_collectionIndex >= 0 && _collectionIndex < _collectionCards.Count)
						? _collectionCards[_collectionIndex].Name : "deck";
					ClearDeckAction();
					MelonCoroutines.Start(FindAndClickDeleteButton(countBefore, deckName));
					return;
				}
			}
			catch { }
			ClearDeckAction();
			AnnouncementService.Instance.Announce(Loc.Get("menu_delete_not_available"));
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.C))
		{
			_deckActionMode = false;
			try
			{
				// Open context menu for copy: try slot OnClick, fallback to button click
				bool opened = false;
				CollectionDeckSlotView slotView = ResolveActiveDeckSlot();
				if (slotView != null)
				{
					slotView.OnClick();
					opened = true;
				}
				else if ((Object)(object)_activeDeckButton != (Object)null)
				{
					UIHelper.SimulateMouseClick(((Component)_activeDeckButton).gameObject);
					opened = true;
				}
				if (opened)
				{
					ClearDeckAction();
					MelonCoroutines.Start(FindAndClickCopyButton());
					return;
				}
			}
			catch { }
			ClearDeckAction();
			AnnouncementService.Instance.Announce(Loc.Get("menu_copy_not_available"));
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.V))
		{
			_deckActionMode = false;
			ClearDeckAction();
			try
			{
				// Paste doesn't need context menu — use DeckCopyPasteComponent directly
				var copyPaste = Object.FindObjectOfType<Il2CppCubeUnity.App.Collection.Landscape.DeckCopyPasteComponent>();
				if (copyPaste != null)
				{
					copyPaste.PasteDeck();
					AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_pasting_code"));
					return;
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Paste deck failed: " + ex.Message);
			}
			AnnouncementService.Instance.Announce(Loc.Get("menu_paste_not_available"));
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.H))
		{
			AnnouncementService.Instance.Announce(Loc.Get("menu_deck_actions"), AnnouncementPriority.High);
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsKeyDown(SDLInput.Key.Escape))
		{
			_deckActionMode = false;
			ClearDeckAction();
			AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_cancelled"));
		}
	}

	/// <summary>
	/// Resolves the active deck slot view using multiple strategies.
	/// AccessibleArena pattern: try stored reference first, then search by button hierarchy, then by name.
	/// </summary>
	private CollectionDeckSlotView ResolveActiveDeckSlot()
	{
		// Strategy 1: Use stored reference if still valid
		if ((Object)(object)_activeDeckSlot != (Object)null)
		{
			try
			{
				if (((Component)_activeDeckSlot).gameObject.activeInHierarchy)
					return _activeDeckSlot;
			}
			catch { }
		}

		// Strategy 2: Walk up from stored button
		if ((Object)(object)_activeDeckButton != (Object)null)
		{
			var slot = FindDeckSlotViewFromButton(_activeDeckButton);
			if (slot != null) return slot;
		}

		// Strategy 3: Match by deck name across all active slot views
		string targetName = (_collectionIndex >= 0 && _collectionIndex < _collectionCards.Count)
			? _collectionCards[_collectionIndex].Name : null;

		Il2CppArrayBase<CollectionDeckSlotView> allSlots = Object.FindObjectsOfType<CollectionDeckSlotView>();
		if (allSlots != null && !string.IsNullOrEmpty(targetName))
		{
			for (int i = 0; i < allSlots.Count; i++)
			{
				var slot = allSlots[i];
				if ((Object)(object)slot == (Object)null || !((Component)slot).gameObject.activeInHierarchy) continue;
				try
				{
					var deck = slot.Deck;
					if (deck != null)
					{
						string deckName = deck.Name;
						if (!string.IsNullOrEmpty(deckName) &&
							deckName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
							return slot;
					}
				}
				catch { }
			}

			// Strategy 4: If only one active slot with a deck exists, use it
			CollectionDeckSlotView singleSlot = null;
			int activeCount = 0;
			for (int i = 0; i < allSlots.Count; i++)
			{
				var slot = allSlots[i];
				if ((Object)(object)slot == (Object)null || !((Component)slot).gameObject.activeInHierarchy) continue;
				try { if (slot.Deck != null) { singleSlot = slot; activeCount++; } } catch { }
			}
			if (activeCount == 1 && singleSlot != null) return singleSlot;
		}

		DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
			$"ResolveActiveDeckSlot: all strategies failed. Target={targetName ?? "null"}, slots={allSlots?.Count ?? 0}");
		return null;
	}

	private void ClearDeckAction()
	{
		_activeDeckSlot = null;
		_activeDeckButton = null;
	}

	private IEnumerator FindAndClickDeleteButton(int countBefore, string deckName)
	{
		// Wait for context menu to appear after OnClick
		for (int attempt = 0; attempt < 4; attempt++)
		{
			yield return new WaitForSeconds(0.3f);
			GameObject panel = FindDeckOptionsPanel();
			if (panel != null)
			{
				Button deleteBtn = FindDeckActionButton(panel, "btn_discard", "btn_delete", "Delete", "Discard", "delete");
				if (deleteBtn != null)
				{
					UIHelper.ActivateButton(((Component)deleteBtn).gameObject);
					// DialogHandler will handle the confirm dialog ("Do it!" / "Cancel")
					// Start rescan after enough time for user to confirm and game to process
					MelonCoroutines.Start(RescanAfterDeleteConfirm(countBefore, deckName));
					yield break;
				}
			}
		}
		AnnouncementService.Instance.Announce(Loc.Get("menu_delete_not_available"));
	}

	private IEnumerator FindAndClickCopyButton()
	{
		// Wait for context menu to appear after OnClick
		for (int attempt = 0; attempt < 4; attempt++)
		{
			yield return new WaitForSeconds(0.3f);
			GameObject panel = FindDeckOptionsPanel();
			if (panel != null)
			{
				Button copyBtn = FindDeckActionButton(panel, "btn_copy", "Copy", "copy");
				if (copyBtn != null)
				{
					UIHelper.ActivateButton(((Component)copyBtn).gameObject);
					AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_code_copied"));
					yield break;
				}
			}
		}
		AnnouncementService.Instance.Announce(Loc.Get("menu_copy_not_available"));
	}

	/// <summary>
	/// Waits for the user to confirm deletion via DialogHandler,
	/// then rescans the collection to remove the deleted deck.
	/// </summary>
	private IEnumerator RescanAfterDeleteConfirm(int countBefore, string deckName)
	{
		// Wait for the confirm dialog to appear and be dismissed by DialogHandler
		// The dialog has "Do it!" and "Cancel" buttons — DialogHandler handles navigation
		// We poll until the dialog closes (modal overlay goes away)
		float waited = 0f;
		float maxWait = 30f; // Give user up to 30 seconds to confirm
		bool dialogAppeared = false;

		while (waited < maxWait)
		{
			yield return new WaitForSeconds(0.5f);
			waited += 0.5f;

			// Check if confirm dialog is visible
			bool hasDialog = HasActiveModalOverlay();
			if (hasDialog) dialogAppeared = true;

			// Dialog appeared and is now gone — user confirmed or cancelled
			if (dialogAppeared && !hasDialog)
			{
				// Wait for game to process the action
				yield return new WaitForSeconds(1.0f);
				break;
			}
		}

		// Rescan the collection
		MelonCoroutines.Start(RescanCollectionAfterDelete(countBefore, deckName));
	}

	private IEnumerator RescanCollectionAfterDelete(int countBefore, string deletedName)
	{
		// Wait for game to process the deletion and refresh UI
		// Try multiple times with increasing delays
		for (int attempt = 0; attempt < 5; attempt++)
		{
			yield return new WaitForSeconds(attempt == 0 ? 1.0f : 1.5f);

			_collectionCards.Clear();
			ScanCollectionCardsInSection(_collectionSectionIndex);

			// Check if deleted deck name is gone from the list
			bool stillPresent = false;
			foreach (var card in _collectionCards)
			{
				if (card.Name.Equals(deletedName, StringComparison.OrdinalIgnoreCase))
				{
					stillPresent = true;
					break;
				}
			}

			if (!stillPresent || _collectionCards.Count < countBefore)
			{
				if (_collectionIndex >= _collectionCards.Count)
					_collectionIndex = Math.Max(0, _collectionCards.Count - 1);
				AnnouncementService.Instance.Announce(
					Loc.Get("menu_deck_deleted_count", deletedName, _collectionCards.Count), AnnouncementPriority.High);
				yield break;
			}
		}

		// Fallback: announce deletion even if UI hasn't updated
		if (_collectionIndex >= _collectionCards.Count)
			_collectionIndex = Math.Max(0, _collectionCards.Count - 1);
		AnnouncementService.Instance.Announce(
			Loc.Get("menu_deck_deleted_count", deletedName, Math.Max(0, countBefore - 1)), AnnouncementPriority.High);
	}

	private IEnumerator RescanCollectionDelayed()
	{
		yield return new WaitForSeconds(0.8f);
		ScanCollectionCardsInSection(_collectionSectionIndex);
		_collectionIndex = 0;
		if (_collectionCards.Count > 0)
			AnnouncementService.Instance.Announce(_collectionCards[_collectionIndex].Name + ", 1 of " + _collectionCards.Count);
	}

	private void ReadCollectionCardInfo()
	{
		if (_collectionIndex < 0 || _collectionIndex >= _collectionCards.Count) return;
		var card = _collectionCards[_collectionIndex];
		try
		{
			if (card.Renderer != null)
			{
				if (_collectionDetailLevel == 0)
				{
					var costView = card.Renderer._CostValueView;
					if ((Object)(object)costView != (Object)null)
						AnnouncementService.Instance.Announce(Loc.Get("menu_cost", costView.Value), AnnouncementPriority.Low);
					else
						AnnouncementService.Instance.Announce(Loc.Get("menu_cost_unknown"), AnnouncementPriority.Low);
				}
				else if (_collectionDetailLevel == 1)
				{
					var powerView = card.Renderer._PowerValueView;
					if ((Object)(object)powerView != (Object)null)
						AnnouncementService.Instance.Announce(Loc.Get("menu_power", powerView.Value), AnnouncementPriority.Low);
					else
						AnnouncementService.Instance.Announce(Loc.Get("menu_power_unknown"), AnnouncementPriority.Low);
				}
				else
				{
					string ability = GetCollectionCardAbility(card.Renderer);
					if (!string.IsNullOrEmpty(ability))
						AnnouncementService.Instance.Announce(ability, AnnouncementPriority.Low);
					else
						AnnouncementService.Instance.Announce(Loc.Get("menu_no_ability"), AnnouncementPriority.Low);
				}
				_collectionDetailLevel = (_collectionDetailLevel + 1) % 3;
			}
			else
			{
				AnnouncementService.Instance.Announce(card.Name);
			}
		}
		catch { AnnouncementService.Instance.Announce(card.Name); }
	}

	/// <summary>Extracts ability/description text from a CardRenderer in the collection view.</summary>
	private string GetCollectionCardAbility(CardRenderer renderer)
	{
		// Try 1: Direct _AbilityText field
		try
		{
			TMP_Text abilityTmp = renderer._AbilityText;
			if ((Object)(object)abilityTmp != (Object)null)
			{
				string raw = abilityTmp.text;
				if (!string.IsNullOrWhiteSpace(raw) && !raw.Contains("Missing Entry"))
				{
					string cleaned = UIHelper.StripRichText(raw.Trim());
					if (cleaned.Length > 3) return cleaned;
				}
			}
		}
		catch { }

		// Try 2: Search child TMP_Text named "Ability Text"
		try
		{
			Il2CppArrayBase<TMP_Text> texts = ((Component)renderer).GetComponentsInChildren<TMP_Text>(true);
			if (texts != null)
			{
				for (int i = 0; i < texts.Count; i++)
				{
					TMP_Text tmp = texts[i];
					if ((Object)(object)tmp == (Object)null) continue;
					string goName = ((Object)((Component)tmp).gameObject).name;
					if (!goName.Contains("Ability", StringComparison.OrdinalIgnoreCase)) continue;
					string text = tmp.text;
					if (string.IsNullOrWhiteSpace(text) || text.Contains("Missing Entry")) continue;
					string cleaned = UIHelper.StripRichText(text.Trim());
					if (cleaned.Length > 3) return cleaned;
				}
			}
		}
		catch { }
		return null;
	}

	/// <summary>A-Z letter jump in collection card list.</summary>
	private bool TryLetterJumpCollection()
	{
		if (_collectionCards.Count == 0) return false;
		for (int i = 0; i < 26; i++)
		{
			SDLInput.Key key = (SDLInput.Key)(65 + i);
			if (!SDLInput.IsKeyDown(key)) continue;
			char letter = (char)('A' + i);
			for (int j = 0; j < _collectionCards.Count; j++)
			{
				int idx = (_collectionIndex + 1 + j) % _collectionCards.Count;
				string name = _collectionCards[idx].Name;
				if (!string.IsNullOrEmpty(name) && char.ToUpper(name[0]) == letter)
				{
					_collectionIndex = idx;
					AnnounceCollectionCard();
					return true;
				}
			}
			AnnouncementService.Instance.Announce(Loc.Get("menu_collection_no_letter", letter.ToString()));
			return true;
		}
		return false;
	}

	/// <summary>Counts total cards across all collection sections and announces stats.</summary>
	private void AnnounceCollectionStats()
	{
		try
		{
			int totalItems = 0;
			int sectionCount = _collectionSections.Count;
			// Count items in each section
			for (int s = 0; s < sectionCount; s++)
			{
				ScanCollectionCardsInSection(s);
				totalItems += _collectionCards.Count;
			}
			// Restore current section scan
			if (_collectionSectionIndex >= 0 && _collectionSectionIndex < sectionCount)
				ScanCollectionCardsInSection(_collectionSectionIndex);

			AnnouncementService.Instance.Announce(
				Loc.Get("collection_stats", sectionCount.ToString(), totalItems.ToString()),
				AnnouncementPriority.Normal);
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", $"CollectionStats error: {ex.Message}");
		}
	}

	private void AnnounceCollectionCard()
	{
		if (_collectionCards.Count == 0) return;
		_collectionDetailLevel = 0;
		var card = _collectionCards[_collectionIndex];
		// For deck section items, add "Deck:" prefix for clarity
		string isDeckSlot = card.Button != null && card.Button.gameObject.name == "DeckSlotCell" ? "Deck: " : "";
		string suffix = card.Name == "New Deck" ? ". Press Enter to create." : "";
		AnnouncementService.Instance.Announce(isDeckSlot + card.Name + suffix + ", " + (_collectionIndex + 1) + " of " + _collectionCards.Count);
	}

	private void ScanCollectionSections()
	{
		_collectionSections.Clear();
		// Cards tab uses CollectionContentLayoutGroup, Albums tab uses CollectionAlbumContentLayoutGroup
		bool isAlbumsTab = _collectionTabIndex == 1;
		GameObject rootObj = isAlbumsTab
			? GameObject.Find("CollectionAlbumContentLayoutGroup")
			: GameObject.Find("CollectionContentLayoutGroup");
		if (rootObj == null) return;
		Transform root = rootObj.transform;

		// Albums tab has a flat layout — treat as a single browsable section
		if (isAlbumsTab)
		{
			// Check if there are any active children with buttons
			Il2CppArrayBase<Button> albumBtns = rootObj.GetComponentsInChildren<Button>(false);
			if (albumBtns != null && albumBtns.Length > 0)
				_collectionSections.Add("Albums");
			else
				_collectionSections.Add("Albums");
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
				$"ScanCollectionSections: Albums tab, {(albumBtns != null ? albumBtns.Length : 0)} buttons");
			return;
		}

		for (int i = 0; i < root.childCount; i++)
		{
			var child = root.GetChild(i);
			if (!child.gameObject.activeInHierarchy) continue;
			string childName = child.name;

			// Skip DeckEditSectionContainer — it's the deck editor, not a browsable category
			if (childName.Contains("DeckEdit")) continue;
			// Skip non-content layout children (spacers, dividers, etc.)
			if (childName.StartsWith("Spacer") || childName.StartsWith("Divider")) continue;

			// Accept both "SectionContainer" children and other active content children
			// that may contain buttons or cards (Avatars, Titles, CardBacks, etc.)
			bool isSection = childName.Contains("Section") || childName.Contains("Container");
			if (!isSection)
			{
				// Check if this child has any buttons — if so, it's likely a browsable section
				Il2CppArrayBase<Button> childBtns = child.GetComponentsInChildren<Button>(false);
				if (childBtns == null || childBtns.Length == 0) continue;
			}

			// Clean up the display name
			string name = childName
				.Replace("SectionContainer", "")
				.Replace("Section", "")
				.Replace("Container", "")
				.Replace("(Clone)", "")
				.Trim();

			// Try to read a header label from the section for a better display name
			try
			{
				Transform header = UIHelper.FindChildByName(child, "Text_Header");
				if ((Object)(object)header == (Object)null)
					header = UIHelper.FindChildByName(child, "text_Header");
				if ((Object)(object)header == (Object)null)
					header = UIHelper.FindChildByName(child, "Header");
				if ((Object)(object)header != (Object)null)
				{
					var headerTmp = header.GetComponent<TMP_Text>();
					if ((Object)(object)headerTmp != (Object)null)
					{
						string headerText = UIHelper.StripRichText(headerTmp.text);
						if (!string.IsNullOrEmpty(headerText) && headerText.Length > 1 && !headerText.Contains("{Missing"))
							name = headerText;
					}
				}
			}
			catch { }

			if (string.IsNullOrEmpty(name) || name.Length < 2) name = "Items";
			_collectionSections.Add(name);
		}
		DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
			$"ScanCollectionSections: found {_collectionSections.Count} categories: {string.Join(", ", _collectionSections)}");
	}

	private void ScanCollectionCardsInSection(int sectionIdx)
	{
		_collectionCards.Clear();

		// Albums tab: scan for AlbumCardView objects directly
		if (_collectionTabIndex == 1)
		{
			ScanAlbumCards();
			return;
		}

		GameObject rootObj = GameObject.Find("CollectionContentLayoutGroup");
		if (rootObj == null) return;
		Transform root = rootObj.transform;

		Transform section = null;
		int sCount = 0;
		for (int i = 0; i < root.childCount; i++)
		{
			Transform child = root.GetChild(i);
			if (!child.gameObject.activeInHierarchy) continue;
			string childName = child.name;
			if (childName.Contains("DeckEdit")) continue;
			if (childName.StartsWith("Spacer") || childName.StartsWith("Divider")) continue;

			bool isSection = childName.Contains("Section") || childName.Contains("Container");
			if (!isSection)
			{
				Il2CppArrayBase<Button> childBtns = child.GetComponentsInChildren<Button>(false);
				if (childBtns == null || childBtns.Length == 0) continue;
			}

			if (sCount == sectionIdx) { section = child; break; }
			sCount++;
		}
		if (section == null) return;

		// Determine if this is a Deck section (only DeckSlotCell buttons should appear)
		bool isDeckSection = section.name.Contains("Deck");

		// Track seen names to avoid duplicates from pooled objects
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		Il2CppArrayBase<Button> btns = section.GetComponentsInChildren<Button>(true);
		foreach (var b in btns)
		{
			if (!b.gameObject.activeInHierarchy) continue;
			if (b.name.Contains("Mastery")) continue;
			if (b.name.Contains("filter_")) continue;
			if (b.name.Contains("icon_")) continue;

			// In Deck sections, only include DeckSlotCell buttons (not card views or editor buttons)
			if (isDeckSection && b.gameObject.name != "DeckSlotCell") continue;

			// For card buttons: try CardRenderer.CardName first (the actual card name)
			string label = "";
			CardRenderer foundRenderer = null;
			if (!isDeckSection)
			{
				try
				{
					var cardRenderer = ((Component)b).GetComponentInChildren<CardRenderer>(true);
					if (cardRenderer != null)
					{
						string cardName = cardRenderer.CardName;
						if (!string.IsNullOrEmpty(cardName) && cardName.Length >= 2)
						{
							label = cardName;
							foundRenderer = cardRenderer;
						}
					}
				}
				catch { }
			}

			// For deck slots: search for Text_Name TMP_Text specifically
			if (string.IsNullOrEmpty(label) && b.gameObject.name == "DeckSlotCell")
			{
				try
				{
					Transform textNameTf = UIHelper.FindChildByName(((Component)b).transform, "Text_Name");
					if ((Object)(object)textNameTf != (Object)null)
					{
						var tmpText = ((Component)textNameTf).GetComponent<TMP_Text>();
						if ((Object)(object)tmpText != (Object)null && !string.IsNullOrWhiteSpace(tmpText.text))
							label = UIHelper.StripRichText(tmpText.text.Trim());
					}
				}
				catch { }
			}

			// For other buttons: fall back to TMP_Text / GetButtonLabel
			if (string.IsNullOrEmpty(label))
				label = UIHelper.GetButtonLabel(b);

			if (string.IsNullOrEmpty(label) || label.Length < 2) continue;
			if (label.Contains("Gridsize") || label.Contains("{Missing")) continue;
			if (label == "Landscape Collection Card View" || label == "LandscapeCollectionCardView") continue;

			// Rename empty deck slot to "New Deck" for clarity
			if (label == "Deck Slot Cell" || label == "DeckSlotCell"
				|| label.Equals("Deck name", StringComparison.OrdinalIgnoreCase)
				|| label.Equals("Deck Name\u200B", StringComparison.OrdinalIgnoreCase))
				label = "New Deck";

			// Skip duplicates (pooled cards can appear multiple times)
			if (seen.Contains(label)) continue;
			seen.Add(label);

			_collectionCards.Add(new CollectionCard { Name = label, Button = b, Renderer = foundRenderer, IsDeckSlot = (b.gameObject.name == "DeckSlotCell") });
		}
	}

	/// <summary>Scans album card views for the Albums tab in collection.</summary>
	private void ScanAlbumCards()
	{
		_collectionCards.Clear();
		try
		{
			// Albums are rendered via AlbumCardView objects pooled under ObjectPoolManager.
			// They have Button components we can click, and may have TMP_Text children with album/card names.
			GameObject albumPool = GameObject.Find("ObjectPool_AlbumCardView");
			if (albumPool == null)
			{
				// Fallback: scan CollectionAlbumContentLayoutGroup for buttons
				GameObject albumRoot = GameObject.Find("CollectionAlbumContentLayoutGroup");
				if (albumRoot == null) return;
				Il2CppArrayBase<Button> btns = albumRoot.GetComponentsInChildren<Button>(false);
				if (btns == null) return;
				HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (Button b in btns)
				{
					if (!b.gameObject.activeInHierarchy) continue;
					string label = UIHelper.GetButtonLabel(b);
					if (string.IsNullOrEmpty(label) || label.Length < 2 || label.Contains("{Missing")) continue;
					if (seen.Contains(label)) continue;
					seen.Add(label);
					_collectionCards.Add(new CollectionCard { Name = label, Button = b });
				}
				return;
			}

			Transform pool = albumPool.transform;
			HashSet<string> seenAlbums = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < pool.childCount; i++)
			{
				Transform albumGo = pool.GetChild(i);
				if (!albumGo.gameObject.activeInHierarchy) continue;

				// Try to get a meaningful name from the album card
				string albumName = "";

				// Try CardRenderer first
				try
				{
					var cardRenderer = albumGo.GetComponentInChildren<CardRenderer>(true);
					if (cardRenderer != null)
					{
						string cn = cardRenderer.CardName;
						if (!string.IsNullOrEmpty(cn) && cn.Length >= 2 && !cn.Contains("{Missing"))
							albumName = cn;
					}
				}
				catch { }

				// Try TMP_Text children for album name
				if (string.IsNullOrEmpty(albumName))
				{
					try
					{
						var texts = albumGo.GetComponentsInChildren<TMP_Text>(false);
						if (texts != null)
						{
							for (int t = 0; t < texts.Count; t++)
							{
								TMP_Text tmp = texts[t];
								if ((Object)(object)tmp == (Object)null) continue;
								string text = UIHelper.StripRichText(tmp.text);
								if (!string.IsNullOrEmpty(text) && text.Length >= 2
									&& !text.Contains("{Missing") && !text.Contains("(Clone)"))
								{
									albumName = text;
									break;
								}
							}
						}
					}
					catch { }
				}

				// Fall back to cleaned GO name
				if (string.IsNullOrEmpty(albumName))
					albumName = UIHelper.CleanGameObjectName(albumGo.name);

				if (string.IsNullOrEmpty(albumName) || albumName.Length < 2) continue;
				if (seenAlbums.Contains(albumName)) continue;
				seenAlbums.Add(albumName);

				Button btn = albumGo.GetComponentInChildren<Button>(false);
				_collectionCards.Add(new CollectionCard { Name = albumName, Button = btn });
			}
		}
		catch (System.Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", $"ScanAlbumCards failed: {ex.Message}");
		}
		DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", $"ScanAlbumCards: found {_collectionCards.Count} albums");
	}

	// --- Rewards ---

	private void ScanRewards()
	{
		_rewardEvents.Clear();
		try
		{
			// CarouselStagingArea is instantiated as a clone — search by partial name
			GameObject carousel = null;
			Transform playScreen = GameObject.Find("PlayScreenLandscape(Clone)")?.transform;
			if (playScreen != null)
				carousel = UIHelper.FindChildByName(playScreen, "CarouselStagingArea(Clone)")?.gameObject;
			if (carousel == null)
			{
				foreach (var go in Object.FindObjectsOfType<GameObject>())
				{
					if (go.name.StartsWith("CarouselStagingArea") && go.activeInHierarchy) { carousel = go; break; }
				}
			}
			if (carousel == null) return;

			// Find all Cells in the carousel and scan each as an event
			Transform scrollContainer = UIHelper.FindChildByName(carousel.transform, "Container");
			if ((Object)(object)scrollContainer == (Object)null) scrollContainer = carousel.transform;

			for (int i = 0; i < scrollContainer.childCount; i++)
			{
				Transform cell = scrollContainer.GetChild(i);
				if (!cell.gameObject.name.StartsWith("Cell")) continue;
				if (!cell.gameObject.activeInHierarchy) continue;

				// Only process Login Rewards cells (skip Tips, Vault promos, etc.)
				bool isLoginReward = false;
				Button seeAllBtn = null;
				Il2CppArrayBase<Button> cellButtons = cell.GetComponentsInChildren<Button>(true);
				foreach (var btn in cellButtons)
				{
					if (btn != null && ((Object)btn.gameObject).name == "btn_SeeAllRewards")
					{
						seeAllBtn = btn;
						isLoginReward = true;
						break;
					}
				}
				if (!isLoginReward) continue;

				var evt = new RewardEvent { SeeAllButton = seeAllBtn };

				// Read event name from text_Header
				evt.EventName = GetEventName(cell);
				if (string.IsNullOrEmpty(evt.EventName)) evt.EventName = "Login Rewards";

				// Read event end time
				Transform headerSection = UIHelper.FindChildByName(cell, "HeaderSection");
				if ((Object)(object)headerSection != (Object)null)
				{
					Transform countdown = UIHelper.FindChildByName(headerSection, "text_countdown");
					if ((Object)(object)countdown != (Object)null)
					{
						var tmp = countdown.GetComponent<TMP_Text>();
						if ((Object)(object)tmp != (Object)null)
						{
							string val = UIHelper.StripRichText(tmp.text);
							if (!string.IsNullOrEmpty(val) && !val.Contains("--"))
								evt.EventEndTime = val;
						}
					}
				}

				// Read next reward info (NextRewardDay section)
				Transform nextRewardSlot = UIHelper.FindChildByName(cell, "NextRewardDay");
				if ((Object)(object)nextRewardSlot != (Object)null)
				{
					ReadRewardSlot(nextRewardSlot, out evt.NextRewardDay, out evt.NextRewardCountdown, out evt.ExtraInfo);
				}

				// Read final reward info (FinalRewardDay section)
				Transform finalRewardSlot = UIHelper.FindChildByName(cell, "FinalRewardDay");
				if ((Object)(object)finalRewardSlot != (Object)null)
				{
					string dummy1, dummy2;
					ReadRewardSlot(finalRewardSlot, out evt.FinalRewardDay, out dummy1, out dummy2);
				}

				_rewardEvents.Add(evt);
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenu", "ScanRewards failed: " + ex.Message);
		}
	}

	private void ReadRewardSlot(Transform slot, out string day, out string countdown, out string extra)
	{
		day = "";
		countdown = "";
		extra = "";
		try
		{
			Il2CppArrayBase<TMP_Text> texts = slot.GetComponentsInChildren<TMP_Text>(true);
			foreach (var t in texts)
			{
				if (t == null) continue;
				string goName = ((Object)((Component)t).gameObject).name;
				string val = UIHelper.StripRichText(t.text);
				if (string.IsNullOrEmpty(val) || val.Contains("{Missing")) continue;

				if (goName.Contains("text_Day")) day = val;
				else if (goName.Contains("text_countdown"))
				{
					// "0m 0s" / "0h 0m" / "--h --m" = ready now
					if (!val.Contains("0m 0s") && !val.Contains("0h 0m") && !val.Contains("--"))
						countdown = val;
				}
				else if (goName == "Text" && val.Length > 3 && !val.Contains("Reward"))
					extra = val; // e.g. "20 Ant Man Boosters"
			}

			// Also try to find reward description from BaseReward/TextContainer
			Transform textContainer = UIHelper.FindChildByName(slot, "TextContainer");
			if ((Object)(object)textContainer != (Object)null)
			{
				var tmp = textContainer.GetComponentInChildren<TMP_Text>(true);
				if ((Object)(object)tmp != (Object)null)
				{
					string val = UIHelper.StripRichText(tmp.text);
					if (!string.IsNullOrEmpty(val) && val.Length > 2)
						extra = val;
				}
			}
		}
		catch { }
	}

	/// <summary>Extracts a human-readable event name from a carousel Cell's children.</summary>
	private string GetEventName(Transform cell)
	{
		try
		{
			// Look for a Promo child whose name tells us the event type
			for (int i = 0; i < cell.childCount; i++)
			{
				string childName = ((Object)cell.GetChild(i).gameObject).name;
				if (childName.Contains("LoginBonus") || childName.Contains("LoginRewards"))
				{
					// Try to read the header text
					Transform header = UIHelper.FindChildByName(cell, "text_Header");
					if ((Object)(object)header != (Object)null)
					{
						var tmp = ((Component)header).GetComponent<TMP_Text>();
						if ((Object)(object)tmp != (Object)null && !string.IsNullOrWhiteSpace(tmp.text))
							return UIHelper.StripRichText(tmp.text.Trim());
					}
					return "Login Rewards";
				}
				if (childName.Contains("NewVariant")) return "New Variant";
				if (childName.Contains("Tips")) return "Tips";
				if (childName.Contains("CardsShop")) return "Card Shop";
			}
			// Deeper search for Promo_LoginRewards
			var promos = cell.GetComponentsInChildren<Transform>(true);
			foreach (var p in promos)
			{
				string pName = ((Object)p.gameObject).name;
				if (pName.StartsWith("Promo_LoginRewards"))
				{
					Transform header = UIHelper.FindChildByName(p, "text_Header");
					if ((Object)(object)header != (Object)null)
					{
						var tmp = ((Component)header).GetComponent<TMP_Text>();
						if ((Object)(object)tmp != (Object)null && !string.IsNullOrWhiteSpace(tmp.text))
							return UIHelper.StripRichText(tmp.text.Trim());
					}
					return "Login Rewards";
				}
			}
		}
		catch { }
		return "";
	}

	private void ProcessRewardsInput()
	{
		if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
		{
			if (_rewardEvents.Count == 0) return;
			_rewardIndex = (_rewardIndex - 1 + _rewardEvents.Count) % _rewardEvents.Count;
			AnnounceReward();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
		{
			if (_rewardEvents.Count == 0) return;
			_rewardIndex = (_rewardIndex + 1) % _rewardEvents.Count;
			AnnounceReward();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Home))
		{
			if (_rewardEvents.Count > 0) { _rewardIndex = 0; AnnounceReward(); }
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.End))
		{
			if (_rewardEvents.Count > 0) { _rewardIndex = _rewardEvents.Count - 1; AnnounceReward(); }
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			if (_rewardIndex >= 0 && _rewardIndex < _rewardEvents.Count)
			{
				var evt = _rewardEvents[_rewardIndex];
				var details = new List<string>();
				if (!string.IsNullOrEmpty(evt.NextRewardDay))
				{
					string next = Loc.Get("rw_next_reward") + evt.NextRewardDay;
					if (!string.IsNullOrEmpty(evt.NextRewardCountdown))
						next += " " + Loc.Get("rw_in_time", evt.NextRewardCountdown);
					else
						next += ", " + Loc.Get("rw_available_now");
					details.Add(next);
				}
				if (!string.IsNullOrEmpty(evt.ExtraInfo))
					details.Add(evt.ExtraInfo);
				if (!string.IsNullOrEmpty(evt.FinalRewardDay))
					details.Add(Loc.Get("rw_final_reward") + evt.FinalRewardDay);
				if (!string.IsNullOrEmpty(evt.EventEndTime))
					details.Add(Loc.Get("rw_ends_in", evt.EventEndTime));
				if (evt.SeeAllButton != null)
					details.Add(Loc.Get("rw_enter_to_see_all"));
				AnnouncementService.Instance.Announce(
					details.Count > 0 ? string.Join(". ", details) : Loc.Get("rw_no_details"));
			}
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.H))
		{
			AnnouncementService.Instance.Announce(Loc.Get("rw_help"), AnnouncementPriority.High);
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			if (_rewardIndex < 0 || _rewardIndex >= _rewardEvents.Count) return;
			var evt = _rewardEvents[_rewardIndex];
			if (evt.SeeAllButton != null)
			{
				AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_opening_rewards", evt.EventName));
				UIHelper.ActivateButton(evt.SeeAllButton);
				MelonCoroutines.Start(EnterRewardDetailsDelayed());
			}
			else
			{
				AnnouncementService.Instance.Announce(Loc.Get("menu_no_details"));
			}
		}
		// Backspace handled in HandleBackCommand
	}

	private IEnumerator EnterRewardDetailsDelayed()
	{
		// Wait for LoginBonusSubSceneContainer to appear
		for (int attempt = 0; attempt < 5; attempt++)
		{
			yield return new WaitForSeconds(0.4f);
			ScanRewardDetails();
			if (_rewardDays.Count > 0) break;
		}
		if (_rewardDays.Count > 0)
		{
			_browsingRewards = false;
			_browsingRewardDetails = true;

			// Auto-focus on first claimable day (ready now)
			_rewardDayIndex = 0;
			for (int i = 0; i < _rewardDays.Count; i++)
			{
				if (_rewardDays[i].IsClaimable && string.IsNullOrEmpty(_rewardDays[i].Countdown))
				{
					_rewardDayIndex = i;
					break;
				}
			}

			// Count claimable
			int claimable = 0;
			for (int i = 0; i < _rewardDays.Count; i++)
				if (_rewardDays[i].IsClaimable && string.IsNullOrEmpty(_rewardDays[i].Countdown))
					claimable++;

			string intro = Loc.Get("rw_details_intro", _rewardDays.Count.ToString());
			if (claimable > 0)
				intro += " " + Loc.Get("rw_claimable_count", claimable.ToString());
			AnnouncementService.Instance.Announce(intro, AnnouncementPriority.High);
			AnnounceRewardDay();
		}
		else
		{
			AnnouncementService.Instance.Announce(Loc.Get("menu_reward_load_failed"));
		}
	}

	private void AnnounceReward()
	{
		if (_rewardIndex < 0 || _rewardIndex >= _rewardEvents.Count) return;
		var evt = _rewardEvents[_rewardIndex];
		string announcement = evt.EventName;
		if (!string.IsNullOrEmpty(evt.NextRewardDay))
		{
			announcement += ", " + Loc.Get("rw_next_reward") + evt.NextRewardDay;
			if (string.IsNullOrEmpty(evt.NextRewardCountdown))
				announcement += " (" + Loc.Get("rw_status_claimable") + ")";
			else
				announcement += " " + Loc.Get("rw_in_time", evt.NextRewardCountdown);
		}
		announcement += ", " + (_rewardIndex + 1) + " of " + _rewardEvents.Count;
		AnnouncementService.Instance.Announce(announcement);
	}

	// --- Reward Detail Screen (LoginBonusSubSceneContainer) ---

	private void ScanRewardDetails()
	{
		_rewardDays.Clear();
		try
		{
			GameObject loginBonus = GameObject.Find("LoginBonusSubSceneContainer(Clone)");
			if (loginBonus == null || !loginBonus.activeInHierarchy) return;

			// Find the scroll container with Cells
			Transform viewport = UIHelper.FindChildByName(loginBonus.transform, "Container");
			if ((Object)(object)viewport == (Object)null) return;

			// Each Cell contains multiple LoginDailyRewardSlot items
			// We process Cells in reverse order (Cell 0 = latest days) to show chronological order
			var cellList = new List<Transform>();
			for (int i = 0; i < viewport.childCount; i++)
			{
				Transform cell = viewport.GetChild(i);
				if (cell.gameObject.name.StartsWith("Cell") && cell.gameObject.activeInHierarchy)
					cellList.Add(cell);
			}

			// Process cells in reverse so Day 1 comes first
			for (int c = cellList.Count - 1; c >= 0; c--)
			{
				Transform cell = cellList[c];
				Il2CppArrayBase<Button> slotButtons = cell.GetComponentsInChildren<Button>(true);
				// Collect slots in this cell, then reverse for chronological order
				var cellDays = new List<RewardDay>();
				foreach (var btn in slotButtons)
				{
					if (btn == null || !btn.gameObject.activeInHierarchy) continue;
					if (btn.gameObject.name != "ClaimButton") continue;

					var day = new RewardDay { ClaimButton = btn };

					// Read text children
					Il2CppArrayBase<TMP_Text> texts = ((Component)btn).GetComponentsInChildren<TMP_Text>(true);
					foreach (var t in texts)
					{
						if (t == null) continue;
						string goName = ((Object)((Component)t).gameObject).name;
						string val = UIHelper.StripRichText(t.text);
						if (string.IsNullOrEmpty(val) || val.Contains("{Missing")) continue;

						if (goName.Contains("text_Day"))
							day.Day = val;
						else if (goName.Contains("text_Reward"))
							day.Reward = val;
						else if (goName.Contains("text_countdown"))
						{
							if (!val.Contains("0m 0s") && !val.Contains("0h 0m") && !val.Contains("--"))
								day.Countdown = val;
						}
						else if (goName == "Title")
							day.Reward = val; // Title names like "Thou Shalt Not Retreat"
						else if (goName == "PackText" && string.IsNullOrEmpty(day.Reward))
							day.Reward = val + " Pack";
						else if (goName.Contains("FinalReward") || goName.Contains("NextReward"))
						{
							// Skip these label texts
						}
					}

					// Check if this slot is claimable via the button label
					string btnLabel = UIHelper.GetButtonLabel(btn);
					if (!string.IsNullOrEmpty(btnLabel) && btnLabel.StartsWith("Claim"))
						day.IsClaimable = true;

					// Only add if we found a day number
					if (!string.IsNullOrEmpty(day.Day))
						cellDays.Add(day);
				}

				// Reverse within cell so days go from lowest to highest
				cellDays.Reverse();
				_rewardDays.AddRange(cellDays);
			}

			DebugLogger.Log(LogCategory.Handler, "MainMenu", "ScanRewardDetails: found " + _rewardDays.Count + " daily rewards");
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenu", "ScanRewardDetails failed: " + ex.Message);
		}
	}

	private void ProcessRewardDetailsInput()
	{
		// Check if LoginBonusSubSceneContainer is still visible
		GameObject loginBonus = GameObject.Find("LoginBonusSubSceneContainer(Clone)");
		if (loginBonus == null || !loginBonus.activeInHierarchy)
		{
			_browsingRewardDetails = false;
			_rewardDays.Clear();
			_browsingRewards = true;
			AnnouncementService.Instance.Announce(Loc.Get("menu_reward_details_closed"), AnnouncementPriority.High);
			AnnounceReward();
			return;
		}

		if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
		{
			if (_rewardDays.Count == 0) return;
			_rewardDayIndex = (_rewardDayIndex - 1 + _rewardDays.Count) % _rewardDays.Count;
			AnnounceRewardDay();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
		{
			if (_rewardDays.Count == 0) return;
			_rewardDayIndex = (_rewardDayIndex + 1) % _rewardDays.Count;
			AnnounceRewardDay();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Home))
		{
			if (_rewardDays.Count > 0) { _rewardDayIndex = 0; AnnounceRewardDay(); }
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.End))
		{
			if (_rewardDays.Count > 0) { _rewardDayIndex = _rewardDays.Count - 1; AnnounceRewardDay(); }
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			if (_rewardDayIndex >= 0 && _rewardDayIndex < _rewardDays.Count)
			{
				var d = _rewardDays[_rewardDayIndex];
				var details = new List<string>();
				details.Add(d.Day);
				if (!string.IsNullOrEmpty(d.Reward))
					details.Add(Loc.Get("rw_reward_label") + d.Reward);
				if (d.IsClaimable)
				{
					if (!string.IsNullOrEmpty(d.Countdown))
						details.Add(Loc.Get("rw_available_in", d.Countdown));
					else
						details.Add(Loc.Get("rw_claim_now"));
				}
				else
				{
					details.Add(Loc.Get("rw_already_claimed"));
				}
				AnnouncementService.Instance.Announce(string.Join(". ", details));
			}
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.H))
		{
			AnnouncementService.Instance.Announce(Loc.Get("rw_details_help"), AnnouncementPriority.High);
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			if (_rewardDayIndex < 0 || _rewardDayIndex >= _rewardDays.Count) return;
			var d = _rewardDays[_rewardDayIndex];
			if (d.IsClaimable && string.IsNullOrEmpty(d.Countdown) && d.ClaimButton != null)
			{
				AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("rw_claiming", d.Day));
				UIHelper.ActivateButton(d.ClaimButton);
				// Rescan after claiming to update states
				MelonCoroutines.Start(RescanRewardDetailsDelayed());
			}
			else if (d.IsClaimable && !string.IsNullOrEmpty(d.Countdown))
			{
				AnnouncementService.Instance.Announce(Loc.Get("menu_reward_not_yet", d.Countdown));
			}
			else
			{
				AnnouncementService.Instance.Announce(Loc.Get("menu_reward_not_claimable"));
			}
		}
		// Backspace handled in HandleBackCommand
	}

	private void AnnounceRewardDay()
	{
		if (_rewardDayIndex < 0 || _rewardDayIndex >= _rewardDays.Count) return;
		var d = _rewardDays[_rewardDayIndex];
		string reward = !string.IsNullOrEmpty(d.Reward) ? ", " + d.Reward : "";
		string status;
		if (d.IsClaimable && string.IsNullOrEmpty(d.Countdown))
			status = ", " + Loc.Get("rw_status_claimable");
		else if (d.IsClaimable && !string.IsNullOrEmpty(d.Countdown))
			status = ", " + Loc.Get("rw_in_time", d.Countdown);
		else
			status = ", " + Loc.Get("rw_status_claimed");
		AnnouncementService.Instance.Announce(d.Day + reward + status + ", " + (_rewardDayIndex + 1) + " of " + _rewardDays.Count);
	}

	private IEnumerator RescanRewardDetailsDelayed()
	{
		yield return new WaitForSeconds(1.0f);
		ScanRewardDetails();
		if (_rewardDays.Count > 0 && _rewardDayIndex < _rewardDays.Count)
			AnnounceRewardDay();
	}

	private void TryClickRewardDetailBackButton()
	{
		try
		{
			GameObject loginBonus = GameObject.Find("LoginBonusSubSceneContainer(Clone)");
			if (loginBonus == null) return;
			Transform backBtn = UIHelper.FindChildByName(loginBonus.transform, "Escape_BackButton");
			if ((Object)(object)backBtn != (Object)null)
			{
				Button btn = backBtn.GetComponentInChildren<Button>(true);
				if (btn != null) UIHelper.ActivateButton(btn);
				return;
			}
		}
		catch { }
		UIHelper.SimulateKeyPress(SDLInput.Key.Escape);
	}

	// --- Helpers ---

	private bool FindNavigator()
	{
		if ((Object)(object)_navigator != (Object)null) return true;
		_navigator = Object.FindObjectOfType<Navigator>();
		return (Object)(object)_navigator != (Object)null;
	}

	private bool IsNavigatorVisible()
	{
		if ((Object)(object)_navigator == (Object)null) return false;
		return _navigator.gameObject.activeInHierarchy;
	}

	private void OnNavigatorShown()
	{
		_focusIndex = 0;
		var dh = NavigatorManager.Instance.GetNavigator<DialogHandler>();
		if (dh != null) dh.SuppressNextAnnounce = true;
		AnnounceFocused();
	}

	private void OnNavigatorHidden() { Deactivate(); }

	public void Deactivate()
	{
		_active = false;
		_inSubScreen = false;
		_onPlayScreen = false;
		_browsingCollection = false;
		_inGameModes = false;
		_browsingRewards = false;
		_browsingRewardDetails = false;
		_deckActionMode = false;
		_deckSubmenuMode = false;
		_activeDeckSlot = null;
		_activeDeckButton = null;
		_deckBuilderActive = false;
		_modalOverlayWasActive = false;
		_modalOverlayCache = false;
		_holdRepeater.Reset();
	}

	public void OnSceneChanged(string sceneName)
	{
		_active = false;
		_onPlayScreen = false;
		_browsingCollection = false;
		_collectionLevel = 0;
		_deckActionMode = false;
		_deckSubmenuMode = false;
		_activeDeckSlot = null;
		_activeDeckButton = null;
		_deckBuilderActive = false;
		_browsingRewards = false;
		_browsingRewardDetails = false;
		_inSubScreen = false;
		_inGameModes = false;
		_modalOverlayWasActive = false;
		_modalOverlayCache = false;
		_lastModalCheck = 0f;
		_collectionCards.Clear();
		_rewardEvents.Clear();
		_rewardDays.Clear();
		_gameModeEntries.Clear();
		_navigator = null;
		_wasShown = false;
	}

	public void AnnounceContext()
	{
		// Announce context-specific state, then the current focus
		if (_browsingRewardDetails) AnnounceRewardDay();
		else if (_browsingRewards) AnnounceReward();
		else if (_inGameModes) AnnounceGameMode();
		else if (_onPlayScreen)
		{
			AnnouncementService.Instance.Announce(Loc.Get("bf_help"), AnnouncementPriority.High);
			AnnouncePlayCategory();
		}
		else if (_browsingCollection && _collectionLevel == 1) AnnounceCollectionCard();
		else if (_browsingCollection) AnnouncementService.Instance.Announce(_collectionSections.Count > 0 ? _collectionSections[_collectionSectionIndex] : Loc.Get("menu_collection"));
		else
		{
			AnnouncementService.Instance.Announce(Loc.Get("help_text"), AnnouncementPriority.High);
			AnnounceFocused();
		}
	}

	private bool IsButtonActive(MenuEntry e) { return false; }

	private Button FindButtonUnder(Transform parent, string name)
	{
		Il2CppArrayBase<Button> btns = parent.GetComponentsInChildren<Button>(true);
		foreach (var b in btns)
		{
			if (b.gameObject.name == name && b.gameObject.activeInHierarchy) return b;
		}
		return null;
	}

	private Button FindButtonByName(string name)
	{
		Il2CppArrayBase<Button> all = Object.FindObjectsOfType<Button>();
		foreach (var b in all) if (b.gameObject.name == name && b.gameObject.activeInHierarchy) return b;
		return null;
	}

	private void TryClickGameBackButton()
	{
		Button b = FindButtonByName("btn_back") ?? FindButtonByName("BackButton") ?? FindButtonByName("btn_close") ?? FindButtonByName("Esc");
		if (b != null) UIHelper.ActivateButton(b);
		else UIHelper.SimulateKeyPress(SDLInput.Key.Escape);
	}

	private void TryOpenDeckEditor()
	{
		try
		{
			// Strategy 1: Find the deck's CollectionDeckSlotView and SimulateMouseClick on it
			Il2CppArrayBase<CollectionDeckSlotView> slots = Object.FindObjectsOfType<CollectionDeckSlotView>();
			if (slots != null)
			{
				for (int i = 0; i < slots.Length; i++)
				{
					var slot = slots[i];
					if ((Object)(object)slot == (Object)null || !((Component)slot).gameObject.activeInHierarchy) continue;
					try
					{
						var deck = slot.Deck;
						if (deck != null)
						{
							string name = deck.Name;
							if (!string.IsNullOrEmpty(name) && name.Equals(_deckName, StringComparison.OrdinalIgnoreCase))
							{
								// SimulateMouseClick on the slot's GameObject — real mouse events
								UIHelper.SimulateMouseClick(((Component)slot).gameObject);

								// Also try direct method call
								try { slot.EnterEditModeForClonedDeck(); } catch { }
								AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_editing_deck"));
								DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "TryOpenDeckEditor: SimulateMouseClick + EnterEditMode on " + name);
								return;
							}
						}
					}
					catch { }
				}
			}

			// Strategy 2: Find any button with "edit" in its name and SimulateMouseClick
			Il2CppArrayBase<Button> allBtns = Object.FindObjectsOfType<Button>();
			if (allBtns != null)
			{
				for (int i = 0; i < allBtns.Length; i++)
				{
					Button btn = allBtns[i];
					if ((Object)(object)btn == (Object)null || !((Component)btn).gameObject.activeInHierarchy) continue;
					string goName = ((Object)((Component)btn).gameObject).name;
					if (goName.Contains("Edit", StringComparison.OrdinalIgnoreCase)
						&& !goName.Contains("Credit", StringComparison.OrdinalIgnoreCase))
					{
						UIHelper.SimulateMouseClick(((Component)btn).gameObject);
						AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("menu_editing_deck"));
						DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "TryOpenDeckEditor: Edit via button: " + goName);
						return;
					}
				}
			}

			// Strategy 3: Click the deck name button to open tray, then look for edit
			if (_deckLeftButton != null)
			{
				UIHelper.ActivateButton(((Component)_deckLeftButton).gameObject);
				AnnouncementService.Instance.Announce(Loc.Get("menu_opening_deck_tray"));
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Opening deck tray to find edit button");
			}
			else
			{
				AnnouncementService.Instance.Announce(Loc.Get("menu_edit_button_not_found"));
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Error("TryOpenDeckEditor failed: " + ex.Message);
			AnnouncementService.Instance.Announce(Loc.Get("menu_edit_button_not_found"));
		}
	}

}
