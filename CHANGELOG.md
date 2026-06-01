# Changelog

## [Unreleased]

### Added
- Game-free `SnapAccess.Core.Tests` xUnit project covering the logic that does not need the game (`Loc`, `ModSettings`, `UpdateChecker`, `AnnouncementService`), plus a GitHub Actions CI workflow that builds and runs it.
- `LICENSE` file (MIT), which the README already referenced.

### Fixed
- **Update checker version comparison** compared versions lexically, so `0.10.0` was treated as older than `0.9.0`, and a downgrade counted as an available update. It now compares version components numerically.
- **Settings persistence** read the JSON line by line, so a settings file written on a single line parsed to all-default values and silently discarded the user's choices. Parsing is now independent of layout.
- **Update checker never ran** because its target repository was never configured. It is now wired to the upstream repository on mod load.
- **Turn-start announcement** spoke a stray "less-than" every turn ("Turn 2&lt;, energy 2, go") from turn 2 on. The turn-counter label is TMP rich text like "&lt;size=490&gt;2&lt;/size&gt; / 6", and the code split it on '/', which cut through the closing "&lt;/size&gt;" tag. Markup is now stripped before the number is parsed.
- **Turn announcement repeated within a turn.** The battlefield detects turn changes by watching the hand count, but playing a card also changes the hand count, so the full "Turn N, energy X, go" line was re-spoken after each play (heard as "Turn 3, energy 1, go" then "Turn 3, energy 0, go"). A turn is now announced once, when its number actually changes.

### Changed
- `AnnouncementService` now takes an injectable speech-output seam and clock instead of calling the screen-reader bridge and the system clock directly. Behavior is unchanged; the two identical High/Immediate branches were collapsed into one.

---

## v0.5.0 — 2026-05-29

### Architecture Overhaul (v0.3)
- Refactored from linear IHandler loop to **NavigatorManager** with priority-based activation and preemption (inspired by AccessibleArena MTGA mod)
- Created **AnnouncementService** with priority queue (Low/Normal/High/Immediate/Critical), duplicate suppression, critical cooldown, and browsable history
- Created **IScreenNavigator** interface replacing the old IHandler pattern
- All 9 handlers converted to IScreenNavigator with proper priority assignments:
  - Login (1000), Battlefield (900), PlayDeckTray (700), DeckBuilder (650), Missions (600), FriendlyMatch (550), Shop (500), MainMenu (400), Dialog (200)

### New Handlers
- **ShopHandler** — Dedicated handler for Shop and Battle Pass screens with section-grouped item navigation, tab switching, and price reading
- **LoginHandler** — Handles consent/age gate screens at game startup

### New Systems
- **KeyHoldRepeater** — Hold arrow key for 0.5s, then auto-repeats every 0.1s. Integrated in: BattlefieldHandler, MainMenuHandler, DeckBuilderHandler, DialogHandler, MissionsHandler, PlayDeckTrayHandler
- **GameLogNavigator** — Press O to open browsable announcement history. Up/Down navigate entries, O or Escape to close
- **ModSettings** — 6 configurable settings with JSON persistence at UserData/SnapAccess.json:
  - PositionCounts, VerboseCardInfo, OpponentAnnouncements, AutoTurnAnnounce, TransitionAnnouncements, TutorialMessages
- **ModSettingsNavigator** — Press F4 to open settings. Up/Down browse, Enter/Left/Right toggle, F4 or Escape to close
- **InputFieldHelper** — Character-by-character tracking for text input fields (typed/deleted chars, cursor navigation with Left/Right)
- **UpdateChecker** — GitHub release version checker framework (ready for repo URL configuration)
- **NavigatorManager** — Transition announcements when active screen changes (configurable via settings)

### Battlefield Improvements
- **Turn timer integration** — Direct access to game's TurnTimer component
  - W key: Read time remaining on demand
  - Auto-warns at 15 seconds and 5 seconds remaining
- **Quick-play two-stage confirmation** — Pressing 1/2/3 first previews the location (name, your power, opponent power, slots used), second press plays the card
- **End turn guard** — Warns if you have playable cards when pressing E, requires double-press within 3 seconds to confirm
- **Location power scores** — Each location announcement includes "You X, Opponent Y"
- **Cube stake in turn info** — T key now includes cube value
- **Detailed end-of-game results** — Per-location score breakdown and cubes at stake
- **VerboseCardInfo setting** — When enabled, card ability auto-appends when browsing hand
- **TutorialMessages setting** — When disabled, suppresses tutorial/tooltip announcements
- **Card detail inspection** (Down arrow x3): cost, power, ability text
- **Opponent card announcements** — Detects new opponent cards on board, announces name and location

### Timing Overhaul
- Quick-play keys: 1/2/3 play current card directly to location 1/2/3
- Faster scanning: ScanInterval 0.25s, CheckTurnPhase 0.35s, opponent cooldown 2.0s
- Concise turn announcement: "Turn 3, energy 3, go."
- Deferred drawn cards: press D to hear on demand
- S key: silence all speech immediately

### Navigation
- **Home/End keys** — Jump to first/last item in all list navigators (Battlefield hand/locations, DeckBuilder, MainMenu collection, Missions, PlayDeckTray, Dialog)
- **A-Z letter jump** — Press a letter to jump to matching item in DeckBuilder and MainMenu collection
- **Up arrow in DialogHandler** — Reads previous screen text line (Down reads next)

### Localization
- ALL hardcoded English strings moved to Loc.Get() (~150+ keys total)
- Covers all handlers: MainMenuHandler, FriendlyMatchHandler, MissionsHandler, PlayDeckTrayHandler, DialogHandler, BattlefieldHandler (including popup button labels)
- Full F1 context-aware help for every handler with available key listing

### Polish
- Improved Deactivate() cleanup across all handlers
- Full gamepad (DPad) support alongside keyboard in all handlers
- Navigator transition announcements when screens switch
- Screen entry announcements (DeckBuilder announces deck name/count)
- Popup button labels localized (Upgrade, Claim, OK, Confirm, Cancel, Close, Back, Retreat, Stay, Resume)

### Key Bindings (Complete)

**Keyboard:**
- F1: Context help / F3: Repeat last / F4: Mod settings / F12: Debug toggle
- Left/Right: Navigate (with hold-to-repeat)
- Home/End: Jump to first/last item
- Up/Down: Detail levels or area switching
- Enter: Select/Confirm / Backspace: Back/Close / Escape: Cancel
- A-Z: Letter jump (collection/deck builder)
- Tab: Switch area (DeckBuilder, collection tabs)
- O: Game log toggle
- **Battle-specific:** C: Hand, B: Locations, E: End Turn, T: Turn Info + Cubes, W: Timer, A: Energy, D: Drawn cards, S: Silence, G: Snap, R: Retreat, 1/2/3: Quick-play to location, Space: Advance tutorial

**Gamepad:**
- DPad: Navigate / South (A): Confirm / East (B): Back
- West (X): Tutorial/Edit / North (Y): Game info
- Start: End turn / L1/R1: Tab switch / Shoulders: Energy/Info

---

## v0.2.0 — 2026-05-27

- Modular IHandler architecture
- Specialized PlayDeckTrayHandler for deck switching
- Specialized MissionsHandler for daily/season missions
- Refined DialogHandler scoping
- Fixed rank and season info on Play screen
- Improved collection scanning with slot detection and deduplication

## v0.1.0 — 2026-05-26

- Initial release
- Basic battlefield navigation (hand, locations, card playing)
- Main menu navigation
- Dialog handler
- Screen reader integration (Tolk/NVDA/JAWS)
- SDL3 input for keyboard and gamepad
