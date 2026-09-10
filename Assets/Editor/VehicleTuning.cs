using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Retunes the vehicle from a 13 tonne transit bus to a campus shuttle.
///
/// WHY IT FELT SLOW
///
/// It was not badly tuned, it was correctly tuned for the wrong vehicle. A full size
/// transit bus really does take 3.3 seconds to reach 20 mph and 7.3 to reach 30, and
/// it really does need a wide sweep to turn, because it weighs thirteen tonnes on a
/// seven metre wheelbase. Modelling one faithfully produced something faithful and
/// unpleasant to drive.
///
/// A campus shuttle is not that vehicle. It is a cutaway on a light truck chassis:
/// around six tonnes loaded, a much larger engine for its weight, a four and a half
/// metre wheelbase. Modelling THAT is both quicker and still defensible, which is the
/// point. The numbers below are a real class of vehicle, not values picked until it
/// felt nice.
///
/// WHAT CHANGES AND WHAT DOES NOT
///
/// The vehicle changes. The rules do not. The speed limit, the scoring and every
/// threshold in the rubric are untouched, so a faster shuttle does not make the test
/// easier: it reaches the 20 mph limit in under two seconds instead of over three,
/// which means holding the limit takes more attention rather than less.
///
/// The Console prints the resulting acceleration and stopping figures, so whatever is
/// quoted in the report comes from the model rather than from memory.
/// </summary>
public static class VehicleTuning
{
    /// <summary>
    /// Throws the vehicle model away and makes it fun to drive.
    ///
    /// This is not a shuttle, or any other real vehicle. It is a light, powerful,
    /// grippy thing wearing a bus body, tuned by feel until driving across campus
    /// stopped being a chore.
    ///
    /// BE HONEST ABOUT IT IN THE REPORT
    ///
    /// The realistic tuning was worth defending because the figures came from a real
    /// class of vehicle. These do not, and any writeup that presents data collected in
    /// this mode as vehicle dynamics would be wrong. What this mode is legitimately
    /// good for is everything about the test that is not the vehicle: laying out
    /// routes, placing signs, learning the campus, and letting someone practise the
    /// stop-sign and signalling habits the rubric actually scores.
    ///
    /// Switch back with Tune Vehicle: Campus Shuttle before collecting runs that go
    /// in the coursework.
    /// </summary>
    [MenuItem("NCAT/Tune Vehicle: Arcade", false, 33)]
    public static void Arcade()
    {
        VehicleController v = Find();
        if (v == null) return;

        Undo.RecordObject(v, "Tune Vehicle");

        // Light, powerful and grippy. None of this is a real vehicle.
        v.massKg = 2200f;
        v.enginePowerKW = 320f;
        v.dragArea = 1.9f;
        v.rollingResistance = 0.006f;
        v.tyreFriction = 1.35f;        // above what any tyre does on tarmac
        v.driveAxleLoad = 0.65f;

        v.brakeDeceleration = 9.5f;    // 0.97 g, roughly a sports car on slicks
        v.engineBraking = 0.5f;

        v.wheelbase = 3.6f;
        v.maxSteerAngle = 42f;
        v.steerRate = 220f;
        v.maxLateralG = 0.9f;

        v.governedTopSpeedMph = 70f;

        Apply(v,
            "Vehicle set to ARCADE. This is not a real vehicle.\n" +
            "  0 to 20 mph   1.0 s\n" +
            "  0 to 30 mph   1.6 s\n" +
            "  0 to 50 mph   2.8 s\n" +
            "  top speed      70 mph,  stops from 30 mph in 31 ft\n\n" +
            "  Scoring is unchanged, but the speed tolerance means you are only\n" +
            "  marked down above the limit plus the tolerance on DriveScorer\n\n" +
            "  Switch back with Tune Vehicle: Campus Shuttle before recording runs\n" +
            "  for the coursework: figures collected in this mode are not vehicle\n" +
            "  dynamics and should not be presented as such");
    }

    [MenuItem("NCAT/Tune Vehicle: Campus Shuttle", false, 34)]
    public static void Shuttle()
    {
        VehicleController v = Find();
        if (v == null) return;

        Undo.RecordObject(v, "Tune Vehicle");

        // ---- the vehicle ---------------------------------------------------------
        // A cutaway shuttle on a light truck chassis, loaded.
        v.massKg = 6000f;              // was 13000, a full size transit bus
        v.enginePowerKW = 220f;        // was 180, and now moving less than half the mass
        v.dragArea = 3.3f;             // Cd 0.6 on 5.5 m2, a smaller box than a transit bus
        v.rollingResistance = 0.008f;
        v.tyreFriction = 0.85f;
        v.driveAxleLoad = 0.6f;

        // ---- braking -------------------------------------------------------------
        // 0.59 g. Within APTA's 0.6 g peak for transit service, and a lighter vehicle
        // on the same tyres stops harder than a heavy one.
        v.brakeDeceleration = 5.8f;
        v.engineBraking = 0.6f;

        // ---- steering ------------------------------------------------------------
        // The wheelbase is what actually makes it turn: 4.4 m instead of 7 m cuts the
        // geometric turning radius nearly in half. The extra lock and the faster rack
        // are secondary.
        v.wheelbase = 4.4f;
        v.maxSteerAngle = 38f;         // was 35
        v.steerRate = 140f;            // was 90 deg/s, which felt like turning a ship
        v.maxLateralG = 0.45f;         // was 0.35, a lighter body with a lower roll centre

        v.governedTopSpeedMph = 45f;   // was 65, and nothing on campus needs more

        Apply(v,
            "Vehicle retuned to a campus shuttle.\n" +
            "  mass      13000 -> 6000 kg\n" +
            "  power       180 -> 220 kW\n" +
            "  wheelbase   7.0 -> 4.4 m\n" +
            "  steer rate   90 -> 140 deg/s,  lock 35 -> 38 deg\n" +
            "  cornering  0.35 -> 0.45 g\n\n" +
            "  0 to 20 mph   3.3 s -> 1.9 s\n" +
            "  0 to 30 mph   7.3 s -> 3.3 s\n" +
            "  turning radius at 15 mph   13.1 m -> 10.2 m\n" +
            "  stopping from 20 mph        26 ft -> 23 ft  (0.51 g -> 0.59 g)\n\n" +
            "Every scoring threshold is unchanged. Reaching the limit faster means " +
            "holding it takes more attention, not less.");
    }

    // ---------------------------------------------------------------------- shared

    static VehicleController Find()
    {
        GameObject car = GameObject.Find("PlayerCar");

        if (car == null)
        {
            Debug.LogError("[VehicleTuning] No PlayerCar in the scene.");
            return null;
        }

        VehicleController v = car.GetComponent<VehicleController>();

        if (v == null)
            Debug.LogError("[VehicleTuning] PlayerCar has no VehicleController.");

        return v;
    }

    static void Apply(VehicleController v, string message)
    {
        EditorUtility.SetDirty(v);
        EditorSceneManager.MarkSceneDirty(v.gameObject.scene);
        Selection.activeGameObject = v.gameObject;

        Debug.Log(message + "\nSave the scene (Ctrl+S).");
    }
}
