using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Puts a person at every crossing on the route, and nowhere else.
///
/// The direction they walk is taken from the route rather than guessed: a crossing
/// runs across the road, so the walk is perpendicular to the way the bus is travelling
/// at that point. Working that out from the waypoints either side is the only way it
/// comes out right without being placed by hand, and placing a dozen of them by hand
/// is how it never gets done.
/// </summary>
public static class CrossingPedestrianTools
{
    const string ContainerName = "CrossingPedestrians";

    /// <summary>Half the walk, in metres. Comfortably wider than the carriageway.</summary>
    const float HalfWidth = 9f;

    [MenuItem("NCAT/Route/Add People To Crossings", false, 113)]
    public static void Add()
    {
        GameObject routeGO = GameObject.Find("Route");

        if (routeGO == null)
        {
            Debug.LogError("[Crossings] No Route object in the scene.");
            return;
        }

        // Every waypoint in order, so the direction of travel at a crossing can be
        // read off its neighbours.
        var all = new List<RouteWaypoint>();

        for (int i = 0; i < routeGO.transform.childCount; i++)
        {
            RouteWaypoint w = routeGO.transform.GetChild(i).GetComponent<RouteWaypoint>();
            if (w != null) all.Add(w);
        }

        var crossings = new List<int>();

        for (int i = 0; i < all.Count; i++)
            if (all[i].kind == RouteWaypoint.Kind.Crosswalk) crossings.Add(i);

        if (crossings.Count == 0)
        {
            Debug.LogError(
                "[Crossings] No Crosswalk waypoints on the route, so there is nowhere " +
                "to put anybody.\n" +
                "  Place some first: NCAT -> Place Crosswalk Here, at real crossings\n" +
                "  on campus. Then run this again.");
            return;
        }

        GameObject container = GameObject.Find(ContainerName);

        if (container == null)
        {
            container = new GameObject(ContainerName);
            Undo.RegisterCreatedObjectUndo(container, "Add Crossing People");
        }

        // Rebuilt, so running this twice does not leave two people per crossing
        // standing inside each other.
        for (int i = container.transform.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(container.transform.GetChild(i).gameObject);

        Transform busTransform = null;
        VehicleController vehicle = Object.FindObjectOfType<VehicleController>();
        if (vehicle != null) busTransform = vehicle.transform;

        foreach (int index in crossings)
        {
            RouteWaypoint crossing = all[index];

            GameObject person = BuildPerson(crossing.name);
            Undo.RegisterCreatedObjectUndo(person, "Add Crossing Person");

            person.transform.SetParent(container.transform, false);
            person.transform.position = crossing.transform.position;

            CrossingWalker walker = person.AddComponent<CrossingWalker>();
            walker.crossing = crossing;
            walker.bus = busTransform;
            walker.halfWidth = HalfWidth;

            // Across the road is perpendicular to along it.
            Vector3 along = Along(all, index);
            walker.across = Vector3.Cross(Vector3.up, along).normalized;

            // The crossing radius decides how close a person has to be to count as
            // being in it, so it has to cover the walk or they can be mid-road and
            // invisible to the scoring.
            if (crossing.crossingRadius < HalfWidth)
            {
                Undo.RecordObject(crossing, "Widen Crossing");
                crossing.crossingRadius = HalfWidth;
                EditorUtility.SetDirty(crossing);
            }
        }

        // One person is built per crossing, but only some of them turn up on any
        // given run: the chooser lives on the container and decides at startup.
        CrossingPopulation population = container.GetComponent<CrossingPopulation>();
        if (population == null) population = Undo.AddComponent<CrossingPopulation>(container);

        // Written explicitly, because a component already in the scene keeps whatever
        // was serialised for it and would ignore a change to the defaults.
        population.minimumActive = Mathf.Clamp(Mathf.Max(1, crossings.Count / 4), 1, crossings.Count);
        population.maximumActive = Mathf.Clamp(Mathf.Max(2, crossings.Count / 2), 1, crossings.Count);

        EditorUtility.SetDirty(population);
        EditorSceneManager.MarkSceneDirty(routeGO.scene);
        Selection.activeGameObject = container;

        Debug.Log(
            "Put a person at each of " + crossings.Count + " crossing(s).\n" +
            "  Between " + population.minimumActive + " and " + population.maximumActive +
            " of them will actually turn up on any given run, chosen at startup\n" +
            "  They wait on the kerb and step out when the bus is within 32 m and\n" +
            "  heading towards them, then walk across at 1.3 m/s\n" +
            "  They do not get out of your way. That is the point: a pedestrian who\n" +
            "  dodges is one you were never tested against\n" +
            "  Crossing radius widened to " + HalfWidth + " m where it was smaller, or\n" +
            "  somebody mid-road would not count as being in the crossing\n" +
            "  Select one to see its walking line and trigger range as gizmos\n\n" +
            "  BEFORE COLLECTING RESULTS: set Random Seed on " + ContainerName + " to a\n" +
            "  fixed number. Left at zero the crossings are rolled fresh every run, so\n" +
            "  one driver may meet four pedestrians and another one, and part of the\n" +
            "  difference in their scores is just the dice\n\n" +
            "  This is what makes YIELDING TO PEDESTRIANS assessable, and that is one\n" +
            "  of the three critical elements\n" +
            "Save the scene (Ctrl+S).");
    }

    [MenuItem("NCAT/Route/Remove People From Crossings", false, 114)]
    public static void Remove()
    {
        GameObject container = GameObject.Find(ContainerName);

        if (container == null)
        {
            Debug.Log("[Crossings] Nobody to remove.");
            return;
        }

        Undo.DestroyObjectImmediate(container);
        Debug.Log("Crossing pedestrians removed. Save the scene (Ctrl+S).");
    }

    /// <summary>The direction the bus travels through a waypoint, flat and normalised.</summary>
    static Vector3 Along(List<RouteWaypoint> all, int index)
    {
        Vector3 from = all[Mathf.Max(0, index - 1)].transform.position;
        Vector3 to = all[Mathf.Min(all.Count - 1, index + 1)].transform.position;

        Vector3 along = to - from;
        along.y = 0f;

        return along.sqrMagnitude > 0.01f ? along.normalized : Vector3.forward;
    }

    /// <summary>
    /// A person, from primitives.
    ///
    /// Crude on purpose. At the distance a driver decides whether to brake, what
    /// matters is that something upright and person-sized is in the road, and a capsule
    /// with a head carries that at fifty metres as well as a rigged model does. No
    /// colliders: the collision that matters is scored by distance, and a physical one
    /// would send the bus into the air.
    /// </summary>
    static GameObject BuildPerson(string crossingName)
    {
        GameObject root = new GameObject("Pedestrian_" + crossingName);

        Material coat = Shared("CrossingPersonMaterial", new Color(0.86f, 0.31f, 0.20f));
        Material skin = Shared("CrossingPersonHeadMaterial", new Color(0.45f, 0.33f, 0.26f));

        Part(root, PrimitiveType.Capsule, "Body", coat,
             new Vector3(0f, 0.85f, 0f), new Vector3(0.42f, 0.56f, 0.42f));

        Part(root, PrimitiveType.Sphere, "Head", skin,
             new Vector3(0f, 1.63f, 0f), Vector3.one * 0.25f);

        return root;
    }

    static void Part(GameObject parent, PrimitiveType type, string name, Material material,
                     Vector3 position, Vector3 scale)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;

        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    static Material Shared(string name, Color tint)
    {
        const string folder = "Assets/Prefabs/Signs";
        string path = folder + "/" + name + ".mat";

        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        System.IO.Directory.CreateDirectory(folder);

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.color = tint;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);

        AssetDatabase.CreateAsset(material, path);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }
}
