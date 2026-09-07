using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

/// <summary>
/// Builds the speedometer in the corner of the HUD.
///
/// The dial face, tick ring and needle are generated as PNGs the first time this
/// runs, so there is no art to download and nothing to lose. Delete the files in
/// Assets/UI and re-run to redraw them.
/// </summary>
public static class SpeedometerSetup
{
    const string CanvasName = "HUDCanvas";
    const string PanelName = "Speedometer";
    const string UIFolder = "Assets/UI";

    const string FacePath = UIFolder + "/DialFace.png";
    const string TicksPath = UIFolder + "/DialTicks.png";
    const string NeedlePath = UIFolder + "/DialNeedle.png";

    const float PanelSize = 240f;

    // The bus is governed at 65, so 80 leaves headroom without wasting the arc.
    const float MaxDisplay = 80f;
    const float TickStep = 10f;         // numbered
    const float MinorStep = 5f;         // unnumbered

    const float StartAngle = 130f;
    const float Sweep = 260f;

    [MenuItem("NCAT/Set Up Speedometer", false, 36)]
    public static void SetUp()
    {
        Sprite face = GetOrCreateSprite(FacePath, DrawFace, 256);
        Sprite ticks = GetOrCreateSprite(TicksPath, DrawTicks, 256);
        Sprite needleSprite = GetOrCreateSprite(NeedlePath, DrawNeedle, 64);

        GameObject canvasGO = GetOrCreateCanvas();

        Transform existing = canvasGO.transform.Find(PanelName);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        // ---- panel -------------------------------------------------------------
        GameObject panel = new GameObject(PanelName, typeof(RectTransform));
        panel.transform.SetParent(canvasGO.transform, false);
        Undo.RegisterCreatedObjectUndo(panel, "Create Speedometer");

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 0f);   // bottom-right
        panelRect.pivot = new Vector2(1f, 0f);
        panelRect.anchoredPosition = new Vector2(-28f, 28f);
        panelRect.sizeDelta = new Vector2(PanelSize, PanelSize);

        AddImage(panel, "Face", face, new Color(1f, 1f, 1f, 0.92f));
        AddImage(panel, "Ticks", ticks, Color.white);

        // ---- numbers -----------------------------------------------------------
        // Placed as real Text rather than baked into the tick texture, so changing
        // the scale is an edit here rather than a redraw.
        for (float speed = 0f; speed <= MaxDisplay; speed += TickStep)
        {
            float t = speed / MaxDisplay;
            float angle = (StartAngle - t * Sweep) * Mathf.Deg2Rad;
            // Inside the needle's reach, so the numerals stay readable rather than
            // being sat on by the needle at rest.
            float radius = PanelSize * 0.31f;

            GameObject label = new GameObject("Label_" + speed,
                                              typeof(RectTransform), typeof(Text));
            label.transform.SetParent(panel.transform, false);

            RectTransform rect = label.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(44f, 24f);
            rect.anchoredPosition = new Vector2(-Mathf.Sin(angle) * radius,
                                                 Mathf.Cos(angle) * radius);

            Text text = label.GetComponent<Text>();
            ApplyFont(text, 15, FontStyle.Bold);
            text.text = Mathf.RoundToInt(speed).ToString();
            text.color = new Color(0.75f, 0.79f, 0.84f);
        }

        // ---- needle ------------------------------------------------------------
        GameObject needleGO = new GameObject("Needle", typeof(RectTransform), typeof(Image));
        needleGO.transform.SetParent(panel.transform, false);

        RectTransform needleRect = needleGO.GetComponent<RectTransform>();
        needleRect.anchorMin = needleRect.anchorMax = new Vector2(0.5f, 0.5f);

        // Pivot low on the sprite so it swings about the dial centre with a short
        // counterweight behind, as a real instrument needle does.
        needleRect.pivot = new Vector2(0.5f, 0.15f);
        needleRect.anchoredPosition = Vector2.zero;
        needleRect.sizeDelta = new Vector2(14f, PanelSize * 0.42f);

