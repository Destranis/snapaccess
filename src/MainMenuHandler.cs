using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppCubeUnity.App.Navigator;
using Il2CppSecondDinner.CubeRendering.Card;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using MelonLoader;

namespace SnapAccess;

/// <summary>
/// Manages navigation within the main menu hub.
/// </summary>
public class MainMenuHandler : IHandler
{
	private class MenuEntry
	{
		public string LocKey;
		public Action<Navigator> Activate;
	}

	private Navigator _navigator;
	private int _focusIndex = -1;
	private bool _wasShown = false;
	private bool _inSubScreen = false;
	private float _inputBlockUntil = 0f;
	private readonly DialogHandler _dialogHandler;
	private readonly FriendlyMatchHandler _friendlyMatchHandler;
	private readonly MissionsHandler _missionsHandler;

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

	public MainMenuHandler(DialogHandler dialogHandler, FriendlyMatchHandler friendlyMatchHandler, MissionsHandler missionsHandler)
	{
		_dialogHandler = dialogHandler;
		_friendlyMatchHandler = friendlyMatchHandler;
		_missionsHandler = missionsHandler;
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
                ScreenReader.Say("Opening Alliances");
                UIHelper.ClickButton(b);
                _inSubScreen = true;
                _dialogHandler.ResetWithDelay(0.8f);
            }
        } });
	}

	public bool IsActive => (Object)(object)_navigator != (Object)null && IsNavigatorVisible();

	public bool InSubScreen => _inSubScreen && !_onPlayScreen && !_browsingCollection && !_inGameModes;

	public bool Update()
	{
		if (!FindNavigator()) return false;

		bool visible = IsNavigatorVisible();
		if (visible && !_wasShown) OnNavigatorShown();
		else if (!visible && _wasShown) OnNavigatorHidden();
		_wasShown = visible;

		if (!visible) return false;

		bool consumed = ProcessInput();

		if (_onPlayScreen || _browsingCollection || _browsingRewards || _browsingRewardDetails || _inGameModes) return true;
		return consumed;
	}

	/// <summary>Returns true if input was consumed and no other handler should process it.</summary>
	private bool ProcessInput()
	{
		if (Time.time < _inputBlockUntil) return false;

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
		return false;
	}

	private void HandleBackCommand()
	{
		// Deactivate any sub-handlers first
		_friendlyMatchHandler.Reset();
		_missionsHandler.Reset();

		if (_browsingRewardDetails)
		{
			_browsingRewardDetails = false;
			_rewardDays.Clear();
			// Click the back button on the reward detail screen
			TryClickRewardDetailBackButton();
			_browsingRewards = true;
			ScreenReader.Say("Back to reward events.");
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
				ScreenReader.Say(_collectionSections[_collectionSectionIndex] + ", " + (_collectionSectionIndex + 1) + " of " + _collectionSections.Count);
				return;
			}
			_browsingCollection = false;
			_deckActionMode = false;
			_inSubScreen = false;
			TryClickGameBackButton();
			ScreenReader.Say("Exiting collection.");
			AnnounceFocused();
		}
		else if (_inGameModes)
		{
			_inGameModes = false;
			_inSubScreen = false;
			ScreenReader.Say(Loc.Get("menu_nav_focus"));
			AnnounceFocused();
		}
		else if (_inSubScreen)
		{
			TryClickGameBackButton();
			_inSubScreen = false;
			ScreenReader.Say(Loc.Get("menu_nav_focus"));
			AnnounceFocused();
		}
		else if (_onPlayScreen)
		{
			_onPlayScreen = false;
			ScreenReader.Say(Loc.Get("menu_nav_focus"));
			AnnounceFocused();
		}
	}

	private void HandleMenuBarInput()
	{
		if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft)) MoveFocus(-1);
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight)) MoveFocus(1);
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
		ScreenReader.Say(Loc.Get(entry.LocKey) + ", " + (_focusIndex + 1) + " of " + _entries.Count);
	}

	private void ActivateFocused()
	{
		if (_focusIndex < 0 || _focusIndex >= _entries.Count) return;
		var entry = _entries[_focusIndex];
		ScreenReader.Say(Loc.Get("menu_activated", Loc.Get(entry.LocKey)));
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
		else { _inSubScreen = true; _dialogHandler.ResetWithDelay(0.5f); }
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
		ScreenReader.Say(Loc.Get("play_screen") + " Deck: " + _deckName);
		AnnouncePlayCategory();
	}

	private void ProcessPlayInput()
	{
		if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
		{
			_playCategory = (PlayMenuCategory)(((int)_playCategory - 1 + _playMenuCount) % _playMenuCount);
			AnnouncePlayCategory();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			_playCategory = (PlayMenuCategory)(((int)_playCategory + 1) % _playMenuCount);
			AnnouncePlayCategory();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
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
			PlayMenuCategory.SelectDeck => "Select Deck (Current: " + _deckName + ")",
			PlayMenuCategory.EditDeck => "Edit Current Deck",
			PlayMenuCategory.Missions => "Missions",
			PlayMenuCategory.Rewards => "Rewards",
			PlayMenuCategory.GameInfo => "Season & Rank Info",
			_ => "Unknown"
		};
		ScreenReader.Say(label + ", " + ((int)_playCategory + 1) + " of " + _playMenuCount);
	}

	private void AnnouncePlayCategoryDetails()
	{
		string detail = _playCategory switch
		{
			PlayMenuCategory.StartGame => "Press Enter to start a match with deck: " + _deckName,
			PlayMenuCategory.SelectDeck => "Current deck: " + _deckName + ". Press Enter to switch.",
			PlayMenuCategory.EditDeck => "Opens the deck editor for " + _deckName,
			PlayMenuCategory.Missions => "View daily and season missions",
			PlayMenuCategory.Rewards => "View and claim available rewards",
			PlayMenuCategory.GameInfo => ReadRankText() + ". " + ReadSeasonText(),
			_ => ""
		};
		ScreenReader.Say(detail);
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
					ScreenReader.Say(_startButtonLabel);
					// btn_start often ignores onClick — use SendPointerClick then mouse fallback
					if (!UIHelper.SendPointerClick(((Component)_playButton).gameObject))
						UIHelper.SimulateMouseClick(((Component)_playButton).gameObject);
				}
				break;
			case PlayMenuCategory.SelectDeck:
				Button deckBtn = _deckLeftButton ?? _deckRightButton;
				if (deckBtn != null) UIHelper.ClickButtonWithFallback(deckBtn);
				else ScreenReader.Say("Deck selection not available.");
				break;
			case PlayMenuCategory.EditDeck:
				TryOpenDeckEditor();
				break;
			case PlayMenuCategory.Missions:
				_missionsHandler.Activate();
				break;
			case PlayMenuCategory.Rewards:
				ScanRewards();
				if (_rewardEvents.Count > 0)
				{
					_browsingRewards = true;
					_rewardIndex = 0;
					ScreenReader.Say("Rewards, " + _rewardEvents.Count + " events.");
					AnnounceReward();
				}
				else
				{
					ScreenReader.Say("No rewards found.");
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
			TMP_Text[] texts = Object.FindObjectsOfType<TMP_Text>();
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
		ScreenReader.Say(rank + ". " + season);
	}

	private string ReadRankText()
	{
        TMP_Text[] texts = Object.FindObjectsOfType<TMP_Text>();
        foreach (var t in texts) {
            if (t.gameObject.name == "Text_Rank" && t.gameObject.activeInHierarchy) return "Rank " + t.text;
        }
		return "Rank unknown";
	}

	private string ReadSeasonText()
	{
        TMP_Text[] texts = Object.FindObjectsOfType<TMP_Text>();
        foreach (var t in texts) {
            if (t.gameObject.name == "seasonpass_title" && t.gameObject.activeInHierarchy) return "Season: " + t.text;
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
			ScreenReader.Say("Game Modes. " + _gameModeEntries.Count + " modes.");
			AnnounceGameMode();
		}
		else
		{
			ScreenReader.Say("No game modes found.");
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
					TMP_Text[] allTexts = child.GetComponentsInChildren<TMP_Text>(true);
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
		if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
		{
			if (_gameModeEntries.Count == 0) return;
			_gameModeIndex = (_gameModeIndex - 1 + _gameModeEntries.Count) % _gameModeEntries.Count;
			AnnounceGameMode();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			if (_gameModeEntries.Count == 0) return;
			_gameModeIndex = (_gameModeIndex + 1) % _gameModeEntries.Count;
			AnnounceGameMode();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
		{
			// Read details
			if (_gameModeIndex >= 0 && _gameModeIndex < _gameModeEntries.Count)
			{
				var mode = _gameModeEntries[_gameModeIndex];
				if (mode.IsLocked && !string.IsNullOrEmpty(mode.LockReason))
					ScreenReader.Say(mode.LockReason);
				else if (mode.IsLocked)
					ScreenReader.Say("This mode is locked.");
				else
					ScreenReader.Say(mode.Name + ". Press Enter to open.");
			}
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			if (_gameModeIndex >= 0 && _gameModeIndex < _gameModeEntries.Count)
			{
				var mode = _gameModeEntries[_gameModeIndex];
				if (mode.IsLocked)
				{
					ScreenReader.Say("Locked. " + (string.IsNullOrEmpty(mode.LockReason) ? "" : mode.LockReason));
				}
				else if (mode.Button != null)
				{
					ScreenReader.Say("Opening " + mode.Name);
					if (!UIHelper.ClickButton(mode.Button))
						UIHelper.SimulateMouseClick(mode.GameObject);
					_inGameModes = false;
					_inSubScreen = true;
					_dialogHandler.ResetWithDelay(0.8f);
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
		ScreenReader.Say(mode.Name + locked + ", " + (_gameModeIndex + 1) + " of " + _gameModeEntries.Count);
	}

	// --- Collection ---

	private void EnterCollection()
	{
		_browsingCollection = true;
		_inSubScreen = true;
		_collectionSectionIndex = 0;
        _collectionIndex = 0;
		_collectionLevel = 0;
		MelonCoroutines.Start(ScanCollectionDelayed());
	}

	private IEnumerator ScanCollectionDelayed()
	{
		// Collection scene needs time to load — retry up to 5 times
		for (int attempt = 0; attempt < 5; attempt++)
		{
			yield return new WaitForSeconds(attempt == 0 ? 1.0f : 0.5f);
			ScanCollectionSections();
			if (_collectionSections.Count > 0) break;
		}

		if (_collectionSections.Count > 0)
		{
			_collectionLevel = 0;
			ScreenReader.Say("Collection. " + _collectionSections.Count + " categories. Left and Right to browse, Enter to open.");
			ScreenReader.SayQueued(_collectionSections[_collectionSectionIndex] + ", 1 of " + _collectionSections.Count);
		}
		else ScreenReader.Say("Collection is empty or loading.");
	}

	private void ProcessCollectionInput()
	{
		// Delete confirmation takes priority
		if (_deleteConfirmMode)
		{
			ProcessDeleteConfirm();
			return;
		}

		// Deck action mode: waiting for E/D/C key after pressing Enter on a deck
		if (_deckActionMode)
		{
			ProcessDeckAction();
			return;
		}

		// Level 0: Category navigation
		if (_collectionLevel == 0)
		{
			if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
			{
				if (_collectionSections.Count == 0) return;
				_collectionSectionIndex = (_collectionSectionIndex - 1 + _collectionSections.Count) % _collectionSections.Count;
				ScreenReader.Say(_collectionSections[_collectionSectionIndex] + ", " + (_collectionSectionIndex + 1) + " of " + _collectionSections.Count);
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
			{
				if (_collectionSections.Count == 0) return;
				_collectionSectionIndex = (_collectionSectionIndex + 1) % _collectionSections.Count;
				ScreenReader.Say(_collectionSections[_collectionSectionIndex] + ", " + (_collectionSectionIndex + 1) + " of " + _collectionSections.Count);
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
			{
				// Preview item count
				ScanCollectionCardsInSection(_collectionSectionIndex);
				ScreenReader.Say(_collectionSections[_collectionSectionIndex] + ": " + _collectionCards.Count + " items.");
			}
			else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
			{
				// Enter category
				ScanCollectionCardsInSection(_collectionSectionIndex);
				_collectionIndex = 0;
				_collectionLevel = 1;
				if (_collectionCards.Count > 0)
				{
					ScreenReader.Say(_collectionSections[_collectionSectionIndex] + ", " + _collectionCards.Count + " items.");
					ScreenReader.SayQueued(_collectionCards[0].Name + ", 1 of " + _collectionCards.Count);
				}
				else
				{
					ScreenReader.Say(_collectionSections[_collectionSectionIndex] + " is empty.");
				}
			}
			// Backspace handled in HandleBackCommand
			return;
		}

		// Level 1: Items within category
		// Left: previous item
		if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
		{
			if (_collectionCards.Count == 0) return;
			_collectionIndex = (_collectionIndex - 1 + _collectionCards.Count) % _collectionCards.Count;
			AnnounceCollectionCard();
		}
		// Right: next item
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			if (_collectionCards.Count == 0) return;
			_collectionIndex = (_collectionIndex + 1) % _collectionCards.Count;
			AnnounceCollectionCard();
		}
		// Down: read card details (cost, power)
		else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
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
					// Enter deck action mode for existing decks
					_deckActionMode = true;
					// Click the deck to select it first
					if (!UIHelper.SendPointerClick(card.Button.gameObject))
						UIHelper.SimulateMouseClick(card.Button.gameObject);
					ScreenReader.Say(card.Name + ". E to edit, D to delete, C to copy code, Backspace to cancel.");
				}
				else if (card.IsDeckSlot && card.Name == "New Deck")
				{
					ScreenReader.Say("Creating new deck.");
					Button innerBtn = UIHelper.FindButtonInChildren(card.Button.transform, "Btn_Control");
					if (innerBtn != null)
						UIHelper.ClickButtonWithFallback(innerBtn);
					else
						UIHelper.SimulateMouseClick(card.Button.gameObject);
				}
				else
				{
					ScreenReader.Say("Activating " + card.Name);
					UIHelper.ClickButton(card.Button);
				}
			}
		}
		// Backspace goes back to categories (handled in HandleBackCommand)
	}

	// --- Delete confirmation state ---
	private bool _deleteConfirmMode = false;
	private Button _deleteConfirmButton = null;
	private Button _deleteCancelButton = null;
	private int _deleteConfirmIndex = 0; // 0=cancel, 1=confirm

	private void ProcessDeckAction()
	{
		if (SDLInput.IsKeyDown(SDLInput.Key.E))
		{
			_deckActionMode = false;
			try
			{
				GameObject deckEditOptions = GameObject.Find("DeckEditOptions");
				if (deckEditOptions != null)
				{
					Button backBtn = UIHelper.FindButtonInChildren(deckEditOptions.transform, "btn_back");
					if (backBtn != null)
					{
						UIHelper.ClickButton(backBtn);
						ScreenReader.Say("Editing deck.");
						return;
					}
				}
			}
			catch { }
			ScreenReader.Say("Edit not available.");
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.D))
		{
			_deckActionMode = false;
			try
			{
				GameObject deckEditOptions = GameObject.Find("DeckEditOptions");
				if (deckEditOptions != null)
				{
					Button discardBtn = UIHelper.FindButtonInChildren(deckEditOptions.transform, "btn_discard");
					if (discardBtn != null)
					{
						if (!UIHelper.SendPointerClick(((Component)discardBtn).gameObject))
							UIHelper.SimulateMouseClick(((Component)discardBtn).gameObject);
						// Enter delete confirm mode — wait for confirm dialog to appear
						_deleteConfirmMode = true;
						_deleteConfirmIndex = 0;
						MelonCoroutines.Start(ScanForDeleteConfirm());
						return;
					}
				}
			}
			catch { }
			ScreenReader.Say("Delete not available.");
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.C))
		{
			_deckActionMode = false;
			try
			{
				GameObject deckEditOptions = GameObject.Find("DeckEditOptions");
				if (deckEditOptions != null)
				{
					Button copyBtn = UIHelper.FindButtonInChildren(deckEditOptions.transform, "btn_copy");
					if (copyBtn != null)
					{
						UIHelper.ClickButton(copyBtn);
						ScreenReader.Say("Deck code copied to clipboard.");
						return;
					}
				}
			}
			catch { }
			ScreenReader.Say("Copy not available.");
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsKeyDown(SDLInput.Key.Escape))
		{
			_deckActionMode = false;
			ScreenReader.Say("Cancelled.");
		}
	}

	private IEnumerator ScanForDeleteConfirm()
	{
		// Wait for confirm dialog to appear
		for (int attempt = 0; attempt < 6; attempt++)
		{
			yield return new WaitForSeconds(0.3f);
			// Look for "Do it!" and "Cancel" buttons in Canvas-Dialogs or any popup
			Button[] allBtns = Object.FindObjectsOfType<Button>();
			foreach (var btn in allBtns)
			{
				if (btn == null || !btn.gameObject.activeInHierarchy) continue;
				string label = UIHelper.GetButtonLabel(btn);
				if (string.IsNullOrEmpty(label)) continue;
				if (label.Contains("Do it", StringComparison.OrdinalIgnoreCase))
					_deleteConfirmButton = btn;
				else if (label.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
					_deleteCancelButton = btn;
			}
			if (_deleteConfirmButton != null) break;
		}

		if (_deleteConfirmButton != null)
		{
			ScreenReader.Say("Delete deck? Left for Cancel, Right for Confirm. Enter to select.");
			_deleteConfirmIndex = 0;
		}
		else
		{
			_deleteConfirmMode = false;
			ScreenReader.Say("Confirm dialog not found.");
		}
	}

	private void ProcessDeleteConfirm()
	{
		if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
		{
			_deleteConfirmIndex = 0;
			ScreenReader.Say("Cancel");
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			_deleteConfirmIndex = 1;
			ScreenReader.Say("Confirm delete");
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			_deleteConfirmMode = false;
			if (_deleteConfirmIndex == 1 && _deleteConfirmButton != null)
			{
				ScreenReader.Say("Deleting deck.");
				int countBefore = _collectionCards.Count;
				string deckName = (_collectionIndex >= 0 && _collectionIndex < _collectionCards.Count)
					? _collectionCards[_collectionIndex].Name : "deck";
				// Try all click methods for maximum reliability
				if (!UIHelper.ClickButtonWithFallback(_deleteConfirmButton))
					UIHelper.SendPointerClick(((Component)_deleteConfirmButton).gameObject);
				// Rescan after deletion and verify
				MelonCoroutines.Start(RescanCollectionAfterDelete(countBefore, deckName));
			}
			else if (_deleteCancelButton != null)
			{
				ScreenReader.Say("Cancelled.");
				UIHelper.ClickButton(_deleteCancelButton);
			}
			else
			{
				ScreenReader.Say("Cancelled.");
			}
			_deleteConfirmButton = null;
			_deleteCancelButton = null;
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
		{
			_deleteConfirmMode = false;
			if (_deleteCancelButton != null)
				UIHelper.ClickButton(_deleteCancelButton);
			ScreenReader.Say("Cancelled.");
			_deleteConfirmButton = null;
			_deleteCancelButton = null;
		}
	}

	private IEnumerator RescanCollectionAfterDelete(int countBefore, string deletedName)
	{
		// Go back to category level — the game recycles UI objects and the old deck name
		// stays stale. Re-entering the category forces a fresh scan.
		_collectionLevel = 0;
		_collectionCards.Clear();
		yield return new WaitForSeconds(1.5f);

		// Rescan to verify the deck was actually removed
		ScanCollectionCardsInSection(_collectionSectionIndex);
		int countAfter = _collectionCards.Count;
		_collectionCards.Clear(); // Back to category level
		_collectionLevel = 0;

		if (countAfter < countBefore)
		{
			ScreenReader.Say("Deck deleted. Press Enter on Deck category to see updated list.");
		}
		else
		{
			// Deck was NOT actually deleted — game refused
			ScreenReader.Say(deletedName + " could not be deleted. The game may be protecting this deck. Try equipping a different deck first.");
		}
		ScreenReader.SayQueued(_collectionSections[_collectionSectionIndex] + ", " + (_collectionSectionIndex + 1) + " of " + _collectionSections.Count);
	}

	private IEnumerator RescanCollectionDelayed()
	{
		yield return new WaitForSeconds(0.8f);
		ScanCollectionCardsInSection(_collectionSectionIndex);
		_collectionIndex = 0;
		if (_collectionCards.Count > 0)
			ScreenReader.Say(_collectionCards[_collectionIndex].Name + ", 1 of " + _collectionCards.Count);
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
						ScreenReader.Say("Cost " + costView.Value);
					else
						ScreenReader.Say("Cost unknown");
				}
				else
				{
					var powerView = card.Renderer._PowerValueView;
					if ((Object)(object)powerView != (Object)null)
						ScreenReader.Say("Power " + powerView.Value);
					else
						ScreenReader.Say("Power unknown");
				}
				_collectionDetailLevel = (_collectionDetailLevel + 1) % 2;
			}
			else
			{
				ScreenReader.Say(card.Name);
			}
		}
		catch { ScreenReader.Say(card.Name); }
	}

	private void AnnounceCollectionCard()
	{
		if (_collectionCards.Count == 0) return;
		_collectionDetailLevel = 0;
		var card = _collectionCards[_collectionIndex];
		// For deck section items, add "Deck:" prefix for clarity
		string isDeckSlot = card.Button != null && card.Button.gameObject.name == "DeckSlotCell" ? "Deck: " : "";
		string suffix = card.Name == "New Deck" ? ". Press Enter to create." : "";
		ScreenReader.Say(isDeckSlot + card.Name + suffix + ", " + (_collectionIndex + 1) + " of " + _collectionCards.Count);
	}

	private void ScanCollectionSections()
	{
		_collectionSections.Clear();
		GameObject rootObj = GameObject.Find("CollectionContentLayoutGroup");
        if (rootObj == null) return;
        Transform root = rootObj.transform;
		for (int i = 0; i < root.childCount; i++)
		{
			var child = root.GetChild(i);
			if (child.gameObject.activeInHierarchy && child.name.Contains("SectionContainer"))
			{
				// Skip DeckEditSectionContainer — it's the deck editor, not a browsable category
				if (child.name.Contains("DeckEdit")) continue;
				string name = child.name.Replace("SectionContainer", "").Replace("Section", "");
				if (string.IsNullOrEmpty(name)) name = "Items";
				_collectionSections.Add(name);
			}
		}
	}

	private void ScanCollectionCardsInSection(int sectionIdx)
	{
		_collectionCards.Clear();
		GameObject rootObj = GameObject.Find("CollectionContentLayoutGroup");
		if (rootObj == null) return;
		Transform root = rootObj.transform;

		Transform section = null;
		int sCount = 0;
		for (int i = 0; i < root.childCount; i++)
		{
			if (root.GetChild(i).gameObject.activeInHierarchy && root.GetChild(i).name.Contains("SectionContainer"))
			{
				if (sCount == sectionIdx) { section = root.GetChild(i); break; }
				sCount++;
			}
		}
		if (section == null) return;

		// Skip DeckEditSectionContainer entirely
		if (section.name.Contains("DeckEdit")) return;

		// Determine if this is a Deck section (only DeckSlotCell buttons should appear)
		bool isDeckSection = section.name.Contains("Deck");

		// Track seen names to avoid duplicates from pooled objects
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		Button[] btns = section.GetComponentsInChildren<Button>(true);
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
				Button[] cellButtons = cell.GetComponentsInChildren<Button>(true);
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
			TMP_Text[] texts = slot.GetComponentsInChildren<TMP_Text>(true);
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
		if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
		{
			if (_rewardEvents.Count == 0) return;
			_rewardIndex = (_rewardIndex - 1 + _rewardEvents.Count) % _rewardEvents.Count;
			AnnounceReward();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			if (_rewardEvents.Count == 0) return;
			_rewardIndex = (_rewardIndex + 1) % _rewardEvents.Count;
			AnnounceReward();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
		{
			if (_rewardIndex >= 0 && _rewardIndex < _rewardEvents.Count)
			{
				var evt = _rewardEvents[_rewardIndex];
				var details = new List<string>();
				if (!string.IsNullOrEmpty(evt.NextRewardDay))
				{
					string next = "Next reward: " + evt.NextRewardDay;
					if (!string.IsNullOrEmpty(evt.NextRewardCountdown))
						next += " in " + evt.NextRewardCountdown;
					else
						next += ", available now";
					details.Add(next);
				}
				if (!string.IsNullOrEmpty(evt.ExtraInfo))
					details.Add(evt.ExtraInfo);
				if (!string.IsNullOrEmpty(evt.FinalRewardDay))
					details.Add("Final reward: " + evt.FinalRewardDay);
				if (!string.IsNullOrEmpty(evt.EventEndTime))
					details.Add("Event ends in " + evt.EventEndTime);
				if (evt.SeeAllButton != null)
					details.Add("Press Enter to see all rewards.");
				ScreenReader.Say(details.Count > 0 ? string.Join(". ", details) : "No details available.");
			}
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			if (_rewardIndex < 0 || _rewardIndex >= _rewardEvents.Count) return;
			var evt = _rewardEvents[_rewardIndex];
			if (evt.SeeAllButton != null)
			{
				ScreenReader.Say("Opening " + evt.EventName + " rewards.");
				UIHelper.ClickButtonWithFallback(evt.SeeAllButton);
				// Transition to detail view after a short delay
				MelonCoroutines.Start(EnterRewardDetailsDelayed());
			}
			else
			{
				ScreenReader.Say("No details available for this event.");
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
			_rewardDayIndex = 0;
			ScreenReader.Say(_rewardDays.Count + " daily rewards. Left and Right to browse, Down for details.");
			AnnounceRewardDay();
		}
		else
		{
			ScreenReader.Say("Could not load reward details.");
		}
	}

	private void AnnounceReward()
	{
		if (_rewardIndex < 0 || _rewardIndex >= _rewardEvents.Count) return;
		var evt = _rewardEvents[_rewardIndex];
		string announcement = evt.EventName;
		if (!string.IsNullOrEmpty(evt.NextRewardDay))
		{
			announcement += ", next: " + evt.NextRewardDay;
			if (string.IsNullOrEmpty(evt.NextRewardCountdown))
				announcement += " (ready)";
			else
				announcement += " in " + evt.NextRewardCountdown;
		}
		announcement += ", " + (_rewardIndex + 1) + " of " + _rewardEvents.Count;
		ScreenReader.Say(announcement);
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
				Button[] slotButtons = cell.GetComponentsInChildren<Button>(true);
				// Collect slots in this cell, then reverse for chronological order
				var cellDays = new List<RewardDay>();
				foreach (var btn in slotButtons)
				{
					if (btn == null || !btn.gameObject.activeInHierarchy) continue;
					if (btn.gameObject.name != "ClaimButton") continue;

					var day = new RewardDay { ClaimButton = btn };

					// Read text children
					TMP_Text[] texts = ((Component)btn).GetComponentsInChildren<TMP_Text>(true);
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
			ScreenReader.Say("Reward details closed.");
			AnnounceReward();
			return;
		}

		if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
		{
			if (_rewardDays.Count == 0) return;
			_rewardDayIndex = (_rewardDayIndex - 1 + _rewardDays.Count) % _rewardDays.Count;
			AnnounceRewardDay();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			if (_rewardDays.Count == 0) return;
			_rewardDayIndex = (_rewardDayIndex + 1) % _rewardDays.Count;
			AnnounceRewardDay();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
		{
			if (_rewardDayIndex >= 0 && _rewardDayIndex < _rewardDays.Count)
			{
				var d = _rewardDays[_rewardDayIndex];
				var details = new List<string>();
				details.Add(d.Day);
				if (!string.IsNullOrEmpty(d.Reward))
					details.Add("Reward: " + d.Reward);
				if (d.IsClaimable)
				{
					if (!string.IsNullOrEmpty(d.Countdown))
						details.Add("Available in " + d.Countdown);
					else
						details.Add("Available to claim now. Press Enter to claim.");
				}
				else
				{
					details.Add("Already claimed or not yet available.");
				}
				ScreenReader.Say(string.Join(". ", details));
			}
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			if (_rewardDayIndex < 0 || _rewardDayIndex >= _rewardDays.Count) return;
			var d = _rewardDays[_rewardDayIndex];
			if (d.IsClaimable && string.IsNullOrEmpty(d.Countdown) && d.ClaimButton != null)
			{
				ScreenReader.Say("Claiming " + d.Day);
				UIHelper.ClickButtonWithFallback(d.ClaimButton);
			}
			else if (d.IsClaimable && !string.IsNullOrEmpty(d.Countdown))
			{
				ScreenReader.Say("Not available yet. " + d.Countdown + " remaining.");
			}
			else
			{
				ScreenReader.Say("This reward cannot be claimed.");
			}
		}
		// Backspace handled in HandleBackCommand
	}

	private void AnnounceRewardDay()
	{
		if (_rewardDayIndex < 0 || _rewardDayIndex >= _rewardDays.Count) return;
		var d = _rewardDays[_rewardDayIndex];
		string reward = !string.IsNullOrEmpty(d.Reward) ? ", " + d.Reward : "";
		string status = "";
		if (d.IsClaimable && string.IsNullOrEmpty(d.Countdown))
			status = ", claimable";
		else if (d.IsClaimable && !string.IsNullOrEmpty(d.Countdown))
			status = ", in " + d.Countdown;
		ScreenReader.Say(d.Day + reward + status + ", " + (_rewardDayIndex + 1) + " of " + _rewardDays.Count);
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
				if (btn != null) UIHelper.ClickButton(btn);
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

	private void OnNavigatorShown() { _focusIndex = 0; _dialogHandler.SuppressNextAnnounce = true; AnnounceFocused(); }
	private void OnNavigatorHidden() { Reset(); }

	public void Reset()
	{
		_onPlayScreen = false;
		_browsingCollection = false;
		_collectionLevel = 0;
		_deckActionMode = false;
		_deleteConfirmMode = false;
		_deleteConfirmButton = null;
		_deleteCancelButton = null;
		_browsingRewards = false;
		_browsingRewardDetails = false;
		_inSubScreen = false;
		_inGameModes = false;
		_collectionCards.Clear();
		_rewardEvents.Clear();
		_rewardDays.Clear();
		_gameModeEntries.Clear();
	}

	public void AnnounceContext()
	{
		if (_browsingRewardDetails) AnnounceRewardDay();
		else if (_browsingRewards) AnnounceReward();
		else if (_inGameModes) AnnounceGameMode();
		else if (_onPlayScreen) AnnouncePlayCategory();
		else if (_browsingCollection && _collectionLevel == 1) AnnounceCollectionCard();
		else if (_browsingCollection) ScreenReader.Say(_collectionSections.Count > 0 ? _collectionSections[_collectionSectionIndex] : "Collection");
		else AnnounceFocused();
	}

	private bool IsButtonActive(MenuEntry e) { return false; } 

	private Button FindButtonUnder(Transform parent, string name)
	{
		Button[] btns = parent.GetComponentsInChildren<Button>(true);
		foreach (var b in btns)
		{
			if (b.gameObject.name == name && b.gameObject.activeInHierarchy) return b;
		}
		return null;
	}

	private Button FindButtonByName(string name)
	{
		Button[] all = Object.FindObjectsOfType<Button>();
		foreach (var b in all) if (b.gameObject.name == name && b.gameObject.activeInHierarchy) return b;
		return null;
	}

	private void TryClickGameBackButton()
	{
		Button b = FindButtonByName("btn_back") ?? FindButtonByName("BackButton") ?? FindButtonByName("btn_close") ?? FindButtonByName("Esc");
		if (b != null) UIHelper.ClickButton(b);
		else UIHelper.SimulateKeyPress(SDLInput.Key.Escape);
	}

	private void TryOpenDeckEditor()
	{
		Button b = FindButtonByName("EditButton") ?? FindButtonByName("btn_edit");
		if (b != null) UIHelper.ClickButton(b);
	}
}
