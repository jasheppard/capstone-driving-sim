using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Camera for the vehicle. Two modes:
///
///   FirstPerson — locked to the driver's seat, no smoothing, so the view turns
///                 exactly with the car.
///
///   ThirdPerson — a real orbit around the car rather than a fixed offset. The
///                 right stick swings the camera around it, and when you let go
///                 it eases back behind the car so you are facing where you drive.
///
/// C or Triangle switches. Right stick or right-mouse-drag orbits.
/// </summary>
public class CarFollowCamera : MonoBehaviour
{
    public enum ViewMode { FirstPerson, ThirdPerson }

    [Header("Target")]
    public Transform target;

    [Header("Mode")]
    public ViewMode mode = ViewMode.FirstPerson;
    public KeyCode toggleKey = KeyCode.C;

    [Tooltip("Hide the car's mesh while in first person so you aren't inside it.")]
    public bool hideCarInFirstPerson = true;

    [Tooltip("On Play, switch off every other camera and claim the MainCamera tag. " +
             "Stops Cesium's DynamicCamera stealing the view and flying around on your gamepad.")]
    public bool takeExclusiveControl = true;

    [Header("First person")]
    [Tooltip("Driver's eye position in the car's local space: x right, y up, z forward.")]
    public Vector3 eyeOffset = new Vector3(-0.4f, 0.35f, 0.6f);

    [Header("Third person orbit")]
    [Tooltip("Distance from the car. Larger shows more of the road ahead.")]
    public float orbitDistance = 6f;

    [Tooltip("Height on the car the camera orbits around. Low values sit level with the boot.")]
    public float pivotHeight = 1.1f;

    [Tooltip("Downward tilt when you are not looking around. Small values look along the road.")]
    public float restPitch = 6f;

    public float orbitMinPitch = -12f;
    public float orbitMaxPitch = 55f;

    [Tooltip("Ease the camera back behind the car after you stop orbiting. " +
             "Off means it stays wherever you leave it, so you can hold a side or front view.")]
    public bool autoRecentre = false;
    public float recentreSpeed = 3f;

    [Tooltip("How quickly the camera swings to a new orbit angle. Only affects rotation " +
             "around the car — the camera tracks the car's position exactly, with no lag.")]
    public float orbitSmoothing = 12f;

    [Header("Look")]
    public bool allowLookAround = true;

    [Tooltip("Restrict free look to first person. Leave off for orbit in both modes.")]
    public bool firstPersonLookOnly = false;

    public float lookSpeed = 150f;
    public float maxPitch = 45f;
    public float stickDeadzone = 0.2f;

    float lookYaw;      // offset from the car's heading
    float lookPitch;
    Vector3 smoothedOffset;
    bool offsetInitialised;
    Renderer[] carRenderers;
    ViewMode appliedMode;

    void Start()
    {
        if (takeExclusiveControl) ClaimTheView();

        lookPitch = restPitch;
        offsetInitialised = false;
        CacheRenderers();
        appliedMode = mode;
        ApplyRendererVisibility();
    }

    /// <summary>
    /// Makes this the only camera in play. Otherwise another camera renders on top,
    /// and Cesium's CesiumCameraController keeps reading the gamepad — which is why
    /// the triggers used to fly the view up and down instead of driving.
    /// Runtime only; stopping Play restores everything.
    /// </summary>
    void ClaimTheView()
    {
        int disabled = 0;

        foreach (Camera other in UnityEngine.Object.FindObjectsOfType<Camera>())
        {
            if (other == null || other.gameObject == gameObject) continue;
            if (!other.gameObject.activeInHierarchy) continue;

            // The minimap renders to a texture, not to the screen, so it is not
            // competing for the view and must survive this.
            if (other.GetComponent<MinimapCamera>() != null) continue;

            other.gameObject.SetActive(false);
            disabled++;
            Debug.Log("[CarFollowCamera] Disabled \"" + other.name +
                      "\" for this play session — it was competing for the view and for input.");
        }

        gameObject.tag = "MainCamera";      // Cesium streams tiles for this tag in Play mode

        if (disabled == 0) Debug.Log("[CarFollowCamera] This is the only camera. Good.");
    }

    void LateUpdate()
    {
        if (target == null) return;

        HandleToggle();
        UpdateLookAround();

        if (mode == ViewMode.FirstPerson) DriveFirstPerson();
        else DriveThirdPerson();
    }

    void HandleToggle()
    {
        bool pressed = Input.GetKeyDown(toggleKey);
#if ENABLE_INPUT_SYSTEM
        Gamepad pad = Gamepad.current;
        if (pad != null && pad.dpad.up.wasPressedThisFrame) pressed = true;       // D-pad Up
#endif
        if (!pressed) return;

        mode = mode == ViewMode.FirstPerson ? ViewMode.ThirdPerson : ViewMode.FirstPerson;
        lookYaw = 0f;
        lookPitch = mode == ViewMode.ThirdPerson ? restPitch : 0f;

        appliedMode = mode;
        ApplyRendererVisibility();
        Debug.Log("[CarFollowCamera] View: " + mode);
    }

