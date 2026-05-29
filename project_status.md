# Project Status: SnapAccess

## Project Info

- **Game:** Marvel Snap
- **Engine:** Unity (Il2Cpp)
- **Architecture:** x64
- **Mod Loader:** MelonLoader (Latest)
- **Runtime:** net6.0 (Il2Cpp)
- **Game directory:** C:\Program Files (x86)\Steam\steamapps\common\MARVEL SNAP
- **User experience level:** A Lot (Senior peer programmer)
- **User game familiarity:** Very well

## Setup Progress

- [x] Experience level determined
- [x] Game name and path confirmed
- [x] Game familiarity assessed
- [x] Game directory auto-check completed (via tool use)
- [x] Mod loader selected and installed (MelonLoader)
- [x] Tolk DLLs in place
- [x] .NET SDK available
- [x] Decompiler tool ready
- [x] Game code decompiled (Existing mod decompiled for reference)
- [ ] Tutorial texts extracted (if applicable)
- [x] Multilingual support decided (Existing mod has Loc system)
- [x] Project directory set up (csproj, Main.cs, etc.)
- [x] CLAUDE.md / GEMINI.md updated with project-specific values
- [x] First build successful
- [x] "Mod loaded" announcement working in game
- [x] Git repository local structure prepared in Documents\snapaccess_gitrepo
- [x] Pushed v0.2 update to GitHub with modular handlers and improved UI scanning
- [x] Refactored to modular IHandler architecture
- [x] Implemented specialized PlayDeckTrayHandler for reliable deck switching
- [x] Implemented specialized MissionsHandler for daily/season missions
- [x] Refined DialogHandler to prevent interference with base screens
- [x] Fixed rank and season info reporting on Play screen
- [x] Improved collection scanning with better slot detection and deduplication
- [x] Resolved "random clicking" issues by adding scan delays

## Current Phase

