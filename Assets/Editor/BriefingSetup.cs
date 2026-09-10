using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Builds the pre-drive briefing as two pages.
///
/// Page 1 is the controls and the job. Page 2 is the nine elements of the North
/// Carolina road skills test, which is what the run is actually graded on. They are
/// separate because they answer different questions and neither is short: putting
/// the elements underneath a control list produced a wall of text, and a
/// briefing nobody reads is worse than no briefing.
///
/// The speed limit and stopping distance are read from the DriveScorer and
/// VehicleController in the scene rather than typed in, so the screen cannot drift
/// out of step with the rules it describes.
///
/// WHAT GETS SHOWN AND HIDDEN
///
/// The overlay is a full-screen near-opaque Dim with both pages inside it. Only the
/// Dim is toggled by Show and Hide; the pages are swapped between themselves.
/// Hiding a page while leaving the Dim up darkens the whole game, which is what
/// caused the "everything went dark" bug, so exactly one object is toggled at each
/// level.
/// </summary>
public static class BriefingSetup
{
    const string CanvasName = "BriefingCanvas";
    const string CarName = "PlayerCar";

    const float Body = 26f;
    const float Row = 46f;

    // Page 1 columns. Left edges and widths, so no two can ever overlap.
    const float ActionX = -620f, ActionW = 300f;
    const float KeyX = -300f, KeyW = 170f;
    const float PadX = -110f, PadW = 220f;
    const float JobX = 120f, JobW = 560f;

    // Page 2 columns.
    const float ElementRow = 44f;
    const float NumW = 40f;
    const float NameW = 350f;

    [MenuItem("NCAT/Set Up Driver Briefing", false, 39)]
    public static void SetUp()
    {
        GameObject old = GameObject.Find(CanvasName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        GameObject car = GameObject.Find(CarName);
        DriveScorer scorer = car != null ? car.GetComponent<DriveScorer>() : null;
        VehicleController vehicle = car != null ? car.GetComponent<VehicleController>() : null;

        float limit = scorer != null ? scorer.speedLimitMph : 20f;
        float decel = vehicle != null ? vehicle.brakeDeceleration : 5f;

        // The speed the driver is actually marked down at, read from the scorer so
        // the briefing cannot promise a tolerance the scoring does not give.
        float marked = scorer != null ? scorer.ToleratedSpeedMph : limit + 10f;

        float speedMs = limit / 2.23694f;
        int stopFeet = Mathf.RoundToInt((speedMs * speedMs) / (2f * decel) * 3.28084f);

        // ---- canvas ---------------------------------------------------------------
        GameObject canvasGO = new GameObject(CanvasName);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Briefing Canvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasGO.AddComponent<GraphicRaycaster>();

        DriverBriefing briefing = canvasGO.AddComponent<DriverBriefing>();

        // ---- overlay --------------------------------------------------------------
        GameObject dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(canvasGO.transform, false);
        Stretch(dim.GetComponent<RectTransform>());
        dim.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.04f, 0.88f);

        GameObject page1 = Panel(dim, "Page1Controls", 1400f, 740f);
        GameObject page2 = Panel(dim, "Page2Grading", 1440f, 780f);

        Text routeLine = BuildControlsPage(page1, limit, marked, stopFeet);
        BuildGradingPage(page2);

        // ---- wire -----------------------------------------------------------------
        briefing.panel = dim;
        briefing.page1 = page1;
        briefing.page2 = page2;
        briefing.routeLine = routeLine;
        briefing.reopenKey = KeyCode.F1;

        page1.SetActive(true);
        page2.SetActive(false);
        dim.SetActive(false);

        EditorUtility.SetDirty(briefing);
        EditorSceneManager.MarkSceneDirty(canvasGO.scene);
        Selection.activeGameObject = canvasGO;

        Debug.Log(
            "Driver briefing rebuilt as two pages.\n" +
            "  Page 1: controls and the job. Page 2: the 9 road-test elements\n" +
            "  Any key turns the page, then begins. F1 reopens it at page 1\n" +
            "  Stopping distance read live from the vehicle: " + stopFeet + " ft from " +
            limit.ToString("0") + " mph\n" +
            "Save the scene (Ctrl+S).");
    }

