using System;
using System.Collections.Generic;
using Il2CppCubeUnity.App.NewsFeed;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Handles the News/Inbox screen with navigable article list.
/// Activates when NewsFeedView is present.
/// </summary>
public class NewsHandler : IScreenNavigator
{
    private bool _active = false;
    private readonly List<NewsItem> _items = new List<NewsItem>();
    private int _itemIndex = 0;
    private float _lastScanTime = 0f;
    private bool _announced = false;
    private readonly KeyHoldRepeater _holdRepeater = new KeyHoldRepeater();

    public string NavigatorId => "News";
    public int Priority => 450; // Between MainMenu (400) and Shop (500)
    public bool IsActive => _active;

    private class NewsItem
    {
        public string Title;
        public string Author;
        public Component Source;
    }

    public void Update()
    {
        if (Time.time - _lastScanTime > 1f)
        {
            _lastScanTime = Time.time;
            var feedView = Object.FindObjectOfType<NewsFeedView>();
            if ((Object)(object)feedView == (Object)null || !((Component)feedView).gameObject.activeInHierarchy)
            {
                if (_active)
                {
                    _active = false;
                    _items.Clear();
                    _announced = false;
                }
                return;
            }

            if (!_active)
            {
                _active = true;
                ScanNewsItems();
                if (!_announced)
                {
                    _announced = true;
                    string msg = Loc.Get("news_entered", _items.Count.ToString());
                    AnnouncementService.Instance.Announce(msg, AnnouncementPriority.High);
                    if (_items.Count > 0)
                    {
                        _itemIndex = 0;
                        AnnounceCurrentItem();
                    }
                }
            }
        }

        if (!_active) return;
        ProcessInput();
    }

    private void ProcessInput()
    {
        if (_holdRepeater.Check(SDLInput.Key.Up, () => { NavigateItem(-1); })) { }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Up) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp))
        {
            NavigateItem(-1);
        }
        else if (_holdRepeater.Check(SDLInput.Key.Down, () => { NavigateItem(1); })) { }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown))
        {
            NavigateItem(1);
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Home))
        {
            if (_items.Count > 0) { _itemIndex = 0; AnnounceCurrentItem(); }
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.End))
        {
            if (_items.Count > 0) { _itemIndex = _items.Count - 1; AnnounceCurrentItem(); }
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight))
        {
            AnnounceCurrentItemDetails();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South))
        {
            // Click on the current news item
            ActivateCurrentItem();
        }
        else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East))
        {
            // Let MainMenuHandler handle the back navigation
            _active = false;
            _announced = false;
        }
    }

    private void NavigateItem(int direction)
    {
        if (_items.Count == 0) return;
        _itemIndex = (_itemIndex + direction + _items.Count) % _items.Count;
        AnnounceCurrentItem();
    }

    private void AnnounceCurrentItem()
    {
        if (_itemIndex < 0 || _itemIndex >= _items.Count) return;
        var item = _items[_itemIndex];
        string msg = Loc.Get("news_item", item.Title, (_itemIndex + 1).ToString(), _items.Count.ToString());
        AnnouncementService.Instance.Announce(msg, AnnouncementPriority.Normal);
    }

    private void AnnounceCurrentItemDetails()
    {
        if (_itemIndex < 0 || _itemIndex >= _items.Count) return;
        var item = _items[_itemIndex];
        string detail = item.Title;
        if (!string.IsNullOrEmpty(item.Author))
            detail += ". " + Loc.Get("news_author", item.Author);
        AnnouncementService.Instance.Announce(detail, AnnouncementPriority.Normal);
    }

    private void ActivateCurrentItem()
    {
        if (_itemIndex < 0 || _itemIndex >= _items.Count) return;
        var item = _items[_itemIndex];
        // Try to find and click a button on the news item
        if ((Object)(object)item.Source != (Object)null)
        {
            try
            {
                var btn = item.Source.GetComponentInChildren<UnityEngine.UI.Button>();
                if ((Object)(object)btn != (Object)null)
                {
                    btn.onClick.Invoke();
                    AnnouncementService.Instance.Announce(Loc.Get("news_opening", item.Title), AnnouncementPriority.Normal);
                    return;
                }
            }
            catch { }
            AnnouncementService.Instance.Announce(item.Title, AnnouncementPriority.Normal);
        }
    }

    private void ScanNewsItems()
    {
        _items.Clear();
        try
        {
            // Scan InboxMessageContentHandler instances (inbox messages)
            var inboxHandlers = Object.FindObjectsOfType<InboxMessageContentHandler>();
            if (inboxHandlers != null)
            {
                for (int i = 0; i < inboxHandlers.Count; i++)
                {
                    var handler = inboxHandlers[i];
                    if ((Object)(object)handler == (Object)null || !((Component)handler).gameObject.activeInHierarchy) continue;
                    string topic = "";
                    string author = "";
                    if ((Object)(object)handler._TopicText != (Object)null)
                        topic = UIHelper.StripRichText(handler._TopicText.text?.Trim() ?? "");
                    if ((Object)(object)handler._AuthorText != (Object)null)
                        author = UIHelper.StripRichText(handler._AuthorText.text?.Trim() ?? "");
                    if (!string.IsNullOrEmpty(topic))
                        _items.Add(new NewsItem { Title = topic, Author = author, Source = handler });
                }
            }

            // If no inbox handlers found, fall back to scanning all TMP_Text under NewsFeedView
            if (_items.Count == 0)
            {
                var feedView = Object.FindObjectOfType<NewsFeedView>();
                if ((Object)(object)feedView != (Object)null)
                {
                    Il2CppArrayBase<TMP_Text> texts = ((Component)feedView).GetComponentsInChildren<TMP_Text>(true);
                    if (texts != null)
                    {
                        for (int i = 0; i < texts.Count; i++)
                        {
                            var tmp = texts[i];
                            if ((Object)(object)tmp == (Object)null || !tmp.gameObject.activeInHierarchy) continue;
                            string text = UIHelper.StripRichText(tmp.text?.Trim() ?? "");
                            if (text.Length > 5 && !text.Contains("News") && !text.Contains("Tab"))
                            {
                                _items.Add(new NewsItem { Title = text, Source = tmp, Author = "" });
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "NewsHandler", $"ScanNewsItems error: {ex.Message}");
        }
    }

    public void Deactivate()
    {
        _active = false;
        _items.Clear();
        _announced = false;
        _holdRepeater.Reset();
    }

    public void OnSceneChanged(string sceneName)
    {
        Deactivate();
    }

    public void AnnounceContext()
    {
        AnnouncementService.Instance.Announce(Loc.Get("news_help"), AnnouncementPriority.High);
    }
}
