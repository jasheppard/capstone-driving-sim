using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Builds the post-drive report screen and attaches the scorer to the vehicle.
/// </summary>
public static class DebriefSetup
{
    const string CarName = "PlayerCar";
    const string CanvasName = "DebriefCanvas";
    const string PanelName = "DebriefPanel";

    const float PanelWidth = 900f;
    const float PanelHeight = 600f;

    [MenuItem("NCAT/Set Up Drive Debrief", false, 38)]
    public static void SetUp()
    {
        // ---- scorer ------------------------------------------------------------
        GameObject car = GameObject.Find(CarName);

        if (car == null)
        {
            Debug.LogError("No " + CarName + " in the scene. Run NCAT → Set Up Player Car first.");
            return;
        }

        DriveScorer scorer = car.GetComponent<DriveScorer>();
        if (scorer == null) scorer = Undo.AddComponent<DriveScorer>(car);
        EditorUtility.SetDirty(scorer);

        // ---- canvas ------------------------------------------------------------
        GameObject old = GameObject.Find(CanvasName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        GameObject canvasGO = new GameObject(CanvasName);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Debrief Canvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;                  // above HUD and loading screen

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasGO.AddComponent<GraphicRaycaster>();

        // The controller lives on the canvas, not the panel, so it keeps running
        // while the panel is hidden and can listen for the end-drive key.
        DebriefScreen screen = canvasGO.AddComponent<DebriefScreen>();

        // ---- dim backdrop ------------------------------------------------------
        GameObject dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(canvasGO.transform, false);
        Stretch(dim.GetComponent<RectTransform>());
        dim.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.04f, 0.82f);

        // ---- panel -------------------------------------------------------------
        GameObject panel = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(dim.transform, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panelRect.anchoredPosition = Vector2.zero;

        panel.GetComponent<Image>().color = new Color(0.09f, 0.10f, 0.13f, 0.98f);

        Text title = AddText(panel, "Title", 30, FontStyle.Bold, TextAnchor.MiddleCenter,
                             new Vector2(0f, PanelHeight * 0.5f - 44f), new Vector2(600f, 40f));
        title.text = "DRIVE REPORT";
        title.color = new Color(0.62f, 0.67f, 0.74f);

        // ---- layout ------------------------------------------------------------
        //
        // Positions are worked out from the panel's own dimensions rather than
        // guessed. The first attempt at this was mocked up at full size before
        // shipping, which is how three collisions were caught: the value column
        // ran off the right edge of the panel, the footnote sat on top of the
        // events block, and the two blocks overlapped each other.
        //
        // Panel is 900 x 600, so the usable area is x -450..450, y -300..300.
        // The score block owns the left third; the two label/value columns own
        // the rest, ending 30px short of the right edge.

        const float LeftX = -300f;          // centre of the score block
        const float LabelX = -120f;         // left edge of the label column
        const float ValueRight = 420f;      // right edge of the value column
        const float SummaryBlock = 130f;    // six rows at 20px
        const float EventBlock = 150f;      // seven rows at 20px

        Text score = AddText(panel, "Score", 92, FontStyle.Bold, TextAnchor.MiddleCenter,
                             new Vector2(LeftX, 105f), new Vector2(260f, 110f));
        score.text = "--";

        Text outOf = AddText(panel, "OutOf", 15, FontStyle.Normal, TextAnchor.MiddleCenter,
                             new Vector2(LeftX, 43f), new Vector2(260f, 24f));
        outOf.text = "SAFETY SCORE / 100";
        outOf.color = new Color(0.48f, 0.53f, 0.60f);

        Text grade = AddText(panel, "Grade", 56, FontStyle.Bold, TextAnchor.MiddleCenter,
                             new Vector2(LeftX, -25f), new Vector2(200f, 70f));
        grade.text = "-";

        Text verdict = AddText(panel, "Verdict", 14, FontStyle.Italic, TextAnchor.UpperCenter,
                               new Vector2(LeftX, -110f), new Vector2(280f, 90f));
        verdict.text = "";
        verdict.color = new Color(0.66f, 0.71f, 0.78f);

        // ---- columns, right -----------------------------------------------------
        // Label rects are anchored by their centre, so x is the left edge plus half
        // the width; the value column mirrors that from its right edge.
        float labelCentre = LabelX + 130f;
        float valueCentre = ValueRight - 140f;

        Text sectionA = AddText(panel, "SectionA", 15, FontStyle.Bold, TextAnchor.MiddleLeft,
                                new Vector2(labelCentre, 190f), new Vector2(260f, 24f));
        sectionA.text = "THE DRIVE";
        sectionA.color = new Color(1f, 0.82f, 0.25f);

        Text summaryLabels = AddText(panel, "SummaryLabels", 15, FontStyle.Normal,
                                     TextAnchor.UpperLeft,
                                     new Vector2(labelCentre, 105f), new Vector2(260f, SummaryBlock));
        summaryLabels.color = new Color(0.58f, 0.63f, 0.70f);
        summaryLabels.lineSpacing = 1.35f;

        Text summaryValues = AddText(panel, "SummaryValues", 15, FontStyle.Bold,
                                     TextAnchor.UpperRight,
                                     new Vector2(valueCentre, 105f), new Vector2(280f, SummaryBlock));
        summaryValues.lineSpacing = 1.35f;

        Text sectionB = AddText(panel, "SectionB", 15, FontStyle.Bold, TextAnchor.MiddleLeft,
                                new Vector2(labelCentre, 20f), new Vector2(260f, 24f));
        sectionB.text = "SAFETY EVENTS";
        sectionB.color = new Color(1f, 0.82f, 0.25f);

        Text eventLabels = AddText(panel, "EventLabels", 15, FontStyle.Normal,
                                   TextAnchor.UpperLeft,
                                   new Vector2(labelCentre, -75f), new Vector2(260f, EventBlock));
        eventLabels.color = new Color(0.58f, 0.63f, 0.70f);
        eventLabels.lineSpacing = 1.35f;

        Text eventValues = AddText(panel, "EventValues", 15, FontStyle.Bold,
                                   TextAnchor.UpperRight,
                                   new Vector2(valueCentre, -75f), new Vector2(280f, EventBlock));
        eventValues.lineSpacing = 1.35f;

        Text footnote = AddText(panel, "Footnote", 12, FontStyle.Italic, TextAnchor.MiddleCenter,
                                new Vector2(0f, -170f), new Vector2(820f, 22f));
        footnote.text = "Driving events are scored per minute and route rules per opportunity, " +
                        "so neither a longer drive nor a longer route is penalised for its length";
        footnote.color = new Color(0.38f, 0.42f, 0.48f);

        // ---- buttons ------------------------------------------------------------
        Button again = AddButton(panel, "DriveAgain", "DRIVE AGAIN",
                                 new Vector2(-115f, -225f),
                                 new Color(1f, 0.82f, 0.25f), Color.black);

        Button menu = AddButton(panel, "MainMenu", "MAIN MENU",
                                new Vector2(115f, -225f),
                                new Color(0.20f, 0.23f, 0.28f), Color.white);

        // ---- wire ---------------------------------------------------------------
        screen.panel = panel;
        screen.scoreText = score;
        screen.gradeText = grade;
        screen.verdictText = verdict;
        screen.summaryLabels = summaryLabels;
        screen.summaryValues = summaryValues;
        screen.eventLabels = eventLabels;
        screen.eventValues = eventValues;
        screen.driveAgainButton = again;
        screen.menuButton = menu;

        panel.SetActive(false);

        EditorUtility.SetDirty(screen);
        EditorSceneManager.MarkSceneDirty(canvasGO.scene);
        Selection.activeGameObject = canvasGO;

        Debug.Log(
            "Drive debrief set up.\n" +
            "  Press Escape (or Options on the pad) after driving to end the run\n" +
            "  Needs 20 seconds of driving before it will report: rates need a sample\n" +
            "  Writes drive_report_<timestamp>.txt next to the telemetry CSV\n" +
            "  Score is rate-based: events per minute, not totals\n" +
            "Tune the thresholds and weights on DriveScorer, on PlayerCar.\n" +
            "Save the scene (Ctrl+S).");
    }

    // ------------------------------------------------------------------ helpers

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static Text AddText(GameObject parent, string name, int size, FontStyle style,
                        TextAnchor anchor, Vector2 position, Vector2 dimensions)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = anchor;
        text.supportRichText = true;          // the value columns colour themselves
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.raycastTarget = false;
        text.color = new Color(0.90f, 0.92f, 0.95f);

        return text;
    }

    static Button AddButton(GameObject parent, string name, string caption,
                            Vector2 position, Color background, Color foreground)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(210f, 48f);

        Image image = go.GetComponent<Image>();
        image.color = background;

        Text label = AddText(go, "Label", 17, FontStyle.Bold, TextAnchor.MiddleCenter,
                             Vector2.zero, new Vector2(210f, 48f));
        label.text = caption;
        label.color = foreground;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;

        return button;
    }
}
