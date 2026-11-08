using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class MainMenuSceneChecks
{
    [MenuItem("WH30K/Run UI Panel Layout Checks")]
    public static void RunBatch()
    {
        var fixture = new GameObject("UiPanelLayoutFixture", typeof(RectTransform), typeof(Canvas));
        var checks = 0;
        var passed = true;
        try
        {
            var commandUi = typeof(WH30K.UI.NewGameMenu).Assembly.GetType("WH30K.UI.CommandUi");
            var create = commandUi?.GetMethod("CreatePortraitCard", BindingFlags.Static | BindingFlags.NonPublic);
            Check(create != null, "shared portrait card factory is available");
            if (create == null) throw new InvalidOperationException("Shared portrait card factory is missing.");

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var card = create.Invoke(null, new object[] { fixture.transform, font });
            var cardType = card.GetType();
            var layout = cardType.GetMethod("SetShipInspectorLayout", BindingFlags.Instance | BindingFlags.NonPublic);
            var topOffset = cardType.GetMethod("SetTopOffset", BindingFlags.Instance | BindingFlags.NonPublic);
            var root = cardType.GetField("root", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(card) as GameObject;
            var rect = root?.GetComponent<RectTransform>();
            var heading = root?.transform.Find("VoxSpeaker")?.GetComponent<Text>();
            Check(layout != null && topOffset != null && rect != null && heading != null,
                "shared portrait card exposes its responsive layout elements");
            if (layout == null || topOffset == null || rect == null || heading == null)
                throw new InvalidOperationException("Shared portrait card layout elements are missing.");

            var width1280 = ExpandCanvasWidth(1280f, 800f);
            layout.Invoke(card, new object[] { true, width1280 });
            Check(Mathf.Abs(rect.sizeDelta.x - 348f) < .1f && Mathf.Abs(rect.anchoredPosition.x + 178f) < .1f,
                "1280-wide inspector layout moves the alert into the clear side lane");
            Check(heading.fontSize == 11 && Mathf.Abs(heading.rectTransform.sizeDelta.x - 228f) < 1f,
                "compact alert heading fits within the 1280-wide side lane");

            var width1920 = ExpandCanvasWidth(1920f, 1080f);
            Check(Mathf.Abs(width1920 - 1422.22f) < 1f,
                "1920x1080 Expand scaling produces the expected logical canvas width");
            layout.Invoke(card, new object[] { true, width1920 });
            Check(Mathf.Abs(rect.sizeDelta.x - 446f) < .1f && Mathf.Abs(rect.anchoredPosition.x + 178f) < .1f,
                "1920-wide inspector layout retains a full-width alert in the side lane");

            var width2560 = ExpandCanvasWidth(2560f, 890f);
            layout.Invoke(card, new object[] { true, width2560 });
            Check(Mathf.Abs(rect.sizeDelta.x - 446f) < .1f && Mathf.Abs(rect.anchoredPosition.x) < .1f,
                "ultrawide inspector layout centers the full-width alert clear of the inspector");

            topOffset.Invoke(card, new object[] { -218f });
            Check(Mathf.Abs(rect.anchoredPosition.y + 218f) < .1f,
                "the alert card supports a lower offset when another HUD panel occupies the top lane");
        }
        catch (Exception exception)
        {
            passed = false;
            Debug.LogException(exception);
        }
        finally
        {
            var card = fixture.transform.Find("VoxPortraitCard");
            if (card != null) UnityEngine.Object.DestroyImmediate(card.gameObject);
            UnityEngine.Object.DestroyImmediate(fixture);
        }

        Debug.Log($"UI panel layout checks finished: {checks} assertions.");
        if (!passed) throw new InvalidOperationException("UI panel layout checks failed.");

        void Check(bool condition, string label)
        {
            checks++;
            if (condition) Debug.Log("UI panel PASS: " + label);
            else
            {
                passed = false;
                Debug.LogError("UI panel FAIL: " + label);
            }
        }
    }

    private static float ExpandCanvasWidth(float screenWidth, float screenHeight)
    {
        var scale = Mathf.Min(screenWidth / 1280f, screenHeight / 800f);
        return screenWidth / scale;
    }
}