        Image needleImage = needleGO.GetComponent<Image>();
        needleImage.sprite = needleSprite;
        needleImage.color = new Color(1f, 0.32f, 0.26f);
        needleImage.raycastTarget = false;

        // Hub, drawn after the needle so it covers the pivot.
        GameObject hub = AddImage(panel, "Hub", face, new Color(0.16f, 0.18f, 0.21f, 1f));
        RectTransform hubRect = hub.GetComponent<RectTransform>();
        hubRect.anchorMin = hubRect.anchorMax = new Vector2(0.5f, 0.5f);
        hubRect.sizeDelta = new Vector2(26f, 26f);
        hubRect.anchoredPosition = Vector2.zero;

        // ---- digital readout ---------------------------------------------------
        Text digital = AddText(panel, "Digital", 42, FontStyle.Bold,
                               new Vector2(0f, -PanelSize * 0.20f), new Vector2(160f, 52f));
        digital.text = "0";

        Text unit = AddText(panel, "Unit", 16, FontStyle.Bold,
                            new Vector2(0f, -PanelSize * 0.32f), new Vector2(120f, 22f));
        unit.text = "MPH";
        unit.color = new Color(0.6f, 0.64f, 0.69f);

        Text stopping = AddText(panel, "StoppingDistance", 15, FontStyle.Normal,
                                new Vector2(0f, -PanelSize * 0.42f), new Vector2(180f, 22f));
        stopping.text = "";

        // ---- wire it up --------------------------------------------------------
        Speedometer speedo = panel.AddComponent<Speedometer>();
        speedo.needle = needleRect;
        speedo.digital = digital;
        speedo.stoppingLabel = stopping;
        speedo.maxDisplaySpeed = MaxDisplay;
        speedo.startAngle = StartAngle;
        speedo.sweepAngle = Sweep;
        speedo.vehicle = Object.FindObjectOfType<VehicleController>();

        if (speedo.vehicle == null)
            Debug.LogWarning("No VehicleController in the scene yet: the speedometer " +
                             "will find one at runtime, but check PlayerCar exists.");

        EditorUtility.SetDirty(speedo);
        EditorSceneManager.MarkSceneDirty(canvasGO.scene);
        Selection.activeGameObject = panel;