    // ------------------------------------------------------------------- page one

    static Text BuildControlsPage(GameObject page, float limit, float marked, int stopFeet)
    {
        Centred(page, "Title", "NC A&T CAMPUS SHUTTLE", 46, FontStyle.Bold, 300f,
                new Color(1f, 0.82f, 0.25f), 1400f);

        Text routeLine = Centred(page, "RouteLine", "Follow the beacons to the depot.",
                                 24, FontStyle.Normal, 246f,
                                 new Color(0.82f, 0.86f, 0.91f), 1400f);

        Header(page, "ControlsHead", "CONTROLS", ActionX, ActionW + KeyW + PadW, 170f);

        string[] actions = { "Drive / brake", "Steer", "Hard brake", "Horn",
                             "Indicators", "Change camera", "This briefing", "End run" };
        string[] keys = { "W  S", "A  D", "Space", "H", "Q  E", "C", "F1", "Esc" };
        string[] pads = { "RT  LT", "Left stick", "Cross", "Circle", "L1  R1",
                          "D-pad Up", "Touchpad", "Options" };

        Column(page, "ControlActions", ActionX, ActionW, actions, 125f, Row,
               new Color(0.62f, 0.68f, 0.75f));
        Column(page, "ControlKeys", KeyX, KeyW, keys, 125f, Row,
               new Color(0.95f, 0.97f, 0.99f)).fontStyle = FontStyle.Bold;
        Column(page, "ControlPads", PadX, PadW, pads, 125f, Row,
               new Color(0.52f, 0.58f, 0.66f));

        Header(page, "JobHead", "YOUR JOB", JobX, JobW, 170f);

        string[] job =
        {
            "Drive the route to the depot",
            "Obey every sign and signal",
            "Yield to anyone crossing",
            "Aim for " + limit.ToString("0") + " mph, marked at " + marked.ToString("0"),
            "Signal every turn",
            "Brake and steer smoothly",
        };

        string bullets = "";
        for (int i = 0; i < job.Length; i++)
        {
            bullets += "<color=#6FCF8B>•</color>  " + job[i];
            if (i < job.Length - 1) bullets += "\n";
        }

        Text jobText = Block(page, "Job", JobX, JobW, job.Length, 125f, Row);
        jobText.text = bullets;
        jobText.color = new Color(0.86f, 0.89f, 0.93f);

        Centred(page, "Warning",
                "From " + limit.ToString("0") + " mph this bus needs " + stopFeet +
                " ft to stop. Brake early.",
                24, FontStyle.Bold, -250f, new Color(1f, 0.82f, 0.25f), 1400f);

        Centred(page, "Dismiss", "Press any key for how you are graded", 26,
                FontStyle.Bold, -305f, new Color(0.60f, 0.66f, 0.73f), 1400f);

        PageNumber(page, "1 OF 2", 620f, -330f);

        return routeLine;
    }

    // ------------------------------------------------------------------- page two

    static void BuildGradingPage(GameObject page)
    {
        Centred(page, "Title", "HOW YOU ARE GRADED", 42, FontStyle.Bold, 320f,
                new Color(1f, 0.82f, 0.25f), 1440f);

        Centred(page, "Subtitle",
                "The nine elements of the North Carolina road skills test that this\n" +
                "simulator measures.",
                24, FontStyle.Normal, 272f, new Color(0.82f, 0.86f, 0.91f), 1440f);

        Header(page, "ElementsHead", "THE 9 ELEMENTS", -300f, 600f, 218f);

        // One column now that there are nine. Two columns of eight existed only
        // because sixteen would not fit down the page.
        BuildElementColumn(page, -300f, -288f);

        Text passRule = Add(page, "PassRule", 26, FontStyle.Normal, TextAnchor.MiddleLeft,
                            new Vector2(-660f + 300f, -215f), new Vector2(600f, 34f));
        passRule.text = "<color=#6FCF8B>•</color>  80% or better passes an element.";
        passRule.color = new Color(0.86f, 0.89f, 0.93f);

        Text failRule = Add(page, "FailRule", 26, FontStyle.Normal, TextAnchor.MiddleLeft,
                            new Vector2(30f + 320f, -215f), new Vector2(640f, 34f));
        failRule.text = "<color=#FF6B57>•</color>  More than 5 failed is an automatic fail.";
        failRule.color = new Color(0.86f, 0.89f, 0.93f);

        Centred(page, "Footnote",
                "An element the route never tests is reported as not assessed, never " +
                "as a pass.",
                22, FontStyle.Normal, -272f, new Color(0.55f, 0.60f, 0.67f), 1440f);

        Centred(page, "Dismiss", "Press any key to begin", 26, FontStyle.Bold, -330f,
                new Color(0.60f, 0.66f, 0.73f), 1440f);

        PageNumber(page, "2 OF 2", 660f, -355f);
    }

