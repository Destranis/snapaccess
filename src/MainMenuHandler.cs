using System;
using System.Collections.Generic;
using Il2CppCubeUnity.App.Game;
using Il2CppCubeUnity.App.Navigator;
using Il2CppCubeUnity.App.View;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSecondDinner.CubeRendering.Card;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Handles the main menu. Provides structured navigation for the Play screen
/// (deck info + play button) and generic button navigation for other sections.
/// Tab switches between menu bar and screen content. Backspace goes back.
/// </summary>
public class MainMenuHandler
{
	private class MenuEntry
	{
		public string Name;
		public string LocKey;
		public Func<Navigator, NavigatorButton> GetButton;
		public Action<Navigator> Activate;
	}

	private Navigator _navigator;
	private int _focusIndex = -1;
	private bool _wasShown = false;
	private float _lastSearchTime = 0f;
	private bool _inSubScreen = false;
	private float _inputBlockUntil = 0f;
	private readonly DialogHandler _dialogHandler;

	// Play screen state
	private Button _playButton;
	private Button _deckLeftButton;
	private Button _deckRightButton;
	private string _deckName = "";
	private bool _onPlayScreen = false;

	// Deck card browsing
	private bool _browsingDeck = false;
	private readonly List<CardView> _deckCards = new List<CardView>();
	private int _deckCardIndex = -1;
	private int _deckDetailLevel = 0;

	// Missions menu browsing
	private bool _browsingMissions = false;
	private int _missionMenuLevel = 0; // 0 = categories, 1 = individual missions
	private readonly List<MissionCategory> _missionCategories = new List<MissionCategory>();
	private int _missionCategoryIndex = 0;
	private int _missionIndex = 0;
	private int _missionDetailLevel = 0; // 0 = name, 1 = description, 2 = progress, 3 = reward

	private class MissionInfo
	{
		public string Title;
		public string Progress;
		public string Goal;
		public string Description;
		public string Reward;
		public Button TileButton;
	}

	private class MissionCategory
	{
		public string Name;
		public readonly List<MissionInfo> Missions = new List<MissionInfo>();
	}

	// Rewards menu browsing
	private bool _browsingRewards = false;
	private int _rewardMenuLevel = 0; // 0 = categories, 1 = individual rewards
	private readonly List<RewardCategory> _rewardCategories = new List<RewardCategory>();
	private int _rewardCategoryIndex = 0;
	private int _rewardIndex = 0;

	private class RewardItem
	{
		public string Label;
		public Button ClaimButton;
		public bool Claimable;
	}

	private class RewardCategory
	{
		public string Name;
		public readonly List<RewardItem> Rewards = new List<RewardItem>();
	}

	// Collection browsing
	private bool _browsingCollection = false;
	private readonly List<CollectionCard> _collectionCards = new List<CollectionCard>();
	private int _collectionIndex = 0;
	private int _collectionDetailLevel = 0;
	private bool _returnToCollection = false;
	private bool _returnToPlayScreen = false;
	private readonly List<string> _collectionSections = new List<string>();
	private int _collectionSectionIndex = 0;

	private class CollectionCard
	{
		public string Name;
		public Button Button;
		public CardRenderer Renderer;
	}

	// Pending collection scan (delayed to allow screen render)
	private bool _pendingCollectionScan = false;
	private float _collectionScanTime = 0f;

	public MainMenuHandler(DialogHandler dialogHandler)
	{
		_dialogHandler = dialogHandler;
	}

	private readonly List<MenuEntry> _entries = new List<MenuEntry>
	{
		new MenuEntry
		{
			Name = "News",
			LocKey = "menu_news",
			GetButton = (Navigator nav) => nav._NewsButton,
			Activate = delegate(Navigator nav) { nav.OnNewsButton(); }
		},
		new MenuEntry
		{
			Name = "Shop",
			LocKey = "menu_shop",
			GetButton = (Navigator nav) => nav._ShopButton,
			Activate = delegate(Navigator nav) { nav.OnShopButton(); }
		},
		new MenuEntry
		{
			Name = "Play",
			LocKey = "menu_play",
			GetButton = (Navigator nav) => nav._PlayButton,
			Activate = delegate(Navigator nav) { nav.OnPlayButton(); }
		},
		new MenuEntry
		{
			Name = "Collection",
			LocKey = "menu_collection",
			GetButton = (Navigator nav) => nav._CollectionButton,
			Activate = delegate(Navigator nav) { nav.OnCollectionButton(); }
		},
		new MenuEntry
		{
			Name = "Game Modes",
			LocKey = "menu_game_modes",
			GetButton = (Navigator nav) => nav._GameModeButton,
			Activate = delegate(Navigator nav) { nav.OnGameModesButton(); }
		},
		new MenuEntry
		{
			Name = "Clan",
			LocKey = "menu_clan",
			GetButton = (Navigator nav) => nav._ClanButton,
			Activate = delegate(Navigator nav) { nav.OnClanButton(); }
		},
		new MenuEntry
		{
			Name = "Settings",
			LocKey = "menu_settings",
			GetButton = null, // Not a Navigator button — standalone UI button
			Activate = delegate(Navigator nav) { ActivateSettingsButton(); }
		}
	};

