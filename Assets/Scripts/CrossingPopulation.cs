using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Decides which crossings have somebody at them this run.
///
/// WHY NOT ALL OF THEM
///
/// A driver who learns that every crossing is occupied stops reading crossings and
/// starts reading the map. They slow at the places they remember rather than at the
/// places where somebody is, which is the opposite of the habit a road test is meant
/// to measure. Leaving most crossings empty means the only way to catch the one that
/// is not is to actually look.
///
/// WHY THERE IS A MINIMUM
///
/// Yielding to pedestrians is one of the three critical elements, and an element with
/// no opportunities in it is not scored at all — it is reported as not assessed, and
/// the overall result is averaged over whatever is left. A run that happened to roll
/// zero pedestrians would quietly drop a critical element and nobody would know why
/// the result moved. So at least a couple always appear.
///
/// WHY THERE IS A SEED
///
/// Two drivers compared against each other have to have been given the same test. If
/// the crossings are rolled fresh for each of them, one may meet four pedestrians and
/// the other one, and the difference in their scores is partly just that. Set a seed
/// and every driver gets the same selection, which is what makes the comparison mean
/// something. Leave it at zero while you are playing and it is different each time.
/// </summary>
[DefaultExecutionOrder(-200)]
public class CrossingPopulation : MonoBehaviour
{
    [Header("How many")]
    [Tooltip("Fewest crossings that will have somebody at them. Keep this at one or " +
             "more: a run with no pedestrians leaves a critical element unassessed.")]
    public int minimumActive = 2;

    [Tooltip("Most crossings that will have somebody at them. Well under the number " +
             "of crossings, or there is nothing to be caught out by.")]
    public int maximumActive = 4;

    [Header("Repeatability")]
    [Tooltip("Zero rolls a different selection every time you press Play, which is " +
             "what you want while driving for fun.\\n\\n" +
             "Any other number gives the same selection every single run. Use that " +
             "when you are collecting results you intend to compare between drivers: " +
             "otherwise one person meets four pedestrians and another meets one, and " +
             "part of the difference in their scores is just the dice.")]
    public int randomSeed = 0;

    [Header("Reporting")]
    [Tooltip("Write the chosen crossings to the console. Worth leaving on: it is the " +
             "record of what test this particular drive was actually given.")]
    public bool announce = true;

    /// <summary>How many were chosen, for anything that wants to report the run.</summary>
    public int ActiveCount { get; private set; }

    void Awake()
    {
        // Every walker under here, including ones already switched off by a previous
        // run, so the selection is made from the full set each time.
        CrossingWalker[] walkers = GetComponentsInChildren<CrossingWalker>(true);

        if (walkers.Length == 0)
        {
            Debug.LogWarning("[Crossings] No crossing pedestrians under " + name +
                             ". Run NCAT -> Route -> Add People To Crossings.");
            return;
        }

        // A seed of zero means genuinely different each run; anything else is the same
        // every run, by construction.
        System.Random dice = randomSeed == 0
            ? new System.Random(System.Environment.TickCount)
            : new System.Random(randomSeed);

        var order = new List<int>();
        for (int i = 0; i < walkers.Length; i++) order.Add(i);

        // Fisher-Yates. Shuffling the list and taking the front is the only way to
        // choose n of m without the same one coming up twice.
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = dice.Next(i + 1);
            int swap = order[i]; order[i] = order[j]; order[j] = swap;
        }

        int wanted = Mathf.Clamp(
            dice.Next(Mathf.Min(minimumActive, maximumActive),
                      Mathf.Max(minimumActive, maximumActive) + 1),
            0, walkers.Length);

        var chosen = new List<string>();

        for (int i = 0; i < order.Count; i++)
        {
            bool on = i < wanted;
            walkers[order[i]].gameObject.SetActive(on);

            if (on) chosen.Add(walkers[order[i]].name);
        }

        ActiveCount = wanted;

        if (!announce) return;

        Debug.Log(
            "[Crossings] " + wanted + " of " + walkers.Length +
            " crossings have somebody at them this run" +
            (randomSeed == 0 ? " (rolled fresh)" : " (seed " + randomSeed + ", repeatable)") +
            ".\n  " + string.Join("\n  ", chosen.ToArray()));
    }
}
