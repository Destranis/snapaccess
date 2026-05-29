# Marvel Snap - Game API Documentation

## Overview

- **Game:** Marvel Snap
- **Engine:** Unity (Il2Cpp)
- **Runtime:** net6.0
- **Architecture:** x64
- **Developer:** Second Dinner

---

## 1. Singleton Access Points

### Main Controllers

- `GameInputManager` - Manages gameplay input and dragging.
  - Access: `UIHelper.FindComponent<GameInputManager>()`
- `HandZoneController` - Manages the player's hand.
  - Access: `UIHelper.FindComponent<HandZoneController>()`
- `GameView` - Main game view.
  - Access: `GameView.Get()`
- `Navigator` - Manages navigation between scenes/views.
  - Access: `Il2CppApp.Navigator.FindNavigator_navigator()`

### UI Management

- `TextMeshPro` / `TMP_Text` - Primary text components used for UI.

---

## 2. Game Key Bindings (DO NOT Override!)

### Navigation

- **Arrow Keys / WASD**: Used for general UI navigation and selection.
- **Enter / Space**: Used for confirming actions and advancing tutorials.
- **Escape**: Used for backing out of menus or opening settings.

---

## 3. Safe Mod Keys

### Reserved for Accessibility Mod

- **Tab**: Used to switch between area focus (e.g., Hand vs Locations).
- **Enter**: Activate/Confirm selection in the mod.
- **Escape**: Cancel card selection.

### Available

- **F1**: Show help.
- **F12**: Toggle debug mode.
- **Numpad 0-9**: Used for various shortcuts in the existing mod.
- **I**: Announce game info (Hand count, Location count).
- **E**: Try End Turn.
- **W**: Timer (time remaining).
- **F4**: Mod settings.

---

## 4. UI System

### UI Base Classes

- `View` - Base class for most game views.
- `CardView` - Component for rendering and interacting with cards.
- `LocationView` - Component for rendering and interacting with locations.
- `SpeechBubbleView` - Component for tutorial speech bubbles.
- `TutorialTooltip` - Component for tutorial tooltips.

### Text Components

- `TextMeshPro` / `TMP_Text`: Used for all labels and descriptions.
- `LocalizeStringEvent`: Used for localized strings.

---

## 5. Game Mechanics - Feature Catalog

### Card Interaction

- `CardRenderer.CardName`: Property for getting the card's name.
- `CardRenderer._CostValueView.Value`: Property for card cost.
- `CardRenderer._PowerValueView.Value`: Property for card power.

### Playing Cards

- **Primary:** `GameViewControllerProvider` (MonoBehaviour, FindObjectOfType) -> `.GameViewController` -> `.StageCard(CardView, Il2CppSystem.Object target)`
- **Fallback:** Mouse simulation drag from card world position to location world position
- **Namespace:** `GameViewControllerProvider` and `GameViewController` are in `Il2CppCubeUnity.App.Game`
- **NOTE:** `GameInputManager` does NOT have `StartDragging`/`DropCard` methods. `GameView` does NOT have a static `Get()` method.
- `PlayerController.CanStageCard(int cardEntityId, int locationEntityId)` can check validity before staging.

### End Turn

- `EndTurnButtonView` (MonoBehaviour) in namespace `Il2CppApp.Game.UI.Button`
- No direct click method available; use mouse simulation on the button's world position
- The button is NOT a Unity `Button` - it's a custom MonoBehaviour

### Location Interaction

- Locations are represented by `LocationView`.
- Name is typically found in a child named "Location Name Text".

### Tutorial System

- `DefaultTutorialState` is a nested class: `DefaultScenarioTutorialBehavior.DefaultTutorialState` (in `Il2CppCubeUnity.App.Game`)
- `DefaultTutorialState.StepMap`: Dictionary keyed by `VfxScenarioTutorialStep.Type` enum (20=ShowText, 18=ShowTooltip)
- `SpeechBubbleView` (in `Il2CppCubeUnity.App.Game.SpeechBubble`) has `_TextMeshPro` and `_SpeechBubbleMeshRenderer` fields
- `TutorialTooltip` and `TutorialSpeechBubble`: Components used for teaching mechanics (in `Il2CppCubeUnity.App.Tutorials`).

---

## 6. Status and Notifications

### Battlefield Status

- Hand count and Location count are important for gameplay flow.
- "End Turn" state is managed by `EndTurnButton`.

---

## 7. Event Hooks for Harmony Patches

### Tutorial Hooks

- `TutorialTooltip.Initialize`: Postfix to read tooltip text.
- `TutorialSpeechBubble.Initialize`: Postfix to read speech bubble text.
- `LocalizeStringEvent.RefreshString` / `ForceUpdate`: Postfix to catch localized text changes.

### StepMap Hooks

- `DefaultTutorialState.OnShowTextStep` and `OnShowTooltipStep` (via `StepMap` modification).

---

## 8. Localization

- The mod uses a custom `Loc` class with a dictionary-based translation system.
- Keys like `bf_card_played`, `bf_not_in_game`, etc., are used.

---

## 9. Code Examples

### Playing a Card

```csharp
bool flag = _gim.StartDragging(_selectedCard);
if (flag) {
    flag = _gim.DropCard(_selectedCard, (Object)(object)val);
}
```

### Finding Components

```csharp
public static T FindComponent<T>() where T : Component
{
    return Object.FindObjectOfType<T>();
}
```
