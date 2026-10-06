using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Builds the Aggie Shuttle Stop sign: the navy and gold post sign that stands at
/// every shuttle stop on campus.
///
/// WHY IT IS PAINTED RATHER THAN MODELLED
///
/// The sign is a flat printed panel on a wooden post. Modelling that is pointless:
/// everything that makes it recognisable is the artwork, and at the distance a driver
/// sees it from, the geometry is a rectangle. So the face is painted into a texture
/// here, in code, and mapped onto a thin box. Nothing to download, nothing to lose,
/// and the colours and wording are in one place if they need changing.
///
/// Same approach the stop sign uses, so the two look like they belong to one project.
///
/// THE LETTERING
///
/// Drawn from a 5x7 bitmap font defined below rather than from a TrueType font. An
/// editor script cannot rasterise a font without dragging in a dependency, and a
/// TextMesh child would need its own shader to survive the render pipeline. A bitmap
/// font scaled up is also honestly what road signage looks like: blocky, high
/// contrast, readable at speed.
///
/// ON THE LOGO
///
/// The emblem is a deliberate approximation — a gold shield with A&T on it — not a
/// reproduction of the university's actual athletic mark, which this cannot draw and
/// should not fake. If you have the real logo as a PNG, drop it in Assets/UI as
/// ShuttleStopLogo.png and this will use it instead of the drawn shield.
/// </summary>
public static class ShuttleStopBuilder
{
    const string PrefabFolder = "Assets/Prefabs/Signs";
    const string UIFolder = "Assets/UI";
    const string FacePath = UIFolder + "/ShuttleStopFace.png";
    const string LogoPath = UIFolder + "/ShuttleStopLogo.png";

    // Measured off a real stop: the panel sits about head height on a 4x4 timber post.
    const float PostHeight = 2.35f;
    const float PostThickness = 0.065f;
    const float PlateWidth = 0.46f;
    const float PlateHeight = 0.92f;

    const int W = 256;
    const int H = 512;

    static readonly Color32 Navy = new Color32(12, 35, 73, 255);
    static readonly Color32 Gold = new Color32(255, 198, 44, 255);

