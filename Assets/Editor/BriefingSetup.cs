using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Builds the pre-drive briefing screen.
///
/// Every number quoted on it is read from the DriveScorer and VehicleController in
/// the scene rather than typed in, so the briefing cannot drift out of step with
/// the rules it describes. Retune a weight and re-run this, and the screen says
/// the new figure.
///
/// LAYOUT
///
/// The first version put the action names and their keys in the SAME rect, one
/// left-aligned and one right-aligned, and let them overflow toward each other.
/// That works only while every line stays short enough, and "Right-drag / Right
/// stick" was not: the two collided.
///
/// So every column now owns a rect of its own, at an explicit x with an explicit
/// width and a real gap to its neighbour. Two pieces of text can no longer meet in
/// the middle however long they get.
/// </summary>
public static class BriefingSetup
{
    const string CanvasName = "BriefingCanvas";
    const string PanelName = "BriefingPanel";
    const string CarName = "PlayerCar";

    // Wide enough to spread three sections across the screen instead of stacking
    // them into two cramped columns.
    const float PanelWidth = 1520f;
    const float PanelHeight = 700f;

    const float Row = 26f;              // one line, with room to breathe

    // Three sections across the panel. Each x is a LEFT edge; each width is the
    // space that column owns outright.
    const float ControlsX = -700f;
    const float ActionW = 230f;
    const float KeyX = -450f;
    const float KeyW = 150f;
    const float PadX = -285f;
    const float PadW = 165f;

    const float JobX = -70f;
    const float JobW = 330f;

    const float ScoreX = 300f;
    const float ScoreW = 300f;
    const float CostX = 615f;
    const float CostW = 90f;

    // Vertical anchors.
    const float HeaderY = 185f;
    const float BodyTop = 155f;
    const float CostsHeaderY = 15f;
    const float CostsBodyTop = -15f;