        Debug.Log(
            "Speedometer set up, bottom-right.\n" +
            "  Dial reads 0-" + MaxDisplay + " mph, needle plus digital readout\n" +
            "  Readout turns amber over the speed limit (20 mph) and red 5 over\n" +
            "  Stopping distance shown underneath: that is real physics, not a guess\n" +
            "Set Max Display Speed to 40 on the Speedometer component if you want " +
            "finer resolution at campus speeds.\n" +
            "Save the scene (Ctrl+S).");
    }

    // ------------------------------------------------------------------ helpers

    static GameObject AddImage(GameObject parent, string name, Sprite sprite, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = colour;
        image.raycastTarget = false;

        return go;
    }

    static Text AddText(GameObject parent, string name, int size, FontStyle style,
                        Vector2 position, Vector2 dimensions)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        Text text = go.GetComponent<Text>();
        ApplyFont(text, size, style);

        return text;
    }

    static void ApplyFont(Text text, int size, FontStyle style)
    {
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.color = new Color(0.92f, 0.94f, 0.96f);
    }

    static GameObject GetOrCreateCanvas()
    {
        GameObject canvasGO = GameObject.Find(CanvasName);
        if (canvasGO != null) return canvasGO;

        canvasGO = new GameObject(CanvasName);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create HUD Canvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasGO.AddComponent<GraphicRaycaster>();
        return canvasGO;
    }

    // ------------------------------------------------------------------ sprites

    delegate Color32[] Painter(int size);

    static Sprite GetOrCreateSprite(string path, Painter painter, int size)
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(UIFolder)) AssetDatabase.CreateFolder("Assets", "UI");

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(painter(size));
        texture.Apply();

        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path);

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>Dark translucent disc with a lighter rim.</summary>
    static Color32[] DrawFace(int size)
    {
        Color32[] pixels = new Color32[size * size];
        float centre = (size - 1) * 0.5f;
        float outer = centre - 1f;
        float rim = outer - 4f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - centre;
                float dy = y - centre;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                Color32 colour;

                if (d > outer)
                {
                    colour = new Color32(0, 0, 0, 0);
                }
                else if (d > rim)
                {
                    float alpha = Mathf.Clamp01(outer - d) * 255f;
                    colour = new Color32(120, 130, 142, (byte)alpha);
                }
                else
                {
                    // Slight vignette so the centre reads as recessed.
                    float shade = Mathf.Lerp(0.10f, 0.16f, d / Mathf.Max(1f, rim));
                    byte v = (byte)(shade * 255f);
                    colour = new Color32(v, (byte)(v + 6), (byte)(v + 12), 235);
                }

                pixels[y * size + x] = colour;
            }
        }

        return pixels;
    }

    /// <summary>Radial tick marks around the sweep — long and bright at each numbered step.</summary>
    static Color32[] DrawTicks(int size)
    {
        Color32[] pixels = new Color32[size * size];
        float centre = (size - 1) * 0.5f;
        float outer = centre - 8f;

        int steps = Mathf.RoundToInt(MaxDisplay / MinorStep);

        for (int i = 0; i <= steps; i++)
        {
            float speed = i * MinorStep;
            float t = speed / MaxDisplay;

            // Same mapping the needle uses, or the two would disagree.
            float angle = (StartAngle - t * Sweep) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle));

            bool major = Mathf.Abs(speed % TickStep) < 0.01f;

            float inner = major ? outer - 18f : outer - 9f;
            float width = major ? 2.2f : 1.2f;
            byte brightness = major ? (byte)235 : (byte)150;

            for (float r = inner; r <= outer; r += 0.4f)
            {
                float px = centre + direction.x * r;
                float py = centre + direction.y * r;
                Splat(pixels, size, px, py, width, brightness);
            }
        }

        return pixels;
    }

    /// <summary>A tapered needle pointing up, with a short counterweight below the pivot.</summary>
    static Color32[] DrawNeedle(int size)
    {
        Color32[] pixels = new Color32[size * size];
        float centre = (size - 1) * 0.5f;

        // Matches the RectTransform pivot of 0.15.
        const float pivot = 0.15f;

        for (int y = 0; y < size; y++)
        {
            float t = y / (float)(size - 1);

            float halfWidth;
            if (t < pivot)
            {
                // Counterweight: a stub behind the hub.
                halfWidth = Mathf.Lerp(size * 0.10f, size * 0.14f, t / pivot);
            }
            else
            {
                // Tapers to a point at the tip.
                float u = (t - pivot) / (1f - pivot);
                halfWidth = Mathf.Lerp(size * 0.14f, size * 0.02f, u);
            }

            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Abs(x - centre);
                float alpha = Mathf.Clamp01(halfWidth - distance);   // 1px soft edge
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        return pixels;
    }

    /// <summary>Writes a soft dot, so the ticks are not stair-stepped.</summary>
    static void Splat(Color32[] pixels, int size, float px, float py, float radius, byte brightness)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(px - radius - 1f));
        int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(px + radius + 1f));
        int minY = Mathf.Max(0, Mathf.FloorToInt(py - radius - 1f));
        int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(py + radius + 1f));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float d = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                float alpha = Mathf.Clamp01(radius - d);
                if (alpha <= 0f) continue;

                int index = y * size + x;
                byte existing = pixels[index].a;
                byte value = (byte)(alpha * 255f);

                if (value > existing)
                    pixels[index] = new Color32(brightness, brightness, brightness, value);
            }
        }
    }
}
