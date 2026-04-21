# SnapAccess: Marvel Snap Accessibility Mod

SnapAccess is an accessibility mod for Marvel Snap designed for blind and low-vision players. It provides screen reader support, keyboard/gamepad navigation, and automated announcements for game states.

## Credits

This mod is based on the foundational work and initial builds by **Amethyst** ([GitHub](https://github.com/RealAmethyst/)), including the original SDL implementation for gamepad and keyboard support. We are grateful for his contributions to making Marvel Snap accessible.

## Table of Contents

1. [What's Working](#whats-working)
2. [In Progress](#in-progress)
3. [Known Issues](#known-issues)
4. [Command Reference](#command-reference)
5. [Installation Guide](#installation-guide)
6. [Contribution Guide](#contribution-guide)

---

## What's Working

- **Screen Reader Support:** Integration with NVDA and JAWS via Tolk.
- **Battlefield Navigation:** Full keyboard and gamepad support for browsing your hand and the three locations.
- **Card Interaction:** View card names, costs, and power. Play cards to specific locations.
- **Main Menu Navigation:** Browse the bottom menu bar and the Play screen (including deck browsing).
- **Dialog & Popup Support:** Scoped scanning of dialog boxes to read text and navigate buttons.
- **Tutorial Support:** Automated reading of tutorial speech bubbles and tooltips. Use Space/X to advance.
- **Game Status Announcements:** 
  - Opponent name at start.
  - Turn start/end.
  - Game over results (Win/Loss/Draw).
- **Localization:** Support for multiple languages (currently English and German).
- **Gamepad Support:** Custom SDL3-based input wrapper for consistent controller experience.

## In Progress

- **Deck Selection:** Improvements to the "Switch Deck" flow on the Play screen. (Works, but not fully)
- **Collection Browsing:** A dedicated handler for managing and viewing your card collection. (Also works, but not fully)
- **Shop & News:** Enhanced accessibility for scrolling and interacting with the Shop and News tabs.

## Known Issues

- **Deck Names:** Occasionally, the deck name detection on the Play screen might pick up promotional text if a special event is active.
- **Card Selection Cancel:** In some rare cases, canceling a card selection might require pressing Escape twice.
- **Tooltip Timing:** Fast-advancing tutorials might skip the reading of some tooltips.

---

## Command Reference

### Keyboard Controls (Battlefield / In-Game)

- **C**: Focus Hand (browse cards with Left/Right)
- **B**: Focus Locations (browse with Left/Right, or play if a card is selected)
- **Arrow Left/Right**: Navigate within the focused area (Hand or Locations)
- **Arrow Down**: Inspect current item (full card details or location status)
- **Arrow Up**: Stop inspecting / Go back to basic info
- **Enter**: Select Card / Play selected card to focused location / Confirm Button
- **Escape**: Cancel card selection / Open Pause Menu
- **A**: Announce current Energy
- **T**: Announce Turn info (Current Turn / Total)
- **G**: Try to Snap (Double the cube stakes)
- **R**: Try to Retreat (Leave match early)
- **I**: Announce Tutorial Instruction (Reads the current speech bubble or tooltip)
- **Space**: Advance Tutorial (simulates a screen click)
- **E**: End Turn / Collect Rewards (Game Over screen)
- **F1**: Help / Context Announcement (Tells you where you are and what is focused)
- **F12**: Toggle Debug Mode (Voice: "Debug On" / "Debug Off")

### Keyboard Controls (Main Menu)

- **Arrow Left/Right**: Navigate the bottom menu bar (Play, Collection, Shop, etc.)
- **Enter**: Activate selected menu or button
- **Backspace / Escape**: Go back or close current sub-menu
- **S**: Open Deck Selector (on Play screen)
- **D**: Scan Deck Cards (on Play screen)
- **M**: Open Missions Menu
- **I**: (In Collection) Announce card details
- **R**: Refresh current view, or rewards menu

### Gamepad Controls (Xbox/PlayStation)

- **DPad Left/Right**: Navigate current area or menu
- **DPad Down**: Inspect item (details)
- **DPad Up**: Stop inspecting
- **A / Cross**: Confirm / Select / Play
- **B / Circle**: Cancel / Back
- **X / Square)**: Advance Tutorial
- **Y / Triangle**: Game Info / Tutorial Info
- **Start**: End Turn
- **L1 / R1**: Navigate buttons in dialogs
- **L-Shoulder**: Announce Energy (In-game)

---

## Installation Guide (For Players)

### Prerequisites
- **Marvel Snap** installed via Steam.
- A Windows-based screen reader (NVDA or JAWS).

### Step 1: Install MelonLoader
1. Download the **MelonLoader.Installer.exe** from the [official MelonLoader website](https://melonwiki.xyz/#/?id=automated-installation) or [GitHub Releases](https://github.com/LavaGang/MelonLoader/releases/latest).
2. Run the installer.
3. Click "Select" and navigate to your `MARVEL SNAP.exe` (usually in `C:\Program Files (x86)\Steam\steamapps\common\MARVEL SNAP\`).
4. Ensure the version is set to **v0.7.2** (or latest) and click **Install**.

### Step 2: Install SnapAccess
1. Download the latest release from the [Releases](https://github.com/Destranis/snapaccess/releases) page.
2. Place `SnapAccess.dll` into the `Mods` folder inside your game directory.
3. Place `SDL3.dll`, `Tolk.dll`, and `nvdaControllerClient64.dll` into the **root** game folder (next to `MARVEL SNAP.exe`).

### Step 3: Run the Game
1. Launch Marvel Snap via Steam. 
2. A console window will appear; wait for it to finish initializing.
3. Once the main menu loads, you will hear "Mod Loaded".

---

## Contribution Guide (For Developers)

### Environment Setup
1. Clone this repository: `git clone https://github.com/Destranis/snapaccess.git`
2. Open `src/SnapAccess.csproj` in Visual Studio.
3. The source code and build scripts are located in the `src/` directory.

### Repository Structure
- **Root**: Essential DLLs for players and `README.md`.
- `src/`: Mod source code (.cs files), project file (.csproj), and `scripts/`.

---

License: MIT