**Phase:** Feature Completion (v0.5.0) — All major features implemented
**Currently working on:** Testing remaining features
**Completed (2026-05-29):**
  - **Navigator Architecture (v0.3):**
    - Decompiled AccessibleArena.dll (MTGA accessibility mod, 143 files) and analyzed its architecture
    - Created NavigatorManager with priority-based activation/preemption
    - Created AnnouncementService with priority queue, duplicate suppression, critical cooldown
    - Created IScreenNavigator interface, InputGuard
    - Converted all 8 handlers to IScreenNavigator
    - Fixed MissionsHandler/FriendlyMatchHandler preemption bug (restored _activated gate)
  - **Battlefield Timing Overhaul:**
    - Quick-play keys: 1/2/3 play current card directly to location 1/2/3
    - Faster scanning: ScanInterval 0.25s, CheckTurnPhase 0.35s, opponent cooldown 2.0s
    - Concise turn announcement: "Turn 3, energy 3, go."
    - Deferred drawn cards: press D to hear on demand
    - S key: silence all speech immediately
  - **Comprehensive Polish (v0.4):**
    - **KeyHoldRepeater** (new): Hold arrow key → 0.5s delay → repeats every 0.1s. Added to BattlefieldHandler, MainMenuHandler, DeckBuilderHandler, DialogHandler
    - **GameLogNavigator** (new): Press O to open/close browsable announcement history (Up/Down to navigate)
    - **Navigator transition announcements**: NavigatorManager announces screen name on navigator change
    - **Screen entry announcements**: DeckBuilder announces deck name/count on entry
    - **Full localization**: ALL hardcoded English strings moved to Loc.Get() across all handlers (~70+ strings total). Covers: MainMenuHandler (30+ strings), FriendlyMatchHandler (15+ strings), MissionsHandler, PlayDeckTrayHandler, DialogHandler settings strings
    - **Improved F1 help (AnnounceContext)**: Every handler now provides useful context-aware help text with available keys
    - **Improved Deactivate() cleanup**: MainMenuHandler, FriendlyMatchHandler reset all sub-state flags
    - **Gamepad consistency**: All handlers support DPad equivalents for keyboard navigation
  - **ShopHandler + BattlePassHandler (v0.4):**
    - Dedicated ShopHandler (priority 500) with section-grouped items, tab switching, price reading
    - Battle Pass detection and browsing
    - Letter search (A-Z) in DeckBuilder collection
  - **ModSettings system (v0.4):**
    - 6 configurable settings: PositionCounts, VerboseCardInfo, OpponentAnnouncements, AutoTurnAnnounce, TransitionAnnouncements, TutorialMessages
    - F4 to open settings navigator, Up/Down browse, Enter/Left/Right toggle
    - JSON persistence at UserData/SnapAccess.json
  - **Feature Completion (v0.5):**
    - **Location power scores**: AnnounceCurrentLocation() shows "You X, Opponent Y" per location
    - **Snap indicator**: T key (Turn Info) now includes cube stake value
    - **End-of-game detailed results**: ReadGameResult() shows per-location scores + cubes at stake
    - **Card ability text during battle**: Down x3 reads ability (already existed), VerboseCardInfo auto-appends it
    - **VerboseCardInfo wired**: When enabled, card ability auto-announced with card name in hand
    - **TutorialMessages wired**: When disabled, suppresses tutorial/tooltip announcements
    - **Home/End keys**: Jump to first/last in all list navigators (Battlefield, DeckBuilder, MainMenu collection, Missions, PlayDeckTray, Dialog)
    - **Up arrow in DialogHandler**: Reads previous screen text line
    - **KeyHoldRepeater**: Added to MissionsHandler, PlayDeckTrayHandler
    - **MainMenu collection letter jump (A-Z)**: Press letter to jump to matching card/deck in collection
    - **BattlefieldHandler popup labels localized**: All button override labels (Upgrade, Claim, OK, etc.) now through Loc.Get()
    - **Localized detail strings**: Cost/power/ability inspection, location descriptions, cards-at-location all through Loc keys
    - Updated all help texts to mention new keys (Home/End, Up for Dialog, cube info on T)
  - **MTGA-Inspired Features (v0.5):**
    - **TurnTimer integration**: Direct access to game's TurnTimer component for reliable timer tracking
    - **W key: Time remaining**: Press W to hear seconds remaining on turn timer
    - **Auto timer warnings**: Announces at 15 seconds (warning) and 5 seconds (urgent)
    - **End turn guard**: Warns if you have playable cards when pressing E, requires double-press to confirm
    - **InputFieldHelper** (new): Character-by-character tracking for text input fields (typed/deleted chars, cursor navigation)
    - **UpdateChecker** (new): GitHub release checker framework (ready for repo URL configuration)
    - **Opponent card localization**: "Opponent played X at Y" now through Loc.Get()
  - Build succeeds with 0 errors, 0 warnings, deployed to game (v0.5.0)
**Remaining:**
  - Season/rank info richer on play screen
  - News screen dedicated handler

## Codebase Analysis Progress

### GATE: Tier 1 MUST be complete before Phase 2 (Framework)!

- [x] 1.1 Structure overview (namespaces, singletons) → Documented via decompilation
- [x] 1.2 Input system — ALL game key bindings documented in game-api.md "Game Key Bindings"
- [x] 1.2 Input system — Safe mod keys identified and listed in game-api.md "Safe Mod Keys"
- [x] 1.3 UI system (base classes, text access patterns, Reflection needed?)
- [x] 1.4 State management decision → Using AccessStateManager (from existing mod)
- [x] 1.5 Localization: game's language system analyzed

### GATE: Relevant Tier 2 items MUST be done before implementing each feature!

- [ ] 1.6 Game mechanics (analyzed as needed per feature)
- [ ] 1.7 Status/feedback systems
- [x] 1.8 Event system / Harmony patch points (tutorial patches restored)
- [x] 1.9 Results documented in `docs/game-api.md`
- [x] 1.10 Tutorial analysis — DefaultTutorialState is nested in DefaultScenarioTutorialBehavior, StepMap uses VfxScenarioTutorialStep.Type enum

## Game Key Bindings (Original)

- WASD / Arrow keys: UI Navigation / Gameplay
- Enter: Confirm
- Escape: Cancel / Back

## Implemented Features

