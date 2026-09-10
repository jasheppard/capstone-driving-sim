using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Builds the StopSign and TrafficLight prefabs from the downloaded models, so
/// nobody has to do the drag-into-scene, drag-into-folder, delete-from-scene dance.
///
/// WHAT IT SORTS OUT ALONG THE WAY
///
/// Three things about these particular downloads need handling, and all three are
/// silent failures if they are not:
///
/// The stop sign texture is a palette PNG whose background is transparent through a
/// tRNS chunk rather than a real alpha channel. Imported with Unity's defaults it
/// renders as a rectangle with a stop sign printed on it, so the importer settings
/// are set explicitly and the material gets alpha clipping.
///
/// Model scale from a free download is arbitrary. A sign that imports at a tenth of
/// its size is invisible from a bus, and one at ten times is a wall. Both prefabs are
/// rescaled to the real height of the thing they represent, measured from the mesh
/// rather than assumed.
///
/// The traffic light download is a ten piece props pack including a dumpster, so the
/// piece that is actually a signal has to be picked out. It is chosen by shape: tall,
/// narrow, and the tallest thing that fits that description.
/// </summary>
public static class SignPrefabBuilder
{
    const string PrefabFolder = "Assets/Prefabs/Signs";

    const string StopTexture = "Assets/Models/Signs/StopSign/StopPlate.png";
    const string LightFbx = "Assets/Models/Signs/TrafficLight/StreetProps.fbx";

    /// <summary>A US stop sign stands about 2.4 m to the top of the plate.</summary>
    const float StopSignHeight = 2.4f;

    /// <summary>A pedestal signal head on its pole, roughly.</summary>
    const float TrafficLightHeight = 3.6f;

