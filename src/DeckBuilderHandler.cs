using System;
using System.Collections.Generic;
using Il2CppCubeUnity.App.View;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSecondDinner.CubeRendering.Card;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Handles the Deck Editor screen. 
/// Provides hierarchical navigation: 
/// Level 0: Categories (Deck Info, Cards in Deck, Collection)
/// Level 1: Details within categories.
/// </summary>
public class DeckBuilderHandler : IHandler
{
    private enum MenuCategory
    {
        DeckInfo,
        DeckCards,
        Collection
    }

    private MenuCategory _currentCategory = MenuCategory.DeckInfo;
    private int _menuLevel = 0; // 0 = Categories, 1 = Inside Category

    // State for Deck Cards
    private readonly List<CollectionCard> _deckCards = new List<CollectionCard>();
    private int _deckCardIndex = 0;

    // State for Collection
    private readonly List<CollectionCard> _collectionCards = new List<CollectionCard>();
    private int _collectionIndex = 0;
    
    private string _deckName = "";
    private Button _saveButton;
    private bool _isActive = false;
    private float _lastScanTime = 0f;

    private class CollectionCard
    {
        // Unassigned fields commented out to fix warnings until logic is implemented
        public string Name = "Unknown";
        public Button Button = null;
        public CardRenderer Renderer = null;
    }

    public bool IsActive => _isActive;

    public bool Update()
    {
        if (!ScanForDeckEditor()) 
        {
            _isActive = false;
            return false;
        }

        _isActive = true;
        ProcessInput();
        return true;
    }

    private bool ScanForDeckEditor()
    {
        if (Time.time - _lastScanTime < 1.0f) return _isActive;
        _lastScanTime = Time.time;

        try
        {
            // Check for DeckEditor root or specific elements
            GameObject root = GameObject.Find("DeckEditView") ?? GameObject.Find("DeckEditorView");
            if (root == null || !root.activeInHierarchy) return false;

            _deckName = ReadDeckName(root);
            _saveButton = FindSaveButton(root);
            
            return true;
        }
        catch { return false; }
    }

    private void ProcessInput()
    {
        if (_menuLevel == 0)
        {
            HandleCategoryNavigation();
        }
        else
        {
            HandleDetailNavigation();
        }
    }

    private void HandleCategoryNavigation()
    {
        if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft))
        {
            _currentCategory = (MenuCategory)(((int)_currentCategory - 1 + 3) % 3);
            AnnounceCategory();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
        {
            _currentCategory = (MenuCategory)(((int)_currentCategory + 1) % 3);
            AnnounceCategory();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
        {
            // Read details about current category
            string detail = _currentCategory switch
            {
                MenuCategory.DeckInfo => "Deck name: " + _deckName + ". Enter to save.",
                MenuCategory.DeckCards => "Browse cards in this deck. Enter to remove a card.",
                MenuCategory.Collection => "Browse all cards. Enter to add to deck.",
                _ => ""
            };
            ScreenReader.Say(detail);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
        {
            _menuLevel = 1;
            EnterCategory();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
        {
            // Close editor
            TryCloseEditor();
        }
    }

    private void HandleDetailNavigation()
    {
        if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
        {
            _menuLevel = 0;
            ScreenReader.Say(Loc.Get("deck_builder_back"));
            AnnounceCategory();
            return;
        }

        switch (_currentCategory)
        {
            case MenuCategory.DeckInfo:
                HandleDeckInfoInput();
                break;
            case MenuCategory.DeckCards:
                HandleDeckCardsInput();
                break;
            case MenuCategory.Collection:
                HandleCollectionInput();
                break;
        }
    }

    private void HandleDeckInfoInput()
    {
        // For now, just show name and save button
        if (SDLInput.IsKeyDown(SDLInput.Key.Return))
        {
            if (_saveButton != null)
            {
                ScreenReader.Say(Loc.Get("deck_builder_saving"));
                UIHelper.ClickButton(_saveButton);
            }
        }
    }

    private void HandleDeckCardsInput()
    {
        // Navigation through cards in the deck
        if (SDLInput.IsKeyDown(SDLInput.Key.Left))
        {
            _deckCardIndex = (_deckCardIndex - 1 + _deckCards.Count) % Math.Max(1, _deckCards.Count);
            AnnounceDeckCard();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Right))
        {
            _deckCardIndex = (_deckCardIndex + 1) % Math.Max(1, _deckCards.Count);
            AnnounceDeckCard();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return))
        {
            // Remove card from deck
            RemoveCard(_deckCardIndex);
        }
    }

    private void HandleCollectionInput()
    {
        // Navigation through all cards
        if (SDLInput.IsKeyDown(SDLInput.Key.Left))
        {
            _collectionIndex = (_collectionIndex - 1 + _collectionCards.Count) % Math.Max(1, _collectionCards.Count);
            AnnounceCollectionCard();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Right))
        {
            _collectionIndex = (_collectionIndex + 1) % Math.Max(1, _collectionCards.Count);
            AnnounceCollectionCard();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return))
        {
            // Add card to deck
            AddCard(_collectionIndex);
        }
    }

    private void AnnounceCategory()
    {
        string name = _currentCategory switch
        {
            MenuCategory.DeckInfo => "Deck Info: " + _deckName,
            MenuCategory.DeckCards => "Cards in Deck",
            MenuCategory.Collection => "Add Cards from Collection",
            _ => "Unknown"
        };
        ScreenReader.Say(name);
    }

    private void EnterCategory()
    {
        ScreenReader.Say("Entering " + _currentCategory.ToString());
        if (_currentCategory == MenuCategory.DeckCards) ScanDeckCards();
        if (_currentCategory == MenuCategory.Collection) ScanCollection();
    }

    private void AnnounceDeckCard()
    {
        if (_deckCards.Count == 0) ScreenReader.Say("No cards in deck.");
        else ScreenReader.Say(_deckCards[_deckCardIndex].Name + ", press Enter to remove.");
    }

    private void AnnounceCollectionCard()
    {
        if (_collectionCards.Count == 0) ScreenReader.Say("Collection is empty or loading.");
        else ScreenReader.Say(_collectionCards[_collectionIndex].Name + ", press Enter to add to deck.");
    }

    // --- Helper Scan Logic (Placeholders for actual game object paths) ---

    private void ScanDeckCards()
    {
        _deckCards.Clear();
        // TODO: Find CardRenderers inside the "DeckContent" or "SlotContainer"
    }

    private void ScanCollection()
    {
        _collectionCards.Clear();
        // TODO: Find CardRenderers inside the "CollectionContent"
    }

    private string ReadDeckName(GameObject root)
    {
        TMP_Text text = root.GetComponentInChildren<TMP_Text>();
        return text != null ? UIHelper.StripRichText(text.text) : "My Deck";
    }

    private Button FindSaveButton(GameObject root)
    {
        return root.GetComponentInChildren<Button>(); // Simplified
    }

    private void AddCard(int index) { /* TODO */ }
    private void RemoveCard(int index) { /* TODO */ }
    private void TryCloseEditor() { /* TODO */ }

    public void AnnounceContext()
    {
        ScreenReader.Say("Deck Builder: " + _deckName);
        AnnounceCategory();
    }

    public void Reset()
    {
        _isActive = false;
        _menuLevel = 0;
        _deckCards.Clear();
        _collectionCards.Clear();
    }
}
