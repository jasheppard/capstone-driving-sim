using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Assigns colours and surface properties to a car model's materials.
///
/// Models exported out of Blender to FBX often carry their look entirely in
/// material properties rather than texture maps. FBX does not round-trip those
/// reliably, so every material arrives white. There is nothing to repair by
/// re-extracting textures — there were never any textures. They have to be set.
///
/// Matching is by material name, which is why this works: exporters keep the
/// author's names, and those names describe the surface ("Car paint", "Window").
/// </summary>
public static class VehiclePaintSetup
{
    const string CarName = "PlayerCar";
    const string ModelChildName = "Model";

    [MenuItem("NCAT/Paint Vehicle - Black", false, 7)]
    public static void PaintBlack() { Paint(new Color(0.045f, 0.045f, 0.055f)); }

    [MenuItem("NCAT/Paint Vehicle - Silver", false, 8)]
    public static void PaintSilver() { Paint(new Color(0.62f, 0.64f, 0.67f)); }

    [MenuItem("NCAT/Paint Vehicle - Aggie Blue", false, 9)]
    public static void PaintAggieBlue() { Paint(new Color(0.04f, 0.19f, 0.42f)); }

    [MenuItem("NCAT/Paint Vehicle - School Bus Yellow", false, 10)]
    public static void PaintBusYellow() { Paint(new Color(0.95f, 0.68f, 0.05f)); }

    static void Paint(Color bodyColour)
    {
        GameObject car = GameObject.Find(CarName);
        Transform model = car != null ? car.transform.Find(ModelChildName) : null;

        if (model == null)
        {
            Debug.LogError("No " + ModelChildName + " under " + CarName +
                           ". Run NCAT → Attach Vehicle Model first.");
            return;
        }

        int painted = 0;

        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                if (!AssetDatabase.Contains(material)) continue;   // embedded, read-only

                if (ApplyByName(material, bodyColour)) painted++;
            }
        }

        AssetDatabase.SaveAssets();

        Debug.Log(painted > 0
            ? "Painted " + painted + " material(s). Tweak any of them in Assets/.../Materials — " +
              "Base Map colour, Metallic and Smoothness are the three that matter."
            : "No editable materials found. If they are embedded in the FBX, set " +
              "Material Location to 'Use External Materials' on the model importer first.");
    }

    /// <summary>Reads the author's material name and applies a matching surface.</summary>
    static bool ApplyByName(Material material, Color bodyColour)
    {
        string name = material.name.ToLowerInvariant();

        if (name.Contains("paint") || name.Contains("body") || name.Contains("carpaint"))
        {
            SetSurface(material, bodyColour, metallic: 0.1f, smoothness: 0.88f);
        }
        else if (name.Contains("tire") || name.Contains("tyre") || name.Contains("rubber"))
        {
            SetSurface(material, new Color(0.032f, 0.032f, 0.034f), 0f, 0.16f);
        }
        else if (name.Contains("rim") || name.Contains("wheel") || name.Contains("chrome"))
        {
            SetSurface(material, new Color(0.78f, 0.79f, 0.81f), 1f, 0.85f);
        }
        else if (name.Contains("steel") || name.Contains("metal"))
        {
            SetSurface(material, new Color(0.42f, 0.43f, 0.46f), 0.95f, 0.6f);
        }
        else if (name.Contains("window") || name.Contains("glass"))
        {
            MakeTransparent(material);
            SetSurface(material, new Color(0.04f, 0.05f, 0.07f, 0.45f), 0f, 0.97f);
        }
        else if (name.Contains("light") || name.Contains("lamp") || name.Contains("head"))
        {
            SetSurface(material, new Color(0.92f, 0.91f, 0.86f), 0.2f, 0.92f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(0.6f, 0.58f, 0.5f));
        }
        else
        {
            // Interior, trim, grille, unnamed slots.
            SetSurface(material, new Color(0.13f, 0.13f, 0.14f), 0.25f, 0.35f);
        }

        EditorUtility.SetDirty(material);
        return true;
    }

    static void SetSurface(Material material, Color colour, float metallic, float smoothness)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
        if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
    }

    /// <summary>
    /// URP Lit needs several properties changed together to become transparent —
    /// setting the alpha alone does nothing.
    /// </summary>
    static void MakeTransparent(Material material)
    {
        if (!material.HasProperty("_Surface")) return;

        material.SetFloat("_Surface", 1f);          // 0 opaque, 1 transparent
        material.SetFloat("_Blend", 0f);            // alpha blend
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);

        material.SetShaderPassEnabled("ShadowCaster", false);
        material.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }
}
