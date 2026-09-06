using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// Top-down camera that renders the world into the HUD minimap.
///
/// Orthographic and looking straight down, it follows the player in X and Z at a
/// fixed height. It renders into a RenderTexture rather than to the screen, which
/// is why it can coexist with the gameplay camera.
///
/// It does not need registering with Cesium: it looks at the same patch of ground
/// the main camera does, so the tiles are already streamed and loaded by the time
/// this renders them.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MinimapCamera : MonoBehaviour
{
    [Header("Following")]
    [Tooltip("What the map centres on. Left empty, it uses the main camera — which " +
             "works whether you are driving or on foot.")]
    public Transform followTarget;

    [Tooltip("What the arrow points along. Usually PlayerCar, so the arrow shows the " +
             "vehicle's heading rather than the camera's.")]
    public Transform headingSource;

    [Tooltip("Height above the target. Only needs to clear the tallest thing nearby.")]
    public float height = 120f;

    [Header("View")]
    [Tooltip("How many metres from the centre to the edge of the map.")]
    public float viewRadius = 60f;

    [Tooltip("Turn the map with the vehicle instead of keeping north up. " +
             "North-up is easier to read; rotating is more immersive.")]
    public bool rotateWithHeading = false;

    [Header("Output")]
    [Tooltip("Square resolution of the map texture. 256 is plenty at HUD size.")]
    public int textureSize = 256;

    [Header("References")]
    public RawImage display;
    public RectTransform arrow;

    Camera cam;
    RenderTexture texture;

    void Awake()
    {
        cam = GetComponent<Camera>();

        cam.orthographic = true;
        cam.orthographicSize = viewRadius;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = height + 400f;

        // Created at runtime rather than as an asset — nothing to manage in the
        // project, and it is released cleanly when the scene ends.
        texture = new RenderTexture(textureSize, textureSize, 16);
        texture.name = "MinimapTexture";
        texture.Create();
        cam.targetTexture = texture;

        // Clear it immediately. An unrendered RenderTexture holds uninitialised
        // memory, which is what shows as a blank white disc.
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = texture;
        GL.Clear(true, true, cam.backgroundColor);
        RenderTexture.active = previous;

        if (display != null)
        {
            display.texture = texture;
            display.color = Color.white;
        }
        else
        {
            Debug.LogError("[MinimapCamera] No RawImage assigned to Display — " +
                           "the map has nowhere to draw. Re-run NCAT → Set Up Minimap.");
        }
    }

    void Start()
    {
        // Something else in the scene may have switched this off. CarFollowCamera
        // disables every other camera on Play to stop Cesium's DynamicCamera
        // stealing the view, and older builds of it did not spare this one.
        if (!gameObject.activeInHierarchy)
        {
            Debug.LogError("[MinimapCamera] This camera's GameObject was disabled, so the " +
                           "map renders nothing. Recompile — CarFollowCamera should skip it.");
            return;
        }

        if (cam != null && !cam.enabled)
        {
            cam.enabled = true;
            Debug.LogWarning("[MinimapCamera] Camera component was disabled — re-enabled it.");
        }

        Debug.Log("[MinimapCamera] Rendering at " + textureSize + "px, " +
                  viewRadius + "m radius, following " +
                  (followTarget != null ? followTarget.name : "the main camera") + ".");

        RegisterWithCesium();
    }

    /// <summary>
    /// Tells Cesium to stream tiles for this camera as well as the gameplay one.
    ///
    /// Cesium culls and unloads tiles outside the frusta of the cameras it knows
    /// about. Unregistered, the map only ever shows ground the gameplay view has
    /// already asked for — which is exactly why everything behind you is black until
    /// you turn around and the main camera pulls those tiles back in.
    ///
    /// Done by reflection so this file never references the Cesium assembly and the
    /// project still builds without it.
    /// </summary>
    void RegisterWithCesium()
    {
        System.Type managerType = FindType("CesiumForUnity.CesiumCameraManager");
        if (managerType == null)
        {
            Debug.Log("[MinimapCamera] No Cesium in this project — nothing to register with.");
            return;
        }

        Object manager = FindObjectOfType(managerType);

        if (manager == null)
        {
            MethodInfo getOrCreate = managerType.GetMethod(
                "GetOrCreate", BindingFlags.Public | BindingFlags.Static);

            if (getOrCreate != null)
            {
                ParameterInfo[] parameters = getOrCreate.GetParameters();
                object[] args = parameters.Length == 1
                    ? new object[] { gameObject.scene }
                    : new object[0];

                manager = getOrCreate.Invoke(null, args) as Object;
            }
        }

        if (manager == null)
        {
            Debug.LogWarning("[MinimapCamera] Could not find or create a CesiumCameraManager. " +
                             "Add one to the scene and put this camera in its Additional Cameras list.");
            return;
        }

        PropertyInfo camerasProperty = managerType.GetProperty("additionalCameras");
        if (camerasProperty == null)
        {
            DumpMembers(managerType);
            return;
        }

        List<Camera> cameras = camerasProperty.GetValue(manager) as List<Camera>;
        if (cameras == null)
        {
            Debug.LogWarning("[MinimapCamera] additionalCameras was not a List<Camera> — " +
                             "add this camera to it by hand in the Inspector.");
            return;
        }

        if (!cameras.Contains(cam)) cameras.Add(cam);

        Debug.Log("[MinimapCamera] Registered with Cesium — it now streams tiles for the " +
                  "map view too, so the area behind you stays loaded.");
    }

    static System.Type FindType(string fullName)
    {
        foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            System.Type type = assembly.GetType(fullName);
            if (type != null) return type;
        }

        return null;
    }

    /// <summary>Prints what the Cesium API actually exposes, if the expected name is gone.</summary>
    static void DumpMembers(System.Type type)
    {
        string names = "";
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            names += "\n    " + property.PropertyType.Name + " " + property.Name;

        Debug.LogWarning("[MinimapCamera] No 'additionalCameras' property on CesiumCameraManager. " +
                         "What it does have:" + names);
    }

    void LateUpdate()
    {
        Transform target = followTarget;

        if (target == null)
        {
            if (Camera.main == null) return;
            target = Camera.main.transform;
            followTarget = target;
        }

        transform.position = target.position + Vector3.up * height;

        float mapYaw = rotateWithHeading && headingSource != null
            ? headingSource.eulerAngles.y
            : 0f;

        transform.rotation = Quaternion.Euler(90f, mapYaw, 0f);

        UpdateArrow(mapYaw);

        if (cam != null && !Mathf.Approximately(cam.orthographicSize, viewRadius))
            cam.orthographicSize = viewRadius;      // so zoom is tunable while playing
    }

    /// <summary>
    /// Points the arrow along the vehicle's heading, relative to however the map
    /// itself is oriented. In rotating mode the two cancel out and the arrow sits
    /// still, which is correct — the world turns around you instead.
    /// </summary>
    void UpdateArrow(float mapYaw)
    {
        if (arrow == null) return;

        Transform heading = headingSource != null ? headingSource : followTarget;
        if (heading == null) return;

        float relative = heading.eulerAngles.y - mapYaw;
        arrow.localRotation = Quaternion.Euler(0f, 0f, -relative);
    }

    public void SetZoom(float radius)
    {
        viewRadius = Mathf.Clamp(radius, 10f, 500f);
    }

    void OnDestroy()
    {
        if (cam != null) cam.targetTexture = null;

        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
        }
    }
}
