using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Resizes the bus's collider to the shape of the bus.
///
/// THE BUG THIS FIXES
///
/// The collider was added before the bus model was attached, so Unity sized it to
/// whatever was on the object at the time: a box 2.1 m long, centred on the origin.
/// A 35 ft transit bus is about 11 m long, and because the box was centred rather
/// than sitting on the wheels, roughly half its height hung BELOW the origin.
///
/// A collider that extends below the road does not look like a collider problem. It
/// looks like the road randomly stopping you, because the thing being hit is under
/// the visible surface: a tile seam, the underside of the mesh, a scanned kerb that
/// only exists below the tarmac. Every one of those reads to the driver as an
/// invisible wall on flat ground.
///
/// HOW THE NEW SIZE IS DECIDED
///
/// From the bus that is actually there. The combined bounds of every mesh renderer
/// under the vehicle are measured in the vehicle's own local space, so the collider
/// matches the model rather than a number typed in here that would be wrong the
/// moment the model changed.
///
/// It is then shrunk slightly and lifted clear of the ground. The shrink stops the
/// corners of a box catching on scanned surfaces that the rounded body of a real bus
/// would slide past; the lift gives the ground clearance a real vehicle has, so the
/// underside stops dragging through the terrain.
/// </summary>
public static class VehicleColliderFix
{
    const string CarName = "PlayerCar";

    /// <summary>Shrink applied to width and length. The body is not a perfect box.</summary>
    const float Inset = 0.94f;

    /// <summary>
    /// Ground clearance in metres.
    ///
    /// Kept at almost nothing on purpose. Clearance here does not lift the bus off
    /// the road, it lifts the COLLIDER off the bottom of the bus: the box then rests
    /// on the tarmac with that much of the model buried underneath it, and the wheels
    /// visibly sink. Real clearance would need the model's own wheels modelled, which
    /// they are not. Step assist handles the bumps this was meant to clear.
    /// </summary>
    const float Clearance = 0.02f;

    [MenuItem("NCAT/Fix Vehicle Collider", false, 33)]
    public static void Fix()
    {
        GameObject car = GameObject.Find(CarName);

        if (car == null)
        {
            Debug.LogError("[VehicleColliderFix] No " + CarName + " in the scene.");
            return;
        }

        BoxCollider box = car.GetComponent<BoxCollider>();
        if (box == null) box = Undo.AddComponent<BoxCollider>(car);

        Vector3 oldSize = box.size;
        Vector3 oldCentre = box.center;

        // Every renderer under the car except any the collider itself spawned.
        Renderer[] renderers = car.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            Debug.LogError("[VehicleColliderFix] No mesh renderers under " + CarName +
                           ". Run NCAT > Attach Vehicle Model first, so there is a " +
                           "bus to measure.");
            return;
        }

        // Measured in the car's local space rather than world space, so the result is
        // right whichever way the bus happens to be pointing when this is run.
        bool started = false;
        Bounds local = new Bounds();

        foreach (Renderer renderer in renderers)
        {
            if (renderer is ParticleSystemRenderer) continue;

            Bounds world = renderer.bounds;

            // Eight corners, each brought into local space. Transforming the centre
            // and extents separately would be wrong for any rotated child.
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? world.min.x : world.max.x,
                    (i & 2) == 0 ? world.min.y : world.max.y,
                    (i & 4) == 0 ? world.min.z : world.max.z);

                Vector3 point = car.transform.InverseTransformPoint(corner);

                if (!started) { local = new Bounds(point, Vector3.zero); started = true; }
                else local.Encapsulate(point);
            }
        }

        if (!started)
        {
            Debug.LogError("[VehicleColliderFix] Could not measure the bus.");
            return;
        }

        Undo.RecordObject(box, "Fix Vehicle Collider");

        Vector3 size = local.size;
        Vector3 centre = local.center;

        // Narrow and shorten a little so the corners of a box do not catch where a
        // rounded body would pass.
        size.x *= Inset;
        size.z *= Inset;

        // Lift the underside off the road. The bottom of the box moves up by the
        // clearance, so the box loses that much height and its centre rises by half.
        size.y = Mathf.Max(0.5f, size.y - Clearance);
        centre.y += Clearance * 0.5f;

        box.size = size;
        box.center = centre;

        EditorUtility.SetDirty(box);
        EditorSceneManager.MarkSceneDirty(car.scene);
        Selection.activeGameObject = car;

        float bottom = centre.y - size.y * 0.5f;
        float oldBottom = oldCentre.y - oldSize.y * 0.5f;

        Debug.Log(
            "Vehicle collider refitted to the bus.\n" +
            "  size    " + Format(oldSize) + "  ->  " + Format(size) + "\n" +
            "  centre  " + Format(oldCentre) + "  ->  " + Format(centre) + "\n" +
            "  bottom edge, relative to the origin: " + oldBottom.ToString("F2") +
            " m  ->  " + bottom.ToString("F2") + " m\n" +
            (oldBottom < -0.5f
                ? "  The old collider reached " + Mathf.Abs(oldBottom).ToString("F1") +
                  " m BELOW the origin, which is what was catching on things under\n" +
                  "  the visible road and reading as invisible walls.\n"
                : "") +
            "Save the scene (Ctrl+S), then press Play and drive the same stretch again.");
    }

    static string Format(Vector3 v)
    {
        return "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " +
               v.z.ToString("F2") + ")";
    }
}
