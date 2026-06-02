using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Il2CppTMPro;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSecondDinner.CubeRendering.Card;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Handles UI button navigation on menu sub-screens.
/// Scans the topmost active content canvas (not Navigator, not FullscreenModals).
/// Provides Up/Down + Enter navigation and Right/Left for screen content reading.
/// </summary>
/// <summary>
/// Handles UI button navigation on menu sub-screens.
/// Implements a category-based scan for structured browsing when possible.
/// </summary>
public class DialogHandler : IScreenNavigator
{
    private enum ContentCategory
    {
        Buttons,
        Settings,
        ScreenText
    }

	private readonly List<Button> _buttons = new List<Button>();
	private readonly List<string> _labels = new List<string>();
	private readonly List<string> _screenTexts = new List<string>();

	private int _focusIndex = -1;
	private int _textReadIndex = -1;
	private bool _scanned = false;
	private readonly KeyHoldRepeater _holdRepeater = new KeyHoldRepeater();
	private bool _needsRescan = false;
	private float _rescanDelay = 0f;
	private float _inputBlockUntil = 0f; // Block input briefly after reset to prevent double-activation
	private GameObject _scanRoot = null;

	// --- Settings mode ---
	private enum SettingsItemType { Slider, Toggle, Button, ActionComponent }

	private struct SettingsItem
	{
		public string Label;
		public SettingsItemType Type;
		public Slider Slider;
		public Toggle Toggle;
		public Button Button;
		/// <summary>For ActionComponent type: the MonoBehaviour to invoke via reflection.</summary>
		public Component ActionTarget;
		/// <summary>For ActionComponent type: the method name to call.</summary>
		public string ActionMethod;
	}

	private bool _settingsMode = false;
	private readonly List<SettingsItem> _settingsItems = new List<SettingsItem>();
	private int _settingsIndex = -1;
	private const float SliderStep = 0.05f; // 5% per Left/Right press

	// --- Text input mode ---
	private bool _textInputMode = false;
	private TMP_InputField _activeInputField = null;
	private string _lastInputText = "";
	private int _inputFieldButtonIndex = -1; // Index in _buttons/_labels for standalone input field entry

	// GameObjects to exclude from scanning (by name pattern)
	// NOTE: Canvas-Rewards is NOT excluded — the scoring system naturally deprioritises it
	// when other screens are open (score ~31 vs News ~57, Collection ~128).
	// When no screen is active, it becomes the content root so the user can claim rewards.
	private static readonly string[] _excludeParents = new string[]
	{
		"Navigator", "NavBar", "Canvas-Game",
		"Canvas-Gameplay", "Canvas-DevConsole", "LoadingScreen",
		"CardDetailsCardView", "CardDetail",
		"AlbumCardView", "CardPreview",
		"CarouselStagingArea",
		"ObjectPool",
		"SearchFiltersSection",
		"MasteryContainer",
		"ScreenCanvas", // Main menu bar — handled by MainMenuHandler
		"InvisibleButtonPanel", // Left/Right page nav junk in FloatingScreenContainer
	};