    /// <summary>
    /// The nine elements down one column: numbers right-aligned to their own edge,
    /// names from a fixed left edge.
    ///
    /// Two rects rather than one, because sharing a rect is what made the
    /// double-digit rows step sideways relative to the single-digit ones.
    /// </summary>
    static void BuildElementColumn(GameObject page, float numRight, float nameLeft)
    {
        int count = System.Enum.GetValues(typeof(NcElement)).Length;

        string numbers = "", names = "";

        for (int i = 0; i < count; i++)
        {
            numbers += "<color=#9EADBF>" + (i + 1) + ".</color>";
            names += "<color=#E0E7F0>" + NcRubric.Label((NcElement)i) + "</color>";

            if (i < count - 1) { numbers += "\n"; names += "\n"; }
        }

        Text numberText = Block(page, "ElementNumbers", numRight - NumW, NumW,
                                count, 168f, ElementRow);
        numberText.text = numbers;
        numberText.alignment = TextAnchor.UpperRight;

        Text nameText = Block(page, "ElementNames", nameLeft, NameW, count, 168f, ElementRow);
        nameText.text = names;
    }

    // ---------------------------------------------------------------------- helpers

    static GameObject Panel(GameObject parent, string name, float width, float height)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent.transform, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = Vector2.zero;

        panel.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 0.98f);
        return panel;
    }

    static Text Column(GameObject page, string name, float leftX, float width,
                       string[] lines, float top, float row, Color colour)
    {
        Text text = Block(page, name, leftX, width, lines.Length, top, row);
        text.text = string.Join("\n", lines);
        text.color = colour;
        return text;
    }

    static Text Block(GameObject page, string name, float leftX, float width,
                      int lines, float top, float row)
    {
        float height = lines * row;

        Text text = Add(page, name, (int)Body, FontStyle.Normal, TextAnchor.UpperLeft,
                        new Vector2(leftX + width * 0.5f, top - height * 0.5f),
                        new Vector2(width, height));

        // Row spacing is set here rather than left to the font, so every column steps
        // down by exactly the same amount and the rows stay aligned.
        text.lineSpacing = row / Body;
        return text;
    }

    static void Header(GameObject page, string name, string caption,
                       float leftX, float width, float y)
    {
        Text header = Add(page, name, 22, FontStyle.Bold, TextAnchor.MiddleLeft,
                          new Vector2(leftX + width * 0.5f, y), new Vector2(width, 30f));
        header.text = caption;
        header.color = new Color(1f, 0.82f, 0.25f);
    }

    static Text Centred(GameObject page, string name, string caption, int size,
                        FontStyle style, float y, Color colour, float panelWidth)
    {
        Text text = Add(page, name, size, style, TextAnchor.MiddleCenter,
                        new Vector2(0f, y), new Vector2(panelWidth - 80f, size + 18f));
        text.text = caption;
        text.color = colour;
        return text;
    }

    static void PageNumber(GameObject page, string caption, float rightX, float y)
    {
        Text text = Add(page, "PageNumber", 18, FontStyle.Bold, TextAnchor.MiddleRight,
                        new Vector2(rightX - 100f, y), new Vector2(200f, 24f));
        text.text = "PAGE " + caption;
        text.color = new Color(0.36f, 0.40f, 0.45f);
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
        text.color = new Color(0.90f, 0.93f, 0.96f);

        return text;
    }
}