- **Battlefield Handler** - Full keyboard + gamepad navigation of hand/locations, card playing, tutorial support, quick-play (1/2/3), silence (S), deferred draws (D)
- **Tutorial System** - SpeechBubbleView scanning, StepMap hooks, Space/X to advance
- **Main Menu Handler** - Menu bar navigation, Play screen with deck browsing, collection browsing with Cards/Albums tabs, game modes, rewards, deck actions (edit/delete/copy)
- **Dialog Handler** - Scoped canvas scanning, settings mode (sliders/toggles), text input mode, Down for screen text reading
- **Deck Builder Handler** - Full deck editor with card scanning, add/remove, save/close, entry announcement
- **Play Deck Tray Handler** - Quick deck switching with Left/Right, Enter to equip, E to edit
- **Missions Handler** - Daily/Season missions browsing with category hierarchy
- **Friendly Match Handler** - Create/Join match flow with code input
- **Shop Handler** - Section-grouped shop browsing with tab switching, price reading, Battle Pass support
- **Mod Settings** - F4 settings navigator with 6 toggleable settings, JSON persistence
- **Game Over** - Detects win/lose/draw, announces detailed per-location results + cube stake, E to collect rewards
- **KeyHoldRepeater** - Hold-to-repeat navigation (0.5s delay, 0.1s repeat) in BattlefieldHandler, MainMenuHandler, DeckBuilderHandler, DialogHandler, MissionsHandler, PlayDeckTrayHandler
- **GameLogNavigator** - Browsable announcement history (O to open/close)
- **NavigatorManager** - Priority-based navigator activation with transition announcements
- **AnnouncementService** - Priority queue with duplicate suppression, critical cooldown, history
- **SDL3 Input Wrapper** - Custom input handling for gamepads and keyboard
- **Localization (Loc)** - Full English localization (~150+ keys), ready for i18n
- **Home/End Navigation** - Jump to first/last item in all list-based handlers
- **Letter Jump (A-Z)** - Quick jump by letter in DeckBuilder and MainMenu collection
- **Turn Timer** - Direct TurnTimer component integration with W key readout and auto-warnings at 15s/5s
- **End Turn Guard** - Warns if playable cards remain when pressing E, double-press to confirm
- **Input Field Helper** - Character-by-character announcements in text input fields
- **Update Checker** - GitHub release version checker (framework ready)
- **Screen Reader (Tolk)** - Bridge to NVDA/JAWS

## Pending Tests

- [ ] Test W key reads time remaining during battle
- [ ] Test timer warnings (15s and 5s auto-announcements)
- [ ] Test end turn guard (E warns when playable cards exist, double-E confirms)
- [ ] Test input field character announcements (FriendlyMatch code input or any text field)
- [ ] Test location power scores announced with location (B, Left/Right in battle)
- [ ] Test T key includes cube stake value
- [ ] Test end-of-game announces per-location scores and cube count
- [ ] Test VerboseCardInfo: card ability auto-reads when browsing hand (toggle off with F4)
- [ ] Test TutorialMessages: tutorials suppressed when disabled via F4
- [ ] Test Home/End in battle hand, locations, DeckBuilder, MainMenu collection, Missions, PlayDeckTray, Dialog
- [ ] Test A-Z letter jump in MainMenu collection (Cards tab)
- [ ] Test Up arrow reads previous text in dialogs
- [ ] Test KeyHoldRepeater in Missions (hold Left/Right) and PlayDeckTray

## Known Issues

- Deck name detection on Play screen can pick up promo text (mitigated with IsPromoText filter)
- Shop/News screens depend on DialogHandler quality — need testing
- Upgrade screen flow is clunky (handled by generic DialogHandler, may need dedicated handler)
- Some cards may show no ability text if localization hasn't loaded

## Implementation Roadmap