	// Labels that are UI decoration, not real buttons
	private static readonly HashSet<string> _junkLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"Glass Backing", "Background", "BG", "Tooltip View Container",
		"Right Bracket", "Left Bracket", "Shadow", "Glow", "Gradient",
		"Mask", "Frame", "Border", "Divider", "Spacer", "Blocker",
		"Click Catcher", "ClickCatcher", "Touch Blocker", "TouchBlocker",
		"Overlay", "Underlay", "btn hex prp", "Button",
		"Background Panel",
		"BackgroundPanel",
		"btn header",
		"btn_header",
		"Button  Background Close",
		"LandscapeCollectionCardView",
		"Landscape Collection Card View",
		"DeckSlotCell",
		"Create or Join a Match",
		"Tooltip View Container  Friendly",
		"Your code will be generated",
		"Ask your friend for the Code",
	};

	private static readonly string[] _junkPartials = new string[]
	{
		"img ", "Parallelogram", "Backing", "Hex Prp", "Glass",
		"Bracket", "blocker", "catcher", "LandscapeCollectionCardView", "DeckSlotCell",
	};

	// Text patterns to skip when reading screen content
	private static readonly string[] _junkTextPatterns = new string[]
	{
		"img ", "\\u", "<sprite", "Glass", "(Clone)",
		"{Missing Entry}", "Missing Entry",
		// Card cosmetic/upgrade junk
		" Border", "Base Finish", "No Flare", "Base Card",
		"Equipped Cosmetics", "Card Information",
		"Series 1", "Series 2", "Series 3", "Series 4", "Series 5",
	};

	/// <summary>Suppresses auto-announce on next scan. Set by MainMenuHandler to prevent text vomit.</summary>
	public bool SuppressNextAnnounce { get; set; }

	// Rename confusing labels by game object name
	private static readonly Dictionary<string, string> _goNameOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		{ "Esc", "Close" },
		{ "btn_hex_prp", "Close" },
		{ "btn_close", "Close" },
		{ "btn_back", "Back" },
		{ "CloseButton", "Close" },
		{ "BackButton", "Back" },
		{ "Left Button", "Previous page" },
		{ "Right Button", "Next page" },
		{ "LeftButton", "Previous page" },
		{ "RightButton", "Next page" },
		{ "btn_left", "Previous page" },
		{ "btn_right", "Next page" },
		{ "btn_paste", "Paste Code" },
		{ "btn_discard", "Delete Deck" },
		{ "btn_copy", "Copy Code" },
		{ "SmartDeckAutofillButton", "Auto-fill Deck" },
	};

	public string NavigatorId => "Dialog";
	public int Priority => 200;
	public bool IsActive => _buttons.Count > 0 || _settingsMode;
	public bool HasActiveDialog => _buttons.Count > 0 || _settingsMode;

	public void Update()
	{
		// Delayed rescan after clicking a button
		if (_needsRescan && Time.time >= _rescanDelay)
		{
			_needsRescan = false;
			DoScan();
		}

		// First scan on entry
		if (!_scanned)
		{
			_scanned = true;
			DoScan();
		}

		// Text input mode: user is typing in a text field
		if (_textInputMode)
		{
			ProcessTextInput();
			return;
		}

		if (_settingsMode)
		{
			if (_settingsItems.Count == 0) return;
			ProcessSettingsInput();
			return;
		}

		if (_buttons.Count == 0)
		{
			return;
		}
		ProcessInput();
	}

	public void AnnounceContext()
	{
		AnnouncementService.Instance.Announce(Loc.Get("dialog_help"), AnnouncementPriority.High);
		if (_buttons.Count == 0)
		{
			AnnouncementService.Instance.Announce(Loc.Get("dialog_no_buttons"));
		}
		else if (_focusIndex >= 0 && _focusIndex < _buttons.Count)
		{
			AnnouncementService.Instance.Announce(Loc.Get("dialog_focused_button", GetLabel(_focusIndex), _focusIndex + 1, _buttons.Count), AnnouncementPriority.High);
		}
		else
		{
			AnnouncementService.Instance.Announce(Loc.Get("dialog_has_buttons", _buttons.Count), AnnouncementPriority.High);
		}
	}

	public void Deactivate()
	{
		_buttons.Clear();
		_labels.Clear();
		_screenTexts.Clear();
		_focusIndex = -1;
		_textReadIndex = -1;
		_scanned = false;
		_needsRescan = false;
		_scanRoot = null;
		_settingsMode = false;
		_settingsItems.Clear();
		_settingsIndex = -1;
		_textInputMode = false;
		_activeInputField = null;
		_lastInputText = "";
		_inputFieldButtonIndex = -1;
		// Block input for 0.3s to prevent the Enter that triggered Deactivate from double-firing
		_inputBlockUntil = Time.time + 0.3f;
		_holdRepeater.Reset();
	}

	public void OnSceneChanged(string sceneName)
	{
		_buttons.Clear();
		_labels.Clear();
		_screenTexts.Clear();
		_focusIndex = -1;
		_textReadIndex = -1;
		_scanned = false;
		_needsRescan = false;
		_scanRoot = null;
		_settingsMode = false;
		_settingsItems.Clear();
		_settingsIndex = -1;
		_textInputMode = false;
		_activeInputField = null;
		_lastInputText = "";
		_inputFieldButtonIndex = -1;
		_inputBlockUntil = 0f;
	}

	/// <summary>Reset with a delayed first scan, for cases where UI hasn't rendered yet.</summary>
	public void ResetWithDelay(float delay = 0.5f)
	{
		Deactivate();
		// Prevent immediate scan — schedule it via the rescan mechanism
		_scanned = true;
		_needsRescan = true;
		_rescanDelay = Time.time + delay;
	}

	/// <summary>Force an immediate rescan.</summary>
	public void Rescan()
	{
		_scanned = false;
	}

	// --- Scanning ---

	private void DoScan()
	{
		_scanRoot = FindContentRoot();
		_buttons.Clear();
		_labels.Clear();
		_screenTexts.Clear();
		_textReadIndex = -1;

		List<Button> allButtons;
		if ((Object)(object)_scanRoot != (Object)null)
		{
			allButtons = UIHelper.FindButtonsUnder(_scanRoot);
			DebugLogger.Log(LogCategory.Handler, "DialogHandler",
				"Scanning under: " + ((Object)_scanRoot).name + " (" + allButtons.Count + " raw buttons)");
		}
		else
		{
			// Fallback: scan everything but filter aggressively
			allButtons = UIHelper.FindAllButtons();
			DebugLogger.Log(LogCategory.Handler, "DialogHandler",
				"No content root found, scanning all (" + allButtons.Count + " raw buttons)");
		}

		foreach (Button btn in allButtons)
		{
			if (IsExcludedButton(btn)) continue;

			// Filter by GO name too — background buttons get wrong labels from child text
			try
			{
				string goName = ((Object)((Component)btn).gameObject).name;
				if (goName.Contains("Background", StringComparison.OrdinalIgnoreCase)
				    && (goName.Contains("Close", StringComparison.OrdinalIgnoreCase)
				        || goName.Contains("Panel", StringComparison.OrdinalIgnoreCase)
				        || goName.Contains("Button", StringComparison.OrdinalIgnoreCase)))
					continue;
			}
			catch { }

			string label = BuildLabel(btn);
			if (IsJunkLabel(label)) continue;

			_buttons.Add(btn);
			_labels.Add(label);
		}

		// Also scan for non-interactable buttons — dialogs often disable buttons during animation
		// or until user fills in required fields (e.g., Confirm disabled until name entered)
		if ((Object)(object)_scanRoot != (Object)null)
		{
			try
			{
				Il2CppArrayBase<Button> allBtns = _scanRoot.GetComponentsInChildren<Button>(false);
				if (allBtns != null)
				{
					for (int i = 0; i < allBtns.Count; i++)
					{
						Button btn = allBtns[i];
						if ((Object)(object)btn == (Object)null) continue;
						if (!((Component)btn).gameObject.activeInHierarchy) continue;
						if (((Selectable)btn).interactable) continue; // Already found above

						if (IsExcludedButton(btn)) continue;
						try
						{
							string goName = ((Object)((Component)btn).gameObject).name;
							if (goName.Contains("Background", StringComparison.OrdinalIgnoreCase)
							    && (goName.Contains("Close", StringComparison.OrdinalIgnoreCase)
							        || goName.Contains("Panel", StringComparison.OrdinalIgnoreCase)
							        || goName.Contains("Button", StringComparison.OrdinalIgnoreCase)))
								continue;
						}
						catch { }

						string label = BuildLabel(btn);
						if (IsJunkLabel(label)) continue;

						// Skip if a button with this label already exists
						bool dup = false;
						for (int j = 0; j < _labels.Count; j++)
						{
							if (_labels[j].Equals(label, StringComparison.OrdinalIgnoreCase))
							{ dup = true; break; }
						}
						if (dup) continue;

						_buttons.Add(btn);
						_labels.Add(label);
						DebugLogger.Log(LogCategory.Handler, "DialogHandler",
							$"Added non-interactable button: '{label}'");
					}
				}
			}
			catch { }
		}

		// Scan for text input fields
		_activeInputField = null;
		_inputFieldButtonIndex = -1;
		try
		{
			if ((Object)(object)_scanRoot != (Object)null)
			{
				var inputFields = _scanRoot.GetComponentsInChildren<TMP_InputField>(false);
				if (inputFields != null)
				{
					for (int i = 0; i < inputFields.Count; i++)
					{
						var field = inputFields[i];
						if ((Object)(object)field == (Object)null) continue;
						if (!((Component)field).gameObject.activeInHierarchy) continue;
						_activeInputField = field;

						// Build a good label: try sibling/parent text, then placeholder, then GO name
						string fieldLabel = GetInputFieldLabel(field);

						// Include current value in the label
						string currentValue = "";
						try { currentValue = field.text ?? ""; } catch { }

						// Check if there's already a button for this GO
						bool alreadyAdded = false;
						for (int j = 0; j < _buttons.Count; j++)
						{
							try
							{
								if (((Component)_buttons[j]).gameObject == ((Component)field).gameObject ||
									((Component)_buttons[j]).transform.IsChildOf(((Component)field).transform) ||
									((Component)field).transform.IsChildOf(((Component)_buttons[j]).transform))
								{
									string editLabel = fieldLabel;
									if (!string.IsNullOrEmpty(currentValue))
										editLabel += ": " + currentValue;
									_labels[j] = editLabel + " (press Enter to type)";
									_inputFieldButtonIndex = j;
									alreadyAdded = true;
									break;
								}
							}
							catch { }
						}
						// Fallback: match by label text
						if (!alreadyAdded && fieldLabel.Length > 2)
						{
							for (int j = 0; j < _labels.Count; j++)
							{
								if (_labels[j].Equals(fieldLabel, StringComparison.OrdinalIgnoreCase))
								{
									string editLabel = fieldLabel;
									if (!string.IsNullOrEmpty(currentValue))
										editLabel += ": " + currentValue;
									_labels[j] = editLabel + " (press Enter to type)";
									_inputFieldButtonIndex = j;
									alreadyAdded = true;
									break;
								}
							}
						}
						// If the input field is standalone (not associated with any button),
						// add it as a navigable entry at the top of the list
						if (!alreadyAdded)
						{
							string editLabel = fieldLabel;
							if (!string.IsNullOrEmpty(currentValue))
								editLabel += ": " + currentValue;
							editLabel += " (press Enter to type)";
							// Insert at position 0 so it's the first thing the user lands on
							_buttons.Insert(0, null);
							_labels.Insert(0, editLabel);
							_inputFieldButtonIndex = 0;
						}
						DebugLogger.Log(LogCategory.Handler, "DialogHandler",
							$"Input field found: '{fieldLabel}', value='{currentValue}', associated={alreadyAdded}");
						break; // Only handle first input field
					}
				}
			}
		}
		catch { }

		// Remove duplicate labels (keep first occurrence)
		{
			HashSet<string> seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < _buttons.Count; )
			{
				if (!seenLabels.Add(_labels[i]))
				{
					_buttons.RemoveAt(i);
					_labels.RemoveAt(i);
				}
				else i++;
			}
		}

		// Check if this is a settings screen — if so, switch to settings mode
		if (TryScanAsSettings())
		{
			_settingsMode = true;
			_buttons.Clear();
			_labels.Clear();
			return;
		}

		// Collect readable screen text
		CollectScreenTexts();

		if (_buttons.Count > 0)
		{
			if (_focusIndex < 0 || _focusIndex >= _buttons.Count)
				_focusIndex = 0;
			DebugLogger.Log(LogCategory.Handler, "DialogHandler",
				$"Found {_buttons.Count} button(s), {_screenTexts.Count} text(s)");

			if (!SuppressNextAnnounce)
			{
				// Announce screen text FIRST so user knows what this dialog is about
				if (_screenTexts.Count > 0)
				{
					AnnouncementService.Instance.Announce(string.Join(". ", _screenTexts), AnnouncementPriority.High);
				}

				// Then announce focused element
				AnnouncementService.Instance.Announce(Loc.Get("dialog_button_focus", GetLabel(_focusIndex), _focusIndex + 1, _buttons.Count), AnnouncementPriority.Normal);
			}
			SuppressNextAnnounce = false;
		}
		else
		{
			_focusIndex = -1;
			SuppressNextAnnounce = false;
			// No buttons — just read the text content directly
			if (_screenTexts.Count > 0)
			{
				AnnouncementService.Instance.Announce(string.Join(". ", _screenTexts));
			}
		}
	}

	/// <summary>
	/// Finds the best active content Canvas for scanning.
	/// Excludes Navigator, FullscreenModals, card detail overlays, etc.
	/// Prefers canvases with more buttons and text content.
	/// </summary>
	private GameObject FindContentRoot()
	{
		try
		{
			Il2CppArrayBase<Canvas> canvases = Object.FindObjectsOfType<Canvas>();
			if (canvases == null) return null;

			Canvas best = null;
			int bestScore = 0;
			int bestOrder = int.MinValue;

			for (int i = 0; i < canvases.Count; i++)
			{
				Canvas c = canvases[i];
				if ((Object)(object)c == (Object)null) continue;
				if (!((Component)c).gameObject.activeInHierarchy) continue;

				string cName = ((Object)((Component)c).gameObject).name;

				// Skip excluded canvases by name
				if (IsExcludedName(cName)) continue;

				// Also check if any ancestor is excluded
				if (HasExcludedAncestor(((Component)c).transform)) continue;

				// Count buttons and text — this is the canvas's "content score"
				Il2CppArrayBase<Button> btns = ((Component)c).GetComponentsInChildren<Button>(false);
				Il2CppArrayBase<TMP_Text> txts = ((Component)c).GetComponentsInChildren<TMP_Text>(false);
				int btnCount = btns != null ? btns.Count : 0;
				int txtCount = txts != null ? txts.Count : 0;
				int score = btnCount * 3 + txtCount; // Weight buttons higher

				if (score < 3) continue; // Skip nearly-empty canvases

				int order = c.sortingOrder;

				// Bonus: prefer overlay/popup canvases over base screen content
				// Priority: Canvas-Rewards/Canvas-Dialogs > FloatingScreenContainer > SubSceneController
				int locationBonus = 0;
				try
				{
					// Check root canvas name first (Canvas-Rewards, Canvas-Dialogs are root-level)
					string rootName = cName;
					Transform p = ((Component)c).transform;
					while (p.parent != null)
					{
						p = p.parent;
						rootName = ((Object)p.gameObject).name;
					}
					if (rootName.Contains("Canvas-Rewards", StringComparison.Ordinal))
						locationBonus = 600; // Reward popups overlay everything
					else if (rootName.Contains("Canvas-Dialogs", StringComparison.Ordinal))
						locationBonus = 400; // Dialogs overlay sub-scenes

					if (locationBonus == 0)
					{
						// Walk up to check for sub-scene containers
						bool isLoginBonus = false;
						p = ((Component)c).transform;
						int d = 0;
						while (p != null && d < 15)
						{
							string pn = ((Object)p.gameObject).name;
							if (pn.Contains("LoginBonusSubSceneContainer", StringComparison.Ordinal))
								isLoginBonus = true;
							if (pn.Contains("FloatingScreenContainer", StringComparison.Ordinal))
							{
								// LoginBonusSubSceneContainer persists in the background — give it a
								// reduced bonus so active floating screens (Game Modes, etc.) win
								locationBonus = isLoginBonus ? 300 : 500;
								break;
							}
							if (pn.Contains("SubSceneController", StringComparison.Ordinal))
							{
								locationBonus = 200; // Base sub-scene content
								break;
							}
							p = p.parent;
							d++;
						}
					}
				}
				catch { }
				score += locationBonus;

				// Prefer canvas with highest score; break ties by sort order
				if (score > bestScore || (score == bestScore && order > bestOrder))
				{
					bestScore = score;
					bestOrder = order;
					best = c;
				}
			}

			if ((Object)(object)best != (Object)null)
			{
				DebugLogger.Log(LogCategory.Handler, "DialogHandler",
					"Content root: " + ((Object)((Component)best).gameObject).name +
					" (score=" + bestScore + ", order=" + bestOrder + ")");
				return ((Component)best).gameObject;
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "DialogHandler", "FindContentRoot failed: " + ex.Message);
		}
		return null;
	}

	private bool IsExcludedName(string name)
	{
		foreach (string excl in _excludeParents)
		{
			if (name.Contains(excl, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	private bool HasExcludedAncestor(Transform t)
	{
		try
		{
			t = t.parent;
			int depth = 0;
			while (t != null && depth < 10)
			{
				string name = ((Object)t.gameObject).name;
				if (IsExcludedName(name)) return true;
				t = t.parent;
				depth++;
			}
		}
		catch { }
		return false;
	}

	/// <summary>Collects all meaningful text from the screen for Down-arrow reading.</summary>
	private void CollectScreenTexts()
	{
		_screenTexts.Clear();
		GameObject root = _scanRoot;
		if ((Object)(object)root == (Object)null) return;

		try
		{
			Il2CppArrayBase<TMP_Text> texts = root.GetComponentsInChildren<TMP_Text>(false);
			if (texts == null) return;

			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			for (int i = 0; i < texts.Count; i++)
			{
				TMP_Text tmp = texts[i];
				if ((Object)(object)tmp == (Object)null) continue;
				if (!((Component)tmp).gameObject.activeInHierarchy) continue;

				string raw = tmp.text;
				if (string.IsNullOrWhiteSpace(raw)) continue;

				string text = UIHelper.StripRichText(raw.Trim());
				if (text.Length < 2) continue;
				if (IsJunkScreenText(text)) continue;
				if (IsNumericLabel(text)) continue;
				if (seen.Contains(text)) continue;

				seen.Add(text);
				_screenTexts.Add(text);
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "DialogHandler", "CollectScreenTexts failed: " + ex.Message);
		}
	}

	// --- Settings Mode ---

	/// <summary>
	/// Detect if the scan root contains settings (sliders or toggle-like controls).
	/// If so, build a flat list of settings items and return true.
	/// </summary>
	private bool TryScanAsSettings()
	{
		if ((Object)(object)_scanRoot == (Object)null) return false;

		try
		{
			// Look for Slider components — if we find any, this is likely a settings screen
			Il2CppArrayBase<Slider> sliders = _scanRoot.GetComponentsInChildren<Slider>(false);
			Il2CppArrayBase<Toggle> toggles = _scanRoot.GetComponentsInChildren<Toggle>(false);

			int sliderCount = sliders != null ? sliders.Count : 0;
			int toggleCount = toggles != null ? toggles.Count : 0;

			// Need at least 1 slider to consider this a settings screen
			if (sliderCount == 0) return false;

			_settingsItems.Clear();

			// Add sliders with their labels
			if (sliders != null)
			{
				for (int i = 0; i < sliders.Count; i++)
				{
					Slider slider = sliders[i];
					if ((Object)(object)slider == (Object)null) continue;
					if (!((Component)slider).gameObject.activeInHierarchy) continue;
					if (!((Selectable)slider).interactable) continue;

					string label = FindSettingLabel((Component)slider);
					if (string.IsNullOrEmpty(label)) label = UIHelper.CleanGameObjectName(((Object)((Component)slider).gameObject).name);

					_settingsItems.Add(new SettingsItem
					{
						Label = label,
						Type = SettingsItemType.Slider,
						Slider = slider
					});
				}
			}

			// Add toggles with their labels
			if (toggles != null)
			{
				for (int i = 0; i < toggles.Count; i++)
				{
					Toggle toggle = toggles[i];
					if ((Object)(object)toggle == (Object)null) continue;
					if (!((Component)toggle).gameObject.activeInHierarchy) continue;
					if (!((Selectable)toggle).interactable) continue;

					string label = FindSettingLabel((Component)toggle);
					if (string.IsNullOrEmpty(label)) label = UIHelper.CleanGameObjectName(((Object)((Component)toggle).gameObject).name);
					// Skip toggles that look like junk
					if (IsJunkLabel(label)) continue;

					_settingsItems.Add(new SettingsItem
					{
						Label = label,
						Type = SettingsItemType.Toggle,
						Toggle = toggle
					});
				}
			}

			// Add meaningful buttons (Quit to Desktop, etc.) — skip junk
			List<Button> allButtons;
			allButtons = UIHelper.FindButtonsUnder(_scanRoot);
			foreach (Button btn in allButtons)
			{
				if ((Object)(object)btn == (Object)null) continue;
				if (!((Component)btn).gameObject.activeInHierarchy) continue;

				// Skip buttons that are part of a slider or toggle (slider handles, toggle checkmarks)
				if (((Component)btn).GetComponentInParent<Slider>() != null) continue;
				if (((Component)btn).GetComponentInParent<Toggle>() != null) continue;

				string goName = "";
				try { goName = ((Object)((Component)btn).gameObject).name; } catch { }

				// Skip background/close/junk buttons
				if (goName.Contains("Background", StringComparison.OrdinalIgnoreCase)) continue;
				if (goName.Contains("BackgroundPanel", StringComparison.OrdinalIgnoreCase)) continue;

				string label = BuildLabel(btn);
				if (IsJunkLabel(label)) continue;
				if (IsNumericLabel(label)) continue;

				_settingsItems.Add(new SettingsItem
				{
					Label = label,
					Type = SettingsItemType.Button,
					Button = btn
				});
			}

			// Scan for special action components (e.g. AccountLogoutButton)
			// These are MonoBehaviours with click handlers that aren't standard Buttons
			ScanActionComponents(_scanRoot);

			if (_settingsItems.Count == 0) return false;

			_settingsIndex = 0;
			DebugLogger.Log(LogCategory.Handler, "DialogHandler",
				$"Settings mode: {_settingsItems.Count} items ({sliderCount} sliders, {toggleCount} toggles)");

			// Announce
			AnnouncementService.Instance.Announce(Loc.Get("settings_entered", _settingsItems.Count), AnnouncementPriority.High);
			AnnounceCurrentSetting();
			return true;
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "DialogHandler", "TryScanAsSettings failed: " + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// Scans for game-specific action components (MonoBehaviours with click handlers)
	/// that aren't standard Unity Buttons but should appear in the settings list.
	/// </summary>
	private void ScanActionComponents(GameObject root)
	{
		if ((Object)(object)root == (Object)null) return;
		try
		{
			// Map of component type name patterns to their click method and label
			var actionPatterns = new (string TypePattern, string MethodName, string Label)[]
			{
				("AccountLogoutButton", "OnButtonClick", "Sign Out"),
			};

			Il2CppArrayBase<MonoBehaviour> behaviours = root.GetComponentsInChildren<MonoBehaviour>(false);
			if (behaviours == null) return;

			for (int i = 0; i < behaviours.Count; i++)
			{
				MonoBehaviour mb = behaviours[i];
				if ((Object)(object)mb == (Object)null) continue;
				if (!((Component)mb).gameObject.activeInHierarchy) continue;

				string typeName = mb.GetType().Name;
				foreach (var (pattern, method, label) in actionPatterns)
				{
					if (!typeName.Contains(pattern)) continue;

					// Verify the method exists
					var methodInfo = mb.GetType().GetMethod(method,
						System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
					if (methodInfo == null) continue;

					// Check we don't already have this as a regular button
					bool duplicate = false;
					foreach (var existing in _settingsItems)
					{
						if (existing.Label == label) { duplicate = true; break; }
					}
					if (duplicate) continue;

					_settingsItems.Add(new SettingsItem
					{
						Label = label,
						Type = SettingsItemType.ActionComponent,
						ActionTarget = (Component)mb,
						ActionMethod = method,
					});
					DebugLogger.Log(LogCategory.Handler, "DialogHandler",
						$"Found action component: {typeName}.{method} -> \"{label}\"");
				}
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "DialogHandler", "ScanActionComponents error: " + ex.Message);
		}
	}

	/// <summary>Find the text label associated with a setting control by searching children, then section headers.</summary>
	private string FindSettingLabel(Component control)
	{
		try
		{
			// Phase 1: Search children of the control for a proper "Label-xxx" TMP_Text
			// Sliders have labels like Label-MusicVolume, Label-MasterVolume as children
			Il2CppArrayBase<TMP_Text> childTexts = control.GetComponentsInChildren<TMP_Text>(false);
			string firstChildText = null;
			if (childTexts != null)
			{
				for (int i = 0; i < childTexts.Count; i++)
				{
					TMP_Text tmp = childTexts[i];
					if ((Object)(object)tmp == (Object)null) continue;
					string text = UIHelper.StripRichText((tmp.text ?? "").Trim());
					if (text.Length < 3 || IsJunkScreenText(text) || IsNumericLabel(text)) continue;

					string goName = ((Object)((Component)tmp).gameObject).name;
					// Proper label GOs are named "Label-xxx" (not "Label_FilterTerm" which is toggle option text)
					if (goName.StartsWith("Label-", StringComparison.OrdinalIgnoreCase))
						return text;

					if (firstChildText == null)
						firstChildText = text;
				}
			}

			// Phase 2: For toggles — combine section header with option text
			// e.g., section "Graphics Quality" + option "High" → "Graphics Quality: High"
			if (firstChildText != null)
			{
				string section = FindSectionHeader(control.transform);
				if (section != null && !section.Equals(firstChildText, StringComparison.OrdinalIgnoreCase))
					return section + ": " + firstChildText;
				return firstChildText;
			}

			// Phase 3: Check siblings in the parent container
			Transform parent = control.transform.parent;
			if ((Object)(object)parent != (Object)null)
			{
				for (int i = 0; i < parent.childCount; i++)
				{
					Transform sibling = parent.GetChild(i);
					if (sibling == control.transform) continue;
					TMP_Text tmp = sibling.GetComponentInChildren<TMP_Text>(false);
					if ((Object)(object)tmp != (Object)null)
					{
						string text = UIHelper.StripRichText((tmp.text ?? "").Trim());
						if (text.Length >= 3 && !IsJunkScreenText(text) && !IsNumericLabel(text))
							return text;
					}
				}
			}
		}
		catch { }
		return null;
	}

	/// <summary>Walk up the hierarchy to find a section header label (e.g., "Graphics Quality", "Collection Level Privacy").</summary>
	private string FindSectionHeader(Transform controlTransform)
	{
		try
		{
			// Start from grandparent to skip the control's own container
			Transform t = controlTransform.parent;
			if ((Object)(object)t != (Object)null) t = t.parent;
			int depth = 0;
			while ((Object)(object)t != (Object)null && depth < 4)
			{
				for (int i = 0; i < t.childCount; i++)
				{
					Transform child = t.GetChild(i);
					// Check direct TMP_Text on this child
					TMP_Text tmp = child.GetComponent<TMP_Text>();
					if ((Object)(object)tmp != (Object)null && IsHeaderGoName(((Object)((Component)tmp).gameObject).name))
					{
						string text = UIHelper.StripRichText((tmp.text ?? "").Trim());
						if (text.Length >= 3 && !IsJunkScreenText(text) && !IsNumericLabel(text))
							return text;
					}
					// Also check one level deeper
					for (int j = 0; j < child.childCount; j++)
					{
						Transform grandchild = child.GetChild(j);
						TMP_Text gTmp = grandchild.GetComponent<TMP_Text>();
						if ((Object)(object)gTmp != (Object)null && IsHeaderGoName(((Object)((Component)gTmp).gameObject).name))
						{
							string text = UIHelper.StripRichText((gTmp.text ?? "").Trim());
							if (text.Length >= 3 && !IsJunkScreenText(text) && !IsNumericLabel(text))
								return text;
						}
					}
				}
				t = t.parent;
				depth++;
			}
		}
		catch { }
		return null;
	}

	/// <summary>Check if a GO name looks like a section header label.</summary>
	private static bool IsHeaderGoName(string goName)
	{
		// Matches "Label-GraphicsQ", "CollectionPrivacyLabel", "PlayerNameLabel", etc.
		return goName.StartsWith("Label-", StringComparison.OrdinalIgnoreCase)
			|| goName.EndsWith("Label", StringComparison.OrdinalIgnoreCase)
			|| goName.Contains("Header", StringComparison.OrdinalIgnoreCase);
	}

	private void AnnounceCurrentSetting()
	{
		if (_settingsIndex < 0 || _settingsIndex >= _settingsItems.Count) return;
		var item = _settingsItems[_settingsIndex];
		string value = GetSettingValue(item);
		string msg = string.IsNullOrEmpty(value)
			? $"{item.Label}, {_settingsIndex + 1} of {_settingsItems.Count}"
			: $"{item.Label}, {value}, {_settingsIndex + 1} of {_settingsItems.Count}";
		AnnouncementService.Instance.Announce(msg);
	}

	private string GetSettingValue(SettingsItem item)
	{
		try
		{
			switch (item.Type)
			{
				case SettingsItemType.Slider:
					if ((Object)(object)item.Slider != (Object)null)
					{
						float normalized = (item.Slider.value - item.Slider.minValue) /
							(item.Slider.maxValue - item.Slider.minValue);
						return $"{Mathf.RoundToInt(normalized * 100)}%";
					}
					break;
				case SettingsItemType.Toggle:
					if ((Object)(object)item.Toggle != (Object)null)
						return item.Toggle.isOn ? Loc.Get("settings_on") : Loc.Get("settings_off");
					break;
			}
		}
		catch { }
		return "";
	}

	private void ProcessSettingsInput()
	{
		if (Time.time < _inputBlockUntil) return;

		// Up: previous setting
		if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
		{
			if (_settingsItems.Count == 0) return;
			_settingsIndex--;
			if (_settingsIndex < 0) _settingsIndex = _settingsItems.Count - 1;
			AnnounceCurrentSetting();
		}
		// Down: next setting
		else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
		{
			if (_settingsItems.Count == 0) return;
			_settingsIndex++;
			if (_settingsIndex >= _settingsItems.Count) _settingsIndex = 0;
			AnnounceCurrentSetting();
		}
		// Left: decrease slider / toggle off
		else if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
		{
			AdjustCurrentSetting(-1);
		}
		// Right: increase slider / toggle on
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			AdjustCurrentSetting(1);
		}
		// Enter: activate button, toggle
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			ActivateCurrentSetting();
		}
		// Backspace: exit settings
		else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
		{
			_settingsMode = false;
				_settingsItems.Clear();
			Deactivate();
		}
	}

	private void AdjustCurrentSetting(int direction)
	{
		if (_settingsIndex < 0 || _settingsIndex >= _settingsItems.Count) return;
		var item = _settingsItems[_settingsIndex];

		try
		{
			switch (item.Type)
			{
				case SettingsItemType.Slider:
					if ((Object)(object)item.Slider != (Object)null)
					{
						float range = item.Slider.maxValue - item.Slider.minValue;
						float step = range * SliderStep;
						float newVal = Mathf.Clamp(item.Slider.value + step * direction,
							item.Slider.minValue, item.Slider.maxValue);
						item.Slider.value = newVal;

						float normalized = (newVal - item.Slider.minValue) / range;
						AnnouncementService.Instance.Announce($"{Mathf.RoundToInt(normalized * 100)}%");
						DebugLogger.Log(LogCategory.Handler, "DialogHandler",
							$"Slider {item.Label}: {Mathf.RoundToInt(normalized * 100)}%");
					}
					break;
				case SettingsItemType.Toggle:
					if ((Object)(object)item.Toggle != (Object)null)
					{
						bool newState = direction > 0;
						if (item.Toggle.isOn != newState)
						{
							item.Toggle.isOn = newState;
							AnnouncementService.Instance.AnnounceInterrupt(newState ? Loc.Get("settings_on") : Loc.Get("settings_off"));
						}
						else
						{
							AnnouncementService.Instance.Announce(newState ? Loc.Get("settings_already_on") : Loc.Get("settings_already_off"));
						}
					}
					break;
				case SettingsItemType.Button:
					// Left/Right on buttons does nothing
					break;
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "DialogHandler", "AdjustCurrentSetting failed: " + ex.Message);
		}
	}

	private void ActivateCurrentSetting()
	{
		if (_settingsIndex < 0 || _settingsIndex >= _settingsItems.Count) return;
		var item = _settingsItems[_settingsIndex];

		try
		{
			switch (item.Type)
			{
				case SettingsItemType.Toggle:
					if ((Object)(object)item.Toggle != (Object)null)
					{
						item.Toggle.isOn = !item.Toggle.isOn;
						AnnouncementService.Instance.AnnounceInterrupt(item.Toggle.isOn ? Loc.Get("settings_on") : Loc.Get("settings_off"));
					}
					break;
				case SettingsItemType.Button:
					if ((Object)(object)item.Button != (Object)null)
					{
						AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("dialog_activating", item.Label));
						UIHelper.ActivateButton(item.Button);
						// Schedule rescan to pick up any UI changes
						_needsRescan = true;
						_rescanDelay = Time.time + 1.0f;
					}
					break;
				case SettingsItemType.Slider:
					// Read current value — use Left/Right to adjust
					string value = GetSettingValue(item);
					AnnouncementService.Instance.Announce(
						$"{item.Label}, {value}. " + Loc.Get("settings_use_arrows"));
					break;
				case SettingsItemType.ActionComponent:
					if ((Object)(object)item.ActionTarget != (Object)null && !string.IsNullOrEmpty(item.ActionMethod))
					{
						AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("dialog_activating", item.Label));
						try
						{
							var methodInfo = item.ActionTarget.GetType().GetMethod(item.ActionMethod,
								System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
							methodInfo?.Invoke(item.ActionTarget, null);
						}
						catch (Exception ex2)
						{
							DebugLogger.Log(LogCategory.Handler, "DialogHandler",
								$"ActionComponent invoke failed: {ex2.Message}");
						}
						_needsRescan = true;
						_rescanDelay = Time.time + 1.0f;
					}
					break;
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "DialogHandler", "ActivateCurrentSetting failed: " + ex.Message);
		}
	}

	// --- Text Input Mode ---

	/// <summary>Gets a descriptive label for an input field from surrounding context text.</summary>
	private string GetInputFieldLabel(TMP_InputField field)
	{
		// Strategy 1: Search the entire scan root for Title/Body text elements
		// This is the most reliable approach for dialog overlays
		try
		{
			GameObject searchRoot = _scanRoot;
			if ((Object)(object)searchRoot == (Object)null)
			{
				// Fall back to walking up from the field
				Transform p = ((Component)field).transform;
				for (int d = 0; d < 6 && p != null; d++) p = p.parent;
				if (p != null) searchRoot = p.gameObject;
			}
			if ((Object)(object)searchRoot != (Object)null)
			{
				Il2CppArrayBase<TMP_Text> allTexts = searchRoot.GetComponentsInChildren<TMP_Text>(false);
				if (allTexts != null)
				{
					// First pass: look for Body text (more descriptive, e.g. "What should we call you?")
					for (int i = 0; i < allTexts.Count; i++)
					{
						TMP_Text tmp = allTexts[i];
						if ((Object)(object)tmp == (Object)null) continue;
						string goName = ((Object)((Component)tmp).gameObject).name;
						if (goName.Contains("Body", StringComparison.OrdinalIgnoreCase) ||
						    goName.Contains("PopUpBody", StringComparison.OrdinalIgnoreCase))
						{
							string text = UIHelper.StripRichText((tmp.text ?? "").Trim());
							if (text.Length >= 3 && !text.Contains("{Missing"))
								return text;
						}
					}
					// Second pass: look for Title text
					for (int i = 0; i < allTexts.Count; i++)
					{
						TMP_Text tmp = allTexts[i];
						if ((Object)(object)tmp == (Object)null) continue;
						string goName = ((Object)((Component)tmp).gameObject).name;
						if (goName.Contains("Title", StringComparison.OrdinalIgnoreCase) ||
						    goName.Contains("Header", StringComparison.OrdinalIgnoreCase))
						{
							string text = UIHelper.StripRichText((tmp.text ?? "").Trim());
							if (text.Length >= 3 && !text.Contains("{Missing"))
								return text;
						}
					}
				}
			}
		}
		catch { }

		// Strategy 2: Walk up hierarchy looking for text siblings (original approach)
		try
		{
			Transform parent = ((Component)field).transform.parent;
			for (int depth = 0; depth < 3 && parent != null; depth++)
			{
				for (int i = 0; i < parent.childCount; i++)
				{
					Transform sibling = parent.GetChild(i);
					if (sibling == ((Component)field).transform) continue;
					TMP_Text directTmp = sibling.GetComponent<TMP_Text>();
					if ((Object)(object)directTmp != (Object)null)
					{
						string goName = ((Object)((Component)directTmp).gameObject).name;
						if (goName.Contains("Title", StringComparison.OrdinalIgnoreCase) ||
							goName.Contains("Header", StringComparison.OrdinalIgnoreCase) ||
							goName.Contains("Body", StringComparison.OrdinalIgnoreCase))
						{
							string text = UIHelper.StripRichText((directTmp.text ?? "").Trim());
							if (text.Length >= 3 && !text.Contains("{Missing"))
								return text;
						}
					}
				}
				parent = parent.parent;
			}
		}
		catch { }

		// Try placeholder text
		try
		{
			if (field.placeholder != null)
			{
				string placeholder = UIHelper.StripRichText(((TMP_Text)field.placeholder).text);
				if (!string.IsNullOrEmpty(placeholder) && placeholder.Length > 3)
					return placeholder;
			}
		}
		catch { }

		// Fall back to GO name
		try
		{
			return UIHelper.CleanGameObjectName(((Object)((Component)field).gameObject).name);
		}
		catch { }

		return Loc.Get("dialog_text_field");
	}

	/// <summary>Activates the input field and enters text input mode.</summary>
	private void EnterTextInputMode(string fieldName)
	{
		try
		{
			_activeInputField.ActivateInputField();
			_activeInputField.Select();
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "DialogHandler", "Failed to activate input field: " + ex.Message);
		}

		_textInputMode = true;
		_lastInputText = "";
		try { _lastInputText = _activeInputField.text ?? ""; } catch { }
		AnnouncementService.Instance.Announce(Loc.Get("dialog_editing", fieldName));
		if (!string.IsNullOrEmpty(_lastInputText))
			AnnouncementService.Instance.Announce(Loc.Get("login_field_current", _lastInputText), AnnouncementPriority.Normal);
		DebugLogger.Log(LogCategory.Handler, "DialogHandler", "Entered text input mode: " + fieldName);
	}

	/// <summary>Handles input while in text input mode. Enter exits, characters are spoken.</summary>
	private void ProcessTextInput()
	{
		if ((Object)(object)_activeInputField == (Object)null)
		{
			_textInputMode = false;
			return;
		}

		// Enter exits text input mode
		if (SDLInput.IsKeyDown(SDLInput.Key.Return))
		{
			_textInputMode = false;
			string finalText = "";
			try { finalText = _activeInputField.text ?? ""; } catch { }

			try
			{
				_activeInputField.DeactivateInputField();
			}
			catch { }

			if (string.IsNullOrEmpty(finalText))
				AnnouncementService.Instance.Announce(Loc.Get("dialog_done_editing_empty"));
			else
				AnnouncementService.Instance.Announce(Loc.Get("dialog_done_editing", finalText));

			DebugLogger.Log(LogCategory.Handler, "DialogHandler", "Exited text input mode. Text: " + finalText);
			// Block input briefly so Enter doesn't activate a button
			_inputBlockUntil = Time.time + 0.3f;
			// Schedule rescan — buttons may have changed interactable state after text was entered
			_needsRescan = true;
			_rescanDelay = Time.time + 0.5f;
			return;
		}

		// Escape also exits without announcing
		if (SDLInput.IsKeyDown(SDLInput.Key.Escape))
		{
			_textInputMode = false;
			try { _activeInputField.DeactivateInputField(); } catch { }
			AnnouncementService.Instance.Announce(Loc.Get("dialog_editing_cancelled"));
			_inputBlockUntil = Time.time + 0.3f;
			return;
		}

		// Up/Down: read full field content (AccessibleArena pattern)
		if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsKeyDown(SDLInput.Key.Down))
		{
			try
			{
				string content = _activeInputField.text ?? "";
				if (string.IsNullOrEmpty(content))
					AnnouncementService.Instance.Announce(Loc.Get("login_field_empty"), AnnouncementPriority.Immediate);
				else
					AnnouncementService.Instance.Announce(content, AnnouncementPriority.Immediate);
				try { _activeInputField.ActivateInputField(); } catch { }
			}
			catch { }
			return;
		}

		// Track text changes and speak new characters
		try
		{
			string currentText = _activeInputField.text ?? "";
			if (currentText != _lastInputText)
			{
				if (currentText.Length > _lastInputText.Length)
				{
					// Characters added — speak the new portion
					string added = currentText.Substring(_lastInputText.Length);
					AnnouncementService.Instance.Announce(added, AnnouncementPriority.Immediate);
				}
				else if (currentText.Length < _lastInputText.Length)
				{
					// Characters deleted
					AnnouncementService.Instance.Announce(Loc.Get("dialog_char_deleted"));
				}
				_lastInputText = currentText;
			}
		}
		catch { }
	}

	// --- Input ---

	private void ProcessInput()
	{
		// Block input briefly after reset to prevent double-activation
		if (Time.time < _inputBlockUntil) return;

		// 1. Button Navigation (Up/Down) with hold-to-repeat
		Action movePrev = () => {
			CleanupDestroyedButtons();
			if (_buttons.Count == 0) return;
			_focusIndex = (_focusIndex - 1 + _buttons.Count) % _buttons.Count;
			_textReadIndex = -1;
			AnnouncementService.Instance.Announce(Loc.Get("dialog_button_focus", GetLabel(_focusIndex), _focusIndex + 1, _buttons.Count));
		};
		Action moveNext = () => {
			CleanupDestroyedButtons();
			if (_buttons.Count == 0) return;
			_focusIndex = (_focusIndex + 1) % _buttons.Count;
			_textReadIndex = -1;
			AnnouncementService.Instance.Announce(Loc.Get("dialog_button_focus", GetLabel(_focusIndex), _focusIndex + 1, _buttons.Count));
		};
		if (_holdRepeater.Check(SDLInput.Key.Up, movePrev)) { }
		else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp)) movePrev();
		else if (_holdRepeater.Check(SDLInput.Key.Down, moveNext)) { }
		else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown)) moveNext();
		// 2. Details (Right reads next, Left reads previous screen text line)
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			ReadNextScreenText();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
		{
			ReadPreviousScreenText();
		}
		// Home/End: jump to first/last button
		else if (SDLInput.IsKeyDown(SDLInput.Key.Home))
		{
			CleanupDestroyedButtons();
			if (_buttons.Count > 0) { _focusIndex = 0; _textReadIndex = -1; AnnouncementService.Instance.Announce(Loc.Get("dialog_button_focus", GetLabel(0), 1, _buttons.Count)); }
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.End))
		{
			CleanupDestroyedButtons();
			if (_buttons.Count > 0) { _focusIndex = _buttons.Count - 1; _textReadIndex = -1; AnnouncementService.Instance.Announce(Loc.Get("dialog_button_focus", GetLabel(_focusIndex), _focusIndex + 1, _buttons.Count)); }
		}
		// 3. Activation (Enter)
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			ActivateFocused();
		}
		// 4. Close (Backspace)
		else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
		{
			TryCloseDialog();
		}
	}

	/// <summary>Tries to close the current dialog by clicking Close button or game's Escape button.</summary>
	private void TryCloseDialog()
	{
		try
		{
			// First look for a Close button in the current dialog
			for (int i = 0; i < _buttons.Count; i++)
			{
				if ((Object)(object)_buttons[i] == (Object)null) continue;
				string goName = ((Object)((Component)_buttons[i]).gameObject).name;
				if (goName.Equals("Close", StringComparison.OrdinalIgnoreCase) ||
				    goName.Equals("btn_close", StringComparison.OrdinalIgnoreCase) ||
				    goName.Equals("CloseButton", StringComparison.OrdinalIgnoreCase))
				{
					UIHelper.ActivateButton(_buttons[i]);
					DebugLogger.Log(LogCategory.Handler, "DialogHandler", "Closed dialog via: " + goName);
					Deactivate();
					return;
				}
			}
			// Fall back to game's Escape/Back button — search globally
			Il2CppArrayBase<Button> allButtons = Object.FindObjectsOfType<Button>();
			if (allButtons != null)
			{
				// Prefer btn_hex_prp in FloatingScreenContainer (closer back button)
				Button fallback = null;
				for (int i = 0; i < allButtons.Count; i++)
				{
					Button btn = allButtons[i];
					if ((Object)(object)btn == (Object)null) continue;
					if (!((Component)btn).gameObject.activeInHierarchy) continue;
					string goName = ((Object)((Component)btn).gameObject).name;
					if (goName == "btn_hex_prp" || goName == "btn_back" || goName == "BackButton" || goName == "btn_close")
					{
						if (fallback == null) fallback = btn;
						// Prefer buttons closer to the scan root or in FloatingScreenContainer
						try
						{
							Transform p = ((Component)btn).transform;
							int d = 0;
							while (p != null && d < 15)
							{
								if (p.gameObject.name.Contains("FloatingScreen") || p.gameObject.name.Contains("LoginBonus"))
								{
									fallback = btn;
									break;
								}
								p = p.parent;
								d++;
							}
						}
						catch { }
					}
				}
				if (fallback != null)
				{
					UIHelper.ActivateButton(fallback);
					DebugLogger.Log(LogCategory.Handler, "DialogHandler", "Closed via back button: " + ((Object)((Component)fallback).gameObject).name);
					Deactivate();
					return;
				}
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "DialogHandler", "TryCloseDialog failed: " + ex.Message);
		}
	}

	private void ReadNextScreenText()
	{
		if (_screenTexts.Count == 0)
		{
			AnnouncementService.Instance.Announce(Loc.Get("dialog_no_text"));
			return;
		}
		_textReadIndex++;
		if (_textReadIndex >= _screenTexts.Count)
		{
			_textReadIndex = _screenTexts.Count - 1;
			AnnouncementService.Instance.Announce(Loc.Get("dialog_end_of_text"));
			return;
		}
		AnnouncementService.Instance.Announce(Loc.Get("dialog_text_line", _screenTexts[_textReadIndex], _textReadIndex + 1, _screenTexts.Count));
	}

	private void ReadPreviousScreenText()
	{
		if (_screenTexts.Count == 0) return;
		_textReadIndex--;
		if (_textReadIndex < 0) _textReadIndex = 0;
		AnnouncementService.Instance.Announce(Loc.Get("dialog_text_line", _screenTexts[_textReadIndex], _textReadIndex + 1, _screenTexts.Count));
	}

	private void ActivateFocused()
	{
		CleanupDestroyedButtons();
		if (_focusIndex < 0 || _focusIndex >= _buttons.Count)
		{
			AnnouncementService.Instance.Announce(Loc.Get("dialog_no_focus"));
			return;
		}
		Button button = _buttons[_focusIndex];
		string label = GetLabel(_focusIndex);

		// Check if this is a text input field — enter text input mode instead of clicking
		if ((Object)(object)_activeInputField != (Object)null && label.Contains("(press Enter to type)"))
		{
			EnterTextInputMode(label.Replace(" (press Enter to type)", ""));
			return;
		}

		// Null button (shouldn't happen except for input field entries handled above)
		if ((Object)(object)button == (Object)null)
		{
			DebugLogger.Log(LogCategory.Handler, "DialogHandler", "Null button at index " + _focusIndex);
			return;
		}

		DebugLogger.LogInput("Enter", "Clicking: " + label);
		AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("dialog_activating", label));
		UIHelper.ActivateButton(button);

		// Schedule rescan after 0.8s
		_needsRescan = true;
		_rescanDelay = Time.time + 0.8f;
	}

	// --- Label Building ---

	private string GetLabel(int index)
	{
		if (index >= 0 && index < _labels.Count)
			return _labels[index];
		return "Unknown";
	}

	/// <summary>
	/// Builds a meaningful label for a button by checking multiple sources:
	/// 1. GameObject name overrides (btn_close -> Close)
	/// 2. All TMP_Text children of the button — pick the best one
	/// 3. Parent/sibling context for short or numeric labels
	/// </summary>
	private string BuildLabel(Button btn)
	{
		if ((Object)(object)btn == (Object)null) return "";

		// 1. Check game object name for known overrides
		string goName = "";
		try
		{
			goName = ((Object)((Component)btn).gameObject).name;
			if (_goNameOverrides.TryGetValue(goName, out string overridden))
				return overridden;
		}
		catch { }

		// 2. Read ALL TMP_Text children and pick the longest meaningful one
		string bestText = "";
		try
		{
			Il2CppArrayBase<TMP_Text> texts = ((Component)btn).GetComponentsInChildren<TMP_Text>(false);
			if (texts != null)
			{
				for (int i = 0; i < texts.Count; i++)
				{
					TMP_Text tmp = texts[i];
					if ((Object)(object)tmp == (Object)null) continue;
					if (!((Component)tmp).gameObject.activeInHierarchy) continue;
					string raw = tmp.text;
					if (string.IsNullOrWhiteSpace(raw)) continue;
					string clean = UIHelper.StripRichText(raw.Trim());
					if (clean.Length < 2) continue;
					if (IsJunkScreenText(clean)) continue;

					// Prefer longer, more descriptive text
					if (clean.Length > bestText.Length && !IsNumericLabel(clean))
						bestText = clean;
				}
			}
		}
		catch { }

		// If we found good text on the button, use it
		// Also check for mission progress (text_missionprogress / text_missiongoal children)
		if (bestText.Length >= 3)
		{
			string progress = GetMissionProgress(btn);
			if (!string.IsNullOrEmpty(progress))
				return bestText + ", " + progress;
			return bestText;
		}

		// 3. Try reading card name from CardRenderer (for collection card buttons)
		try
		{
			var cardRenderer = ((Component)btn).GetComponentInChildren<Il2CppSecondDinner.CubeRendering.Card.CardRenderer>(true);
			if (cardRenderer != null)
			{
				string cardName = cardRenderer.CardName;
				if (!string.IsNullOrEmpty(cardName) && cardName.Length >= 2)
					return cardName;
			}
		}
		catch { }

		// 4. Try a cleaned-up version of the GameObject name
		string cleanedName = UIHelper.CleanGameObjectName(goName);
		if (!string.IsNullOrEmpty(cleanedName) && cleanedName.Length >= 3
		    && !IsJunkLabel(cleanedName) && !IsNumericLabel(cleanedName))
		{
			// Append any short text we found (like a number) for context
			if (!string.IsNullOrEmpty(bestText) && bestText != cleanedName)
				return cleanedName + " " + bestText;
			return cleanedName;
		}

		// 4. Look for context text in parent/sibling hierarchy
		try
		{
			Transform current = ((Component)btn).transform.parent;
			for (int level = 0; level < 4 && current != null; level++)
			{
				string context = FindSiblingText(current, ((Component)btn).transform);
				if (!string.IsNullOrEmpty(context) && !IsNumericLabel(context) && !IsJunkLabel(context))
				{
					if (!string.IsNullOrEmpty(bestText))
						return context + ", " + bestText;
					return context;
				}
				current = current.parent;
			}
		}
		catch { }

		// 5. Return whatever we have
		if (!string.IsNullOrEmpty(bestText)) return bestText;
		if (!string.IsNullOrEmpty(cleanedName)) return cleanedName;
		return "Button";
	}

	/// <summary>Finds text in siblings of a parent, excluding the button's own subtree.</summary>
	private string FindSiblingText(Transform parent, Transform exclude)
	{
		if (parent == null) return "";
		try
		{
			// First check direct TMP_Text on the parent itself
			TMP_Text parentTmp = parent.GetComponent<TMP_Text>();
			if ((Object)(object)parentTmp != (Object)null && !string.IsNullOrWhiteSpace(parentTmp.text))
			{
				string pt = UIHelper.StripRichText(parentTmp.text.Trim());
				if (pt.Length >= 3 && !IsNumericLabel(pt) && !IsJunkScreenText(pt))
					return pt;
			}

			// Then check siblings
			for (int i = 0; i < parent.childCount; i++)
			{
				Transform child = parent.GetChild(i);
				if ((Object)(object)child == (Object)(object)exclude) continue;
				if (!child.gameObject.activeInHierarchy) continue;

				// Check for text on this sibling and its children
				Il2CppArrayBase<TMP_Text> texts = child.GetComponentsInChildren<TMP_Text>(false);
				if (texts != null)
				{
					string best = "";
					for (int j = 0; j < texts.Count; j++)
					{
						TMP_Text tmp = texts[j];
						if ((Object)(object)tmp == (Object)null) continue;
						if (!((Component)tmp).gameObject.activeInHierarchy) continue;
						string text = tmp.text;
						if (string.IsNullOrWhiteSpace(text)) continue;
						text = UIHelper.StripRichText(text.Trim());
						if (text.Length >= 3 && !IsNumericLabel(text) && !IsJunkScreenText(text))
						{
							if (text.Length > best.Length) best = text;
						}
					}
					if (best.Length > 0) return best;
				}
			}
		}
		catch { }
		return "";
	}

	/// <summary>
	/// Reads mission progress from button children (text_missionprogress / text_missiongoal).
	/// Returns "X of Y" or null if not a mission tile.
	/// </summary>
	private string GetMissionProgress(Button btn)
	{
		try
		{
			Il2CppArrayBase<TMP_Text> texts = ((Component)btn).GetComponentsInChildren<TMP_Text>(true);
			if (texts == null) return null;

			string progress = null;
			string goal = null;
			string rewards = null;

			for (int i = 0; i < texts.Count; i++)
			{
				TMP_Text tmp = texts[i];
				if ((Object)(object)tmp == (Object)null) continue;
				string goName = ((Object)((Component)tmp).gameObject).name;
				string val = tmp.text;
				if (string.IsNullOrWhiteSpace(val)) continue;
				val = val.Trim();

				if (goName == "text_missionprogress") progress = val;
				else if (goName == "text_missiongoal") goal = val;
				else if (goName == "text_credits" && rewards == null) rewards = val + " credits";
				else if (goName == "text_battlepass" && rewards == null) rewards = val + " season XP";
			}

			if (progress != null && goal != null)
			{
				string result = progress + " of " + goal;
				if (progress == goal) result += ", complete";
				if (rewards != null) result += ", reward: " + rewards;
				return result;
			}
		}
		catch { }
		return null;
	}

	// --- Filtering ---

	private bool IsExcludedButton(Button btn)
	{
		if (btn == null) return true;
		try
		{
			Transform val = ((Component)btn).transform;
			int depth = 0;
			while (val != null && depth < 12)
			{
				string name = ((Object)val.gameObject).name;
				foreach (string excl in _excludeParents)
				{
					if (name.Contains(excl, StringComparison.OrdinalIgnoreCase))
						return true;
				}
				val = val.parent;
				depth++;
			}
			return false;
		}
		catch { return true; }
	}

	private bool IsNumericLabel(string label)
	{
		if (string.IsNullOrEmpty(label)) return false;
		foreach (char c in label)
		{
			if (!char.IsDigit(c) && c != ',' && c != '.' && c != '/' && c != ' ' && c != '%' && c != '+' && c != '-')
				return false;
		}
		return true;
	}

	private bool IsJunkLabel(string label)
	{
		if (string.IsNullOrWhiteSpace(label)) return true;
		if (label.Length < 2) return true;
		if (_junkLabels.Contains(label)) return true;
		foreach (string partial in _junkPartials)
		{
			if (label.Contains(partial, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	private bool IsJunkScreenText(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return true;
		foreach (string pattern in _junkTextPatterns)
		{
			if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		// Filter card cosmetic/rarity labels
		if (text.Equals("Common", StringComparison.OrdinalIgnoreCase)) return true;
		if (text.Equals("Uncommon", StringComparison.OrdinalIgnoreCase)) return true;
		if (text.Equals("Rare", StringComparison.OrdinalIgnoreCase)) return true;
		if (text.Equals("Epic", StringComparison.OrdinalIgnoreCase)) return true;
		if (text.Equals("Legendary", StringComparison.OrdinalIgnoreCase)) return true;
		if (text.Equals("Infinity", StringComparison.OrdinalIgnoreCase)) return true;
		if (text.Equals("Ultra", StringComparison.OrdinalIgnoreCase)) return true;
		// Filter XP patterns like "0/5 XP"
		if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+/\d+\s*XP$")) return true;
		// Filter cosmetic upgrade labels like "+1", "+2"
		if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\+\d+$")) return true;
		return false;
	}

	private void CleanupDestroyedButtons()
	{
		for (int i = _buttons.Count - 1; i >= 0; i--)
		{
			// Skip null entries — these are input field placeholders
			if (i == _inputFieldButtonIndex && _buttons[i] == null)
			{
				// Check if the input field itself is still valid
				if ((Object)(object)_activeInputField == (Object)null ||
					!((Component)_activeInputField).gameObject.activeInHierarchy)
				{
					_buttons.RemoveAt(i);
					if (i < _labels.Count) _labels.RemoveAt(i);
					_inputFieldButtonIndex = -1;
				}
				continue;
			}
			try
			{
				if (_buttons[i] == null || !_buttons[i].gameObject.activeInHierarchy)
				{
					_buttons.RemoveAt(i);
					if (i < _labels.Count) _labels.RemoveAt(i);
					// Adjust input field index if it shifted
					if (_inputFieldButtonIndex > i) _inputFieldButtonIndex--;
				}
			}
			catch
			{
				_buttons.RemoveAt(i);
				if (i < _labels.Count) _labels.RemoveAt(i);
				if (_inputFieldButtonIndex > i) _inputFieldButtonIndex--;
			}
		}
		if (_focusIndex >= _buttons.Count)
			_focusIndex = (_buttons.Count > 0) ? (_buttons.Count - 1) : -1;
	}
}
