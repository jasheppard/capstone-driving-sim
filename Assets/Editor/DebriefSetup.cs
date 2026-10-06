using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Builds the road-test debrief: the stamp and the drive summary on the left, all
/// nine elements on the right.
///
/// Every column is its own rect with its own alignment and a reserved width. Unity's
/// Text neither wraps nor clips by default, so a column that is too narrow does not
/// truncate, it silently draws over its neighbour. Every layout bug this screen has
/// had was that.
///
/// The panel is a child of a full-screen Dim, and the Dim is what gets toggled.
/// Hiding only the panel leaves the Dim covering the game, which is what once made
/// the whole thing look like the brightness had been turned down.
/// </summary>
public static class DebriefSetup
{
    const string CanvasName = "DebriefCanvas";
    const string PanelName = "DebriefPanel";

    const float PanelWidth = 1500f;
    const float PanelHeight = 880f;

    const float Body = 22f;
    const float SummaryRow = 38f;
    const float ElementRow = 42f;

    // Left column.
    const float LeftX = -700f;
    const float LeftW = 440f;

    // Right column: number, name, verdict, percentage.
    const float NumRight = -20f, NumW = 40f;
    const float NameX = -8f, NameW = 400f;
    const float ResultRight = 450f, ResultW = 120f;
    const float ScoreRight = 620f, ScoreW = 190f;

    const float ElementsTop = 210f;

    [MenuItem("NCAT/Set Up Drive Debrief", false, 56)]
    public static void SetUp()
    {
        GameObject old = GameObject.Find(CanvasName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        // ---- canvas ---------------------------------------------------------------
        GameObject canvasGO = new GameObject(CanvasName);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Debrief Canvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 240;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasGO.AddComponent<GraphicRaycaster>();

        DebriefScreen screen = canvasGO.AddComponent<DebriefScreen>();

        // ---- overlay --------------------------------------------------------------
        GameObject dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(canvasGO.transform, false);
        Stretch(dim.GetComponent<RectTransform>());
        dim.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.04f, 0.88f);

        GameObject panel = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(dim.transform, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panelRect.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 0.98f);

        // ---- heading ---------------------------------------------------------------
        Centred(panel, "Title", "ROAD SKILLS TEST", 40, FontStyle.Bold, 390f,
                new Color(1f, 0.82f, 0.25f));

        screen.driverLine = Centred(panel, "DriverLine", "", 22, FontStyle.Normal, 344f,
                                    new Color(0.62f, 0.68f, 0.75f));

        // ---- the stamp -------------------------------------------------------------
        screen.stampText = Add(panel, "Stamp", 88, FontStyle.Bold, TextAnchor.MiddleLeft,
                               new Vector2(LeftX + 300f, 250f), new Vector2(600f, 100f));
        screen.stampText.text = "PASS";

        screen.proficiencyText = Add(panel, "Proficiency", 46, FontStyle.Bold,
                                     TextAnchor.MiddleLeft,
                                     new Vector2(LeftX + 70f, 160f), new Vector2(140f, 56f));
        screen.proficiencyText.text = "0%";

        Text caption = Add(panel, "ProficiencyCaption", 20, FontStyle.Normal,
                           TextAnchor.MiddleLeft,
                           new Vector2(-560f + 150f, 152f), new Vector2(300f, 26f));
        caption.text = "overall proficiency";
        caption.color = new Color(0.62f, 0.68f, 0.75f);

        screen.passedText = Left(panel, "Passed", LeftX, LeftW, 100f, 22,
                                 new Color(0.88f, 0.91f, 0.94f));
        screen.assessedText = Left(panel, "Assessed", LeftX, LeftW, 68f, 22,
                                   new Color(0.43f, 0.48f, 0.54f));

        // Two lines of room: the verdict is a sentence, not a label.
        screen.verdictText = Add(panel, "Verdict", 21, FontStyle.Normal, TextAnchor.UpperLeft,
                                 new Vector2(LeftX + 260f, 22f - 28f), new Vector2(520f, 60f));
        screen.verdictText.horizontalOverflow = HorizontalWrapMode.Wrap;
        screen.verdictText.color = new Color(0.62f, 0.68f, 0.75f);

        // ---- drive summary ----------------------------------------------------------
        screen.summaryLabels = Block(panel, "SummaryLabels", LeftX, 300f, 5,
                                     -60f, SummaryRow);
        screen.summaryLabels.color = new Color(0.62f, 0.68f, 0.75f);

        screen.summaryValues = Block(panel, "SummaryValues", -260f - 200f, 200f, 5,
                                     -60f, SummaryRow);
        screen.summaryValues.alignment = TextAnchor.UpperRight;

        screen.governorNote = Left(panel, "GovernorNote", LeftX, 560f, -310f, 20,
                                   new Color(1f, 0.78f, 0.27f));

        // ---- the elements -----------------------------------------------------
        Header(panel, "ElementsHead", "THE 9 ELEMENTS", NameX - 52f, 400f, 290f,
               TextAnchor.MiddleLeft);
        Header(panel, "ResultHead", "RESULT", ResultRight - ResultW, ResultW, 290f,
               TextAnchor.MiddleRight);
        Header(panel, "ScoreHead", "SCORE", ScoreRight - ScoreW, ScoreW, 290f,
               TextAnchor.MiddleRight);

        int rows = System.Enum.GetValues(typeof(NcElement)).Length;

        screen.elementNumbers = Block(panel, "ElementNumbers", NumRight - NumW, NumW, rows,
                                      ElementsTop, ElementRow);
        screen.elementNumbers.alignment = TextAnchor.UpperRight;

        screen.elementNames = Block(panel, "ElementNames", NameX, NameW, rows,
                                    ElementsTop, ElementRow);

        screen.elementResults = Block(panel, "ElementResults", ResultRight - ResultW,
                                      ResultW, rows, ElementsTop, ElementRow);
        screen.elementResults.alignment = TextAnchor.UpperRight;
        screen.elementResults.fontStyle = FontStyle.Bold;

        screen.elementScores = Block(panel, "ElementScores", ScoreRight - ScoreW, ScoreW,
                                     rows, ElementsTop, ElementRow);
        screen.elementScores.alignment = TextAnchor.UpperRight;

        // ---- buttons --------------------------------------------------------------------
        screen.driveAgainButton = MakeButton(panel, "DriveAgain", "DRIVE AGAIN", -220f, -372f);
        screen.menuButton = MakeButton(panel, "MainMenu", "MAIN MENU", 220f, -372f);

        // ---- wire ------------------------------------------------------------------------
        // The panel stays on. The Dim is the thing that gets shown and hidden.
        screen.panel = dim;
        panel.SetActive(true);
        dim.SetActive(false);

        EditorUtility.SetDirty(screen);
        EditorSceneManager.MarkSceneDirty(canvasGO.scene);
        Selection.activeGameObject = canvasGO;

        Debug.Log(
            "Drive debrief rebuilt as a road test result.\n" +
            "  PASS/FAIL stamp, overall proficiency, and all 9 elements\n" +
            "  The 0 to 100 score is gone: the result is now per element\n" +
            "Save the scene (Ctrl+S).");
    }

