using System;
using System.Collections.Generic;
using MelonLoader;

namespace SnapAccess;

public static class Loc
{
    private static bool _initialized = false;

    private static readonly Dictionary<string, string> _english = new Dictionary<string, string>();

    public static void Initialize()
    {
        InitializeStrings();
        _initialized = true;
        MelonLogger.Msg("Localization initialized");
    }

    public static string Get(string key)
    {
        if (!_initialized) Initialize();
        if (_english.TryGetValue(key, out var value))
        {
            return value;
        }
        return key;
    }

    public static string Get(string key, params object[] args)
    {
        string text = Get(key);
        try
        {
            return string.Format(text, args);
        }
        catch
        {
            return text;
        }
    }

    private static void InitializeStrings()
    {
        // General
        _english["mod_loaded"] = "Snap Access loaded. F1 for help.";
        _english["help_text"] = "Key bindings: F1 Help. F2 UI dump. F12 debug mode. Main menu: left right navigate, Enter select, Tab toggle menu and content. Play screen: Enter start, S select deck, D browse deck, M missions, R rewards, I info. Collection: left right browse cards, down details, Enter open, R rescan, Backspace exit. In game: C hand, B locations, left right browse, down details, up back. Enter play card. E end turn. A energy. Space advance tutorial.";
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
        _english["dialog_screen_hint"] = "Down arrow to read screen text, {0} items.";
        _english["dialog_text_only"] = "No buttons. Down arrow to read {0} text items.";
        _english["dialog_text_line"] = "{0}. Text {1} of {2}.";
        _english["dialog_no_text"] = "No text on this screen.";
        _english["dialog_end_of_text"] = "End of text.";

        // Settings
        _english["settings_entered"] = "Settings. {0} items. Up and down to browse, left and right to adjust.";
        _english["settings_on"] = "on";
        _english["settings_off"] = "off";
        _english["settings_already_on"] = "already on";
        _english["settings_already_off"] = "already off";

        // Main Menu
        _english["menu_opened"] = "Main menu. Left and right arrows to navigate, Enter to select.";
        _english["menu_current_section"] = "Current section: {0}";
        _english["menu_button"] = "{0}, {1} of {2}";
        _english["menu_button_active"] = "{0}, current, {1} of {2}";
        _english["menu_activated"] = "Opening {0}";
        _english["menu_locked"] = "{0}, locked, {1} of {2}";
        _english["menu_error"] = "Could not open section.";
        _english["menu_not_available"] = "Main menu not available.";
        _english["menu_current"] = " current";
        _english["menu_context"] = "{0},{3} {1} of {2}. Left and right to navigate, Enter to select.";
        _english["menu_help"] = "Main menu. Left and right arrows to navigate, Enter to select.";
        _english["menu_news"] = "News";
        _english["menu_shop"] = "Shop";
        _english["menu_play"] = "Play";
        _english["menu_collection"] = "Collection";
        _english["menu_game_modes"] = "Game Modes";
        _english["menu_clan"] = "Clan";
        _english["menu_settings"] = "Settings";
        _english["menu_settings_not_found"] = "Settings button not found.";
        _english["menu_content_focus"] = "Screen content. Left and right to browse, Enter to activate. Tab for menu bar.";
        _english["menu_nav_focus"] = "Menu bar.";

        // Play screen
        _english["play_screen"] = "Play screen.";
        _english["play_deck"] = "Deck: {0}.";
        _english["play_instructions"] = "Enter to start. F1 for help.";
        _english["play_instructions_full"] = "Enter to start. F1 for help.";
        _english["play_opening_deck_selector"] = "Opening deck selector.";
        _english["play_no_deck_switch"] = "No deck selector available.";
        _english["play_rank"] = "Rank {0}";
        _english["play_season"] = "Season: {0}.";
        _english["play_missions_header"] = "{0} missions:";
        _english["play_no_missions"] = "No missions found.";
        _english["play_no_info"] = "No rank or season info available.";
        _english["play_starting"] = "Starting game.";

        // Missions menu
        _english["missions_menu_opened"] = "Missions. {0} categories. Up and down to browse, Enter to select, Backspace to exit.";
        _english["missions_category"] = "{0}, {2} of {3}. {1} missions.";
        _english["missions_category_entered"] = "{0}. {1} missions. Left and right to browse, Down for details, Backspace to go back.";
        _english["missions_category_daily"] = "Daily Missions";
        _english["missions_category_weekly"] = "Weekly Missions";
        _english["missions_category_season"] = "Season Pass Missions";
        _english["missions_category_all"] = "Missions";
        _english["missions_mission"] = "{0}, {1} of {2}";
        _english["missions_description"] = "{0}";
        _english["missions_progress"] = "Progress: {0} of {1}";
        _english["missions_progress_unknown"] = "Progress not available.";
        _english["missions_complete"] = "complete";
        _english["missions_reward"] = "Reward: {0}";
        _english["missions_no_reward"] = "No reward info.";
        _english["missions_no_description"] = "No description.";
        _english["missions_exited"] = "Exited missions.";
        _english["missions_back_to_categories"] = "Back to categories.";
        _english["missions_claiming"] = "Claiming {0}.";
        _english["missions_opening"] = "Opening {0}.";
        _english["missions_no_action"] = "Cannot interact with this mission.";

        // Rewards menu
        _english["rewards_menu_opened"] = "Rewards. {0} categories. Up and down to browse, Enter to select, Backspace to exit.";
        _english["rewards_category"] = "{0}, {2} of {3}. {1} items.";
        _english["rewards_category_entered"] = "{0}. {1} items. Left and right to browse, Enter to claim, Backspace to go back.";
        _english["rewards_category_login"] = "Login Rewards";
        _english["rewards_category_season"] = "Season Pass";
        _english["rewards_item"] = "{0}, {1} of {2}";
        _english["rewards_claimable"] = "claimable";
        _english["rewards_opening"] = "Opening rewards.";
        _english["rewards_none"] = "No rewards available.";
        _english["rewards_exited"] = "Exited rewards.";
        _english["rewards_back_to_categories"] = "Back to categories.";

        // Collection
        _english["collection_entered"] = "Collection. {0} items. Left and right to browse, Down for details, Enter to open, Backspace to exit. R to rescan.";
        _english["collection_card"] = "{0}, {1} of {2}";
        _english["collection_opening"] = "Opening {0}.";
        _english["collection_exited"] = "Exited collection.";
        _english["collection_no_cards"] = "No items found in this section.";
        _english["collection_rescanned"] = "Rescanned. {0} items.";
        _english["collection_loading"] = "Loading collection, please wait.";
        _english["collection_section"] = "{0}, section {1} of {2}.";
        _english["collection_section_items"] = "{0}. {1} items.";
        _english["collection_section_hint"] = "Tab and Shift Tab to switch sections. Current: {0}.";
        _english["play_no_button"] = "Play button not found. Press R to refresh.";

        // Deck browsing
        _english["deck_browsing"] = "Deck: {0} cards. Left and right to browse, down for details, up or Escape to exit.";
        _english["deck_card"] = "{0}, {1} of {2}";
        _english["deck_card_cost"] = "Cost: {0}";
        _english["deck_card_power"] = "Power: {0}";
        _english["deck_card_no_ability"] = "No ability text.";
        _english["deck_no_cards"] = "No deck cards found.";
        _english["deck_exit"] = "Exited deck view.";
        _english["deck_empty_slot"] = "Empty deck slot";
        _english["deck_switched"] = "Deck changed to {0}.";
        _english["deck_opening_editor"] = "Opening deck editor for {0}.";

        // Battlefield - General
        _english["bf_game_entered"] = "Game started. F1 for help.";
        _english["bf_not_in_game"] = "Not in a game.";
        _english["bf_help"] = "C: hand. B: locations. Left and Right: browse. Down: details. Up: back. Enter: select or play. E: end turn. T: turn info. A: energy. G: snap. R: retreat. I: tutorial. Escape: cancel. Space: advance tutorial.";
        _english["bf_area_hand"] = "Hand.";
        _english["bf_area_locations"] = "Locations.";

        // Battlefield - Cards
        _english["bf_card_brief"] = "{0}, {1} of {2}";
        _english["bf_card_info"] = "{0}, {1}, card {2} of {3}";
        _english["bf_card_selected"] = "{0} selected. Press B, choose a location and press Enter.";
        _english["bf_card_deselected"] = "{0} deselected.";
        _english["bf_cant_play"] = "Cannot play {0}.";
        _english["bf_card_played"] = "Played {0} to {1}.";
        _english["bf_play_failed"] = "Could not play {0} to {1}. Try a different card.";
        _english["bf_play_error"] = "Could not play card.";
        _english["bf_card_restricted"] = "Cannot play {0} right now. Try again in a moment.";
        _english["bf_play_rolled_back"] = "{0} was rejected and returned to hand. Try a different card.";
        _english["bf_choose_location"] = "Choose location for {0}. Press B, select location, Enter to play.";
        _english["bf_card_drawn"] = "Drew {0}.";
        _english["bf_no_cards"] = "No cards in hand.";

        // Battlefield - Locations
        _english["bf_location_brief"] = "{0}, {1} of {2}";
        _english["bf_location_info"] = "{0}, {1}, location {2} of {3}";
        _english["bf_location_play"] = "{0}, {1}, location {2} of {3}. Press Enter to play {4} here.";
        _english["bf_no_locations"] = "No locations found.";

        // Battlefield - Turn
        _english["bf_end_turn"] = "Ending turn.";
        _english["bf_no_end_turn"] = "Cannot end turn.";

        // Battlefield - Info
        _english["bf_hand_count"] = "{0} cards in hand";
        _english["bf_location_count"] = "{0} locations";
        _english["bf_turn_info"] = "Turn {0} of {1}";
        _english["bf_turn_info_raw"] = "Turn {0}";
        _english["bf_turn_not_available"] = "Turn info not available";

        // Battlefield - Snap & Retreat
        _english["bf_snapped"] = "Snapped! Stakes doubled.";
        _english["bf_snap_not_available"] = "Cannot snap. Current stakes: {0} cubes.";
        _english["bf_snap_no_button"] = "Snap not available.";
        _english["bf_retreat_confirm"] = "Press R again within 3 seconds to retreat. You will lose {0} cubes.";
        _english["bf_retreat_initiated"] = "Retreating. You will lose {0} cubes.";
        _english["bf_retreat_no_button"] = "Retreat not available.";

        // Battlefield - Tutorial
        _english["bf_tutorial"] = "Tutorial: {0}";
        _english["bf_tutorial_instruction"] = "Next step: {0}";
        _english["bf_tutorial_advance"] = "Continue.";
        _english["bf_no_tutorial"] = "No tutorial active.";
        _english["bf_tap_to_continue"] = "Press Space to continue.";
        _english["bf_end_turn_blocked"] = "Cannot end turn yet. Press Space to continue the tutorial.";
        _english["bf_tutorial_hints"] = "Tutorial hint: {0}";
        _english["bf_leaving_game"] = "Leaving game.";

        // Battlefield - Game Result
        _english["bf_game_entered_vs"] = "Game started against {0}.";
        _english["bf_game_over"] = "Game over. {0}";
        _english["bf_game_over_instructions"] = "Press E to collect rewards and exit.";
        _english["bf_result_win"] = "You won!";
        _english["bf_result_lose"] = "You lost.";
        _english["bf_result_draw"] = "Draw.";
    }
}