    [MenuItem("NCAT/Build Sign Prefabs", false, 19)]
    public static void Build()
    {
        Directory.CreateDirectory(PrefabFolder);
        AssetDatabase.Refresh();

        string report = "";

        report += BuildStopSign();
        report += BuildTrafficLight();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Sign prefabs built.\n" + report +
                  "Now run NCAT > Place Stop Sign Here, or Route > 4. Place Signs to " +
                  "fill in waypoints you already made.\nSave the scene (Ctrl+S).");
    }

    // ----------------------------------------------------------------- stop sign

    /// <summary>
    /// Builds the stop sign from scratch rather than from the downloaded model.
    ///
    /// The download was abandoned on purpose. Its UVs mapped the texture sideways and
    /// squashed onto a plate that was not square, which put the legend on its side,
    /// and fixing someone else's UV layout from a script is guesswork. A stop sign is
    /// a plate on a post. Two primitives and a texture give exact control over the
    /// size, the orientation and where the origin sits, and none of it can be broken
    /// by an importer setting.
    ///
    /// The octagon is not modelled. It comes from the texture's alpha, cut out by
    /// alpha clipping, which is how road signs are normally done in games: cheaper
    /// than an eight sided mesh and pixel-exact at any distance.
    /// </summary>
    static string BuildStopSign()
    {
        // Alpha Is Transparency has to be on before the material is made, or the
        // octagon comes out as a square card.
        TextureImporter importer = AssetImporter.GetAtPath(StopTexture) as TextureImporter;

        if (importer == null)
            return "  StopSign: SKIPPED, " + StopTexture + " not found\n";

        importer.alphaIsTransparency = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(StopTexture);
        Material plateMaterial = MakeMaterial("StopSignMaterial", texture, true);
        Material postMaterial = MakeMaterial("SignPostMaterial", null, false);
        postMaterial.color = new Color(0.62f, 0.63f, 0.65f);

        GameObject root = new GameObject("StopSign");

        // ---- post ---------------------------------------------------------------
        // A Unity cylinder is two units tall, so its Y scale is the half height and
        // its centre sits at that half height to stand the base on the ground.
        GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = "Post";
        post.transform.SetParent(root.transform, false);
        post.transform.localPosition = new Vector3(0f, StopSignHeight * 0.5f, 0f);
        post.transform.localScale = new Vector3(0.06f, StopSignHeight * 0.5f, 0.06f);
        post.GetComponent<Renderer>().sharedMaterial = postMaterial;
        Strip(post);

        // ---- plate ---------------------------------------------------------------
        // A thin cube rather than a quad. Unity's quad is one sided and faces a
        // direction people argue about; a box shows the sign correctly from both
        // approaches and needs no rotation to be visible.
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "Plate";
        plate.transform.SetParent(root.transform, false);
        plate.transform.localPosition = new Vector3(0f, StopSignHeight - 0.42f, 0f);
        plate.transform.localScale = new Vector3(0.76f, 0.76f, 0.03f);
        plate.GetComponent<Renderer>().sharedMaterial = plateMaterial;
        Strip(plate);

        string path = PrefabFolder + "/StopSign.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        return "  StopSign: built from primitives, " + StopSignHeight +
               " m to the top of the plate, octagon cut from the texture alpha\n";
    }

    /// <summary>
    /// Removes the collider a Unity primitive comes with.
    ///
    /// A sign the bus can hit is a collision, and a collision is an automatic failure
    /// on the road test. Signs stand off the kerb where nobody should be driving, so
    /// the only thing a collider on one could ever do is fail somebody unfairly.
    /// </summary>
    static void Strip(GameObject go)
    {
        Collider collider = go.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);
    }

    // ------------------------------------------------------------- traffic light

    static string BuildTrafficLight()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(LightFbx);

        if (source == null)
            return "  TrafficLight: SKIPPED, " + LightFbx + " not found\n";

        GameObject pack = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(pack, PrefabUnpackMode.Completely,
                                           InteractionMode.AutomatedAction);

        // Pick the signal out of the props pack by shape. Tall and narrow, and the
        // tallest of the things that qualify. A dumpster is wide and low, a bollard
        // is short, a lamp post is tall but the signal head makes the signal wider
        // at the top, so height alone is a good enough discriminator here.
        Transform best = null;
        float bestHeight = 0f;

        foreach (Renderer renderer in pack.GetComponentsInChildren<Renderer>())
        {
            Bounds b = renderer.bounds;
            float height = b.size.y;
            float width = Mathf.Max(b.size.x, b.size.z);

            if (height < 2.5f) continue;              // too short to be a signal
            if (width > height * 0.6f) continue;      // too wide, that is a skip or a bin

            if (height > bestHeight) { bestHeight = height; best = renderer.transform; }
        }

        if (best == null)
        {
            Object.DestroyImmediate(pack);
            return "  TrafficLight: could not pick a signal out of the props pack. " +
                   "Drag " + LightFbx + " into the scene and make the prefab by hand\n";
        }

        // Lift the chosen piece out of the pack, then throw the rest away.
        GameObject signal = best.gameObject;
        signal.transform.SetParent(null, true);
        signal.name = "TrafficLight";
        Object.DestroyImmediate(pack);

        Material material = MakeMaterial(
            "TrafficLightMaterial",
            AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Models/Signs/TrafficLight/metal_street_props_Base_color.png"),
            false);

        foreach (Renderer renderer in signal.GetComponentsInChildren<Renderer>())
        {
            Material[] materials = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
        }

        float before = MeasureHeight(signal);
        Rescale(signal, before, TrafficLightHeight);

        GameObject root = Wrap(signal, "TrafficLight");

        // On the wrapper, so the lamp offsets are measured from the base of the pole
        // rather than from wherever the artist put the model's origin.
        root.AddComponent<TrafficLightVisual>();

        string path = PrefabFolder + "/TrafficLight.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        return "  TrafficLight: built from the props pack, model was " +
               before.ToString("F2") + " m tall, scaled to " + TrafficLightHeight + " m\n";
    }

    // ------------------------------------------------------------------- helpers

    static Material MakeMaterial(string name, Texture2D texture, bool cutout)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader);

        if (texture != null)
        {
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        }

        if (cutout)
        {
            // Alpha clipping rather than transparency: a sign is opaque where it
            // exists and absent where it does not, and clipping sorts correctly
            // against the terrain where blended transparency does not.
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 1f);
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
        }

        string path = PrefabFolder + "/" + name + ".mat";
        AssetDatabase.CreateAsset(material, path);

        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    static float MeasureHeight(GameObject go)
    {
        bool started = false;
        Bounds bounds = new Bounds();

        foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
        {
            if (!started) { bounds = renderer.bounds; started = true; }
            else bounds.Encapsulate(renderer.bounds);
        }

        return started ? bounds.size.y : 0f;
    }

    static void Rescale(GameObject go, float current, float wanted)
    {
        if (current <= 0.001f) return;

        float factor = wanted / current;
        go.transform.localScale = go.transform.localScale * factor;
    }

    /// <summary>
    /// Puts the model under a fresh root whose origin is at the base of the sign.
    ///
    /// A prefab is placed by its root, so where that root sits decides whether the
    /// sign stands on the road or is buried to the waist in it. Downloaded models put
    /// their origin wherever the artist happened to leave it, usually the centre of
    /// the mesh, which is exactly half a sign underground.
    ///
    /// Shifting the model inside a wrapper rather than moving the model itself is
    /// what makes it stick: the offset is stored as the child's local position and
    /// survives being saved to a prefab, whereas a root position is discarded the
    /// moment the prefab is instantiated somewhere else.
    /// </summary>
    static GameObject Wrap(GameObject model, string name)
    {
        GameObject root = new GameObject(name);
        root.transform.position = Vector3.zero;
        root.transform.rotation = Quaternion.identity;

        model.transform.SetParent(root.transform, true);

        bool started = false;
        Bounds bounds = new Bounds();

        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (!started) { bounds = renderer.bounds; started = true; }
            else bounds.Encapsulate(renderer.bounds);
        }

        if (started)
        {
            // Down by however far the lowest point is above zero, or up by however
            // far it is below.
            model.transform.position -= Vector3.up * bounds.min.y;
        }

        return root;
    }
}
