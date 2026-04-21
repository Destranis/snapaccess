using System;
using System.Collections.Generic;
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
/// Provides Left/Right + Enter navigation and Down for screen content reading.
/// </summary>
public class DialogHandler
{
	private readonly List<Button> _buttons = new List<Button>();
	private readonly List<string> _labels = new List<string>();
	private readonly List<string> _screenTexts = new List<string>();

	private int _focusIndex = -1;
	private int _textReadIndex = -1;
	private bool _scanned = false;
	private bool _needsRescan = false;
	private float _rescanDelay = 0f;
	private float _inputBlockUntil = 0f; // Block input briefly after reset to prevent double-activation
	private GameObject _scanRoot = null;

	// --- Settings mode ---
	private enum SettingsItemType { Slider, Toggle, Button }

	private struct SettingsItem
	{
		public string Label;
		public SettingsItemType Type;
		public Slider Slider;
		public Toggle Toggle;
		public Button Button;
	}

	private bool _settingsMode = false;
	private readonly List<SettingsItem> _settingsItems = new List<SettingsItem>();
	private int _settingsIndex = -1;
	private const float SliderStep = 0.05f; // 5% per Left/Right press

	// GameObjects to exclude from scanning (by name pattern)
	// NOTE: Canvas-Rewards is NOT excluded — the scoring system naturally deprioritises it
	// when other screens are open (score ~31 vs News ~57, Collection ~128).
	// When no screen is active, it becomes the content root so the user can claim rewards.
	private static readonly string[] _excludeParents = new string[]
	{
		"Navigator", "NavBar", "FullscreenModal", "Canvas-Game",
		"Canvas-Gameplay", "LoadingScreen", "PopupCanvas",
		"CardDetailsCardView", "CardDetail",
		"AlbumCardView", "CardPreview",
		"CarouselStagingArea",
		"ObjectPool",
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
		"Button  Background Close",
	};

	private static readonly string[] _junkPartials = new string[]
	{
		"img ", "Parallelogram", "Backing", "Hex Prp", "Glass",
		"Bracket", "blocker", "catcher",
	};

