using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Builds the drive clock in the top-left of the HUD.
/// </summary>
public static class DriveTimerSetup
{
    const string CanvasName = "HUDCanvas";
    const string PanelName = "DriveTimer";

    const float PanelWidth = 224f;
    const float PanelHeight = 96f;
    const float CaptionHeight = 20f;
    const float ClockHeight = 54f;

    [MenuItem("NCAT/Set Up Drive Timer", false, 52)]
    public static void SetUp()
    {
        GameObject canvas = GetOrCreateCanvas();

        // Rebuilt rather than patched, so running this twice cannot leave two clocks
        // stacked on top of each other.
        Transform existing = canvas.transform.Find(PanelName);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        GameObject panel = new GameObject(PanelName, typeof(RectTransform));
        panel.transform.SetParent(canvas.transform, false);
        Undo.RegisterCreatedObjectUndo(panel, "Create Drive Timer");

        // Top-left. The other three corners are taken (minimap bottom-left,
        // speedometer bottom-right, route guidance top-centre), and the guidance
        // panel is 560 wide about the centre, so it reaches no further left than
        // x 680 on a 1920 canvas. A 224 wide clock at x 28 is clear of it.
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(28f, -28f);
        panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        // A backdrop, because white text on photogrammetry is unreadable about half
        // the time: the scan has bright pavement and dark shadow in equal measure.
        GameObject backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
        backdrop.transform.SetParent(panel.transform, false);

        RectTransform backdropRect = backdrop.GetComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = backdropRect.offsetMax = Vector2.zero;

        Image backdropImage = backdrop.GetComponent<Image>();
        backdropImage.color = new Color(0.05f, 0.06f, 0.08f, 0.62f);
        backdropImage.raycastTarget = false;

        // Laid out from the panel height rather than typed in, because typing them in
        // is how the clock ended up hanging out of the bottom: its box started 56 px
        // down and was 56 tall, which is 112 inside a 96 px panel. The text drew fine
        // — overflow is on — so nothing looked broken, it just sat low.
        //
        // Caption and clock are stacked, and whatever is left over is split evenly
        // above and below, which is what centred means.
        const float margin = (PanelHeight - CaptionHeight - ClockHeight) * 0.5f;

        Text caption = AddText(panel, "Caption", 15, FontStyle.Bold,
                               new Vector2(0f, -margin),
                               new Vector2(PanelWidth - 24f, CaptionHeight));
        caption.text = "DRIVE TIME";
        caption.color = new Color(0.58f, 0.63f, 0.70f);

        Text clock = AddText(panel, "Clock", 46, FontStyle.Bold,
                             new Vector2(0f, -(margin + CaptionHeight)),
                             new Vector2(PanelWidth - 14f, ClockHeight));
        clock.text = "0:00";

        DriveTimer timer = panel.AddComponent<DriveTimer>();
        timer.display = clock;
        timer.vehicle = Object.FindObjectOfType<VehicleController>();
        timer.scorer = Object.FindObjectOfType<DriveScorer>();

        // Written explicitly rather than left to the field defaults, because a
        // component already in a scene keeps whatever was serialised for it and
        // changing a default in the script would then do nothing.
        timer.startSpeedMph = 1.5f;
        timer.confirmSeconds = 0.25f;
        timer.countWhileStopped = true;
        timer.showTenths = false;

        if (timer.vehicle == null)
            Debug.LogWarning("[DriveTimer] No VehicleController in the scene yet. The " +
                             "clock will find one at runtime, but check PlayerCar exists.");

        EditorUtility.SetDirty(timer);
        EditorSceneManager.MarkSceneDirty(canvas.scene);
        Selection.activeGameObject = panel;

        Debug.Log(
            "Drive timer set up, top-left.\n" +
            "  Holds at 0:00 in grey until the bus moves forward, then counts up white\n" +
            "  Turns green and freezes when the drive ends\n" +
            "  Keeps counting while stopped at lights: turn off Count While Stopped\n" +
            "  on the DriveTimer component to time only movement\n" +
            "  Tick Show Tenths for 0:00.0, for comparing two runs of a route\n" +
            "Save the scene (Ctrl+S).");
    }

    // ------------------------------------------------------------------ helpers

    static Text AddText(GameObject parent, string name, int size, FontStyle style,
                        Vector2 position, Vector2 dimensions)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.color = new Color(0.92f, 0.94f, 0.96f);

        return text;
    }

    static GameObject GetOrCreateCanvas()
    {
        GameObject canvas = GameObject.Find(CanvasName);
        if (canvas != null) return canvas;

        canvas = new GameObject(CanvasName);
        Undo.RegisterCreatedObjectUndo(canvas, "Create HUD Canvas");

        Canvas component = canvas.AddComponent<Canvas>();
        component.renderMode = RenderMode.ScreenSpaceOverlay;
        component.sortingOrder = 100;

        CanvasScaler scaler = canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvas.AddComponent<GraphicRaycaster>();
        return canvas;
    }
}