    // ---------------------------------------------------------------------- helpers

    static Button MakeButton(GameObject panel, string name, string caption,
                             float x, float y)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image),
                                       typeof(Button));
        go.transform.SetParent(panel.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(300f, 54f);

        go.GetComponent<Image>().color = new Color(0.15f, 0.17f, 0.21f, 1f);

        Text label = Add(go, "Label", 22, FontStyle.Bold, TextAnchor.MiddleCenter,
                         Vector2.zero, new Vector2(300f, 54f));
        label.text = caption;

        return go.GetComponent<Button>();
    }

    static Text Block(GameObject panel, string name, float leftX, float width,
                      int lines, float top, float row)
    {
        float height = lines * row;

        Text text = Add(panel, name, (int)Body, FontStyle.Normal, TextAnchor.UpperLeft,
                        new Vector2(leftX + width * 0.5f, top - height * 0.5f),
                        new Vector2(width, height));

        // Row spacing is set here rather than left to the font, so every column steps
        // down by exactly the same amount and the rows stay aligned across all four.
        text.lineSpacing = row / Body;
        return text;
    }

    static Text Left(GameObject panel, string name, float leftX, float width,
                     float y, int size, Color colour)
    {
        Text text = Add(panel, name, size, FontStyle.Normal, TextAnchor.MiddleLeft,
                        new Vector2(leftX + width * 0.5f, y),
                        new Vector2(width, size + 12f));
        text.color = colour;
        return text;
    }

    static void Header(GameObject panel, string name, string caption, float leftX,
                       float width, float y, TextAnchor anchor)
    {
        Text header = Add(panel, name, 22, FontStyle.Bold, anchor,
                          new Vector2(leftX + width * 0.5f, y), new Vector2(width, 30f));
        header.text = caption;
        header.color = new Color(1f, 0.82f, 0.25f);
    }

    static Text Centred(GameObject panel, string name, string caption, int size,
                        FontStyle style, float y, Color colour)
    {
        Text text = Add(panel, name, size, style, TextAnchor.MiddleCenter,
                        new Vector2(0f, y), new Vector2(PanelWidth - 80f, size + 18f));
        text.text = caption;
        text.color = colour;
        return text;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static Text Add(GameObject parent, string name, int size, FontStyle style,
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
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.color = new Color(0.88f, 0.91f, 0.94f);

        return text;
    }
}