	// Text patterns to skip when reading screen content
	private static readonly string[] _junkTextPatterns = new string[]
	{
		"img ", "\\u", "<sprite", "Glass", "(Clone)",
		"{Missing Entry}", "Missing Entry",
	};

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
	};

	public bool HasActiveDialog => _buttons.Count > 0 || _settingsMode;

	public bool Update()
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

		if (_settingsMode)
		{
			if (_settingsItems.Count == 0) return false;
			ProcessSettingsInput();
			return true;
		}

		if (_buttons.Count == 0)
		{
			return false;
		}
		ProcessInput();
		return true;
	}

	public void AnnounceContext()
	{
		if (_buttons.Count == 0)
		{
			ScreenReader.Say(Loc.Get("dialog_no_buttons"));
		}
		else if (_focusIndex >= 0 && _focusIndex < _buttons.Count)
		{
			ScreenReader.Say(Loc.Get("dialog_focused_button", GetLabel(_focusIndex), _focusIndex + 1, _buttons.Count));
		}
		else
		{
			ScreenReader.Say(Loc.Get("dialog_has_buttons", _buttons.Count));
		}
	}

	public void Reset()
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
		// Block input for 0.3s to prevent the Enter that triggered Reset from double-firing
		_inputBlockUntil = Time.time + 0.3f;
	}

	/// <summary>Reset with a delayed first scan, for cases where UI hasn't rendered yet.</summary>
	public void ResetWithDelay(float delay = 0.5f)
	{
		Reset();
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

			// Announce: first button + how many total
			ScreenReader.Say(Loc.Get("dialog_button_focus", GetLabel(_focusIndex), _focusIndex + 1, _buttons.Count));

			// Queue screen text content so user hears it after button announcement
			if (_screenTexts.Count > 0)
			{
				ScreenReader.SayQueued(string.Join(". ", _screenTexts));
			}
		}
		else
		{
			_focusIndex = -1;
			// No buttons — just read the text content directly
			if (_screenTexts.Count > 0)
			{
				ScreenReader.Say(string.Join(". ", _screenTexts));
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

			if (_settingsItems.Count == 0) return false;

			_settingsIndex = 0;
			DebugLogger.Log(LogCategory.Handler, "DialogHandler",
				$"Settings mode: {_settingsItems.Count} items ({sliderCount} sliders, {toggleCount} toggles)");

			// Announce
			ScreenReader.Say(Loc.Get("settings_entered", _settingsItems.Count));
			AnnounceCurrentSetting();
			return true;
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "DialogHandler", "TryScanAsSettings failed: " + ex.Message);
			return false;
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
		ScreenReader.Say(msg);
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

		// Up: previous item
		if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
		{
			if (_settingsItems.Count == 0) return;
			_settingsIndex--;
			if (_settingsIndex < 0) _settingsIndex = _settingsItems.Count - 1;
			AnnounceCurrentSetting();
		}
		// Down: next item
		else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
		{
			if (_settingsItems.Count == 0) return;
			_settingsIndex++;
			if (_settingsIndex >= _settingsItems.Count) _settingsIndex = 0;
			AnnounceCurrentSetting();
		}
		// Left: decrease slider or toggle off
		else if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
		{
			AdjustCurrentSetting(-1);
		}
		// Right: increase slider or toggle on
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			AdjustCurrentSetting(1);
		}
		// Enter: activate button or toggle
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			ActivateCurrentSetting();
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
						ScreenReader.Say($"{Mathf.RoundToInt(normalized * 100)}%");
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
							ScreenReader.Say(newState ? Loc.Get("settings_on") : Loc.Get("settings_off"));
						}
						else
						{
							ScreenReader.Say(newState ? Loc.Get("settings_already_on") : Loc.Get("settings_already_off"));
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
						ScreenReader.Say(item.Toggle.isOn ? Loc.Get("settings_on") : Loc.Get("settings_off"));
					}
					break;
				case SettingsItemType.Button:
					if ((Object)(object)item.Button != (Object)null)
					{
						ScreenReader.Say(Loc.Get("dialog_activating", item.Label));
						// Try onClick first, fall back to mouse simulation
						if (!UIHelper.ClickButton(item.Button))
							UIHelper.SimulateMouseClick(((Component)item.Button).gameObject);
						// Schedule rescan to pick up any UI changes
						_needsRescan = true;
						_rescanDelay = Time.time + 1.0f;
					}
					break;
				case SettingsItemType.Slider:
					// Enter on slider announces current value
					string value = GetSettingValue(item);
					ScreenReader.Say($"{item.Label}, {value}");
					break;
			}
		}
		catch (Exception ex)
		{
			DebugLogger.Log(LogCategory.Handler, "DialogHandler", "ActivateCurrentSetting failed: " + ex.Message);
		}
	}

	// --- Input ---

	private void ProcessInput()
	{
		// Block input briefly after reset to prevent double-activation
		if (Time.time < _inputBlockUntil) return;

		if (_buttons.Count == 0) return;

		if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
		{
			CleanupDestroyedButtons();
			if (_buttons.Count == 0) return;
			_focusIndex = (_focusIndex - 1 + _buttons.Count) % _buttons.Count;
			ScreenReader.Say(Loc.Get("dialog_button_focus", GetLabel(_focusIndex), _focusIndex + 1, _buttons.Count));
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
		{
			CleanupDestroyedButtons();
			if (_buttons.Count == 0) return;
			_focusIndex = (_focusIndex + 1) % _buttons.Count;
			ScreenReader.Say(Loc.Get("dialog_button_focus", GetLabel(_focusIndex), _focusIndex + 1, _buttons.Count));
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
		{
			ActivateFocused();
		}
		else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace))
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
					UIHelper.ClickButton(_buttons[i]);
					DebugLogger.Log(LogCategory.Handler, "DialogHandler", "Closed dialog via: " + goName);
					return;
				}
			}
			// Fall back to game's Escape/Back button
			Il2CppArrayBase<Button> allButtons = Object.FindObjectsOfType<Button>();
			if (allButtons != null)
			{
				for (int i = 0; i < allButtons.Count; i++)
				{
					Button btn = allButtons[i];
					if ((Object)(object)btn == (Object)null) continue;
					if (!((Component)btn).gameObject.activeInHierarchy) continue;
					string goName = ((Object)((Component)btn).gameObject).name;
					if (goName == "btn_hex_prp")
					{
						UIHelper.ClickButton(btn);
						DebugLogger.Log(LogCategory.Handler, "DialogHandler", "Closed via game back button");
						return;
					}
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
			ScreenReader.Say(Loc.Get("dialog_no_text"));
			return;
		}
		_textReadIndex++;
		if (_textReadIndex >= _screenTexts.Count)
		{
			_textReadIndex = _screenTexts.Count - 1;
			ScreenReader.Say(Loc.Get("dialog_end_of_text"));
			return;
		}
		ScreenReader.Say(Loc.Get("dialog_text_line", _screenTexts[_textReadIndex], _textReadIndex + 1, _screenTexts.Count));
	}

	private void ReadPreviousScreenText()
	{
		if (_screenTexts.Count == 0) return;
		_textReadIndex--;
		if (_textReadIndex < 0)
		{
			_textReadIndex = -1;
			// Back to button navigation
			if (_focusIndex >= 0 && _focusIndex < _buttons.Count)
				ScreenReader.Say(Loc.Get("dialog_button_focus", GetLabel(_focusIndex), _focusIndex + 1, _buttons.Count));
			return;
		}
		ScreenReader.Say(Loc.Get("dialog_text_line", _screenTexts[_textReadIndex], _textReadIndex + 1, _screenTexts.Count));
	}

	private void ActivateFocused()
	{
		CleanupDestroyedButtons();
		if (_focusIndex < 0 || _focusIndex >= _buttons.Count)
		{
			ScreenReader.Say(Loc.Get("dialog_no_focus"));
			return;
		}
		Button button = _buttons[_focusIndex];
		string label = GetLabel(_focusIndex);
		DebugLogger.LogInput("Enter", "Clicking: " + label);
		ScreenReader.Say(Loc.Get("dialog_activating", label));
		if (!UIHelper.ClickButton(button))
			UIHelper.SimulateMouseClick(((Component)button).gameObject);

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
		return false;
	}

	private void CleanupDestroyedButtons()
	{
		for (int i = _buttons.Count - 1; i >= 0; i--)
		{
			try
			{
				if (_buttons[i] == null || !_buttons[i].gameObject.activeInHierarchy)
				{
					_buttons.RemoveAt(i);
					if (i < _labels.Count) _labels.RemoveAt(i);
				}
			}
			catch
			{
				_buttons.RemoveAt(i);
				if (i < _labels.Count) _labels.RemoveAt(i);
			}
		}
		if (_focusIndex >= _buttons.Count)
			_focusIndex = (_buttons.Count > 0) ? (_buttons.Count - 1) : -1;
	}
}
