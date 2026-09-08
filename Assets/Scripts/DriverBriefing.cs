using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The briefing shown before the run starts: the controls, the job, and what the
/// score actually rewards.
///
/// It waits for the loading screen to clear first. Showing it over a half-streamed
/// campus would mean the player reads it, dismisses it, and then sits watching
/// buildings pop in — the briefing has to be the last thing between them and
/// driving, not a middle step.
///
/// While it is up the game is paused outright rather than just disabling input.
/// Pausing stops the physics step, which means the telemetry logger and the scorer
/// record nothing during it. Otherwise every run would open with however long the
/// player spent reading, logged as stationary driving.
///
/// H reopens it mid-run for anyone who forgets which button ends the drive.
/// </summary>
public class DriverBriefing : MonoBehaviour
{
    [Header("Panel")]
    public GameObject panel;

    [Tooltip("Optional. Filled in with the route's stop count once the route loads.")]
    public Text routeLine;

    [Header("Behaviour")]
    [Tooltip("Show automatically at the start of a run.")]
    public bool showOnStart = true;

    [Tooltip("Key that reopens it mid-run.")]
    public KeyCode reopenKey = KeyCode.H;

    [Tooltip("Ignore input for a moment after it appears, so the keypress that " +
             "dismissed the loading screen does not dismiss this too.")]
    public float inputDelay = 0.4f;

    CesiumLoadingScreen loading;
    RouteRunner route;
    VehicleController vehicle;

    bool waitingForLoad;
    bool visible;
    float shownAt;

    void Start()
    {
        loading = FindObjectOfType<CesiumLoadingScreen>();
        route = FindObjectOfType<RouteRunner>();
        vehicle = FindObjectOfType<VehicleController>();

        if (Overlay() != null) Overlay().SetActive(false);

        if (!showOnStart) return;

        // If there is a loading screen, queue up behind it. If there is not, the
        // campus is already there and the briefing can go straight up.
        waitingForLoad = loading != null && loading.gameObject.activeInHierarchy;
        if (!waitingForLoad) Show();
    }

    void Update()
    {
        if (waitingForLoad)
        {
            if (loading == null || !loading.gameObject.activeInHierarchy)
            {
                waitingForLoad = false;
                Show();
            }

            return;
        }

        if (visible)
        {
            // Unscaled, because the game is paused while this is up.
            if (Time.unscaledTime - shownAt < inputDelay) return;
            if (DismissPressed()) Hide();
            return;
        }

        if (Input.GetKeyDown(reopenKey)) Show();
    }

    static bool DismissPressed()
    {
        if (Input.anyKeyDown) return true;

#if ENABLE_INPUT_SYSTEM
        Gamepad pad = Gamepad.current;
        if (pad != null && (pad.buttonSouth.wasPressedThisFrame ||
                            pad.startButton.wasPressedThisFrame)) return true;
#endif
        return false;
    }

    public void Show()
    {
        if (visible) return;

        visible = true;
        shownAt = Time.unscaledTime;

        UpdateRouteLine();

        if (panel != null) panel.SetActive(true);
        if (Overlay() != null) Overlay().SetActive(true);

        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void Hide()
    {
        if (!visible) return;

        visible = false;

        if (Overlay() != null) Overlay().SetActive(false);

        Time.timeScale = 1f;

        // The vehicle may still be switched off from the loading screen's freeze if
        // the briefing went up before it handed control back.
        if (vehicle != null && !vehicle.enabled) vehicle.enabled = true;
    }

    /// <summary>Fills in the one line that cannot be written until the route exists.</summary>
    void UpdateRouteLine()
    {
        if (routeLine == null) return;

        if (route == null || route.WaypointCount == 0)
        {
            routeLine.text = "No route loaded: free driving. Press Esc for your report.";
            return;
        }

        routeLine.text = "Follow the beacons through " + route.WaypointCount +
                         " waypoints to the depot.";
    }

    void OnDestroy()
    {
        // A scene change while paused would leave the next scene frozen.
        Time.timeScale = 1f;
    }

    /// <summary>
    /// What actually gets shown and hidden: the whole overlay, not just the panel.
    ///
    /// The setup used to wire `panel` to the inner panel, whose parent is a
    /// full-screen near-opaque Dim image. Hiding only the panel left that Dim
    /// sitting over the game, and two of them stacked passed under 2% of the light
    /// through, which looked like the whole game had lost brightness and contrast.
    ///
    /// Stepping up one level when the parent is not the Canvas itself repairs
    /// scenes that were already built, without anyone re-running the setup.
    /// </summary>
    GameObject Overlay()
    {
        if (panel == null) return null;

        Transform parent = panel.transform.parent;
        if (parent != null && parent.GetComponent<Canvas>() == null) return parent.gameObject;

        return panel;
    }

}