    [MenuItem("NCAT/Set Up Driver Briefing", false, 39)]
    public static void SetUp()
    {
        GameObject old = GameObject.Find(CanvasName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        // ---- read the live rules ------------------------------------------------
        GameObject car = GameObject.Find(CarName);
        DriveScorer scorer = car != null ? car.GetComponent<DriveScorer>() : null;
        VehicleController vehicle = car != null ? car.GetComponent<VehicleController>() : null;

        if (scorer == null)
            Debug.LogWarning("No DriveScorer on " + CarName + " - the briefing will " +
                             "quote default figures. Run NCAT → Set Up Drive Debrief first.");

        float limit = scorer != null ? scorer.speedLimitMph : 20f;
        float brakeG = scorer != null ? scorer.hardBrakingG : 0.30f;
        float cornerG = scorer != null ? scorer.hardCorneringG : 0.25f;
        float nearM = scorer != null ? scorer.nearMissDistance : 2.5f;
        float stopW = scorer != null ? scorer.stopSignWeight : 30f;
        float yieldW = scorer != null ? scorer.yieldWeight : 25f;
        float speedW = scorer != null ? scorer.speedingWeight : 25f;
        float brakeCap = scorer != null ? scorer.brakingCap : 10f;
        float cornerCap = scorer != null ? scorer.corneringCap : 10f;
        float nearCap = scorer != null ? scorer.nearMissCap : 20f;
        float collCap = scorer != null ? scorer.collisionCap : 30f;

        float decel = vehicle != null ? vehicle.brakeDeceleration : 5f;
        float massTonnes = vehicle != null ? vehicle.massKg / 1000f : 13f;

        // Real stopping distance at the limit, in feet — the one number that makes
        // "brake early" concrete rather than nagging.
        float speedMs = limit / 2.23694f;
        int stopFeet = Mathf.RoundToInt((speedMs * speedMs) / (2f * decel) * 3.28084f);

        // ---- canvas ---------------------------------------------------------------
        GameObject canvasGO = new GameObject(CanvasName);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Briefing Canvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;              // above the HUD, below the debrief

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasGO.AddComponent<GraphicRaycaster>();

        DriverBriefing briefing = canvasGO.AddComponent<DriverBriefing>();

        // ---- backdrop -------------------------------------------------------------
        GameObject dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(canvasGO.transform, false);
        Stretch(dim.GetComponent<RectTransform>());
        dim.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.04f, 0.9f);

        GameObject panel = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(dim.transform, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panelRect.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 0.98f);

        // ---- heading --------------------------------------------------------------
        Centred(panel, "Title", "NC A&T CAMPUS SHUTTLE", 34, FontStyle.Bold, 300f,
                new Color(1f, 0.82f, 0.25f));

        Centred(panel, "Subtitle", "Driver briefing", 17, FontStyle.Normal, 268f,
                new Color(0.55f, 0.60f, 0.67f));

        Text routeLine = Centred(panel, "RouteLine", "Follow the beacons to the depot.",
                                 17, FontStyle.Bold, 236f, new Color(0.88f, 0.91f, 0.95f));

        // ---- controls, left --------------------------------------------------------
        Header(panel, "ControlsHead", "CONTROLS", ControlsX, ActionW + KeyW + PadW);

        string[] actions = { "Throttle / brake", "Steer", "Hard brake", "Camera view",
                             "Look around", "Respawn if stuck", "End run + report" };
        string[] keys = { "W  S", "A  D", "Space", "C", "Right-drag", "R", "Esc" };
        string[] pads = { "RT  LT", "Left stick", "Cross", "D-pad Up", "Right stick",
                          "Circle", "Options" };

        // Three columns, three rects, three reserved slices of the panel. Nothing
        // here can grow into anything else.
        Text actionText = Block(panel, "ControlActions", ControlsX, BodyTop, ActionW, actions.Length);
        actionText.text = string.Join("\n", actions);
        actionText.color = new Color(0.60f, 0.66f, 0.73f);

        Text keyText = Block(panel, "ControlKeys", KeyX, BodyTop, KeyW, keys.Length);
        keyText.text = string.Join("\n", keys);
        keyText.fontStyle = FontStyle.Bold;

        Text padText = Block(panel, "ControlPads", PadX, BodyTop, PadW, pads.Length);
        padText.text = string.Join("\n", pads);
        padText.color = new Color(0.52f, 0.58f, 0.65f);

        // Column captions, so it is obvious which is keyboard and which is pad.
        Caption(panel, "KeyCaption", "KEYBOARD", KeyX, KeyW, BodyTop + 24f);
        Caption(panel, "PadCaption", "CONTROLLER", PadX, PadW, BodyTop + 24f);

        // ---- the job, middle --------------------------------------------------------
        Header(panel, "JobHead", "YOUR JOB", JobX, JobW);

        Text job = Block(panel, "Job", JobX, BodyTop, JobW, 9);
        job.horizontalOverflow = HorizontalWrapMode.Wrap;      // prose, so let it wrap
        job.text =
            "Drive the route in order. The lit beacon is your next stop, and an " +
            "arrow points to it when it is off screen.\n\n" +
            "Come to a full stop at stop signs. Yield to anyone in a crossing. " +
            "Wait at bus stops while riders board.\n\n" +
            "Reach the depot to finish and see your report.";
        job.color = new Color(0.78f, 0.83f, 0.89f);

        // ---- scoring, right ----------------------------------------------------------
        Header(panel, "GoodHead", "EARNS YOU POINTS", ScoreX, ScoreW + CostW + 15f);

        string[] good = { "Full stop at every stop sign",
                          "Yield to anyone in a crossing",
                          "Stay under the posted limit",
                          "Brake early, steer smoothly" };
        string[] goodCost = { "up to " + Pts(stopW), "up to " + Pts(yieldW),
                              "up to " + Pts(speedW), "" };

        Bullets(panel, "Good", good, goodCost, "#6FCF8B", BodyTop);

        Header(panel, "BadHead", "COSTS YOU POINTS", ScoreX, ScoreW + CostW + 15f, CostsHeaderY);

        string[] bad = { "Rolling through a stop sign",
                         "Braking harder than " + brakeG.ToString("0.00") + " g",
                         "Cornering harder than " + cornerG.ToString("0.00") + " g",
                         "Passing within " + nearM.ToString("0.#") + " m of someone",
                         "Hitting anything",
                         "Driving over " + limit.ToString("0") + " mph" };
        string[] badCost = { "", "up to " + Pts(brakeCap), "up to " + Pts(cornerCap),
                             "up to " + Pts(nearCap), "up to " + Pts(collCap), "" };

        Bullets(panel, "Bad", bad, badCost, "#FF6B57", CostsBodyTop);

        // ---- the one fact that changes how people drive --------------------------------
        Centred(panel, "Warning",
                "This bus weighs " + massTonnes.ToString("0") + " tonnes. From " +
                limit.ToString("0") + " mph it needs " + stopFeet + " ft to stop. Brake early.",
                17, FontStyle.Bold, -250f, new Color(1f, 0.82f, 0.25f));

        Centred(panel, "Dismiss", "Press any key to begin          H reopens this screen",
                16, FontStyle.Bold, -295f, new Color(0.52f, 0.58f, 0.65f));

        // ---- wire ------------------------------------------------------------------
        briefing.panel = panel;
        briefing.routeLine = routeLine;
        panel.SetActive(false);

        EditorUtility.SetDirty(briefing);
        EditorSceneManager.MarkSceneDirty(canvasGO.scene);
        Selection.activeGameObject = canvasGO;

        Debug.Log(
            "Driver briefing rebuilt at " + PanelWidth + " x " + PanelHeight + ".\n" +
            "  Controls now span three separate columns: action, keyboard, controller\n" +
            "  Nothing shares a rect, so no two labels can overlap\n" +
            "  Figures read live from DriveScorer: " + stopFeet + " ft to stop from " +
            limit.ToString("0") + " mph\n" +
            "Save the scene (Ctrl+S).");
    }

