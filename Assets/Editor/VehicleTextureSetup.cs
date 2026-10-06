using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Wires a PBR texture set onto the attached vehicle's materials.
///
/// Models that ship textures as loose files — rather than embedded in the FBX —
/// often import with the maps unassigned, because the FBX only names them and the
/// importer does not always resolve those names.
///
/// The awkward one is metallic and roughness. They arrive as two separate greyscale
/// maps, but URP Lit expects a single Metallic Smoothness map: metallic in the red
/// channel, smoothness (the inverse of roughness) in alpha. This builds that
/// combined map rather than leaving you to do it in an image editor.
/// </summary>
public static class VehicleTextureSetup
{
    const string CarName = "PlayerCar";
    const string ModelChildName = "Model";

    [MenuItem("NCAT/Wire Vehicle Textures", false, 32)]
    public static void WireTextures()
    {
        GameObject car = GameObject.Find(CarName);
        Transform model = car != null ? car.transform.Find(ModelChildName) : null;

        if (model == null)
        {
            Debug.LogError("No " + ModelChildName + " under " + CarName +
                           ". Run NCAT → Attach Vehicle Model first.");
            return;
        }

        Object source = PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject);
        string modelPath = source != null ? AssetDatabase.GetAssetPath(source) : null;

        if (string.IsNullOrEmpty(modelPath))
        {
            Debug.LogError("Could not trace the model back to its source file.");
            return;
        }

        string folder = Path.GetDirectoryName(modelPath).Replace('\\', '/');

        Texture2D baseColour = FindTexture(folder, "base_color", "basecolor", "albedo", "diffuse");
        Texture2D normal = FindTexture(folder, "normal");
        Texture2D metallic = FindTexture(folder, "metallic", "metalness");
        Texture2D roughness = FindTexture(folder, "roughness");
        Texture2D occlusion = FindTexture(folder, "ambient_occlusion", "occlusion", "_ao");
        Texture2D emissive = FindTexture(folder, "emissive", "emission");

        if (baseColour == null && normal == null && metallic == null)
        {
            Debug.LogError("Found no PBR textures beside " + Path.GetFileName(modelPath) + ".");
            return;
        }

        // Linear data must not be treated as colour, or the lighting is wrong.
        SetLinear(normal, asNormalMap: true);
        SetLinear(metallic, false);
        SetLinear(roughness, false);
        SetLinear(occlusion, false);

        Texture2D metallicSmoothness = null;
        if (metallic != null || roughness != null)
            metallicSmoothness = BuildMetallicSmoothness(folder, metallic, roughness);

