using System;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Dumps full UI state to the debug log on F2.
/// Shows all active canvases, their buttons, all visible text, and hierarchy paths.
/// This lets the user send a log file for debugging what the mod "sees".
/// </summary>
public static class UIDumper
{
    public static void DumpFullState()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== UI DUMP START ===");

        try
        {
            DumpCanvases(sb);
            sb.AppendLine();
            DumpAllButtons(sb);
            sb.AppendLine();
            DumpAllText(sb);
            sb.AppendLine();
            DumpCardViews(sb);
        }
        catch (Exception ex)
        {
            sb.AppendLine("[ERROR] Dump failed: " + ex.Message);
        }

        sb.AppendLine("=== UI DUMP END ===");

        // Write each line to the log
        foreach (string line in sb.ToString().Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (!string.IsNullOrEmpty(trimmed))
                DebugLogger.Log(LogCategory.Game, "UIDump", trimmed);
        }
    }

    private static void DumpCanvases(StringBuilder sb)
    {
        sb.AppendLine("--- ACTIVE CANVASES ---");
        try
        {
            Il2CppArrayBase<Canvas> canvases = Object.FindObjectsOfType<Canvas>();
            if (canvases == null) { sb.AppendLine("  (none)"); return; }

            for (int i = 0; i < canvases.Count; i++)
            {
                Canvas c = canvases[i];
                if ((Object)(object)c == (Object)null) continue;
                if (!((Component)c).gameObject.activeInHierarchy) continue;

                string name = ((Object)((Component)c).gameObject).name;
                int order = c.sortingOrder;

                // Count children
                Il2CppArrayBase<Button> btns = ((Component)c).GetComponentsInChildren<Button>(false);
                Il2CppArrayBase<TMP_Text> txts = ((Component)c).GetComponentsInChildren<TMP_Text>(false);
                int btnCount = btns != null ? btns.Count : 0;
                int txtCount = txts != null ? txts.Count : 0;

                string path = UIHelper.GetGameObjectPath(((Component)c).gameObject);
                sb.AppendLine($"  Canvas: {name} | order={order} | btns={btnCount} | texts={txtCount} | path={path}");
            }
        }
        catch (Exception ex) { sb.AppendLine("  [ERROR] " + ex.Message); }
    }

    private static void DumpAllButtons(StringBuilder sb)
    {
        sb.AppendLine("--- ALL ACTIVE BUTTONS ---");
        try
        {
            Il2CppArrayBase<Button> buttons = Object.FindObjectsOfType<Button>();
            if (buttons == null) { sb.AppendLine("  (none)"); return; }

            int count = 0;
            for (int i = 0; i < buttons.Count; i++)
            {
                Button btn = buttons[i];
                if ((Object)(object)btn == (Object)null) continue;
                if (!((Component)btn).gameObject.activeInHierarchy) continue;
                if (!((Selectable)btn).interactable) continue;

                string goName = ((Object)((Component)btn).gameObject).name;
                string label = UIHelper.GetButtonLabel(btn);
                string path = UIHelper.GetGameObjectPath(((Component)btn).gameObject);
                sb.AppendLine($"  [{count}] name={goName} | label={label} | path={path}");
                count++;
            }
            sb.AppendLine($"  Total: {count} buttons");
        }
        catch (Exception ex) { sb.AppendLine("  [ERROR] " + ex.Message); }
    }

    private static void DumpAllText(StringBuilder sb)
    {
        sb.AppendLine("--- ALL VISIBLE TEXT ---");
        try
        {
            Il2CppArrayBase<TMP_Text> texts = Object.FindObjectsOfType<TMP_Text>();
            if (texts == null) { sb.AppendLine("  (none)"); return; }

            int count = 0;
            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text tmp = texts[i];
                if ((Object)(object)tmp == (Object)null) continue;
                if (!((Component)tmp).gameObject.activeInHierarchy) continue;

                string raw = tmp.text;
                if (string.IsNullOrWhiteSpace(raw)) continue;

                string clean = UIHelper.StripRichText(raw.Trim());
                if (clean.Length < 1) continue;

                string goName = ((Object)((Component)tmp).gameObject).name;
                string parentPath = "";
                try
                {
                    Transform p = ((Component)tmp).transform.parent;
                    if (p != null)
                    {
                        Transform pp = p.parent;
                        parentPath = (pp != null ? ((Object)pp.gameObject).name + "/" : "") + ((Object)p.gameObject).name;
                    }
                }
                catch { }

                // Truncate long text
                string display = clean.Length > 80 ? clean.Substring(0, 80) + "..." : clean;
                sb.AppendLine($"  [{count}] \"{display}\" | go={goName} | parent={parentPath}");
                count++;
            }
            sb.AppendLine($"  Total: {count} text elements");
        }
        catch (Exception ex) { sb.AppendLine("  [ERROR] " + ex.Message); }
    }

    private static void DumpCardViews(StringBuilder sb)
    {
        sb.AppendLine("--- ACTIVE CARDVIEWS ---");
        try
        {
            Il2CppArrayBase<Il2CppCubeUnity.App.View.CardView> cards =
                Object.FindObjectsOfType<Il2CppCubeUnity.App.View.CardView>();
            if (cards == null) { sb.AppendLine("  (none)"); return; }

            int count = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                var cv = cards[i];
                if ((Object)(object)cv == (Object)null) continue;
                if (!((Component)cv).gameObject.activeInHierarchy) continue;

                string goName = ((Object)((Component)cv).gameObject).name;
                string cardName = "";
                string defId = "";

                try
                {
                    cardName = ((Il2CppSecondDinner.CubeRendering.Card.CardRenderer)cv).CardName ?? "";
                }
                catch { }

                try
                {
                    var id = cv.CardDefId;
                    if (id != null) defId = id.ToString();
                }
                catch { }

                // Try TMP_Text children for name
                string tmpName = "";
                try
                {
                    Il2CppArrayBase<TMP_Text> texts = ((Component)cv).GetComponentsInChildren<TMP_Text>(false);
                    if (texts != null)
                    {
                        for (int j = 0; j < texts.Count; j++)
                        {
                            TMP_Text tmp = texts[j];
                            if ((Object)(object)tmp == (Object)null) continue;
                            if (!((Component)tmp).gameObject.activeInHierarchy) continue;
                            string t = UIHelper.StripRichText(tmp.text ?? "");
                            if (t.Length >= 3 && !int.TryParse(t, out _))
                            {
                                tmpName = t;
                                break;
                            }
                        }
                    }
                }
                catch { }

                string path = UIHelper.GetGameObjectPath(((Component)cv).gameObject);
                sb.AppendLine($"  [{count}] go={goName} | CardName={cardName} | DefId={defId} | tmpText={tmpName} | path={path}");
                count++;
            }
            sb.AppendLine($"  Total: {count} CardViews");
        }
        catch (Exception ex) { sb.AppendLine("  [ERROR] " + ex.Message); }
    }
}