    // -------------------------------------------------------------------- helpers

    static string Pts(float weight)
    {
        return "-" + Mathf.RoundToInt(weight);
    }

    /// <summary>
    /// A bullet list plus a matching column of costs, as two rects side by side
    /// rather than one string with the cost tacked on the end. Keeping them apart
    /// is what lets the costs line up down the page instead of ending wherever
    /// each label happens to stop.
    /// </summary>
    static void Bullets(GameObject panel, string name, string[] lines,
                        string[] costs, string colour, float topY)
    {
        string body = "";
        for (int i = 0; i < lines.Length; i++)
        {
            body += "<color=" + colour + ">•</color>  " + lines[i];
            if (i < lines.Length - 1) body += "\n";
        }

        Text text = Block(panel, name, ScoreX, topY, ScoreW, lines.Length);
        text.text = body;
        text.color = new Color(0.84f, 0.87f, 0.91f);

        string costBody = "";
        for (int i = 0; i < costs.Length; i++)
        {
            costBody += costs[i];
            if (i < costs.Length - 1) costBody += "\n";
        }

        Text cost = Block(panel, name + "Cost", CostX, topY, CostW, costs.Length);
        cost.text = costBody;

        // Same font size as the bullets it sits beside, deliberately. Unity scales
        // line height with font size, so a smaller cost column would drift out of
        // step with its own list — by six rows down it no longer lines up with the
        // thing it is pricing. Colour carries the hierarchy instead.
        cost.color = new Color(0.48f, 0.53f, 0.60f);
    }

    /// <summary>A left-aligned block of lines whose top edge sits at <paramref name="topY"/>.</summary>
    static Text Block(GameObject panel, string name, float leftX, float topY,
                      float width, int lines)
    {
        float height = lines * Row;

        Text text = Add(panel, name, 16, FontStyle.Normal, TextAnchor.UpperLeft,
                        new Vector2(leftX + width * 0.5f, topY - height * 0.5f),
                        new Vector2(width, height));

        text.lineSpacing = 1.35f;
        return text;
    }

    static void Header(GameObject panel, string name, string caption, float leftX, float width)
    {
        Header(panel, name, caption, leftX, width, HeaderY);
    }

    static void Header(GameObject panel, string name, string caption,
                       float leftX, float width, float y)
    {
        Text header = Add(panel, name, 15, FontStyle.Bold, TextAnchor.MiddleLeft,
                          new Vector2(leftX + width * 0.5f, y), new Vector2(width, 22f));
        header.text = caption;
        header.color = new Color(1f, 0.82f, 0.25f);
    }

    static void Caption(GameObject panel, string name, string caption,
                        float leftX, float width, float y)
    {
        Text text = Add(panel, name, 12, FontStyle.Bold, TextAnchor.MiddleLeft,
                        new Vector2(leftX + width * 0.5f, y), new Vector2(width, 18f));
        text.text = caption;
        text.color = new Color(0.42f, 0.47f, 0.54f);
    }

    static Text Centred(GameObject panel, string name, string caption, int size,
                        FontStyle style, float y, Color colour)
    {
        Text text = Add(panel, name, size, style, TextAnchor.MiddleCenter,
                        new Vector2(0f, y), new Vector2(PanelWidth - 80f, size + 14f));
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

        // Never clip vertically — a wrapped paragraph that grows an extra line
        // should spill rather than lose its last sentence.
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.color = new Color(0.90f, 0.93f, 0.96f);

        return text;
    }
}