    // ------------------------------------------------------------------ modes

    void DriveFirstPerson()
    {
        // Hard lock — any smoothing here reads as the car sliding relative to the view.
        transform.position = target.TransformPoint(eyeOffset);
        transform.rotation = target.rotation * Quaternion.Euler(lookPitch, lookYaw, 0f);
    }

    void DriveThirdPerson()
    {
        // Base yaw follows the car, so "no input" always means looking where you drive.
        float yaw = target.eulerAngles.y + lookYaw;
        Quaternion rotation = Quaternion.Euler(lookPitch, yaw, 0f);

        // The pivot is taken from the car every frame with no smoothing at all.
        // Smoothing the camera's world position instead is what made the car outrun
        // the camera: at 30 mph even a tenth of a second of lag is nearly two metres,
        // which reads as the car sliding out of frame when you watch from the side.
        Vector3 pivot = target.position + Vector3.up * pivotHeight;
        Vector3 targetOffset = rotation * Vector3.back * orbitDistance;

        if (!offsetInitialised || orbitSmoothing <= 0f)
        {
            smoothedOffset = targetOffset;
            offsetInitialised = true;
        }
        else
        {
            // Only the direction of the orbit eases. Translation is rigid.
            smoothedOffset = Vector3.Lerp(
                smoothedOffset, targetOffset,
                1f - Mathf.Exp(-orbitSmoothing * Time.deltaTime));
        }

        Vector3 desired = pivot + smoothedOffset;

        // Pull in if a building comes between the camera and the car.
        Vector3 toCamera = desired - pivot;
        if (Physics.Raycast(pivot, toCamera.normalized, out RaycastHit hit,
                            toCamera.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(target))
                desired = hit.point - toCamera.normalized * 0.25f;
        }

        transform.position = desired;
        transform.rotation = rotation;
    }

    // ------------------------------------------------------------------- look

    void UpdateLookAround()
    {
        bool lookEnabled = allowLookAround &&
                           (mode == ViewMode.FirstPerson || !firstPersonLookOnly);

        float x = 0f;
        float y = 0f;
        bool looking = false;

        if (lookEnabled)
        {
#if ENABLE_INPUT_SYSTEM
            Gamepad gp = Gamepad.current;
            if (gp != null)
            {
                Vector2 stick = gp.rightStick.ReadValue();
                if (stick.sqrMagnitude > stickDeadzone * stickDeadzone)
                {
                    x = stick.x;
                    y = stick.y;
                    looking = true;
                }
            }
#endif
            if (!looking && Input.GetMouseButton(1))
            {
                x = Input.GetAxis("Mouse X");
                y = Input.GetAxis("Mouse Y");
                looking = x != 0f || y != 0f;
            }
        }

        float pitchMin = mode == ViewMode.ThirdPerson ? orbitMinPitch : -maxPitch;
        float pitchMax = mode == ViewMode.ThirdPerson ? orbitMaxPitch : maxPitch;

        if (looking)
        {
            // No yaw clamp — a full 360 around the car. Wrapped to -180..180 so the
            // value cannot drift off into large numbers over a long session.
            lookYaw += x * lookSpeed * Time.deltaTime;
            lookYaw = Mathf.Repeat(lookYaw + 180f, 360f) - 180f;

            lookPitch = Mathf.Clamp(lookPitch - y * lookSpeed * Time.deltaTime, pitchMin, pitchMax);
        }
        else if (autoRecentre || mode == ViewMode.FirstPerson)
        {
            float t = 1f - Mathf.Exp(-recentreSpeed * Time.deltaTime);
            float restingPitch = mode == ViewMode.ThirdPerson ? restPitch : 0f;

            lookYaw = Mathf.LerpAngle(lookYaw, 0f, t);
            lookPitch = Mathf.Lerp(lookPitch, restingPitch, t);
        }
    }

    // -------------------------------------------------------------- car mesh

    void CacheRenderers()
    {
        carRenderers = target != null ? target.GetComponentsInChildren<Renderer>() : new Renderer[0];
    }

    void ApplyRendererVisibility()
    {
        if (!hideCarInFirstPerson || carRenderers == null) return;

        bool visible = mode != ViewMode.FirstPerson;
        for (int i = 0; i < carRenderers.Length; i++)
        {
            if (carRenderers[i] != null) carRenderers[i].enabled = visible;
        }
    }

    void OnDisable()
    {
        if (carRenderers == null) return;
        for (int i = 0; i < carRenderers.Length; i++)
        {
            if (carRenderers[i] != null) carRenderers[i].enabled = true;
        }
    }
}
