using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Builds the pre-drive briefing: five controls, five scoring rules with what
/// each is worth, and one warning.
///
/// It is deliberately short. A briefing nobody reads is worse than no briefing,
/// and the full reference lives in the README. What survives here is only what a
/// player needs before their first metre of driving.
///
/// The speed limit and stopping distance are read from the DriveScorer and
/// VehicleController in the scene rather than typed in, so the screen cannot drift
/// out of step with the rules it describes.
///
/// WHAT GETS SHOWN AND HIDDEN
///
/// The overlay is a full-screen near-opaque Dim with the panel inside it. Only the
/// Dim is toggled, and the panel stays permanently active as its child. Hiding the
/// panel while leaving the Dim up darkens the whole game; hiding the Dim while the
/// panel is also off means the briefing never appears again. Toggling exactly one
/// object avoids both.
/// </summary>
public static class BriefingSetup
{
    const string CanvasName = "BriefingCanvas";
    const string PanelName = "BriefingPanel";
    const string CarName = "PlayerCar";

    const float PanelWidth = 1400f;
    const float PanelHeight = 680f;

    const float Body = 26f;             // body text size
    const float Row = 46f;              // distance between rows

    // Column left edges and widths, so no two ever overlap.
    const float ActionX = -620f, ActionW = 300f;
    const float KeyX = -300f, KeyW = 170f;
    const float PadX = -110f, PadW = 220f;
    const float RuleX = 120f, RuleW = 450f;
    const float PointsX = 580f, PointsW = 100f;

    const float HeaderY = 140f;
    const float BodyTop = 95f;

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

        // The weights come from the scorer, so the briefing and the report can
        // never quote different numbers at the player.
        int stopPts = Mathf.RoundToInt(scorer != null ? scorer.stopSignWeight : 30f);
        int yieldPts = Mathf.RoundToInt(scorer != null ? scorer.yieldWeight : 25f);
        int speedPts = Mathf.RoundToInt(scorer != null ? scorer.speedingWeight : 25f);
        int crashPts = Mathf.RoundToInt((scorer != null ? scorer.collisionCap : 30f) +
                                        (scorer != null ? scorer.nearMissCap : 20f));
        int smoothPts = Mathf.RoundToInt((scorer != null ? scorer.brakingCap : 10f) +
                                         (scorer != null ? scorer.corneringCap : 10f));

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

        GameObject panel = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(dim.transform, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panelRect.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 0.98f);

        // ---- heading ----------------------------------------------------------------
        Centred(panel, "Title", "NC A&T CAMPUS SHUTTLE", 46, FontStyle.Bold, 270f,
                new Color(1f, 0.82f, 0.25f));

        Text routeLine = Centred(panel, "RouteLine",
                                 "Follow the beacons to the depot.",
                                 24, FontStyle.Normal, 216f,
                                 new Color(0.82f, 0.86f, 0.91f));

        // ---- controls -----------------------------------------------------------------
        Header(panel, "ControlsHead", "CONTROLS", ActionX, ActionW + KeyW + PadW);

        string[] actions = { "Drive / brake", "Steer", "Hard brake", "Change camera", "End run" };
        string[] keys = { "W  S", "A  D", "Space", "C", "Esc" };
        string[] pads = { "RT  LT", "Left stick", "Cross", "D-pad Up", "Options" };

        Column(panel, "ControlActions", ActionX, ActionW, actions, new Color(0.62f, 0.68f, 0.75f));
        Column(panel, "ControlKeys", KeyX, KeyW, keys, new Color(0.95f, 0.97f, 0.99f)).fontStyle = FontStyle.Bold;
        Column(panel, "ControlPads", PadX, PadW, pads, new Color(0.52f, 0.58f, 0.66f));

        // ---- rules ---------------------------------------------------------------------
        Header(panel, "RulesHead", "SCORING, STARTING AT 100", RuleX, RuleW + PointsW);

        string[] rules =
        {
            "Stop fully at every stop sign",
            "Yield to anyone in a crossing",
            "Stay under " + limit.ToString("0") + " mph",
            "Avoid crashes and close passes",
            "Brake and steer smoothly",
        };

        int[] points = { stopPts, yieldPts, speedPts, crashPts, smoothPts };

        string body = "";
        string cost = "";
        for (int i = 0; i < rules.Length; i++)
        {
            body += "<color=#6FCF8B>•</color>  " + rules[i];
            cost += points[i] + " pts";

            if (i < rules.Length - 1) { body += "\n"; cost += "\n"; }
        }

        Text ruleText = Block(panel, "Rules", RuleX, RuleW, rules.Length);
        ruleText.text = body;
        ruleText.color = new Color(0.86f, 0.89f, 0.93f);

        // Its own rect, right-aligned, at the same size and line spacing as the
        // rules. Shrinking this column would change its line height and walk the
        // numbers out of line with the rules they price.
        Text pointsText = Block(panel, "RulePoints", PointsX, PointsW, rules.Length);
        pointsText.text = cost;
        pointsText.alignment = TextAnchor.UpperRight;
        pointsText.color = new Color(0.55f, 0.61f, 0.68f);

        // ---- the one fact worth knowing --------------------------------------------------
        Centred(panel, "Warning",
                "From " + limit.ToString("0") + " mph this bus needs " + stopFeet +
                " ft to stop. Brake early.",
                24, FontStyle.Bold, -195f, new Color(1f, 0.82f, 0.25f));

        Centred(panel, "Dismiss", "Press any key to begin", 26, FontStyle.Bold, -265f,
                new Color(0.60f, 0.66f, 0.73f));

        // ---- wire -----------------------------------------------------------------------
        // The panel stays on. The Dim is the thing that gets shown and hidden.
        briefing.panel = dim;
        panel.SetActive(true);
        dim.SetActive(false);

        EditorUtility.SetDirty(briefing);
        EditorSceneManager.MarkSceneDirty(canvasGO.scene);
        Selection.activeGameObject = canvasGO;

        Debug.Log(
            "Driver briefing rebuilt.\n" +
            "  Five controls, four rules, one warning, at " + Body + "px\n" +
            "  Shows after the campus loads. Any key begins. H reopens it\n" +
            "  Stopping distance read live from the vehicle: " + stopFeet + " ft from " +
            limit.ToString("0") + " mph\n" +
            "Save the scene (Ctrl+S).");
    }

    // ---------------------------------------------------------------------- helpers

    /// <summary>One column of rows, each on its own line, in its own reserved width.</summary>
    static Text Column(GameObject panel, string name, float leftX, float width,
                       string[] lines, Color colour)
    {
        Text text = Block(panel, name, leftX, width, lines.Length);
        text.text = string.Join("\n", lines);
        text.color = colour;
        return text;
    }

    static Text Block(GameObject panel, string name, float leftX, float width, int lines)
    {
        float height = lines * Row;

        Text text = Add(panel, name, (int)Body, FontStyle.Normal, TextAnchor.UpperLeft,
                        new Vector2(leftX + width * 0.5f, BodyTop - height * 0.5f),
                        new Vector2(width, height));

        // Row spacing is set here rather than left to the font, so every column
        // steps down by exactly the same amount and the rows stay aligned.
        text.lineSpacing = Row / Body;
        return text;
    }

    static void Header(GameObject panel, string name, string caption, float leftX, float width)
    {
        Text header = Add(panel, name, 22, FontStyle.Bold, TextAnchor.MiddleLeft,
                          new Vector2(leftX + width * 0.5f, HeaderY), new Vector2(width, 30f));
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
        text.color = new Color(0.90f, 0.93f, 0.96f);

        return text;
    }
}