    [MenuItem("NCAT/Build Shuttle Stop Sign", false, 81)]
    public static void Build()
    {
        Directory.CreateDirectory(PrefabFolder);

        Texture2D face = PaintFace();

        Material plateMaterial = MakeMaterial("ShuttleStopMaterial", face, Color.white);
        Material postMaterial = MakeMaterial("ShuttleStopPostMaterial", null,
                                             new Color(0.56f, 0.58f, 0.60f));

        // The back of the panel is plain navy. A real printed sign is blank behind,
        // and a box with the artwork on it shows that artwork mirrored on its far
        // face, which looks like a mistake because it is one.
        Material backMaterial = MakeMaterial("ShuttleStopBackMaterial", null,
                                             new Color(0.047f, 0.137f, 0.286f));

        GameObject root = new GameObject("ShuttleStop");

        // ---- post --------------------------------------------------------------
        // Square rather than round, and slim: a galvanised sign post, not a timber
        // 4x4. A cube is two units across, so the scale is the full size and the
        // centre sits at half the height to stand the base on the ground.
        Box(root, "Post", postMaterial,
            new Vector3(0f, PostHeight * 0.5f, 0f),
            new Vector3(PostThickness, PostHeight, PostThickness));

        // ---- panel ---------------------------------------------------------------
        // On the POSITIVE z side of the post, which is the whole reason the sign used
        // to come out backwards.
        //
        // Quaternion.LookRotation aims an object's +z at whatever it is facing, and
        // both the quick placer and the route placer use it to turn a sign towards the
        // driver. A panel hung on -z is therefore turned away from them: you see the
        // post, and the artwork reads mirrored behind it. Hanging it on +z is what
        // makes "face the driver" actually mean that.
        //
        // The post stands at z 0, a centimetre behind the panel and narrower than it,
        // so it is completely hidden from the front and visible from behind, which is
        // how a sign on a post looks.
        float plateCentre = PostHeight - PlateHeight * 0.5f - 0.12f;

        Box(root, "Panel", backMaterial,
            new Vector3(0f, plateCentre, 0.055f),
            new Vector3(PlateWidth, PlateHeight, 0.025f));

        // ---- face ------------------------------------------------------------------
        // A quad, not another box. A box would show the artwork on its far side too,
        // mirrored. A quad is one sided, and turning it 180 degrees about Y puts that
        // one side on +z with the lettering the right way round, because the geometry
        // and its texture coordinates turn together.
        GameObject artwork = GameObject.CreatePrimitive(PrimitiveType.Quad);
        artwork.name = "Face";
        artwork.transform.SetParent(root.transform, false);
        artwork.transform.localPosition = new Vector3(0f, plateCentre, 0.0695f);
        artwork.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        artwork.transform.localScale = new Vector3(PlateWidth, PlateHeight, 1f);
        artwork.GetComponent<Renderer>().sharedMaterial = plateMaterial;
        Object.DestroyImmediate(artwork.GetComponent<Collider>());

        // No mounting straps. They were modelled, and they crossed the face of the
        // panel: anything wide enough to read as hardware from the road is also wide
        // enough to sit over the lettering, and the lettering is the whole point of
        // the sign. The real hardware is small and sits behind the panel, so the
        // honest way to model it is not to.

        string path = PrefabFolder + "/ShuttleStop.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Debug.Log(
            "Aggie Shuttle Stop sign built.\n" +
            "  " + path + "\n" +
            "  Face painted to " + FacePath + " (" + W + "x" + H + ") — delete it and\n" +
            "  re-run to repaint, or replace it with your own artwork\n" +
            "  " + PostHeight.ToString("F2") + " m post, " + PlateWidth.ToString("F2") +
            " x " + PlateHeight.ToString("F2") + " m panel\n" +
            "  No collider: a sign the bus can hit would be scored as a collision\n" +
            "Place them on the route with NCAT -> Route -> 4. Place Signs, on any\n" +
            "waypoint whose kind is BusStop.");
    }

    // -------------------------------------------------------------------- geometry

    static GameObject Box(GameObject parent, string name, Material material,
                          Vector3 position, Vector3 size)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;

        // Signs stand off the kerb where nobody should be driving, so a collider on
        // one could only ever fail somebody unfairly.
        Object.DestroyImmediate(go.GetComponent<Collider>());

        return go;
    }

    // --------------------------------------------------------------------- painting

    static Texture2D PaintFace()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(FacePath);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(UIFolder)) AssetDatabase.CreateFolder("Assets", "UI");

        Color32[] px = new Color32[W * H];

        for (int i = 0; i < px.Length; i++) px[i] = Navy;

        // A gold keyline just inside the edge, as the printed panel has.
        Frame(px, 8, 8, W - 8, H - 8, 3, Gold);

        // ---- gold panel with the wording ---------------------------------------
        Rect(px, 18, 26, W - 18, 220, Gold);

        Text(px, "AGGIE",   152, 5, Navy);
        Text(px, "SHUTTLE",  97, 5, Navy);
        Text(px, "STOP",     42, 5, Navy);

        // ---- emblem -------------------------------------------------------------
        Texture2D logo = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);

        if (logo != null && logo.isReadable) Blit(px, logo, 128, 360, 150);
        else Shield(px);

        Texture2D texture = new Texture2D(W, H, TextureFormat.RGBA32, false);
        texture.SetPixels32(px);
        texture.Apply();

        File.WriteAllBytes(FacePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(FacePath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(FacePath) as TextureImporter;

        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(FacePath);
    }

    /// <summary>The gold shield, drawn when no real logo has been supplied.</summary>
    static void Shield(Color32[] px)
    {
        const int cx = 128;
        const int top = 452;
        const int shoulder = 346;
        const int tip = 286;
        const int half = 70;

        for (int y = tip; y <= top; y++)
        {
            // Straight sides down to the shoulder, then tapering to a point, which is
            // what makes a shape read as a crest rather than as a box.
            float w = y >= shoulder
                ? half
                : half * (y - tip) / (float)(shoulder - tip);

            int wi = Mathf.RoundToInt(w);

            for (int x = cx - wi; x <= cx + wi; x++) Set(px, x, y, Gold);
        }

        Text(px, "A&T", 368, 8, Navy);
    }

    // ----------------------------------------------------------------- draw helpers

    static void Set(Color32[] px, int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        px[y * W + x] = c;
    }

    static void Rect(Color32[] px, int x0, int y0, int x1, int y1, Color32 c)
    {
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) Set(px, x, y, c);
    }

    static void Frame(Color32[] px, int x0, int y0, int x1, int y1, int thickness, Color32 c)
    {
        Rect(px, x0, y0, x1, y0 + thickness, c);
        Rect(px, x0, y1 - thickness, x1, y1, c);
        Rect(px, x0, y0, x0 + thickness, y1, c);
        Rect(px, x1 - thickness, y0, x1, y1, c);
    }

    static void Blit(Color32[] px, Texture2D source, int cx, int cy, int size)
    {
        Color32[] src = source.GetPixels32();

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int sx = x * source.width / size;
                int sy = y * source.height / size;

                Color32 c = src[sy * source.width + sx];
                if (c.a < 128) continue;

                Set(px, cx - size / 2 + x, cy - size / 2 + y, c);
            }
        }
    }

    /// <summary>Centred line of text at a given baseline, from the 5x7 font below.</summary>
    static void Text(Color32[] px, string text, int y, int scale, Color32 c)
    {
        int width = (text.Length * 6 - 1) * scale;
        int x = (W - width) / 2;

        foreach (char ch in text)
        {
            int glyph = Index(ch);

            if (glyph >= 0)
            {
                for (int col = 0; col < 5; col++)
                {
                    int bits = Font[glyph * 5 + col];

                    for (int row = 0; row < 7; row++)
                    {
                        if ((bits & (1 << row)) == 0) continue;

                        // Row 0 is the top of the glyph, so it is drawn at the high
                        // end of the run: texture Y counts upwards.
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                                Set(px, x + col * scale + dx,
                                        y + (6 - row) * scale + dy, c);
                    }
                }
            }

            x += 6 * scale;
        }
    }

    static int Index(char ch)
    {
        if (ch == ' ') return 0;
        if (ch == '&') return 1;
        if (ch >= 'A' && ch <= 'Z') return 2 + (ch - 'A');
        if (ch >= 'a' && ch <= 'z') return 2 + (ch - 'a');
        return -1;
    }

    /// <summary>
    /// A 5x7 bitmap font: five columns per glyph, one bit per row, bit 0 at the top.
    /// Space and ampersand first, then A to Z.
    /// </summary>
    static readonly byte[] Font =
    {
        0x00,0x00,0x00,0x00,0x00,   // space
        0x36,0x49,0x55,0x22,0x50,   // &
        0x7E,0x11,0x11,0x11,0x7E,   // A
        0x7F,0x49,0x49,0x49,0x36,   // B
        0x3E,0x41,0x41,0x41,0x22,   // C
        0x7F,0x41,0x41,0x22,0x1C,   // D
        0x7F,0x49,0x49,0x49,0x41,   // E
        0x7F,0x09,0x09,0x09,0x01,   // F
        0x3E,0x41,0x49,0x49,0x7A,   // G
        0x7F,0x08,0x08,0x08,0x7F,   // H
        0x00,0x41,0x7F,0x41,0x00,   // I
        0x20,0x40,0x41,0x3F,0x01,   // J
        0x7F,0x08,0x14,0x22,0x41,   // K
        0x7F,0x40,0x40,0x40,0x40,   // L
        0x7F,0x02,0x0C,0x02,0x7F,   // M
        0x7F,0x04,0x08,0x10,0x7F,   // N
        0x3E,0x41,0x41,0x41,0x3E,   // O
        0x7F,0x09,0x09,0x09,0x06,   // P
        0x3E,0x41,0x51,0x21,0x5E,   // Q
        0x7F,0x09,0x19,0x29,0x46,   // R
        0x46,0x49,0x49,0x49,0x31,   // S
        0x01,0x01,0x7F,0x01,0x01,   // T
        0x3F,0x40,0x40,0x40,0x3F,   // U
        0x1F,0x20,0x40,0x20,0x1F,   // V
        0x3F,0x40,0x38,0x40,0x3F,   // W
        0x63,0x14,0x08,0x14,0x63,   // X
        0x07,0x08,0x70,0x08,0x07,   // Y
        0x61,0x51,0x49,0x45,0x43,   // Z
    };

    static Material MakeMaterial(string name, Texture2D texture, Color tint)
    {
        string path = PrefabFolder + "/" + name + ".mat";

        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
        {
            Paint(existing, texture, tint);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader);
        Paint(material, texture, tint);

        AssetDatabase.CreateAsset(material, path);

        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    static void Paint(Material material, Texture2D texture, Color tint)
    {
        if (texture != null)
        {
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        }

        material.color = tint;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);

        // Signage is matt. Left glossy, every sign on campus mirrors the sky.
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.15f);
    }
}
