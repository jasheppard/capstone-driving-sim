using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Builds a mast arm traffic signal: a pole on the kerb with an arm reaching out over
/// the road and the signal head hanging from it.
///
/// WHY NOT THE EXISTING ONE
///
/// The old TrafficLight prefab comes out of a downloaded props FBX. It is a pedestal
/// signal: a short post with a head on top, standing at the kerb. That is a real thing
/// but it is the wrong thing here, because it is only visible once you are nearly on
/// top of it. A driver has to read a signal from the far side of the junction, early
/// enough to decide whether to stop, and the whole reason American junctions use mast
/// arms is to put the head out over the lane the driver is already looking down.
///
/// The arm cannot be added to the FBX — that geometry is fixed and not ours to
/// reshape — so this builds the whole thing from primitives instead, the same way the
/// stop sign and the shuttle stop are built. Nothing to download, every dimension in
/// one place.
///
/// DIMENSIONS
///
/// Taken from the US standard rather than invented. The head sits about 5 m above the
/// road, which is inside the 4.6 m minimum and the 5.8 m maximum that MUTCD allows for
/// a signal over a roadway, and the lenses are a little larger than the real 12 inch
/// ones so they read from further away. Everything else follows from those.
///
/// WHICH WAY THE ARM POINTS
///
/// The placer stands signs 4.5 m to the RIGHT of the route and turns them so their +z
/// faces the oncoming driver. With +z toward the driver and +y up, the model's +x is
/// the driver's LEFT, which is where the road is. So the arm runs along +x and the
/// head finishes up over the lane rather than over the verge the pole is standing in.
/// </summary>
public static class TrafficSignalBuilder
{
    const string PrefabFolder = "Assets/Prefabs/Signs";

    const float PoleHeight = 7.0f;
    const float PoleWidth = 0.17f;

    const float ArmHeight = 6.55f;      // where the arm leaves the pole
    const float ArmLength = 11.0f;
    const float ArmWidth = 0.16f;       // thicker, because it is now twice the span

    /// <summary>
    /// How far along the arm the head hangs.
    ///
    /// This is the number to change if the head ends up over the wrong lane. The
    /// placer stands the pole 4.5 m right of the route, so the head finishes up
    /// (HeadOffset - 4.5) m left of the centreline: at 8.6 that is 4.1 m, which puts
    /// it beyond the near lane and over the middle of the road, the way a real mast
    /// arm spans a junction. Pull it back to about 6.5 to hang it over the driver's
    /// own lane instead.
    /// </summary>
    const float HeadOffset = 8.6f;      // along the arm, from the pole
    const float HeadCentre = 5.62f;     // height of the middle of the housing
    const float HeadWidth = 0.56f;
    const float HeadTall = 1.46f;
    const float HeadDeep = 0.30f;

    const float LampSpacing = 0.46f;
    const float LampRadius = 0.17f;

