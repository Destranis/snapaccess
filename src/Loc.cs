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
        _english["help_text"] = "Left/Right: navigate. Enter: select. Backspace: go back. F1: context help. F3: repeat last. F4: mod settings. O: game log. F12: debug.";
        _english["input_deleted"] = "deleted {0}";
        _english["input_pasted"] = "pasted text";
        _english["input_cleared"] = "cleared";
        _english["input_space"] = "space";
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
        _english["settings_done_adjusting"] = "Done adjusting. {0}";
        _english["settings_adjusting"] = "Adjusting {0}, {1}. Left and Right to change, Enter or Backspace to finish.";

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
        _english["menu_opening_alliances"] = "Opening Alliances";
        _english["menu_opening_deck_editor"] = "Opening deck editor.";
        _english["menu_opening_rewards"] = "Opening {0} rewards.";
        _english["menu_locked_reason"] = "Locked. {0}";
        _english["menu_no_details"] = "No details available for this event.";
        _english["menu_deck_not_available"] = "Deck selection not available.";
        _english["menu_rewards_count"] = "Rewards, {0} events.";
        _english["menu_no_rewards"] = "No rewards found.";
        _english["menu_game_modes_count"] = "Game Modes. {0} modes.";
        _english["menu_no_game_modes"] = "No game modes found.";
        _english["menu_mode_locked"] = "This mode is locked.";
        _english["menu_collection_tab"] = "Collection, {0} tab. {1} categories. Left and Right to browse, Tab to switch tabs, Enter to open.";
        _english["menu_collection_empty"] = "Collection is empty or loading.";
        _english["menu_collection_no_letter"] = "No item starting with {0}.";
        _english["menu_tab_switch_failed"] = "Could not switch to {0} tab.";
        _english["menu_edit_not_available"] = "Edit not available.";
        _english["menu_delete_not_available"] = "Delete not available.";
        _english["menu_copy_not_available"] = "Copy not available.";
        _english["menu_delete_confirm"] = "Delete deck? Left for Cancel, Right for Confirm. Enter to select.";
        _english["menu_confirm_not_found"] = "Confirm dialog not found.";
        _english["menu_cancel"] = "Cancel";
        _english["menu_confirm_delete"] = "Confirm delete";
        _english["menu_deck_deleted"] = "Deck deleted. Press Enter on Deck category to see updated list.";
        _english["menu_cost"] = "Cost {0}";
        _english["menu_cost_unknown"] = "Cost unknown";
        _english["menu_power"] = "Power {0}";
        _english["menu_power_unknown"] = "Power unknown";
        _english["menu_no_ability"] = "No ability text available";
        _english["menu_reward_load_failed"] = "Could not load reward details.";
        _english["menu_reward_details_closed"] = "Reward details closed.";
        _english["menu_reward_not_yet"] = "Not available yet. {0} remaining.";
        _english["menu_reward_not_claimable"] = "This reward cannot be claimed.";
        _english["menu_edit_button_not_found"] = "Edit button not found.";

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
        _english["deck_builder_back"] = "Closing deck editor.";
        _english["deck_builder_saving"] = "Saving deck.";
        _english["deck_builder_help"] = "Tab: switch area. Left/Right: browse cards. A-Z: jump to letter. Home/End: first/last. Down: details. Enter: add or remove. S: save. Backspace: close.";
        _english["deck_builder_context"] = "Deck Editor: {0}, {1} cards.";
        _english["deck_builder_area_deck"] = "Deck cards, {0} cards.";
        _english["deck_builder_area_collection"] = "Collection, {0} cards.";
        _english["deck_builder_deck_card"] = "{0}, card {1} of {2}";
        _english["deck_builder_collection_card"] = "{0}, card {1} of {2}";
        _english["deck_builder_cost"] = "cost {0}";
        _english["deck_builder_power"] = "power {0}";
        _english["deck_builder_remove_hint"] = "Enter to remove from deck.";
        _english["deck_builder_add_hint"] = "Enter to add to deck.";
        _english["deck_builder_removed"] = "Removed {0} from deck.";
        _english["deck_builder_added"] = "Added {0} to deck.";
        _english["deck_builder_remove_failed"] = "Could not remove {0}.";
        _english["deck_builder_add_failed"] = "Could not add {0}.";
        _english["deck_builder_deck_empty"] = "No cards in deck.";
        _english["deck_builder_collection_empty"] = "No collection cards found.";
        _english["deck_builder_info"] = "Deck: {0}, {1} of 12 cards.";
        _english["deck_builder_no_save"] = "No save button found.";
        _english["deck_builder_unnamed"] = "Unnamed Deck";
        _english["deck_builder_no_letter"] = "No card starting with {0}.";

        // Battlefield
        _english["bf_not_in_game"] = "Not in a match.";
        _english["bf_help"] = "C: Hand, B: Locations. Arrows: Navigate. Home/End: First/Last. Down: Details. Enter: Select card. 1/2/3: Quick-play to location. E: End Turn. T: Turn Info + Cubes. W: Timer. A: Energy. D: Drawn cards. S: Silence. G: Snap. R: Retreat. O: Game log. Space: Advance tutorial.";
        _english["bf_card_info"] = "{0}, {1}, card {2} of {3}";
        _english["bf_location_info"] = "{0}, {1}, location {2} of {3}";
        _english["bf_game_entered"] = "Game started.";
        _english["bf_card_brief"] = "{0}, cost {1}, card {2} of {3}";
        _english["bf_location_brief"] = "{0}, location {1} of {2}";
        _english["bf_power_score"] = "You {0}, Opponent {1}";
        _english["bf_slots_used"] = "{0} of 4 slots used";
        _english["bf_location_full"] = "Full, cannot play here";
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
        _english["bf_play_failed_silent"] = "{0} could not be played. Card is still in hand.";
        _english["bf_card_face_down"] = "Face-down card, card {0} of {1}. Press Enter to reveal.";
        _english["bf_card_revealing"] = "Revealing card.";
        _english["bf_card_restricted"] = "Cannot play {0} here.";
        _english["bf_no_cards"] = "No cards in hand.";
        _english["bf_no_locations"] = "No locations found.";
        _english["bf_end_turn"] = "Ending turn.";
        _english["bf_no_end_turn"] = "Cannot end turn yet.";
        _english["bf_turn_info"] = "Turn {0} of {1}.";
        _english["bf_turn_info_raw"] = "Turn: {0}";
        _english["bf_final_turn"] = "Final turn.";
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
        _english["bf_turn_start"] = "Turn {0}, energy {1}, go.";
        _english["bf_turn_start_final"] = "Final turn, energy {0}, go.";
        _english["bf_no_new_draws"] = "No new cards drawn.";
        _english["bf_waiting_for_opponent"] = "Waiting for opponent.";
        _english["bf_game_over"] = "Game over. {0}";
        _english["bf_game_over_instructions"] = "Press E to collect rewards. Press Space to skip animations.";
        _english["bf_result_win"] = "You won!";
        _english["bf_result_lose"] = "You lost.";
        _english["bf_result_draw"] = "Draw.";
        _english["bf_result_cubes"] = "{0} cubes at stake.";
        _english["bf_result_location"] = "{0}: you {1}, opponent {2}";
        _english["bf_cube_stake"] = "Cubes: {0}.";
        _english["bf_detail_cost"] = "Cost {0}";
        _english["bf_detail_cost_unknown"] = "Cost unknown";
        _english["bf_detail_power"] = "Power {0}";
        _english["bf_detail_power_unknown"] = "Power unknown";
        _english["bf_detail_no_ability"] = "No ability";
        _english["bf_detail_no_description"] = "No description";
        _english["bf_detail_no_cards_here"] = "No cards here";
        _english["bf_your_cards_at_loc"] = "Your cards: {0}.";
        _english["bf_no_your_cards_at_loc"] = "No cards from you.";
        _english["bf_opponent_cards_at_loc"] = "Opponent cards: {0}.";
        _english["bf_no_opponent_cards_at_loc"] = "No opponent cards.";
        _english["bf_btn_upgrade"] = "Upgrade";
        _english["bf_btn_next"] = "Next";
        _english["bf_btn_collect"] = "Collect";
        _english["bf_btn_claim"] = "Claim";
        _english["bf_btn_ok"] = "OK";
        _english["bf_btn_confirm"] = "Confirm";
        _english["bf_btn_cancel"] = "Cancel";
        _english["bf_btn_close"] = "Close";
        _english["bf_btn_back"] = "Back";
        _english["bf_btn_retreat"] = "Retreat";
        _english["bf_btn_stay"] = "Stay";
        _english["bf_btn_resume"] = "Resume";
        _english["bf_end_turn_guard"] = "You have {0} playable cards. Press E again to end turn.";
        _english["bf_quickplay_preview"] = "{0}. You {1}, Opponent {2}. {3} of 4 slots. Press again to play.";
        _english["bf_opponent_played"] = "Opponent played {0}";
        _english["bf_opponent_at_location"] = "at {0}";
        _english["bf_timer_warning"] = "{0} seconds left!";
        _english["bf_timer_urgent"] = "{0} seconds!";
        _english["bf_timer_remaining"] = "{0} seconds remaining.";
        _english["bf_timer_not_active"] = "No active timer.";

        // Login / Consent
        _english["login_no_elements"] = "No interactive elements on this screen.";
        _english["login_focused"] = "Focused: {0}. Element {1} of {2}.";
        _english["login_element_focus"] = "{0}, {1} of {2}";
        _english["login_help"] = "Login screen. Left/Right: navigate elements. Enter: activate. Up/Down: read text.";

        // Friendly Match
        _english["fm_help"] = "Friendly Battle. Left/Right: switch Create and Join. Enter: activate. Down: details. Backspace: close.";
        _english["fm_focus_create"] = "Create Match, 1 of 2";
        _english["fm_focus_join"] = "Join Match, 2 of 2";
        _english["fm_cancelled"] = "Cancelled. Back to Friendly Battle.";
        _english["fm_no_code"] = "No code entered. Type or paste a code first, then press Enter.";
        _english["fm_join_not_found"] = "Join button not found.";
        _english["fm_joining"] = "Joining match with code {0}";
        _english["fm_code_empty"] = "Code field is empty. Type or paste a code.";
        _english["fm_current_code"] = "Current code: {0}";
        _english["fm_creating"] = "Creating match...";
        _english["fm_create_not_found"] = "Create button not found.";
        _english["fm_code_copied"] = "Code copied: {0}";
        _english["fm_code_input_not_found"] = "Code input field not found.";
        _english["fm_enter_code"] = "Type or paste match code, then press Enter to join. Backspace to cancel.";
        _english["fm_existing_code"] = "Your match code: {0}. Press Enter to copy.";
        _english["fm_create_hint"] = "Press Enter to create a match and generate a code for your friend.";
        _english["fm_code_entered"] = "Code entered: {0}. Press Enter to join.";
        _english["fm_code_enter_hint"] = "Press Enter, then type or paste a match code from your friend.";
        _english["fm_match_code"] = "Match code: {0}. Copied to clipboard. Share with your friend.";
        _english["fm_match_created"] = "Match created. Check screen for your code.";
        _english["fm_closing"] = "Closing Friendly Battle.";

        // Missions
        _english["ms_help"] = "Missions. Left/Right: browse. Home/End: first/last. Enter: open category or claim. Down: details. Backspace: back.";
        _english["ms_category_count"] = "{0} missions in this category.";
        _english["ms_claiming"] = "Claiming {0}";
        _english["ms_opening"] = "Opening {0}";

        // Play Deck Tray
        _english["pdt_help"] = "Deck Selection. {0} decks. Left/Right: browse. Home/End: first/last. Enter: select and equip. E: edit. Backspace: close.";
        _english["pdt_deck_info"] = "Deck: {0}. Enter to select and equip, E to edit.";

        // Dialog
        _english["dialog_help"] = "Dialog. Left/Right: navigate buttons. Up/Down: read screen text. Home/End: first/last button. Enter: activate. Backspace: close.";

        // Deck Builder
        _english["deck_builder_entry"] = "Deck Builder: {0}, {1} of 12 cards.";

        // Game Log
        _english["log_opened"] = "Game log. {0} entries. Up/Down to browse, O or Escape to close.";
        _english["log_closed"] = "Game log closed.";
        _english["log_empty"] = "Game log is empty.";
        _english["log_end"] = "End of log.";
        _english["log_start"] = "Start of log.";
        _english["log_of"] = "of";

        // Shop
        _english["shop_entered"] = "Shop. {0} sections, {1} items. Left/Right: browse. Enter: open section or select item. Tab: switch tabs. Backspace: back.";
        _english["shop_help"] = "Shop. Left/Right: browse sections or items. Enter: open or select. Down: details. Tab: switch tabs. Backspace: back.";
        _english["shop_tab_switched"] = "Switched to {0} tab.";
        _english["shop_section_count"] = "{0} items in this section.";
        _english["shop_activating"] = "Opening {0}";
        _english["shop_items"] = "Items";
        _english["shop_items_label"] = "items";
        _english["shop_price"] = "Price";

        // Battle Pass
        _english["bp_entered"] = "Season Pass. {0} sections, {1} items. Left/Right: browse. Enter: open. Down: details. Backspace: back.";
        _english["bp_help"] = "Season Pass. Left/Right: browse. Enter: open section or claim. Down: details. Backspace: back.";

        // Mod Settings
        _english["mod_settings_opened"] = "Mod Settings. Up/Down: browse. Enter or Left/Right: toggle. Escape: close and save.";
        _english["mod_settings_closed"] = "Settings saved.";
        _english["mod_setting_position_counts"] = "Position Counts";
        _english["mod_setting_position_counts_desc"] = "Announce card X of Y and location X of Y during navigation.";
        _english["mod_setting_verbose_cards"] = "Verbose Card Info";
        _english["mod_setting_verbose_cards_desc"] = "Announce cost and power when navigating cards.";
        _english["mod_setting_opponent"] = "Opponent Announcements";
        _english["mod_setting_opponent_desc"] = "Announce opponent card plays and reveals.";
        _english["mod_setting_auto_turn"] = "Auto Turn Announce";
        _english["mod_setting_auto_turn_desc"] = "Automatically announce turn start. If off, press T for turn info.";
        _english["mod_setting_transitions"] = "Screen Transitions";
        _english["mod_setting_transitions_desc"] = "Announce screen name when switching between screens.";
        _english["mod_setting_tutorials"] = "Tutorial Messages";
        _english["mod_setting_tutorials_desc"] = "Announce tutorial hints and guidance.";

        // Navigator transitions
        _english["nav_battlefield"] = "Battlefield";
        _english["nav_main_menu"] = "Main Menu";
        _english["nav_dialog"] = "Dialog";
        _english["nav_login"] = "Login";
        _english["nav_deck_builder"] = "Deck Builder";
        _english["nav_deck_tray"] = "Deck Selection";
        _english["nav_missions"] = "Missions";
        _english["nav_friendly_match"] = "Friendly Battle";
        _english["nav_shop"] = "Shop";
    }
}