	private static void ActivateSettingsButton()
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
				if (goName == "Button_Settings")
				{
					UIHelper.ClickButton(btn);
					DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Settings button clicked");
					return;
				}
			}
			ScreenReader.Say(Loc.Get("menu_settings_not_found"));
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "ActivateSettingsButton failed: " + ex.Message);
		}
	}

	public bool IsActive => (Object)(object)_navigator != (Object)null && IsNavigatorVisible();

	/// <summary>Whether the user is in sub-screen mode (not navigating the menu bar).</summary>
	public bool InSubScreen => _inSubScreen && !_onPlayScreen && !_browsingDeck && !_browsingMissions && !_browsingRewards && !_browsingCollection;

	public bool Update()
	{
		if (!FindNavigator()) return false;

		bool visible = IsNavigatorVisible();
		if (visible && !_wasShown) OnNavigatorShown();
		else if (!visible && _wasShown) OnNavigatorHidden();
		_wasShown = visible;
		if (!visible) return false;

		// Handle delayed collection scan
		if (_pendingCollectionScan && Time.time >= _collectionScanTime)
		{
			_pendingCollectionScan = false;
			EnterCollection();
		}

		ProcessInput();

		// On Play screen, we handle everything ourselves — block DialogHandler
		if (_onPlayScreen || _browsingDeck || _browsingMissions || _browsingRewards || _browsingCollection) return true;

		// On other screens in sub-screen mode, let DialogHandler run
		return false;
	}

	public void AnnounceContext()
	{
		if (_onPlayScreen)
		{
			AnnouncePlayScreen();
		}
		else if (_focusIndex >= 0 && _focusIndex < _entries.Count)
		{
			MenuEntry entry = _entries[_focusIndex];
			string current = IsButtonActive(entry) ? Loc.Get("menu_current") : "";
			ScreenReader.Say(Loc.Get("menu_context", Loc.Get(entry.LocKey), _focusIndex + 1, _entries.Count, current));
		}
		else
		{
			ScreenReader.Say(Loc.Get("menu_help"));
		}
	}

	public void Reset()
	{
		_navigator = null;
		_focusIndex = -1;
		_wasShown = false;
		_inSubScreen = false;
		_onPlayScreen = false;
		_browsingDeck = false;
		_browsingMissions = false;
		_browsingRewards = false;
		_browsingCollection = false;
		_returnToCollection = false;
		_returnToPlayScreen = false;
		_playButton = null;
		_deckLeftButton = null;
		_deckRightButton = null;
		_deckName = "";
	}

	// --- Navigator Management ---

	private bool FindNavigator()
	{
		if ((Object)(object)_navigator != (Object)null)
		{
			try
			{
				if ((Object)(object)((Component)_navigator).gameObject != (Object)null)
					return true;
			}
			catch { }
			_navigator = null;
		}
		if (Time.time - _lastSearchTime < 1f) return false;
		_lastSearchTime = Time.time;
		try
		{
			_navigator = UIHelper.FindComponent<Navigator>();
			if ((Object)(object)_navigator != (Object)null)
			{
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Navigator found");
				return true;
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Navigator search failed: " + ex.Message);
		}
		return false;
	}

	private bool IsNavigatorVisible()
	{
		if ((Object)(object)_navigator == (Object)null) return false;
		try { return _navigator.IsShown && ((Component)_navigator).gameObject.activeInHierarchy; }
		catch { return false; }
	}

	private void OnNavigatorShown()
	{
		DebugLogger.Log(LogCategory.State, "MainMenuHandler", "Navigator visible — entering main menu");
		AccessStateManager.TryEnter(AccessStateManager.State.MainMenu);
		_focusIndex = 2; // Play
		_inputBlockUntil = Time.time + 0.5f; // Block input briefly to prevent phantom keypresses on scene load

		// Detect current section and enter it
		string section = GetCurrentSectionName();
		if (section == "Play" || string.IsNullOrEmpty(section) || section == "None")
		{
			// Go straight to Play screen — its announcement is sufficient
			EnterPlayScreen();
		}
		else
		{
			_inSubScreen = true;
			_onPlayScreen = false;
			_dialogHandler.Reset();
			// Only announce menu + section for non-Play screens
			ScreenReader.Say(Loc.Get("menu_opened"));
			ScreenReader.SayQueued(Loc.Get("menu_current_section", section));
		}
	}

	private void OnNavigatorHidden()
	{
		DebugLogger.Log(LogCategory.State, "MainMenuHandler", "Navigator hidden");
		AccessStateManager.Exit(AccessStateManager.State.MainMenu);
		_inSubScreen = false;
		_onPlayScreen = false;
		_browsingDeck = false;
		_browsingMissions = false;
		_browsingRewards = false;
		_browsingCollection = false;
		_returnToCollection = false;
		_returnToPlayScreen = false;
	}

	// --- Input Processing ---

	private void ProcessInput()
	{
		// Block input briefly after scene transition to prevent phantom keypresses
		if (Time.time < _inputBlockUntil) return;

		// Deck and missions browsing take priority
		if (_browsingDeck)
		{
			ProcessDeckInput();
			return;
		}
		if (_browsingMissions)
		{
			ProcessMissionsInput();
			return;
		}
		if (_browsingRewards)
		{
			ProcessRewardsInput();
			return;
		}
		if (_browsingCollection)
		{
			ProcessCollectionInput();
			return;
		}

		// Backspace: exit sub-screen → menu bar (or back to collection if we came from there)
		if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
		{
			if (_inSubScreen && _returnToPlayScreen)
			{
				// Return to play screen after deck selector — rescan to pick up new deck name
				_inSubScreen = false;
				_returnToPlayScreen = false;
				TryClickGameBackButton();
				// Delay re-entry to allow the deck tray to close
				_inputBlockUntil = Time.time + 0.5f;
				EnterPlayScreen();
			}
			else if (_inSubScreen && _returnToCollection)
			{
				// Return to collection browsing after viewing a card detail
				_inSubScreen = false;
				_returnToCollection = false;
				_browsingCollection = true;
				_collectionDetailLevel = 0;
				ScreenReader.Say(Loc.Get("collection_entered", _collectionCards.Count));
				AnnounceCollectionCard();
			}
			else if (_inSubScreen)
			{
				// Click the game's Escape/Back button to actually close the sub-screen
				TryClickGameBackButton();
				_inSubScreen = false;
				_returnToCollection = false;
				ScreenReader.Say(Loc.Get("menu_nav_focus"));
				AnnounceFocused();
			}
			else if (_onPlayScreen)
			{
				_onPlayScreen = false;
				ScreenReader.Say(Loc.Get("menu_nav_focus"));
				AnnounceFocused();
			}
			return;
		}

		// Tab: toggle menu bar vs screen content
		if (SDLInput.IsKeyDown(SDLInput.Key.Tab))
		{
			if (_inSubScreen || _onPlayScreen)
			{
				_inSubScreen = false;
				_onPlayScreen = false;
				ScreenReader.Say(Loc.Get("menu_nav_focus"));
				AnnounceFocused();
			}
			else
			{
				EnterCurrentSection();
			}
			return;
		}

		// On Play screen
		if (_onPlayScreen)
		{
			ProcessPlayInput();
			return;
		}

		// In sub-screen on non-Play section — let DialogHandler handle it
		if (_inSubScreen) return;

		// Menu bar navigation
		if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
		{
			MoveFocus(-1);
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			MoveFocus(1);
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			ActivateFocused();
		}
		else
		{
			// Number keys 1-6
			for (int i = 0; i < _entries.Count && i < 6; i++)
			{
				if (SDLInput.IsKeyDown((SDLInput.Key)(49 + i)))
				{
					_focusIndex = i;
					ActivateFocused();
					return;
				}
			}
		}
	}

	private void EnterCurrentSection()
	{
		if (_focusIndex == 2) // Play
		{
			EnterPlayScreen();
		}
		else if (_focusIndex == 3) // Collection
		{
			EnterCollection();
		}
		else
		{
			_inSubScreen = true;
			_onPlayScreen = false;
			_dialogHandler.Reset();
			ScreenReader.Say(Loc.Get("menu_content_focus"));
		}
	}

	// --- Menu Bar ---

	private void MoveFocus(int direction)
	{
		int old = _focusIndex;
		_focusIndex += direction;
		if (_focusIndex >= _entries.Count) _focusIndex = 0;
		else if (_focusIndex < 0) _focusIndex = _entries.Count - 1;
		if (_focusIndex != old) AnnounceFocused();
	}

	private void ActivateFocused()
	{
		if (_focusIndex < 0 || _focusIndex >= _entries.Count) return;

		MenuEntry entry = _entries[_focusIndex];
		if (IsButtonLocked(entry))
		{
			ScreenReader.Say(Loc.Get("menu_locked", Loc.Get(entry.LocKey), _focusIndex + 1, _entries.Count));
			return;
		}
		try
		{
			DebugLogger.LogInput("Enter", "Activating menu: " + entry.Name);
			entry.Activate(_navigator);

			if (_focusIndex == 2) // Play
			{
				EnterPlayScreen();
			}
			else if (_focusIndex == 3) // Collection
			{
				ScreenReader.Say(Loc.Get("menu_activated", Loc.Get(entry.LocKey)));
				// Delay collection scan to allow the screen to render (needs ~2-3s)
				_pendingCollectionScan = true;
				_collectionScanTime = Time.time + 2.5f;
			}
			else
			{
				_inSubScreen = true;
				_onPlayScreen = false;
				// Settings opens in Canvas-Dialogs which takes a moment to render
				_dialogHandler.ResetWithDelay(0.6f);
				ScreenReader.Say(Loc.Get("menu_activated", Loc.Get(entry.LocKey)));
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Activate failed: " + ex.Message);
			ScreenReader.Say(Loc.Get("menu_error"));
		}
	}

	private void AnnounceFocused()
	{
		if (_focusIndex < 0 || _focusIndex >= _entries.Count) return;
		MenuEntry entry = _entries[_focusIndex];
		string name = Loc.Get(entry.LocKey);
		if (IsButtonLocked(entry))
			ScreenReader.Say(Loc.Get("menu_locked", name, _focusIndex + 1, _entries.Count));
		else if (IsButtonActive(entry))
			ScreenReader.Say(Loc.Get("menu_button_active", name, _focusIndex + 1, _entries.Count));
		else
			ScreenReader.Say(Loc.Get("menu_button", name, _focusIndex + 1, _entries.Count));
	}

	private bool IsButtonLocked(MenuEntry entry)
	{
		if (entry.GetButton == null) return false;
		try
		{
			NavigatorButton btn = entry.GetButton(_navigator);
			return (Object)(object)btn != (Object)null && btn._locked;
		}
		catch { return false; }
	}

	private bool IsButtonActive(MenuEntry entry)
	{
		if (entry.GetButton == null) return false;
		try
		{
			NavigatorButton btn = entry.GetButton(_navigator);
			if ((Object)(object)btn == (Object)null) return false;
			GameObject highlight = btn._HighlightObject;
			return (Object)(object)highlight != (Object)null && highlight.activeInHierarchy;
		}
		catch { return false; }
	}

	private string GetCurrentSectionName()
	{
		try { return ((object)_navigator.CurrentSubScene).ToString(); }
		catch { return ""; }
	}

	// --- Play Screen ---

	private void EnterPlayScreen()
	{
		_onPlayScreen = true;
		_inSubScreen = false;
		_browsingDeck = false;
		ScanPlayScreen();
		AnnouncePlayScreen();
	}

	private void ScanPlayScreen()
	{
		_playButton = null;
		_deckLeftButton = null;
		_deckRightButton = null;
		_deckName = "";

		try
		{
			Il2CppArrayBase<Button> allButtons = Object.FindObjectsOfType<Button>();
			if (allButtons != null)
			{
				for (int i = 0; i < allButtons.Count; i++)
				{
					Button btn = allButtons[i];
					if ((Object)(object)btn == (Object)null) continue;
					if (!((Component)btn).gameObject.activeInHierarchy) continue;
					if (!((Selectable)btn).interactable) continue;
					try
					{
						string goName = ((Object)((Component)btn).gameObject).name;
						if (goName == "btn_start" || goName == "PlayButton" ||
						    goName.Contains("btn_play", StringComparison.OrdinalIgnoreCase))
						{
							_playButton = btn;
							DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Play button found: " + goName);
						}
						else if (goName == "btn_left")
						{
							// Deck selection — previous deck
							_deckLeftButton = btn;
							// btn_left's label IS the deck name
							string label = UIHelper.GetButtonLabel(btn);
							if (!string.IsNullOrEmpty(label) && label.Length >= 2 && !IsNumericLabel(label))
							{
								_deckName = label;
								DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Deck name from btn_left: " + _deckName);
							}
						}
						else if (goName == "btn_right")
						{
							// Deck selection — next deck
							_deckRightButton = btn;
						}
					}
					catch { }
				}
			}

			// Fallback: find deck name from text near deck-related parent
			if (string.IsNullOrEmpty(_deckName))
			{
				Il2CppArrayBase<TMP_Text> allTexts = Object.FindObjectsOfType<TMP_Text>();
				if (allTexts != null)
				{
					for (int i = 0; i < allTexts.Count; i++)
					{
						TMP_Text tmp = allTexts[i];
						if ((Object)(object)tmp == (Object)null) continue;
						if (!((Component)tmp).gameObject.activeInHierarchy) continue;
						string text = tmp.text;
						if (string.IsNullOrWhiteSpace(text)) continue;

						try
						{
							Transform t = ((Component)tmp).transform;
							int depth = 0;
							while (t != null && depth < 5)
							{
								string parentName = ((Object)t.gameObject).name;
								if (parentName.Contains("Deck", StringComparison.OrdinalIgnoreCase))
								{
									string cleaned = UIHelper.StripRichText(text.Trim());
									if (cleaned.Length >= 3 && !IsNumericLabel(cleaned) && !IsPromoText(cleaned))
									{
										_deckName = cleaned;
										DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Deck name found: " + _deckName);
									}
									break;
								}
								t = t.parent;
								depth++;
							}
						}
						catch { }
						if (!string.IsNullOrEmpty(_deckName)) break;
					}
				}
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "ScanPlayScreen failed: " + ex.Message);
		}
	}

	private void AnnouncePlayScreen()
	{
		string msg = Loc.Get("play_screen");
		if (!string.IsNullOrEmpty(_deckName))
		{
			msg += " " + Loc.Get("play_deck", _deckName);
		}
		// Read rank info
		string rank = ReadRankText();
		if (!string.IsNullOrEmpty(rank))
			msg += " " + rank;

		bool canSwitch = (Object)(object)_deckLeftButton != (Object)null;
		msg += " " + (canSwitch ? Loc.Get("play_instructions_full") : Loc.Get("play_instructions"));
		ScreenReader.Say(msg);
	}

	/// <summary>Opens the missions menu for structured browsing.</summary>
	private void OpenMissionsMenu()
	{
		ScanMissions();
		if (_missionCategories.Count == 0)
		{
			ScreenReader.Say(Loc.Get("play_no_missions"));
			return;
		}
		_browsingMissions = true;
		_missionMenuLevel = 0;
		_missionCategoryIndex = 0;
		_missionIndex = 0;
		_missionDetailLevel = 0;
		ScreenReader.Say(Loc.Get("missions_menu_opened", _missionCategories.Count));
		AnnounceMissionCategory();
	}

	/// <summary>Scans mission tiles from the Play screen and groups them by category.</summary>
	private void ScanMissions()
	{
		_missionCategories.Clear();
		try
		{
			// Find category panels under the missions layout
			Il2CppArrayBase<Transform> allTransforms = Object.FindObjectsOfType<Transform>();
			if (allTransforms == null) return;

			// Collect category panels: SeasonPassMissionPanel, DailyMissionsPanel, etc.
			var categoryPanels = new List<(string name, Transform transform)>();
			for (int i = 0; i < allTransforms.Count; i++)
			{
				Transform t = allTransforms[i];
				if ((Object)(object)t == (Object)null) continue;
				if (!t.gameObject.activeInHierarchy) continue;
				string goName = ((Object)t.gameObject).name;

				if (goName.Contains("MissionPanel", StringComparison.OrdinalIgnoreCase) ||
				    goName.Contains("MissionsPanel", StringComparison.OrdinalIgnoreCase))
				{
					// Derive a readable category name from the panel name
					string catName = DeriveCategoryName(goName);
					categoryPanels.Add((catName, t));
					DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
						"Mission category panel found: " + goName + " → " + catName);
				}
			}

			// For each category panel, find mission tiles underneath
			foreach (var (catName, panelTransform) in categoryPanels)
			{
				var category = new MissionCategory { Name = catName };

				Il2CppArrayBase<Transform> children = panelTransform.gameObject.GetComponentsInChildren<Transform>(true);
				if (children == null) continue;

				for (int i = 0; i < children.Count; i++)
				{
					Transform t = children[i];
					if ((Object)(object)t == (Object)null) continue;
					if (!t.gameObject.activeInHierarchy) continue;
					string goName = ((Object)t.gameObject).name;
					if (!goName.Contains("tile_pc_main_missions", StringComparison.Ordinal)) continue;

					var mission = ReadMissionTile(t.gameObject);
					if (mission != null)
						category.Missions.Add(mission);
				}

				if (category.Missions.Count > 0)
					_missionCategories.Add(category);
			}

			// Fallback: if no category panels found, scan all mission tiles as one "Missions" category
			if (_missionCategories.Count == 0)
			{
				var fallback = new MissionCategory { Name = Loc.Get("missions_category_all") };
				for (int i = 0; i < allTransforms.Count; i++)
				{
					Transform t = allTransforms[i];
					if ((Object)(object)t == (Object)null) continue;
					if (!t.gameObject.activeInHierarchy) continue;
					string goName = ((Object)t.gameObject).name;
					if (!goName.Contains("tile_pc_main_missions", StringComparison.Ordinal)) continue;

					var mission = ReadMissionTile(t.gameObject);
					if (mission != null)
						fallback.Missions.Add(mission);
				}
				if (fallback.Missions.Count > 0)
					_missionCategories.Add(fallback);
			}

			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
				$"ScanMissions: {_missionCategories.Count} categories found");
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "ScanMissions failed: " + ex.Message);
		}
	}

	/// <summary>Reads a single mission tile's data.</summary>
	private MissionInfo ReadMissionTile(GameObject tile)
	{
		if ((Object)(object)tile == (Object)null) return null;
		string title = "";
		string progress = "";
		string goal = "";
		string description = "";
		string reward = "";

		try
		{
			Il2CppArrayBase<TMP_Text> texts = tile.GetComponentsInChildren<TMP_Text>(true);
			if (texts == null) return null;

			var rewardParts = new List<string>();
			for (int j = 0; j < texts.Count; j++)
			{
				TMP_Text tmp = texts[j];
				if ((Object)(object)tmp == (Object)null) continue;
				string n = ((Object)((Component)tmp).gameObject).name;
				string v = UIHelper.StripRichText((tmp.text ?? "").Trim());
				if (string.IsNullOrEmpty(v)) continue;

				if (n.Contains("title", StringComparison.OrdinalIgnoreCase))
					title = v;
				else if (n.Contains("missionprogress", StringComparison.OrdinalIgnoreCase))
					progress = v;
				else if (n.Contains("missiongoal", StringComparison.OrdinalIgnoreCase))
					goal = v;
				else if (n.Contains("description", StringComparison.OrdinalIgnoreCase))
					description = v;
				else if (n == "text_credits")
					rewardParts.Add(v + " credits");
				else if (n == "text_battlepass")
					rewardParts.Add(v + " season XP");
				else if (n.Contains("reward", StringComparison.OrdinalIgnoreCase))
					rewardParts.Add(v);
			}
			if (rewardParts.Count > 0)
				reward = string.Join(", ", rewardParts);
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "ReadMissionTile failed: " + ex.Message);
		}

		if (string.IsNullOrEmpty(title)) return null;

		// Get the Button component on the tile for clicking
		Button tileBtn = tile.GetComponent<Button>();

		return new MissionInfo
		{
			Title = title,
			Progress = progress,
			Goal = goal,
			Description = description,
			Reward = reward,
			TileButton = tileBtn
		};
	}

	/// <summary>Derives a readable category name from a panel GameObject name.</summary>
	private string DeriveCategoryName(string panelName)
	{
		if (panelName.Contains("SeasonPass", StringComparison.OrdinalIgnoreCase))
			return Loc.Get("missions_category_season");
		if (panelName.Contains("Daily", StringComparison.OrdinalIgnoreCase))
			return Loc.Get("missions_category_daily");
		if (panelName.Contains("Weekly", StringComparison.OrdinalIgnoreCase))
			return Loc.Get("missions_category_weekly");
		// Generic fallback: clean up the name
		string clean = panelName.Replace("Panel", "").Replace("Missions", "").Replace("Mission", "").Trim();
		return string.IsNullOrEmpty(clean) ? Loc.Get("missions_category_all") : UIHelper.CleanGameObjectName(clean);
	}

	/// <summary>Processes input while browsing the missions menu.</summary>
	private void ProcessMissionsInput()
	{
		if (_missionMenuLevel == 0)
		{
			// Category level: Up/Down to browse categories, Enter to drill in, Backspace to exit
			if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
			{
				if (_missionCategoryIndex > 0)
				{
					_missionCategoryIndex--;
					AnnounceMissionCategory();
				}
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
			{
				if (_missionCategoryIndex < _missionCategories.Count - 1)
				{
					_missionCategoryIndex++;
					AnnounceMissionCategory();
				}
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
			{
				if (_missionCategoryIndex >= 0 && _missionCategoryIndex < _missionCategories.Count)
				{
					var cat = _missionCategories[_missionCategoryIndex];
					if (cat.Missions.Count > 0)
					{
						_missionMenuLevel = 1;
						_missionIndex = 0;
						_missionDetailLevel = 0;
						ScreenReader.Say(Loc.Get("missions_category_entered", cat.Name, cat.Missions.Count));
						AnnounceMission();
					}
					else
					{
						ScreenReader.Say(Loc.Get("play_no_missions"));
					}
				}
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsKeyDown(SDLInput.Key.Escape)
			         || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
			{
				_browsingMissions = false;
				ScreenReader.Say(Loc.Get("missions_exited"));
			}
		}
		else if (_missionMenuLevel == 1)
		{
			// Individual mission level: Left/Right to browse, Down for details, Backspace to go back
			if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
			{
				var cat = _missionCategories[_missionCategoryIndex];
				if (_missionIndex > 0)
				{
					_missionIndex--;
					_missionDetailLevel = 0;
					AnnounceMission();
				}
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
			{
				var cat = _missionCategories[_missionCategoryIndex];
				if (_missionIndex < cat.Missions.Count - 1)
				{
					_missionIndex++;
					_missionDetailLevel = 0;
					AnnounceMission();
				}
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
			{
				if (_missionDetailLevel < 3)
				{
					_missionDetailLevel++;
					AnnounceMissionDetail();
				}
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
			{
				if (_missionDetailLevel > 0)
				{
					_missionDetailLevel--;
					if (_missionDetailLevel == 0)
						AnnounceMission();
					else
						AnnounceMissionDetail();
				}
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
			{
				// Click the mission tile to claim completed missions or view details
				var cat2 = _missionCategories[_missionCategoryIndex];
				if (_missionIndex >= 0 && _missionIndex < cat2.Missions.Count)
				{
					var mission = cat2.Missions[_missionIndex];
					if ((Object)(object)mission.TileButton != (Object)null)
					{
						bool isComplete = !string.IsNullOrEmpty(mission.Progress) &&
						                  !string.IsNullOrEmpty(mission.Goal) &&
						                  mission.Progress == mission.Goal;
						UIHelper.ClickButton(mission.TileButton);
						if (isComplete)
							ScreenReader.Say(Loc.Get("missions_claiming", mission.Title));
						else
							ScreenReader.Say(Loc.Get("missions_opening", mission.Title));
						DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
							"Clicked mission tile: " + mission.Title + " (complete=" + isComplete + ")");
					}
					else
					{
						ScreenReader.Say(Loc.Get("missions_no_action"));
					}
				}
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsKeyDown(SDLInput.Key.Escape)
			         || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
			{
				_missionMenuLevel = 0;
				_missionDetailLevel = 0;
				ScreenReader.Say(Loc.Get("missions_back_to_categories"));
				AnnounceMissionCategory();
			}
		}
	}

	private void AnnounceMissionCategory()
	{
		if (_missionCategoryIndex < 0 || _missionCategoryIndex >= _missionCategories.Count) return;
		var cat = _missionCategories[_missionCategoryIndex];
		ScreenReader.Say(Loc.Get("missions_category", cat.Name, cat.Missions.Count,
			_missionCategoryIndex + 1, _missionCategories.Count));
	}

	private void AnnounceMission()
	{
		if (_missionCategoryIndex < 0 || _missionCategoryIndex >= _missionCategories.Count) return;
		var cat = _missionCategories[_missionCategoryIndex];
		if (_missionIndex < 0 || _missionIndex >= cat.Missions.Count) return;
		var mission = cat.Missions[_missionIndex];

		string progressInfo = "";
		if (!string.IsNullOrEmpty(mission.Progress) && !string.IsNullOrEmpty(mission.Goal))
		{
			progressInfo = ", " + mission.Progress + " of " + mission.Goal;
			if (mission.Progress == mission.Goal) progressInfo += ", complete";
		}

		ScreenReader.Say(Loc.Get("missions_mission", mission.Title, _missionIndex + 1, cat.Missions.Count) + progressInfo);
	}

	private void AnnounceMissionDetail()
	{
		if (_missionCategoryIndex < 0 || _missionCategoryIndex >= _missionCategories.Count) return;
		var cat = _missionCategories[_missionCategoryIndex];
		if (_missionIndex < 0 || _missionIndex >= cat.Missions.Count) return;
		var mission = cat.Missions[_missionIndex];

		switch (_missionDetailLevel)
		{
			case 1:
				// Description / goal text
				if (!string.IsNullOrEmpty(mission.Description))
					ScreenReader.Say(Loc.Get("missions_description", mission.Description));
				else if (!string.IsNullOrEmpty(mission.Title))
					ScreenReader.Say(mission.Title);
				else
					ScreenReader.Say(Loc.Get("missions_no_description"));
				break;
			case 2:
				// Progress
				if (!string.IsNullOrEmpty(mission.Progress) && !string.IsNullOrEmpty(mission.Goal))
				{
					string status = mission.Progress == mission.Goal ? Loc.Get("missions_complete") : "";
					ScreenReader.Say(Loc.Get("missions_progress", mission.Progress, mission.Goal) +
					                 (string.IsNullOrEmpty(status) ? "" : ", " + status));
				}
				else
					ScreenReader.Say(Loc.Get("missions_progress_unknown"));
				break;
			case 3:
				// Reward
				if (!string.IsNullOrEmpty(mission.Reward))
					ScreenReader.Say(Loc.Get("missions_reward", mission.Reward));
				else
					ScreenReader.Say(Loc.Get("missions_no_reward"));
				break;
		}
	}

	// --- Rewards Menu ---

	/// <summary>Reads a reward label from the parent hierarchy of a ClaimButton (e.g. "Next Reward: 100 Credits").</summary>
	private static string BuildRewardSlotLabel(Transform claimBtnTransform)
	{
		try
		{
			// Walk up to find the reward slot container (e.g. FinalRewardDay, NextRewardDay)
			Transform slot = claimBtnTransform.parent; // LoginDailyRewardSlot_Carousel
			if (slot != null) slot = slot.parent; // FinalRewardDay or NextRewardDay
			if (slot == null) return null;

			string slotName = ((Object)slot.gameObject).name;
			string prefix = slotName.Contains("Final", StringComparison.OrdinalIgnoreCase) ? "Final Reward" : "Next Reward";

			// Look for reward text in the sibling RewardLayoutGroup area
			// The text children are under Contents/RewardText or similar
			Il2CppArrayBase<TMP_Text> texts = slot.gameObject.GetComponentsInChildren<TMP_Text>(true);
			if (texts != null)
			{
				for (int i = 0; i < texts.Count; i++)
				{
					TMP_Text tmp = texts[i];
					if ((Object)(object)tmp == (Object)null) continue;
					string goName = ((Object)((Component)tmp).gameObject).name;
					string val = UIHelper.StripRichText((tmp.text ?? "").Trim());
					if (string.IsNullOrEmpty(val)) continue;

					// text_Reward Blue, text_Reward Gold contain the reward description
					if (goName.Contains("text_Reward", StringComparison.OrdinalIgnoreCase) &&
					    !goName.Contains("FinalReward", StringComparison.OrdinalIgnoreCase) &&
					    !goName.Contains("NextReward", StringComparison.OrdinalIgnoreCase))
					{
						return prefix + ": " + val;
					}
				}
			}
			return prefix;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>Clicks the game's Escape/Back button (btn_hex_prp) to close the current sub-screen.</summary>
	private void TryClickGameBackButton()
	{
		try
		{
			Il2CppArrayBase<Button> allButtons = Object.FindObjectsOfType<Button>();
			if (allButtons == null) return;
			for (int i = 0; i < allButtons.Count; i++)
			{
				Button btn = allButtons[i];
				if ((Object)(object)btn == (Object)null) continue;
				if (!((Component)btn).gameObject.activeInHierarchy) continue;
				string goName = ((Object)((Component)btn).gameObject).name;
				if (goName == "btn_hex_prp")
				{
					UIHelper.ClickButton(btn);
					DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Clicked game back button");
					return;
				}
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "TryClickGameBackButton failed: " + ex.Message);
		}
	}

	/// <summary>Opens the full rewards screen by clicking "See All Rewards".</summary>
	private void OpenRewardsMenu()
	{
		try
		{
			Il2CppArrayBase<Button> allButtons = Object.FindObjectsOfType<Button>();
			if (allButtons != null)
			{
				for (int i = 0; i < allButtons.Count; i++)
				{
					Button btn = allButtons[i];
					if ((Object)(object)btn == (Object)null) continue;
					if (!((Component)btn).gameObject.activeInHierarchy) continue;
					string goName = ((Object)((Component)btn).gameObject).name;
					if (goName == "btn_SeeAllRewards")
					{
						ScreenReader.Say(Loc.Get("rewards_opening"));
						UIHelper.ClickButton(btn);
						_onPlayScreen = false;
						_inSubScreen = true;
						_dialogHandler.ResetWithDelay(2.0f);
						DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Opened See All Rewards");
						return;
					}
				}
			}
			ScreenReader.Say(Loc.Get("rewards_none"));
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "OpenRewardsMenu failed: " + ex.Message);
			ScreenReader.Say(Loc.Get("rewards_none"));
		}
	}

	/// <summary>Scans reward tracks from the Play screen carousel and season pass.</summary>
	private void ScanRewards()
	{
		_rewardCategories.Clear();
		try
		{
			Il2CppArrayBase<Transform> allTransforms = Object.FindObjectsOfType<Transform>();
			if (allTransforms == null) return;

			// Find carousel cells with login bonus rewards
			for (int i = 0; i < allTransforms.Count; i++)
			{
				Transform t = allTransforms[i];
				if ((Object)(object)t == (Object)null) continue;
				if (!t.gameObject.activeInHierarchy) continue;
				string goName = ((Object)t.gameObject).name;
				if (!goName.Contains("Promo_LoginBonus_Container", StringComparison.Ordinal) &&
				    !goName.Contains("Promo_LoginRewards", StringComparison.Ordinal))
					continue;

				// Find the header text for this reward track
				string header = "";
				var rewards = new List<RewardItem>();

				Il2CppArrayBase<TMP_Text> texts = t.gameObject.GetComponentsInChildren<TMP_Text>(true);
				if (texts != null)
				{
					for (int j = 0; j < texts.Count; j++)
					{
						TMP_Text tmp = texts[j];
						if ((Object)(object)tmp == (Object)null) continue;
						string n = ((Object)((Component)tmp).gameObject).name;
						string v = UIHelper.StripRichText((tmp.text ?? "").Trim());
						if (string.IsNullOrEmpty(v)) continue;
						if (n.Contains("Header", StringComparison.OrdinalIgnoreCase) ||
						    n.Contains("text_Header", StringComparison.OrdinalIgnoreCase))
							header = v;
					}
				}

				// Find claim buttons only (skip btn_SeeAllRewards and other non-claim buttons)
				Il2CppArrayBase<Button> buttons = t.gameObject.GetComponentsInChildren<Button>(false);
				if (buttons != null)
				{
					for (int j = 0; j < buttons.Count; j++)
					{
						Button btn = buttons[j];
						if ((Object)(object)btn == (Object)null) continue;
						if (!((Component)btn).gameObject.activeInHierarchy) continue;
						string btnName = ((Object)((Component)btn).gameObject).name;

						// Only include ClaimButton entries
						if (btnName != "ClaimButton") continue;

						// Build a label from the reward slot parent text
						string label = BuildRewardSlotLabel(((Component)btn).transform);
						if (string.IsNullOrEmpty(label))
						{
							label = UIHelper.GetButtonLabel(btn);
							if (string.IsNullOrEmpty(label) || label.Length < 2) continue;
						}

						rewards.Add(new RewardItem
						{
							Label = label,
							ClaimButton = btn,
							Claimable = true
						});
					}
				}

				if (rewards.Count > 0)
				{
					if (string.IsNullOrEmpty(header)) header = Loc.Get("rewards_category_login");
					// Avoid duplicate categories with same header
					bool exists = false;
					foreach (var c in _rewardCategories)
						if (c.Name == header) { exists = true; break; }
					if (!exists)
						_rewardCategories.Add(new RewardCategory { Name = header, Rewards = { } });
					// Add rewards to matching category
					foreach (var c in _rewardCategories)
					{
						if (c.Name == header) { c.Rewards.AddRange(rewards); break; }
					}
				}
			}

			// Also scan season pass button
			for (int i = 0; i < allTransforms.Count; i++)
			{
				Transform t = allTransforms[i];
				if ((Object)(object)t == (Object)null) continue;
				if (!t.gameObject.activeInHierarchy) continue;
				string goName = ((Object)t.gameObject).name;
				if (goName != "SeasonPass_btn") continue;

				Button spBtn = t.gameObject.GetComponent<Button>();
				if ((Object)(object)spBtn == (Object)null) continue;

				string label = UIHelper.GetButtonLabel(spBtn);
				if (string.IsNullOrEmpty(label)) label = "Season Pass";
				_rewardCategories.Add(new RewardCategory
				{
					Name = Loc.Get("rewards_category_season"),
					Rewards = { new RewardItem { Label = label, ClaimButton = spBtn, Claimable = false } }
				});
				break;
			}

			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
				$"ScanRewards: {_rewardCategories.Count} categories found");
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "ScanRewards failed: " + ex.Message);
		}
	}

	/// <summary>Processes input while browsing the rewards menu.</summary>
	private void ProcessRewardsInput()
	{
		if (_rewardMenuLevel == 0)
		{
			// Category level
			if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
			{
				if (_rewardCategoryIndex > 0)
				{
					_rewardCategoryIndex--;
					AnnounceRewardCategory();
				}
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
			{
				if (_rewardCategoryIndex < _rewardCategories.Count - 1)
				{
					_rewardCategoryIndex++;
					AnnounceRewardCategory();
				}
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
			{
				if (_rewardCategoryIndex >= 0 && _rewardCategoryIndex < _rewardCategories.Count)
				{
					var cat = _rewardCategories[_rewardCategoryIndex];
					if (cat.Rewards.Count > 0)
					{
						_rewardMenuLevel = 1;
						_rewardIndex = 0;
						ScreenReader.Say(Loc.Get("rewards_category_entered", cat.Name, cat.Rewards.Count));
						AnnounceReward();
					}
				}
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsKeyDown(SDLInput.Key.Escape)
			         || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
			{
				_browsingRewards = false;
				ScreenReader.Say(Loc.Get("rewards_exited"));
			}
		}
		else if (_rewardMenuLevel == 1)
		{
			// Individual rewards
			if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
			{
				var cat = _rewardCategories[_rewardCategoryIndex];
				if (_rewardIndex > 0) { _rewardIndex--; AnnounceReward(); }
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
			{
				var cat = _rewardCategories[_rewardCategoryIndex];
				if (_rewardIndex < cat.Rewards.Count - 1) { _rewardIndex++; AnnounceReward(); }
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
			{
				// Claim reward — use SendPointerClick since carousel buttons may be off-screen
				var cat = _rewardCategories[_rewardCategoryIndex];
				if (_rewardIndex >= 0 && _rewardIndex < cat.Rewards.Count)
				{
					var reward = cat.Rewards[_rewardIndex];
					if ((Object)(object)reward.ClaimButton != (Object)null)
					{
						ScreenReader.Say(Loc.Get("dialog_activating", reward.Label));
						// Try pointer click event (works off-screen), then onClick, then mouse sim
						GameObject go = ((Component)reward.ClaimButton).gameObject;
						if (!UIHelper.SendPointerClick(go))
						{
							if (!UIHelper.ClickButton(reward.ClaimButton))
								UIHelper.SimulateMouseClick(go);
						}
					}
				}
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsKeyDown(SDLInput.Key.Escape)
			         || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
			{
				_rewardMenuLevel = 0;
				ScreenReader.Say(Loc.Get("rewards_back_to_categories"));
				AnnounceRewardCategory();
			}
		}
	}

	private void AnnounceRewardCategory()
	{
		if (_rewardCategoryIndex < 0 || _rewardCategoryIndex >= _rewardCategories.Count) return;
		var cat = _rewardCategories[_rewardCategoryIndex];
		ScreenReader.Say(Loc.Get("rewards_category", cat.Name, cat.Rewards.Count,
			_rewardCategoryIndex + 1, _rewardCategories.Count));
	}

	private void AnnounceReward()
	{
		if (_rewardCategoryIndex < 0 || _rewardCategoryIndex >= _rewardCategories.Count) return;
		var cat = _rewardCategories[_rewardCategoryIndex];
		if (_rewardIndex < 0 || _rewardIndex >= cat.Rewards.Count) return;
		var reward = cat.Rewards[_rewardIndex];
		string claimHint = reward.Claimable ? ", " + Loc.Get("rewards_claimable") : "";
		ScreenReader.Say(Loc.Get("rewards_item", reward.Label, _rewardIndex + 1, cat.Rewards.Count) + claimHint);
	}

	// --- Collection Browsing ---

	/// <summary>Enters the collection browsing mode.</summary>
	private void EnterCollection()
	{
		_browsingCollection = false;
		_inSubScreen = false;
		_onPlayScreen = false;
		_collectionDetailLevel = 0;
		_returnToCollection = false;
		ScanCollectionSections();
		ScanCollectionCards();
		if (_collectionCards.Count == 0)
		{
			// First attempt failed — try a delayed retry
			if (!_pendingCollectionScan)
			{
				ScreenReader.Say(Loc.Get("collection_loading"));
				_pendingCollectionScan = true;
				_collectionScanTime = Time.time + 2.0f;
			}
			else
			{
				// Second attempt also failed — fall back to DialogHandler
				ScreenReader.Say(Loc.Get("collection_no_cards"));
				_inSubScreen = true;
				_dialogHandler.ResetWithDelay(0.6f);
			}
			return;
		}
		_browsingCollection = true;
		_collectionIndex = 0;
		string sectionInfo = _collectionSections.Count > 1
			? " " + Loc.Get("collection_section_hint", _collectionSections[_collectionSectionIndex])
			: "";
		ScreenReader.Say(Loc.Get("collection_entered", _collectionCards.Count) + sectionInfo);
		AnnounceCollectionCard();
	}

	/// <summary>Finds the section containers (e.g. CardSectionContainer, DeckSectionContainer) under CollectionContentLayoutGroup.</summary>
	private void ScanCollectionSections()
	{
		_collectionSections.Clear();
		_collectionSectionIndex = 0;
		try
		{
			Il2CppArrayBase<Transform> allTransforms = Object.FindObjectsOfType<Transform>();
			if (allTransforms == null) return;

			for (int i = 0; i < allTransforms.Count; i++)
			{
				Transform t = allTransforms[i];
				if ((Object)(object)t == (Object)null) continue;
				if (!t.gameObject.activeInHierarchy) continue;
				string goName = ((Object)t.gameObject).name;
				if (goName == "CollectionContentLayoutGroup")
				{
					// Enumerate direct children that are section containers
					for (int c = 0; c < t.childCount; c++)
					{
						Transform child = t.GetChild(c);
						if ((Object)(object)child == (Object)null) continue;
						if (!child.gameObject.activeInHierarchy) continue;
						string childName = ((Object)child.gameObject).name;
						if (childName.Contains("SectionContainer", StringComparison.Ordinal))
						{
							// Clean up the name for display: "CardSectionContainer" → "Cards", "DeckEditSectionContainer" → "Deck"
							string displayName = childName
								.Replace("SectionContainer", "")
								.Replace("Section", "");
							if (displayName.EndsWith("Card")) displayName = "Cards";
							else if (displayName.Contains("Deck")) displayName = "Deck";
							else if (string.IsNullOrEmpty(displayName)) displayName = childName;
							_collectionSections.Add(displayName);
						}
					}
					break;
				}
			}

			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
				$"ScanCollectionSections: {_collectionSections.Count} sections found: {string.Join(", ", _collectionSections)}");
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "ScanCollectionSections failed: " + ex.Message);
		}
	}

	/// <summary>Scans collection cards filtered to a specific section by index.</summary>
	private void ScanCollectionCardsInSection(int sectionIndex)
	{
		_collectionCards.Clear();
		try
		{
			// Find the section container name for filtering
			string sectionContainerName = null;
			Il2CppArrayBase<Transform> allTransforms = Object.FindObjectsOfType<Transform>();
			if (allTransforms == null) return;

			for (int i = 0; i < allTransforms.Count; i++)
			{
				Transform t = allTransforms[i];
				if ((Object)(object)t == (Object)null) continue;
				if (!t.gameObject.activeInHierarchy) continue;
				string goName = ((Object)t.gameObject).name;
				if (goName == "CollectionContentLayoutGroup")
				{
					int sIdx = 0;
					for (int c = 0; c < t.childCount; c++)
					{
						Transform child = t.GetChild(c);
						if ((Object)(object)child == (Object)null) continue;
						if (!child.gameObject.activeInHierarchy) continue;
						string childName = ((Object)child.gameObject).name;
						if (childName.Contains("SectionContainer", StringComparison.Ordinal))
						{
							if (sIdx == sectionIndex)
							{
								sectionContainerName = childName;
								break;
							}
							sIdx++;
						}
					}
					break;
				}
			}

			if (sectionContainerName == null)
			{
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
					$"ScanCollectionCardsInSection: section {sectionIndex} not found");
				return;
			}

			// Scan CardRenderers under this specific section
			Il2CppArrayBase<CardRenderer> allRenderers = Object.FindObjectsOfType<CardRenderer>();
			if (allRenderers == null || allRenderers.Count == 0) return;

			for (int i = 0; i < allRenderers.Count; i++)
			{
				CardRenderer renderer = allRenderers[i];
				if ((Object)(object)renderer == (Object)null) continue;
				if (!((Component)renderer).gameObject.activeInHierarchy) continue;

				bool isInSection = false;
				Button parentButton = null;
				Transform t = ((Component)renderer).transform;
				int depth = 0;
				while (t != null && depth < 20)
				{
					string pName = ((Object)t.gameObject).name;
					if (pName == sectionContainerName)
					{
						isInSection = true;
						break;
					}
					if (pName.Contains("CardDetails", StringComparison.Ordinal))
						break;
					if (parentButton == null)
					{
						Button btn = t.gameObject.GetComponent<Button>();
						if ((Object)(object)btn != (Object)null)
							parentButton = btn;
					}
					t = t.parent;
					depth++;
				}

				if (!isInSection) continue;

				string cardName = "";
				try { cardName = renderer.CardName; } catch { }
				if (string.IsNullOrEmpty(cardName) || cardName.Length < 2) continue;

				bool exists = false;
				foreach (var c in _collectionCards)
					if (c.Name == cardName) { exists = true; break; }
				if (exists) continue;

				_collectionCards.Add(new CollectionCard
				{
					Name = cardName,
					Button = parentButton,
					Renderer = renderer
				});
			}

			_collectionCards.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
				$"ScanCollectionCardsInSection({sectionIndex}): {_collectionCards.Count} cards in '{sectionContainerName}'");
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
				"ScanCollectionCardsInSection failed: " + ex.Message);
		}
	}

	/// <summary>Scans visible collection cards from the CollectionViewLandscape hierarchy.</summary>
	private void ScanCollectionCards()
	{
		_collectionCards.Clear();
		try
		{
			// Strategy: find all CardRenderers in the scene that are under CollectionViewLandscape,
			// then match each to its parent button (LandscapeCollectionCardView)
			Il2CppArrayBase<CardRenderer> allRenderers = Object.FindObjectsOfType<CardRenderer>();
			if (allRenderers == null || allRenderers.Count == 0)
			{
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "No CardRenderers found in scene");
				// Fallback: try finding collection by button scan
				ScanCollectionCardsByButton();
				return;
			}

			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
				$"Found {allRenderers.Count} CardRenderers in scene");

			for (int i = 0; i < allRenderers.Count; i++)
			{
				CardRenderer renderer = allRenderers[i];
				if ((Object)(object)renderer == (Object)null) continue;
				if (!((Component)renderer).gameObject.activeInHierarchy) continue;

				// Check if this CardRenderer is under CollectionViewLandscape
				bool isInCollection = false;
				Button parentButton = null;
				Transform t = ((Component)renderer).transform;
				int depth = 0;
				while (t != null && depth < 20)
				{
					string pName = ((Object)t.gameObject).name;
					if (pName.Contains("CollectionViewLandscape", StringComparison.Ordinal))
					{
						isInCollection = true;
						break;
					}
					// Also check if this is a CardDetails view (skip those)
					if (pName.Contains("CardDetails", StringComparison.Ordinal))
						break;
					// Check for parent button
					if (parentButton == null)
					{
						Button btn = t.gameObject.GetComponent<Button>();
						if ((Object)(object)btn != (Object)null)
							parentButton = btn;
					}
					t = t.parent;
					depth++;
				}

				if (!isInCollection) continue;

				string cardName = "";
				try { cardName = renderer.CardName; } catch { }

				if (string.IsNullOrEmpty(cardName) || cardName.Length < 2)
				{
					// Debug: log what we found
					DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
						$"CardRenderer under collection has empty CardName, GO: {((Object)((Component)renderer).gameObject).name}");
					continue;
				}

				// Skip duplicates (same card name)
				bool exists = false;
				foreach (var c in _collectionCards)
					if (c.Name == cardName) { exists = true; break; }
				if (exists) continue;

				_collectionCards.Add(new CollectionCard
				{
					Name = cardName,
					Button = parentButton,
					Renderer = renderer
				});
			}

			// Sort alphabetically for consistent browsing
			_collectionCards.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
				$"ScanCollectionCards (CardRenderer approach): {_collectionCards.Count} cards found");

			// If CardRenderer approach found nothing, try button scan fallback
			if (_collectionCards.Count == 0)
				ScanCollectionCardsByButton();
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "ScanCollectionCards failed: " + ex.Message);
		}
	}

	/// <summary>Fallback: scan collection by finding buttons under CollectionRowListScroller and reading card names.</summary>
	private void ScanCollectionCardsByButton()
	{
		try
		{
			// Find the collection scroller
			Il2CppArrayBase<Transform> allTransforms = Object.FindObjectsOfType<Transform>();
			if (allTransforms == null) return;

			GameObject collectionRoot = null;
			for (int i = 0; i < allTransforms.Count; i++)
			{
				Transform t = allTransforms[i];
				if ((Object)(object)t == (Object)null) continue;
				if (!t.gameObject.activeInHierarchy) continue;
				string goName = ((Object)t.gameObject).name;
				if (goName.Contains("CollectionRowListScroller", StringComparison.Ordinal) ||
				    goName.Contains("CollectionViewLandscape", StringComparison.Ordinal))
				{
					collectionRoot = t.gameObject;
					DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
						"Collection root found: " + goName);
					break;
				}
			}

			if ((Object)(object)collectionRoot == (Object)null)
			{
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "Collection root not found");
				return;
			}

			// Find all buttons and try to get card names via CardRenderer child components
			Il2CppArrayBase<Button> buttons = collectionRoot.GetComponentsInChildren<Button>(false);
			if (buttons == null) return;

			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
				$"Collection buttons found: {buttons.Count}");

			// Debug: log components on the first collection card button
			bool loggedFirst = false;

			for (int i = 0; i < buttons.Count; i++)
			{
				Button btn = buttons[i];
				if ((Object)(object)btn == (Object)null) continue;
				if (!((Component)btn).gameObject.activeInHierarchy) continue;

				string goName = ((Object)((Component)btn).gameObject).name;
				if (!goName.Contains("CollectionCardView", StringComparison.OrdinalIgnoreCase))
					continue;

				// Debug: dump all components on first card
				if (!loggedFirst)
				{
					loggedFirst = true;
					try
					{
						Il2CppArrayBase<Component> components = ((Component)btn).GetComponentsInChildren<Component>(true);
						if (components != null)
						{
							for (int j = 0; j < components.Count && j < 30; j++)
							{
								Component comp = components[j];
								if ((Object)(object)comp == (Object)null) continue;
								string compType = ((object)comp).GetType().Name;
								string compGo = ((Object)((Component)comp).gameObject).name;
								DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
									$"  CollCard component [{j}]: {compType} on {compGo}");
							}
						}
					}
					catch { }
				}

				// Try CardRenderer
				string cardName = "";
				CardRenderer renderer = null;
				try
				{
					renderer = ((Component)btn).GetComponentInChildren<CardRenderer>(true);
					if (renderer != null)
					{
						cardName = renderer.CardName;
						DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
							$"CardRenderer found on {goName}: CardName='{cardName}'");
					}
				}
				catch (Exception ex)
				{
					DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
						$"CardRenderer access failed on {goName}: {ex.Message}");
				}

				if (string.IsNullOrEmpty(cardName) || cardName.Length < 2)
					continue;

				_collectionCards.Add(new CollectionCard
				{
					Name = cardName,
					Button = btn,
					Renderer = renderer
				});
			}

			_collectionCards.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
				$"ScanCollectionCardsByButton: {_collectionCards.Count} cards found");
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
				"ScanCollectionCardsByButton failed: " + ex.Message);
		}
	}

	/// <summary>Processes input while browsing the collection.</summary>
	private void ProcessCollectionInput()
	{
		if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
		{
			if (_collectionCards.Count == 0) return;
			_collectionIndex = (_collectionIndex - 1 + _collectionCards.Count) % _collectionCards.Count;
			_collectionDetailLevel = 0;
			AnnounceCollectionCard();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			if (_collectionCards.Count == 0) return;
			_collectionIndex = (_collectionIndex + 1) % _collectionCards.Count;
			_collectionDetailLevel = 0;
			AnnounceCollectionCard();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
		{
			if (_collectionDetailLevel < 3) _collectionDetailLevel++;
			AnnounceCollectionDetail();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
		{
			if (_collectionDetailLevel > 0)
			{
				_collectionDetailLevel--;
				if (_collectionDetailLevel == 0)
					AnnounceCollectionCard();
				else
					AnnounceCollectionDetail();
			}
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			// Open card detail view
			if (_collectionIndex >= 0 && _collectionIndex < _collectionCards.Count)
			{
				var card = _collectionCards[_collectionIndex];
				if ((Object)(object)card.Button != (Object)null)
				{
					ScreenReader.Say(Loc.Get("collection_opening", card.Name));
					UIHelper.ClickButtonWithFallback(card.Button);
					// Switch to dialog handler for the detail view
					_browsingCollection = false;
					_inSubScreen = true;
					_returnToCollection = true;
					_dialogHandler.ResetWithDelay(0.8f);
				}
			}
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Tab))
		{
			// Cycle collection sections (Cards, Decks, etc.)
			if (_collectionSections.Count > 1)
			{
				bool shift = SDLInput.IsShiftHeld;
				if (shift)
					_collectionSectionIndex = (_collectionSectionIndex - 1 + _collectionSections.Count) % _collectionSections.Count;
				else
					_collectionSectionIndex = (_collectionSectionIndex + 1) % _collectionSections.Count;
				string sectionName = _collectionSections[_collectionSectionIndex];
				// Rescan for this section's cards
				ScanCollectionCardsInSection(_collectionSectionIndex);
				if (_collectionCards.Count > 0)
				{
					_collectionIndex = 0;
					_collectionDetailLevel = 0;
					// For deck section, include deck name in announcement
					string deckInfo = sectionName.Contains("Deck") ? ReadDeckEditorName() : "";
					if (!string.IsNullOrEmpty(deckInfo))
						ScreenReader.Say(Loc.Get("collection_section_items", sectionName + ": " + deckInfo, _collectionCards.Count));
					else
						ScreenReader.Say(Loc.Get("collection_section_items", sectionName, _collectionCards.Count));
					AnnounceCollectionCard();
				}
				else
				{
					ScreenReader.Say(Loc.Get("collection_section", sectionName, _collectionSectionIndex + 1, _collectionSections.Count));
					ScreenReader.SayQueued(Loc.Get("collection_no_cards"));
				}
			}
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsKeyDown(SDLInput.Key.Escape)
		         || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
		{
			_browsingCollection = false;
			_collectionDetailLevel = 0;
			_returnToCollection = false;
			TryClickGameBackButton();
			ScreenReader.Say(Loc.Get("collection_exited"));
			// Return to menu bar
			ScreenReader.SayQueued(Loc.Get("menu_nav_focus"));
			AnnounceFocused();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.R))
		{
			// Rescan collection cards (virtual scrolling may have loaded new ones)
			ScanCollectionCards();
			if (_collectionCards.Count > 0)
			{
				_collectionIndex = 0;
				_collectionDetailLevel = 0;
				ScreenReader.Say(Loc.Get("collection_rescanned", _collectionCards.Count));
				AnnounceCollectionCard();
			}
			else
			{
				ScreenReader.Say(Loc.Get("collection_no_cards"));
			}
		}
	}

	private void AnnounceCollectionCard()
	{
		if (_collectionIndex < 0 || _collectionIndex >= _collectionCards.Count) return;
		var card = _collectionCards[_collectionIndex];
		ScreenReader.Say(Loc.Get("collection_card", card.Name, _collectionIndex + 1, _collectionCards.Count));
	}

	/// <summary>Reads the deck name from the deck editor's InputField_DeckName or Text_DeckName.</summary>
	private string ReadDeckEditorName()
	{
		try
		{
			Il2CppArrayBase<TMP_Text> allTexts = Object.FindObjectsOfType<TMP_Text>();
			if (allTexts == null) return "";
			for (int i = 0; i < allTexts.Count; i++)
			{
				TMP_Text tmp = allTexts[i];
				if ((Object)(object)tmp == (Object)null) continue;
				if (!((Component)tmp).gameObject.activeInHierarchy) continue;
				string goName = ((Object)((Component)tmp).gameObject).name;
				if (goName == "Text_DeckName")
				{
					string val = UIHelper.StripRichText((tmp.text ?? "").Trim());
					if (!string.IsNullOrEmpty(val) && val.Length >= 2)
						return val;
				}
			}
		}
		catch { }
		return "";
	}

	private void AnnounceCollectionDetail()
	{
		if (_collectionIndex < 0 || _collectionIndex >= _collectionCards.Count) return;
		var card = _collectionCards[_collectionIndex];

		switch (_collectionDetailLevel)
		{
			case 1:
				// Cost
				try
				{
					if (card.Renderer != null)
					{
						CardValueView cv = card.Renderer._CostValueView;
						if ((Object)(object)cv != (Object)null)
						{
							ScreenReader.Say(Loc.Get("deck_card_cost", cv.Value));
							return;
						}
					}
				}
				catch { }
				ScreenReader.Say(Loc.Get("deck_card_cost", "?"));
				break;
			case 2:
				// Power
				try
				{
					if (card.Renderer != null)
					{
						CardValueView pv = card.Renderer._PowerValueView;
						if ((Object)(object)pv != (Object)null)
						{
							ScreenReader.Say(Loc.Get("deck_card_power", pv.Value));
							return;
						}
					}
				}
				catch { }
				ScreenReader.Say(Loc.Get("deck_card_power", "?"));
				break;
			case 3:
				// Ability text
				string ability = GetCollectionCardAbility(card);
				ScreenReader.Say(!string.IsNullOrEmpty(ability) ? ability : Loc.Get("deck_card_no_ability"));
				break;
		}
	}

	/// <summary>Tries to read ability text from a collection card's CardRenderer.</summary>
	private string GetCollectionCardAbility(CollectionCard card)
	{
		if (card.Renderer == null) return "";
		try
		{
			TMP_Text abilityTmp = card.Renderer._AbilityText;
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

		// Fallback: search TMP_Text children for ability text
		try
		{
			Il2CppArrayBase<TMP_Text> texts = ((Component)card.Button).GetComponentsInChildren<TMP_Text>(true);
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
					if (cleaned.Length > 3 && cleaned != card.Name) return cleaned;
				}
			}
		}
		catch { }
		return "";
	}

	/// <summary>Reads rank and season info from the Play screen.</summary>
	private void ReadPlayScreenInfo()
	{
		string rank = ReadRankText();
		string season = ReadSeasonText();
		string msg = "";
		if (!string.IsNullOrEmpty(rank)) msg += rank;
		if (!string.IsNullOrEmpty(season)) msg += " " + season;
		if (string.IsNullOrEmpty(msg))
			msg = Loc.Get("play_no_info");
		ScreenReader.Say(msg);
	}

	private string ReadRankText()
	{
		try
		{
			Il2CppArrayBase<TMP_Text> allTexts = Object.FindObjectsOfType<TMP_Text>();
			if (allTexts == null) return "";

			string rankNumber = "";
			string rankProgress = "";

			for (int i = 0; i < allTexts.Count; i++)
			{
				TMP_Text tmp = allTexts[i];
				if ((Object)(object)tmp == (Object)null) continue;
				if (!((Component)tmp).gameObject.activeInHierarchy) continue;
				string goName = ((Object)((Component)tmp).gameObject).name;

				// Text_Rank under container_rank/img_bannerRank has the actual rank number
				if (goName == "Text_Rank")
				{
					string val = (tmp.text ?? "").Trim();
					if (!string.IsNullOrEmpty(val) && val.Length <= 4)
						rankNumber = val;
				}
				// Text_Progress under rankmeter_bg/info shows "0 / 1"
				else if (goName == "Text_Progress")
				{
					string parentPath = "";
					try
					{
						Transform p = ((Component)tmp).transform.parent;
						if (p != null) parentPath = ((Object)p.gameObject).name;
					}
					catch { }
					if (parentPath == "info")
						rankProgress = (tmp.text ?? "").Trim();
				}
			}

			if (!string.IsNullOrEmpty(rankNumber))
			{
				string msg = Loc.Get("play_rank", rankNumber);
				if (!string.IsNullOrEmpty(rankProgress))
					msg += ", " + rankProgress;
				return msg;
			}
		}
		catch { }
		return "";
	}

	private string ReadSeasonText()
	{
		try
		{
			Il2CppArrayBase<TMP_Text> allTexts = Object.FindObjectsOfType<TMP_Text>();
			if (allTexts == null) return "";

			for (int i = 0; i < allTexts.Count; i++)
			{
				TMP_Text tmp = allTexts[i];
				if ((Object)(object)tmp == (Object)null) continue;
				if (!((Component)tmp).gameObject.activeInHierarchy) continue;
				string goName = ((Object)((Component)tmp).gameObject).name;
				if (goName == "seasonpass_title")
				{
					string season = UIHelper.StripRichText(tmp.text ?? "").Trim();
					if (!string.IsNullOrEmpty(season))
						return Loc.Get("play_season", season);
				}
			}
		}
		catch { }
		return "";
	}

	private void ProcessPlayInput()
	{
		if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			if ((Object)(object)_playButton != (Object)null)
			{
				ScreenReader.Say(Loc.Get("play_starting"));
				UIHelper.ClickButton(_playButton);
			}
			else
			{
				ScreenReader.Say(Loc.Get("play_no_button"));
			}
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.S))
		{
			// Open deck selector
			if ((Object)(object)_deckLeftButton != (Object)null)
			{
				ScreenReader.Say(Loc.Get("play_opening_deck_selector"));
				UIHelper.ClickButton(_deckLeftButton);
				// Switch to sub-screen mode so DialogHandler handles the deck tray
				// Use delayed scan — the PlayDeckTray takes a moment to render
				_onPlayScreen = false;
				_inSubScreen = true;
				_returnToPlayScreen = true;
				_dialogHandler.ResetWithDelay(0.6f);
			}
			else
			{
				ScreenReader.Say(Loc.Get("play_no_deck_switch"));
			}
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.D))
		{
			ScanDeckCards();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.M))
		{
			OpenMissionsMenu();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.I))
		{
			ReadPlayScreenInfo();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.R))
		{
			OpenRewardsMenu();
		}
	}

	// --- Deck Card Browsing ---

	private void ScanDeckCards()
	{
		_deckCards.Clear();
		try
		{
			// First try to find cards under a deck-related parent container
			// This avoids picking up collection/album cards
			GameObject deckParent = FindDeckContainer();
			Il2CppArrayBase<CardView> cards;

			if ((Object)(object)deckParent != (Object)null)
			{
				cards = deckParent.GetComponentsInChildren<CardView>(false);
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
					"Scanning deck cards under: " + ((Object)deckParent).name);
			}
			else
			{
				// Fallback: all CardViews, but filter aggressively
				cards = Object.FindObjectsOfType<CardView>();
				DebugLogger.Log(LogCategory.Handler, "MainMenuHandler",
					"No deck container found, scanning all CardViews");
			}

			if (cards != null)
			{
				for (int i = 0; i < cards.Count; i++)
				{
					CardView cv = cards[i];
					if ((Object)(object)cv == (Object)null) continue;
					if (!((Component)cv).gameObject.activeInHierarchy) continue;

					// Skip cards with no real name (unloaded collection cards)
					string name = GetDeckCardName(cv);
					if (string.IsNullOrEmpty(name)) continue;
					if (name.Contains("Card View", StringComparison.OrdinalIgnoreCase)) continue;
					if (name.Contains("Variant List", StringComparison.OrdinalIgnoreCase)) continue;
					if (name.Contains("Album Card", StringComparison.OrdinalIgnoreCase)) continue;
					if (name == "Card" || name == "Unknown card") continue;

					_deckCards.Add(cv);
				}
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "MainMenuHandler", "ScanDeckCards failed: " + ex.Message);
		}

		if (_deckCards.Count == 0)
		{
			ScreenReader.Say(Loc.Get("deck_no_cards"));
			return;
		}

		_browsingDeck = true;
		_deckCardIndex = 0;
		_deckDetailLevel = 0;
		ScreenReader.Say(Loc.Get("deck_browsing", _deckCards.Count));
		AnnounceDeckCard();
	}

	/// <summary>Finds a deck-related container to scope card scanning.</summary>
	private GameObject FindDeckContainer()
	{
		try
		{
			// Look for common deck container names
			string[] deckNames = { "DeckTray", "PlayDeckTray", "DeckList", "DeckCards", "CardList", "SelectDeck" };
			Il2CppArrayBase<Transform> all = Object.FindObjectsOfType<Transform>();
			if (all == null) return null;
			for (int i = 0; i < all.Count; i++)
			{
				Transform t = all[i];
				if ((Object)(object)t == (Object)null) continue;
				if (!t.gameObject.activeInHierarchy) continue;
				string name = ((Object)t.gameObject).name;
				foreach (string dn in deckNames)
				{
					if (name.Contains(dn, StringComparison.OrdinalIgnoreCase))
					{
						// Must have CardViews as children
						Il2CppArrayBase<CardView> cvs = t.gameObject.GetComponentsInChildren<CardView>(false);
						if (cvs != null && cvs.Count > 0)
							return t.gameObject;
					}
				}
			}
		}
		catch { }
		return null;
	}

	private void ProcessDeckInput()
	{
		if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
		{
			_deckCardIndex = (_deckCardIndex - 1 + _deckCards.Count) % _deckCards.Count;
			_deckDetailLevel = 0;
			AnnounceDeckCard();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			_deckCardIndex = (_deckCardIndex + 1) % _deckCards.Count;
			_deckDetailLevel = 0;
			AnnounceDeckCard();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
		{
			if (_deckDetailLevel < 3) _deckDetailLevel++;
			AnnounceDeckDetail();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
		{
			if (_deckDetailLevel > 0)
			{
				_deckDetailLevel--;
				if (_deckDetailLevel == 0) AnnounceDeckCard();
				else AnnounceDeckDetail();
			}
			else
			{
				_browsingDeck = false;
				_deckDetailLevel = 0;
				ScreenReader.Say(Loc.Get("deck_exit"));
			}
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Escape) || SDLInput.IsKeyDown(SDLInput.Key.Backspace))
		{
			_browsingDeck = false;
			_deckDetailLevel = 0;
			ScreenReader.Say(Loc.Get("deck_exit"));
		}
	}

	private void AnnounceDeckCard()
	{
		if (_deckCardIndex < 0 || _deckCardIndex >= _deckCards.Count) return;
		string name = GetDeckCardName(_deckCards[_deckCardIndex]);
		ScreenReader.Say(Loc.Get("deck_card", name, _deckCardIndex + 1, _deckCards.Count));
	}

	private void AnnounceDeckDetail()
	{
		if (_deckCardIndex < 0 || _deckCardIndex >= _deckCards.Count) return;
		CardView card = _deckCards[_deckCardIndex];

		switch (_deckDetailLevel)
		{
			case 1:
				try
				{
					CardValueView cv = ((CardRenderer)card)._CostValueView;
					if ((Object)(object)cv != (Object)null) { ScreenReader.Say(Loc.Get("deck_card_cost", cv.Value)); return; }
				}
				catch { }
				ScreenReader.Say(Loc.Get("deck_card_cost", "?"));
				break;
			case 2:
				try
				{
					CardValueView pv = ((CardRenderer)card)._PowerValueView;
					if ((Object)(object)pv != (Object)null) { ScreenReader.Say(Loc.Get("deck_card_power", pv.Value)); return; }
				}
				catch { }
				ScreenReader.Say(Loc.Get("deck_card_power", "?"));
				break;
			case 3:
				string ability = GetDeckCardAbility(card);
				ScreenReader.Say(!string.IsNullOrEmpty(ability) ? ability : Loc.Get("deck_card_no_ability"));
				break;
		}
	}

	private string GetDeckCardName(CardView card)
	{
		if ((Object)(object)card == (Object)null) return null;
		// 1. Try CardRenderer.CardName
		try
		{
			string n = ((CardRenderer)card).CardName;
			if (!string.IsNullOrEmpty(n)) return UIHelper.StripRichText(n);
		}
		catch { }
		// 2. Try CardDefId
		try
		{
			var id = card.CardDefId;
			if (id != null) { string s = id.ToString(); if (!string.IsNullOrEmpty(s) && s != "0") return UIHelper.StripRichText(s); }
		}
		catch { }
		// 3. Try TMP_Text children — look for the name label (short text, not a number, not ability text)
		try
		{
			Il2CppArrayBase<TMP_Text> texts = ((Component)card).GetComponentsInChildren<TMP_Text>(false);
			if (texts != null)
			{
				for (int i = 0; i < texts.Count; i++)
				{
					TMP_Text tmp = texts[i];
					if ((Object)(object)tmp == (Object)null) continue;
					if (!((Component)tmp).gameObject.activeInHierarchy) continue;
					string t = UIHelper.StripRichText(tmp.text ?? "").Trim();
					// Card names are typically 3-30 chars, not numbers, not ability descriptions
					if (t.Length >= 2 && t.Length <= 35 && !int.TryParse(t, out _)
					    && !t.Contains("Ongoing:") && !t.Contains("On Reveal:")
					    && !t.Contains("Activate:") && !t.Contains("When"))
					{
						return t;
					}
				}
			}
		}
		catch { }
		// 4. Fallback: clean GameObject name but skip "Variant List" and "Card View" patterns
		try
		{
			string goName = ((Object)((Component)card).gameObject).name;
			if (goName.Contains("Variant", StringComparison.OrdinalIgnoreCase)
			    || goName.Contains("Card View", StringComparison.OrdinalIgnoreCase)
			    || goName.Contains("Album", StringComparison.OrdinalIgnoreCase))
			{
				return null; // Don't return useless names
			}
			string g = UIHelper.CleanGameObjectName(goName);
			if (!string.IsNullOrEmpty(g) && g != "Card") return g;
		}
		catch { }
		return null;
	}

	private string GetDeckCardAbility(CardView card)
	{
		if ((Object)(object)card == (Object)null) return "";
		try
		{
			Il2CppArrayBase<TMP_Text> texts = ((Component)card).GetComponentsInChildren<TMP_Text>();
			if (texts == null) return "";
			string cardName = GetDeckCardName(card);
			for (int i = 0; i < texts.Count; i++)
			{
				TMP_Text tmp = texts[i];
				if ((Object)(object)tmp == (Object)null || !((Component)tmp).gameObject.activeInHierarchy) continue;
				string text = tmp.text;
				if (string.IsNullOrWhiteSpace(text)) continue;
				text = UIHelper.StripRichText(text.Trim());
				if (text.Length < 5 || text == cardName || int.TryParse(text, out _)) continue;
				return text;
			}
		}
		catch { }
		return "";
	}

	private bool IsNumericLabel(string label)
	{
		if (string.IsNullOrEmpty(label)) return false;
		foreach (char c in label)
		{
			if (!char.IsDigit(c) && c != ',' && c != '.' && c != '/' && c != ' ' && c != '%')
				return false;
		}
		return true;
	}

	private static readonly string[] _promoPatterns = new[]
	{
		"challenger", "awaits", "welcome", "new season", "limited time",
		"special offer", "featured", "upgrade", "unlock", "coming soon"
	};

	private bool IsPromoText(string text)
	{
		if (string.IsNullOrEmpty(text)) return false;
		// Deck names are typically short (1-3 words). Long text near a Deck parent is likely promo.
		if (text.Length > 30) return true;
		foreach (string pattern in _promoPatterns)
		{
			if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}
}
