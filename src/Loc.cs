using System;
using System.Collections.Generic;
using MelonLoader;

namespace SnapAccess;

/// <summary>
/// Manages localization strings.
/// Current version: English only.
/// </summary>
public static class Loc
{
    private static bool _initialized = false;
    private static readonly Dictionary<string, string> _english = new Dictionary<string, string>();

    public static void Initialize()
    {
        InitializeStrings();
        _initialized = true;
        MelonLogger.Msg("Localization initialized (English)");
    }

    public static string Get(string key)
    {
        if (!_initialized) Initialize();
        if (_english.TryGetValue(key, out var value)) return value;
        return key;
    }

    public static string Get(string key, params object[] args)
    {
        string text = Get(key);
        try { return string.Format(text, args); }
        catch { return text; }
    }

    private static void InitializeStrings()
    {
        // General
        _english["mod_loaded"] = "Snap Access loaded. F1 for help.";
        _english["help_text"] = "Navigation: Up/Down categories, Enter to drill in, Backspace to go back. F1 context help. F12 debug.";
        _english["debug_enabled"] = "Debug mode enabled";
        _english["debug_disabled"] = "Debug mode disabled";

        // Dialog
        _english["dialog_no_buttons"] = "No buttons found.";
        _english["dialog_has_buttons"] = "{0} buttons. Left and right to browse, Enter to activate.";
        _english["dialog_focused_button"] = "Focused: {0}. Button {1} of {2}.";
        _english["dialog_button_focus"] = "{0}, button {1} of {2}";
        _english["dialog_no_focus"] = "No button focused.";
        _english["dialog_activating"] = "Activating {0}";
        _english["dialog_error"] = "Could not activate button.";
        _english["dialog_text_line"] = "{0}. Text {1} of {2}.";
        _english["dialog_no_text"] = "No text on this screen.";
        _english["dialog_end_of_text"] = "End of text.";
        _english["dialog_editing"] = "Editing {0}. Type your text, then press Enter to finish.";
        _english["dialog_done_editing"] = "Done editing. {0}";
        _english["dialog_done_editing_empty"] = "Done editing. Field is empty.";
        _english["dialog_editing_cancelled"] = "Editing cancelled.";
        _english["dialog_char_deleted"] = "Deleted.";

        // Settings
        _english["settings_entered"] = "Settings. {0} items. Up and down to browse, left and right to adjust.";
        _english["settings_on"] = "on";
        _english["settings_off"] = "off";
        _english["settings_already_on"] = "Already on.";
        _english["settings_already_off"] = "Already off.";

        // Main Menu
        _english["menu_opened"] = "Main menu. Left and right to navigate, Enter to select.";
        _english["menu_current_section"] = "Current section: {0}";
        _english["menu_button"] = "{0}, {1} of {2}";
        _english["menu_button_active"] = "{0}, current, {1} of {2}";
        _english["menu_activated"] = "Opening {0}";
        _english["menu_locked"] = "{0}, locked, {1} of {2}";
        _english["menu_context"] = "{0},{3} {1} of {2}. Left and right to navigate, Enter to select.";
        _english["menu_news"] = "News";
        _english["menu_shop"] = "Shop";
        _english["menu_play"] = "Play";
        _english["menu_collection"] = "Collection";
        _english["menu_game_modes"] = "Game Modes";
        _english["menu_clan"] = "Clan";
        _english["menu_settings"] = "Settings";
        _english["menu_nav_focus"] = "Menu bar.";

        // Play screen
        _english["play_screen"] = "Play screen.";
        _english["play_deck"] = "Deck: {0}.";
        _english["play_menu_start"] = "Start Game";
        _english["play_menu_select_deck"] = "Select Deck (Current: {0})";
        _english["play_menu_edit_deck"] = "Edit Current Deck";
        _english["play_menu_friendly"] = "Friendly Match (Battle Mode)";
        _english["play_menu_missions"] = "Missions";
        _english["play_menu_rewards"] = "Rewards";
        _english["play_menu_info"] = "Season & Rank Info";
        _english["play_starting"] = "Starting game.";
        _english["play_opening_deck_selector"] = "Opening deck selector.";
        _english["play_no_deck_switch"] = "No deck selector available.";

        // Deck Builder
        _english["deck_builder_back"] = "Back to deck options.";
        _english["deck_builder_saving"] = "Saving deck changes.";

        // Battlefield
        _english["bf_not_in_game"] = "Not in a match.";
        _english["bf_help"] = "C: Hand, B: Locations. Arrows: Navigate. Down: Details. Enter: Play. E: End Turn. T: Turn Info. A: Energy. G: Snap. R: Retreat. Space: Advance tutorial.";
        _english["bf_card_info"] = "{0}, {1}, card {2} of {3}";
        _english["bf_location_info"] = "{0}, {1}, location {2} of {3}";
        _english["bf_game_entered"] = "Game started.";
        _english["bf_card_brief"] = "{0}, cost {1}, card {2} of {3}";
        _english["bf_location_brief"] = "{0}, location {1} of {2}";
        _english["bf_card_drawn"] = "Drew {0}";
        _english["bf_hand_count"] = "{0} cards in hand.";
        _english["bf_location_count"] = "{0} locations.";
        _english["bf_card_selected"] = "{0} selected. Choose a location.";
        _english["bf_card_deselected"] = "{0} deselected.";
        _english["bf_choose_location"] = "Choose location for {0}.";
        _english["bf_card_played"] = "Played {0} to {1}.";
        _english["bf_play_failed"] = "Could not play {0} to {1}.";
        _english["bf_play_error"] = "Play failed.";
        _english["bf_play_rolled_back"] = "{0} was returned to hand.";
        _english["bf_card_restricted"] = "Cannot play {0} here.";
        _english["bf_no_cards"] = "No cards in hand.";
        _english["bf_no_locations"] = "No locations found.";
        _english["bf_end_turn"] = "Ending turn.";
        _english["bf_no_end_turn"] = "Cannot end turn yet.";
        _english["bf_turn_info"] = "Turn {0} of {1}.";
        _english["bf_turn_info_raw"] = "Turn: {0}";
        _english["bf_turn_not_available"] = "Turn info not available.";
        _english["bf_tutorial_instruction"] = "{0}";
        _english["bf_tutorial_advance"] = "Advancing tutorial.";
        _english["bf_no_tutorial"] = "No tutorial active.";
        _english["bf_tap_to_continue"] = "Tap to continue. Press Space.";
        _english["bf_leaving_game"] = "Leaving game.";
        _english["bf_snapped"] = "Snapped!";
        _english["bf_snap_no_button"] = "Snap not available.";
        _english["bf_snap_not_available"] = "Already snapped. Cube value: {0}.";
        _english["bf_retreat_no_button"] = "Retreat not available.";
        _english["bf_retreat_confirm"] = "Press R again to retreat. You will lose {0} cubes.";
        _english["bf_retreat_initiated"] = "Retreating. Losing {0} cubes.";
        _english["bf_game_entered_vs"] = "Game started against {0}.";
        _english["bf_tutorial_hints"] = "{0}";
        _english["bf_your_turn"] = "Your turn.";
        _english["bf_waiting_for_opponent"] = "Waiting for opponent.";
        _english["bf_game_over"] = "Game over. {0}";
        _english["bf_game_over_instructions"] = "Press E to collect rewards.";
        _english["bf_result_win"] = "You won!";
        _english["bf_result_lose"] = "You lost.";
        _english["bf_result_draw"] = "Draw.";
    }
}