    [MenuItem("NCAT/Build Mast Arm Traffic Signal", false, 82)]
    public static void Build()
    {
        Directory.CreateDirectory(PrefabFolder);

        Material metal = MakeMaterial("MastArmMetalMaterial", new Color(0.58f, 0.60f, 0.62f));
        Material housing = MakeMaterial("MastArmHousingMaterial", new Color(0.09f, 0.10f, 0.10f));
        Material backplate = MakeMaterial("MastArmBackplateMaterial", new Color(0.06f, 0.06f, 0.07f));

        GameObject root = new GameObject("TrafficLightMast");

        // ---- foundation and pole -----------------------------------------------
        Box(root, "Base", metal, new Vector3(0f, 0.22f, 0f),
            new Vector3(PoleWidth * 2.1f, 0.44f, PoleWidth * 2.1f));

        // A cylinder is two units tall, so its Y scale is the half height.
        Cylinder(root, "Pole", metal,
                 new Vector3(0f, PoleHeight * 0.5f, 0f),
                 Quaternion.identity,
                 new Vector3(PoleWidth, PoleHeight * 0.5f, PoleWidth));

        // ---- mast arm ------------------------------------------------------------
        // Laid along +x by rotating the cylinder 90 degrees about Z, and centred half
        // way out so it spans the pole to its tip.
        Cylinder(root, "Arm", metal,
                 new Vector3(ArmLength * 0.5f, ArmHeight, 0f),
                 Quaternion.Euler(0f, 0f, 90f),
                 new Vector3(ArmWidth, ArmLength * 0.5f, ArmWidth));

        // The gusset where the arm meets the pole. Small, but without it the arm looks
        // welded on at a right angle, which is the one detail that reads as unfinished.
        // Scaled with the arm. A gusset sized for a 5 m arm looks like a tab stuck
        // on the side of an 11 m one, and a longer arm genuinely needs more of it.
        Box(root, "Gusset", metal,
            new Vector3(0.62f, ArmHeight - 0.44f, 0f),
            new Vector3(1.25f, 0.90f, 0.12f));

        // ---- signal head -----------------------------------------------------------
        Box(root, "Hanger", metal,
            new Vector3(HeadOffset, ArmHeight - 0.28f, 0f),
            new Vector3(0.09f, 0.56f, 0.09f));

        // Behind the head, from the driver's side, so the lenses read against
        // something dark instead of against the sky. Real junctions use these for
        // exactly that reason.
        Box(root, "Backplate", backplate,
            new Vector3(HeadOffset, HeadCentre, -0.17f),
            new Vector3(HeadWidth * 1.75f, HeadTall * 1.26f, 0.04f));

        Box(root, "Housing", housing,
            new Vector3(HeadOffset, HeadCentre, 0f),
            new Vector3(HeadWidth, HeadTall, HeadDeep));

        // Hoods over each lens, so the lamps still read in direct sun rather than
        // washing out into the housing.
        for (int i = 0; i < 3; i++)
        {
            float y = HeadCentre - LampSpacing + LampSpacing * i;

            Box(root, "Visor" + i, housing,
                new Vector3(HeadOffset, y + LampRadius * 1.05f, 0.17f),
                new Vector3(HeadWidth * 0.95f, 0.05f, 0.22f));
        }

        // ---- the lamps -------------------------------------------------------------
        // Added last and told where to put itself. AddComponent runs OnEnable straight
        // away with the default lamp position still in place, so the settings are
        // written first and then it is asked to rebuild.
        TrafficLightVisual visual = root.AddComponent<TrafficLightVisual>();

        visual.lampOrigin = new Vector3(HeadOffset, HeadCentre - LampSpacing, 0.2f);
        visual.lampSpacing = LampSpacing;
        visual.lampRadius = LampRadius;
        visual.glowRange = 14f;
        visual.Rebuild();

        string path = PrefabFolder + "/TrafficLightMast.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Debug.Log(
            "Mast arm traffic signal built.\n" +
            "  " + path + "\n" +
            "  " + PoleHeight.ToString("F1") + " m pole, " + ArmLength.ToString("F1") +
            " m arm, head " + (HeadCentre - HeadTall * 0.5f).ToString("F1") +
            " m above the road\n" +
            "  Lenses are " + (LampRadius * 2f * 39.37f).ToString("F0") +
            " inches, a little over the real 12, so they read from across a junction\n" +
            "  Backplate behind the head so the lamps do not wash out against sky\n" +
            "  No colliders: a signal the bus can hit would be scored as a collision\n\n" +
            "  The arm reaches out over the road on the driver's left, because the\n" +
            "  placer stands signs on the right. If yours ends up over the verge,\n" +
            "  rotate the placed object 180 degrees\n\n" +
            "To use it instead of the old pedestal signal, rename this prefab to\n" +
            "\"TrafficLight\" (and rename the old one to something else), or change\n" +
            "TrafficLightPrefab in SignPlacer.cs.");
    }

    // -------------------------------------------------------------------- geometry

    static GameObject Box(GameObject parent, string name, Material material,
                          Vector3 position, Vector3 size)
    {
        return Shape(PrimitiveType.Cube, parent, name, material, position,
                     Quaternion.identity, size);
    }

    static GameObject Cylinder(GameObject parent, string name, Material material,
                               Vector3 position, Quaternion rotation, Vector3 size)
    {
        return Shape(PrimitiveType.Cylinder, parent, name, material, position, rotation, size);
    }

    static GameObject Shape(PrimitiveType type, GameObject parent, string name,
                            Material material, Vector3 position, Quaternion rotation,
                            Vector3 size)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;

        // Signals stand off the kerb where nobody should be driving, so a collider on
        // one could only ever fail somebody unfairly.
        Object.DestroyImmediate(go.GetComponent<Collider>());

        return go;
    }

    static Material MakeMaterial(string name, Color tint)
    {
        string path = PrefabFolder + "/" + name + ".mat";

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        Material material = existing != null ? existing : new Material(shader);

        material.color = tint;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.35f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.35f);

        if (existing == null) AssetDatabase.CreateAsset(material, path);
        else EditorUtility.SetDirty(material);

        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }
}
