using UnityEngine;
using UnityEngine.UI;
using System.Reflection;

/// <summary>
/// Holds the player on a loading screen until the campus has actually streamed in.
///
/// The tiles cannot be preloaded before Play. Cesium for Unity has no persistent
/// disk cache — unlike Cesium for Unreal — so every session fetches the tiles
/// again over the network. There is nothing sitting on disk to warm up.
///
/// What can be fixed is the experience. Instead of dropping you into a half-built
/// world and letting it pop in around you, this waits until the view is largely
/// loaded and only then hands over control. The same wait also gives
/// CarGroundSpawner properly refined tiles to seat the vehicle on, rather than the
/// coarse first-pass geometry that used to leave it hovering or falling.
///
/// Progress comes from Cesium3DTileset.ComputeLoadProgress(), which estimates the
/// percentage of tiles needed for the current view that have arrived. It is read
/// by reflection so this file never references the Cesium assembly.
/// </summary>
public class CesiumLoadingScreen : MonoBehaviour
{
    [Header("When to let the player drive")]
    [Tooltip("Percentage of the view that must be loaded. 100 is a bad idea — the " +
             "last few tiles can take a while and are usually far away.")]
    [Range(50f, 100f)] public float requiredProgress = 92f;

    [Tooltip("Never dismiss before this many seconds. Stops the screen flashing on " +
             "and off when the first progress reading is optimistic.")]
    public float minimumSeconds = 2f;

    [Tooltip("Give up waiting after this long and let them drive anyway. A slow or " +
             "failed connection must not lock the player out of the simulation.")]
    public float timeoutSeconds = 45f;

    [Header("References")]
    public CanvasGroup group;
    public Image progressBar;
    public Text percentLabel;
    public Text statusLabel;

    [Header("Behaviour")]
    [Tooltip("Disable driving while loading, so you cannot drive into tiles that " +
             "have not arrived yet.")]
    public bool freezeVehicle = true;

    [Tooltip("Seconds to fade the screen out once loading is done.")]
    public float fadeSeconds = 0.6f;

    Component tileset;
    MethodInfo computeProgress;

    VehicleController vehicle;
    bool vehicleWasEnabled;

    float elapsed;
    float smoothedProgress;
    bool finished;
    float fadeRemaining;

    void Start()
    {
        FindTileset();

        if (freezeVehicle)
        {
            vehicle = FindObjectOfType<VehicleController>();
            if (vehicle != null)
            {
                vehicleWasEnabled = vehicle.enabled;
                vehicle.enabled = false;
            }
        }

        if (group != null) group.alpha = 1f;

        if (tileset == null)
        {
            Debug.LogWarning("[LoadingScreen] No Cesium3DTileset found — dismissing " +
                             "immediately, since there is nothing to wait for.");
            Finish();
        }
    }

    void FindTileset()
    {
        foreach (MonoBehaviour mb in FindObjectsOfType<MonoBehaviour>())
        {
            if (mb == null || mb.GetType().Name != "Cesium3DTileset") continue;

            tileset = mb;
            computeProgress = mb.GetType().GetMethod(
                "ComputeLoadProgress", BindingFlags.Public | BindingFlags.Instance);

            if (computeProgress == null)
            {
                Debug.LogWarning("[LoadingScreen] Cesium3DTileset has no " +
                                 "ComputeLoadProgress() in this version — the screen " +
                                 "will fall back to a fixed wait.");
            }

            return;
        }
    }

    void Update()
    {
        if (finished)
        {
            FadeOut();
            return;
        }

        elapsed += Time.deltaTime;

        float progress = ReadProgress();

        // Ease the bar. Cesium's estimate jumps around as tiles are discovered and
        // requested, and a bar that goes backwards reads as a bug.
        smoothedProgress = Mathf.Max(smoothedProgress,
                                     Mathf.Lerp(smoothedProgress, progress, 3f * Time.deltaTime));

        UpdateDisplay(smoothedProgress);

        bool loaded = progress >= requiredProgress && elapsed >= minimumSeconds;
        bool timedOut = elapsed >= timeoutSeconds;

        if (timedOut && !loaded)
            Debug.LogWarning("[LoadingScreen] Gave up after " + timeoutSeconds +
                             "s at " + Mathf.RoundToInt(progress) + "% — check the " +
                             "network, or the Cesium ion token.");

        if (loaded || timedOut) Finish();
    }

    float ReadProgress()
    {
        if (tileset == null || computeProgress == null)
        {
            // No API to ask, so fall back on time alone.
            return Mathf.Clamp01(elapsed / Mathf.Max(1f, minimumSeconds * 3f)) * 100f;
        }

        object value = computeProgress.Invoke(tileset, null);
        return value is float ? (float)value : 0f;
    }

    void UpdateDisplay(float progress)
    {
        if (progressBar != null) progressBar.fillAmount = Mathf.Clamp01(progress / 100f);
        if (percentLabel != null) percentLabel.text = Mathf.RoundToInt(progress) + "%";

        if (statusLabel == null) return;

        if (progress < 25f) statusLabel.text = "Connecting to Cesium ion";
        else if (progress < 70f) statusLabel.text = "Streaming campus terrain";
        else statusLabel.text = "Building collision meshes";
    }

    void Finish()
    {
        if (finished) return;

        finished = true;
        fadeRemaining = fadeSeconds;

        if (vehicle != null) vehicle.enabled = vehicleWasEnabled;

        Debug.Log("[LoadingScreen] Campus ready after " + elapsed.ToString("F1") + "s.");
    }

    void FadeOut()
    {
        if (group == null)
        {
            gameObject.SetActive(false);
            return;
        }

        fadeRemaining -= Time.deltaTime;
        group.alpha = Mathf.Clamp01(fadeRemaining / Mathf.Max(0.01f, fadeSeconds));

        if (fadeRemaining <= 0f) gameObject.SetActive(false);
    }
}