1. [x] Fix DialogHandler scoping (scan topmost content canvas, not all buttons)
2. [x] Fix DialogHandler labels (read all TMP_Text children, pick best)
3. [x] Add screen text reading (Down/Up arrows in sub-screens)
4. [x] Game over detection + exit mechanism
5. [x] Opponent name at game start
6. [x] DeckBuilderHandler — full deck editor with card scanning, add/remove, save/close
7. [x] BattlefieldHandler popup gamepad support + Down to re-read popup text
8. [x] Full game decompilation (40 assemblies into decompiled_game/)
9. [x] Navigator architecture refactor (IScreenNavigator + NavigatorManager)
10. [x] Battlefield timing overhaul (quick-play, faster scans, concise announcements)
11. [x] Comprehensive polish (KeyHoldRepeater, GameLog, full localization, transition announcements)
12. [x] Shop/BattlePass dedicated handlers
13. [x] Mod settings (6 configurable settings with F4 navigator)
14. [x] Letter search (A-Z) in DeckBuilder and MainMenu collection
15. [x] Location power scores, cube stake in turn info, detailed end-game results
16. [x] VerboseCardInfo and TutorialMessages settings wired into handlers
17. [x] Home/End keys for all list navigators
18. [x] Up arrow in DialogHandler for previous text
19. [x] KeyHoldRepeater in MissionsHandler, PlayDeckTrayHandler
20. [x] Popup button labels localized
21. [ ] In-game testing pass for all new features

## Architecture Decisions

- Staying on MelonLoader (not porting to BepInEx)
- **v0.3 refactored from IHandler linear loop to NavigatorManager priority-based pattern** (inspired by AccessibleArena MTGA mod)
- **v0.4 comprehensive polish**: KeyHoldRepeater, GameLogNavigator, full localization, navigator transitions
- NavigatorManager: priority-sorted navigators, one active at a time, preemption of lower-priority by higher-priority
- AnnouncementService wraps ScreenReader with priority queue, duplicate suppression, critical cooldown
- Handlers self-detect their screens (no manual Activate() calls, except gated MissionsHandler/FriendlyMatchHandler)
- MainMenu yields to Dialog by setting _active=false when in sub-screen mode
- Reusing SDL3 for input to maintain gamepad support
- DefaultTutorialState is nested: DefaultScenarioTutorialBehavior.DefaultTutorialState
- StepMap key type: VfxScenarioTutorialStep.Type enum (values 20=ShowText, 18=ShowTooltip)
- **Reference mod:** AccessibleArena.dll (MTGA) decompiled in decompiled_arena/ — 143 files showing mature TCG accessibility patterns

## Key Bindings (Mod)

### Keyboard
- F1: Help / context announcement
- F3: Repeat last announcement
- F12: Toggle debug mode
- Arrow Left/Right: Navigate (with hold-to-repeat in all handlers)
- Arrow Up: Switch to Locations area (battlefield)
- Arrow Down: Details / Switch to Hand area (battlefield)
- Enter: Select / Confirm / Play card
- Backspace: Go back / Close
- Escape: Cancel card selection
- Space: Advance tutorial (click center screen)
- Tab: Switch area (DeckBuilder: deck/collection)
- Home: Jump to first item in list
- End: Jump to last item in list
- A-Z: Letter jump (collection, deck builder)
- E: End turn (battlefield) / Edit deck (deck tray)
- I: Game info (hand count, location count)
- O: Open/close game log
- S: Silence all speech / Save (DeckBuilder)
- D: Announce drawn cards (deferred)
- W: Timer (time remaining in turn)
- 1/2/3: Quick-play current card to location 1/2/3

### Gamepad
- DPad Left/Right: Navigate
- DPad Up: Switch to Locations area
- DPad Down: Details / Switch to Hand area
- South (A/Cross): Confirm / Select / Play
- East (B/Circle): Cancel / Back
- West (X/Square): Advance tutorial / Edit deck
- North (Y/Triangle): Game info
- Start: End turn
- L1/R1: Tab switch / Navigate dialog buttons

## Notes for Next Session

- All v0.5 features built and deployed, need in-game test pass
- MTGA-inspired features added: TurnTimer integration, end turn guard, InputFieldHelper, UpdateChecker
- Remaining feature ideas: richer season/rank info on play screen, news screen handler
- New keys: W (timer), Home/End (first/last), A-Z (letter jump), Up (prev text in dialogs), F4 (settings)
