using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Puts the road test on the car: the rubric that grades it, the consequences that
/// apply while you drive, and the small HUD that tells you the moment you have
/// failed something.
///
/// Run this once, then save the scene. It is safe to run again: existing components
/// are reused rather than duplicated, so re-running does not stack two rubrics on
/// the same vehicle and count everything twice.
/// </summary>
public static class RoadTestSetup
{
    const string CarName = "PlayerCar";
    const string CanvasName = "FaultCanvas";

    [MenuItem("NCAT/Set Up Road Test", false, 36)]
    public static void SetUp()
    {
        GameObject car = GameObject.Find(CarName);

        if (car == null)
        {
            Debug.LogError("[RoadTestSetup] No object called \"" + CarName + "\" in " +
                           "the scene. Run NCAT > Set Up Player Car first.");
            return;
        }

        // ---- the rubric --------------------------------------------------------
        NcRubric rubric = car.GetComponent<NcRubric>();
        if (rubric == null) rubric = Undo.AddComponent<NcRubric>(car);

        // Written explicitly rather than left to the field's default.
        //
        // A component already in the scene keeps whatever value was serialised when
        // it was first added, so changing a default in code does nothing to it. This
        // one was added when the rubric had sixteen elements and wanted eight of them
        // tested; against nine elements that threshold marked perfectly good runs
        // INCOMPLETE.
        rubric.minimumAssessedElements = 5;

        // ---- the HUD -----------------------------------------------------------
        GameObject old = GameObject.Find(CanvasName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        GameObject canvasGO = new GameObject(CanvasName);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Fault Canvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Below the briefing and the debrief, above the driving HUD. A fault flash
        // must never draw on top of the report that explains it.
        canvas.sortingOrder = 200;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasGO.AddComponent<GraphicRaycaster>();

        // Upper third rather than dead centre, so it does not sit over the road just
        // as the driver needs to see what they nearly hit.
        Text flash = Add(canvasGO, "FaultFlash", 40, FontStyle.Bold,
                         TextAnchor.MiddleCenter, new Vector2(0f, 250f),
                         new Vector2(1200f, 130f));
        flash.color = new Color(1f, 0.42f, 0.34f);
        flash.text = "";

        // Top right, opposite the speedometer, so the two do not compete.
        Text tally = Add(canvasGO, "FaultTally", 26, FontStyle.Bold,
                         TextAnchor.MiddleRight, Vector2.zero, new Vector2(300f, 40f));

        RectTransform tallyRect = tally.GetComponent<RectTransform>();
        tallyRect.anchorMin = tallyRect.anchorMax = new Vector2(1f, 1f);
        tallyRect.pivot = new Vector2(1f, 1f);
        tallyRect.anchoredPosition = new Vector2(-40f, -30f);

        tally.text = "NO FAULTS";
        tally.color = new Color(0.44f, 0.81f, 0.55f);

        // ---- the consequences --------------------------------------------------
        InfractionConsequences consequences = car.GetComponent<InfractionConsequences>();
        if (consequences == null)
            consequences = Undo.AddComponent<InfractionConsequences>(car);

        consequences.flashLabel = flash;
        consequences.tallyLabel = tally;

        // Same reason as above: an existing component keeps its old serialised value,
        // so the governor has to be switched off here rather than only in the field
        // default. Faults still flash and still count, the bus is just not throttled
        // for having made them.
        consequences.speedGovernor = false;

        // ---- where speed starts costing you ------------------------------------
        //
        // The sign says 20 and the marking starts at 25. Both numbers are real: a
        // real examiner marks down speed that is unsafe for the conditions, not a
        // driver drifting a couple over, and 5 mph is roughly where that judgement
        // sits on a campus road.
        //
        // Set here rather than left to the component's default so it survives the
        // scorer being rebuilt, and so the briefing, the report and the debrief all
        // quote the same figure.
        DriveScorer scorer = car.GetComponent<DriveScorer>();

        if (scorer != null)
        {
            scorer.speedToleranceMph = 5f;
            EditorUtility.SetDirty(scorer);
        }

        // ---- indicators --------------------------------------------------------
        // The road test scores whether you signalled, so the driver has to be able to
        // see that they did.
        BusIndicators indicators = car.GetComponent<BusIndicators>();
        if (indicators == null) indicators = Undo.AddComponent<BusIndicators>(car);
        EditorUtility.SetDirty(indicators);

        // ---- how the bus meets the ground --------------------------------------
        // Rides the terrain by raycast instead of resting on it, so scanned ripples
        // and tile seams cannot block the bus or throw it into the air.
        SmoothGroundDrive ground = car.GetComponent<SmoothGroundDrive>();
        if (ground == null) ground = Undo.AddComponent<SmoothGroundDrive>(car);
        EditorUtility.SetDirty(ground);

        // ---- the judge ---------------------------------------------------------
        // Scores signs with no route involved. It stands itself down automatically if
        // a route is loaded, so having both in the scene is safe.
        SignJudge judge = car.GetComponent<SignJudge>();
        if (judge == null) judge = Undo.AddComponent<SignJudge>(car);
        EditorUtility.SetDirty(judge);

        // ---- point the route at the rubric -------------------------------------
        RouteRunner runner = Object.FindObjectOfType<RouteRunner>();
        if (runner != null)
        {
            runner.rubric = rubric;
            EditorUtility.SetDirty(runner);
        }

        EditorUtility.SetDirty(rubric);
        EditorUtility.SetDirty(consequences);
        EditorSceneManager.MarkSceneDirty(car.scene);
        Selection.activeGameObject = car;

        Debug.Log(
            "Road test set up on " + CarName + ".\n" +
            "  NcRubric grades the 9 NCDOT elements\n" +
            "  Speed is marked from 25 mph: the limit is 20, tolerance 5\n" +
            "  The speed governor is OFF: faults cost points, not performance\n" +
            "  SmoothGroundDrive rides the terrain by raycast: no catching, no bouncing\n" +
            "  BusIndicators flashes amber lamps on all four corners, Q and E\n" +
            "  SignJudge scores every stop sign and signal, no route needed\n" +
            "  InfractionConsequences flashes faults and governs the bus\n" +
            "  Turn physicsConsequences OFF on the car when collecting runs for\n" +
            "  analysis, so every driver takes the same test in the same vehicle\n" +
            "Save the scene (Ctrl+S).");
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

        return text;
    }
}