        int wired = 0;

        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || !AssetDatabase.Contains(material)) continue;

                Assign(material, "_BaseMap", baseColour);
                Assign(material, "_MainTex", baseColour);

                if (normal != null && material.HasProperty("_BumpMap"))
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }

                if (metallicSmoothness != null && material.HasProperty("_MetallicGlossMap"))
                {
                    material.SetTexture("_MetallicGlossMap", metallicSmoothness);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");

                    // With maps driving these, the scalars act as multipliers.
                    if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 1f);
                    if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1f);
                    if (material.HasProperty("_SmoothnessTextureChannel"))
                        material.SetFloat("_SmoothnessTextureChannel", 0f);   // metallic alpha
                }

                if (occlusion != null && material.HasProperty("_OcclusionMap"))
                {
                    material.SetTexture("_OcclusionMap", occlusion);
                    material.EnableKeyword("_OCCLUSIONMAP");
                }

                if (emissive != null && material.HasProperty("_EmissionMap"))
                {
                    material.SetTexture("_EmissionMap", emissive);
                    material.SetColor("_EmissionColor", Color.white);
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }

                // A tinted base colour would darken the texture underneath it.
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

                EditorUtility.SetDirty(material);
                wired++;
            }
        }

        AssetDatabase.SaveAssets();

        Debug.Log(
            "Wired textures onto " + wired + " material(s):" +
            Report("base colour", baseColour) +
            Report("normal", normal) +
            Report("metallic+smoothness", metallicSmoothness) +
            Report("occlusion", occlusion) +
            Report("emissive", emissive));
    }

    // ------------------------------------------------------------------ helpers

    static string Report(string label, Texture2D texture)
    {
        return "\n  " + label + ": " + (texture != null ? texture.name : "not found");
    }

    static void Assign(Material material, string property, Texture2D texture)
    {
        if (texture != null && material.HasProperty(property)) material.SetTexture(property, texture);
    }

    static Texture2D FindTexture(string folder, params string[] keywords)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

            foreach (string keyword in keywords)
            {
                if (name.Contains(keyword)) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
        }

        return null;
    }

    static void SetLinear(Texture2D texture, bool asNormalMap)
    {
        if (texture == null) return;

        string path = AssetDatabase.GetAssetPath(texture);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        bool changed = false;

        if (asNormalMap && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            changed = true;
        }
        else if (!asNormalMap && importer.sRGBTexture)
        {
            importer.sRGBTexture = false;
            changed = true;
        }

        if (changed) importer.SaveAndReimport();
    }

    /// <summary>
    /// Packs metallic into red and inverted roughness into alpha, which is the
    /// layout URP Lit reads. Saved beside the source textures as a new asset.
    /// </summary>
    static Texture2D BuildMetallicSmoothness(string folder, Texture2D metallic, Texture2D roughness)
    {
        string path = folder + "/MetallicSmoothness_generated.png";

        Texture2D reference = metallic != null ? metallic : roughness;
        int width = reference.width;
        int height = reference.height;

        Color[] metallicPixels = ReadPixels(metallic, width, height, Color.black);
        Color[] roughnessPixels = ReadPixels(roughness, width, height, new Color(0.5f, 0.5f, 0.5f));

        Texture2D combined = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        Color[] output = new Color[width * height];

        for (int i = 0; i < output.Length; i++)
        {
            float m = metallicPixels[i].r;
            float smoothness = 1f - roughnessPixels[i].r;     // the inversion URP expects
            output[i] = new Color(m, m, m, smoothness);
        }

        combined.SetPixels(output);
        combined.Apply();

        File.WriteAllBytes(path, combined.EncodeToPNG());
        Object.DestroyImmediate(combined);

        AssetDatabase.ImportAsset(path);

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.sRGBTexture = false;             // this is data, not colour
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>Reads a texture regardless of its import settings, then restores them.</summary>
    static Color[] ReadPixels(Texture2D texture, int width, int height, Color fallback)
    {
        Color[] pixels = new Color[width * height];

        if (texture == null)
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fallback;
            return pixels;
        }

        string path = AssetDatabase.GetAssetPath(texture);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        bool wasReadable = importer != null && importer.isReadable;
        TextureImporterType wasType = importer != null ? importer.textureType : TextureImporterType.Default;

        if (importer != null && (!wasReadable || wasType == TextureImporterType.NormalMap))
        {
            importer.isReadable = true;
            importer.textureType = TextureImporterType.Default;
            importer.SaveAndReimport();
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        Color[] source = texture.GetPixels();

        if (texture.width == width && texture.height == height)
        {
            pixels = source;
        }
        else
        {
            // Nearest-neighbour resample, so mismatched map sizes still combine.
            for (int y = 0; y < height; y++)
            {
                int sy = y * texture.height / height;
                for (int x = 0; x < width; x++)
                {
                    int sx = x * texture.width / width;
                    pixels[y * width + x] = source[sy * texture.width + sx];
                }
            }
        }

        if (importer != null && (!wasReadable || wasType == TextureImporterType.NormalMap))
        {
            importer.isReadable = wasReadable;
            importer.textureType = wasType;
            importer.SaveAndReimport();
        }

        return pixels;
    }
}
