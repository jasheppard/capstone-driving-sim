"""
CST499 Senior Project II - Lec03c Python Data Visualization
Project: NC A&T Campus Driving-Safety Simulator (NCATMAPP)

Analyses real telemetry recorded by the Unity simulator. Each play session writes
one CSV to <project>/TelemetryData/ via DrivingTelemetryLogger.cs; this script
loads that data, builds a correlation matrix, identifies the two variables with
the strongest relationship, and plots them.

Usage
-----
    python driving_analysis.py                       # newest session in TelemetryData/
    python driving_analysis.py --csv path/to.csv     # one specific session
    python driving_analysis.py --all                 # every session combined

Requires: pandas, numpy, matplotlib, seaborn
    pip install pandas numpy matplotlib seaborn
"""

import argparse
import glob
import os
import sys

import numpy as np
import pandas as pd
import matplotlib.pyplot as plt
import seaborn as sns


# --------------------------------------------------------------------------- #
# Configuration
# --------------------------------------------------------------------------- #

DATA_DIR = "TelemetryData"
OUTPUT_DIR = "figures"

# Aggie blue, used for every data mark so the figures read as one set.
ACCENT = "#00447C"

# Ceiling for believable cornering load. A road car on dry tarmac tops out near
# 1.0 g and a bus lifts a wheel around 0.35, so anything past 1.5 is a logging
# artefact rather than a measurement.
MAX_PLAUSIBLE_G = 1.5

# Columns that carry driving behaviour. Deliberately excludes the bookkeeping
# columns (time_s, distance_m, pos_x, pos_z, collisions_cum): those rise
# monotonically with the session, so they correlate near-perfectly with each
# other for reasons that have nothing to do with driving. Leaving them in would
# hand back r = 1.00 between time and distance, which is true but says nothing.
ANALYSIS_COLUMNS = [
    "speed_kmh",
    "throttle",
    "brake",
    "steer_input",
    "yaw_rate_dps",
    "lateral_accel_ms2",
    "speed_change_ms2",
    "nearest_ped_m",
    "peds_within_15m",
]

# Readable names for axis labels and the heatmap.
LABELS = {
    "speed_kmh": "Speed (km/h)",
    "throttle": "Throttle (0-1)",
    "brake": "Brake (0-1)",
    "steer_input": "Steering input",
    "steer_magnitude": "Steering magnitude",
    "yaw_rate_dps": "Yaw rate (deg/s)",
    "yaw_rate_magnitude": "Turn rate magnitude (deg/s)",
    "lateral_accel_ms2": "Lateral accel (m/s^2)",
    "lateral_g": "Cornering load (g)",
    "speed_change_ms2": "Accel / brake (m/s^2)",
    "nearest_ped_m": "Nearest pedestrian (m)",
    "peds_within_15m": "Pedestrians within 15 m",
}


def nice(column):
    """Axis-friendly name for a column."""
    return LABELS.get(column, column)


# --------------------------------------------------------------------------- #
# 1. Load the data
# --------------------------------------------------------------------------- #

def load_data(csv_path=None, combine_all=False):
    """
    Loads telemetry into a DataFrame.

    Looks in TelemetryData/ next to this script, then in the parent folder, so
    the script works whether it sits in the Unity project root or beside it.
    """
    if csv_path:
        if not os.path.exists(csv_path):
            sys.exit("No such file: %s" % csv_path)
        print("Loading %s" % csv_path)
        return pd.read_csv(csv_path)

    search_roots = [DATA_DIR, os.path.join("..", DATA_DIR)]
    files = []
    for root in search_roots:
        files = sorted(glob.glob(os.path.join(root, "*.csv")))
        if files:
            break

    if not files:
        sys.exit(
            "No CSV files found in %s.\n\n"
            "Record a session first:\n"
            "  1. In Unity, run NCAT > Set Up Telemetry Logging\n"
            "  2. Press Play and drive around campus for a few minutes\n"
            "  3. Press Stop - this is what closes and saves the file\n"
            "  4. Run NCAT > Open Telemetry Folder to find the CSV\n"
            % DATA_DIR
        )

    if combine_all:
        print("Combining %d session(s):" % len(files))
        frames = []
        for path in files:
            print("   %s" % os.path.basename(path))
            frames.append(pd.read_csv(path))
        return pd.concat(frames, ignore_index=True)

    newest = files[-1]
    print("Loading newest session: %s" % os.path.basename(newest))
    return pd.read_csv(newest)


def prepare(df):
    """
    Cleans the data and adds the derived columns the analysis needs.

    Steering and yaw rate are signed - negative for left, positive for right.
    Averaged over a drive they cancel out, which hides the relationship we care
    about: it is the SIZE of a turn that loads the car, not its direction. So we
    add magnitude columns alongside the signed ones.
    """
    df = df.dropna().copy()

    # Teleports first, while the samples are still consecutive - the detection
    # compares each row with the one before it, so it must run before anything
    # punches holes in the sequence.
    df = drop_teleports(df)

    # Stationary rows are not driving. They would pile a large cluster of zeros
    # onto every scatter plot and drag the correlations toward that cluster.
    before = len(df)
    df = df[df["speed_kmh"] > 1.0]
    dropped = before - len(df)
    if dropped:
        print("Dropped %d stationary row(s) below 1 km/h." % dropped)

    df["steer_magnitude"] = df["steer_input"].abs()
    df["yaw_rate_magnitude"] = df["yaw_rate_dps"].abs()

    # Cornering load expressed in g, which is how vehicle-dynamics work reports
    # it. Roughly 1 g is the limit of a road car on dry tarmac.
    df["lateral_g"] = df["lateral_accel_ms2"].abs() / 9.81

    # Anything left above this is not a measurement of driving. No road vehicle
    # sustains more than about 1.2 g laterally; a bus lifts a wheel near 0.35.
    implausible = df["lateral_g"] > MAX_PLAUSIBLE_G
    if implausible.any():
        print("Dropped %d sample(s) above %.1f g lateral - not physically possible."
              % (implausible.sum(), MAX_PLAUSIBLE_G))
        df = df[~implausible]

    if len(df) < 30:
        sys.exit(
            "Only %d usable rows. Drive for longer before analysing - "
            "a few minutes gives a couple of thousand samples." % len(df)
        )

    print("Analysing %d samples (%.1f seconds of driving).\n"
          % (len(df), df["time_s"].max() - df["time_s"].min()))

    return df


# --------------------------------------------------------------------------- #
# 2. Correlation matrix
# --------------------------------------------------------------------------- #

def drop_teleports(df):
    """
    Removes the samples either side of a respawn.

    Pressing R puts the vehicle back at its spawn point. Position jumps hundreds
    of metres between one sample and the next, and the heading changes to
    whatever the spawn faces. The logger records that faithfully - as an enormous
    yaw rate and an impossible lateral acceleration.

    A teleport is identified geometrically: the distance actually covered between
    two samples is compared with the furthest the vehicle could have travelled at
    its recorded speed. A generous margin is used so that ordinary variation in
    frame timing is never mistaken for a jump.

    The sample after a teleport is dropped too, because yaw rate is derived from
    the previous heading and so inherits the error.
    """
    if "pos_x" not in df.columns:
        return df

    interval = df["time_s"].diff()
    travelled = np.hypot(df["pos_x"].diff(), df["pos_z"].diff())

    # Three times the distance the recorded speed allows, plus two metres of slack.
    plausible = (df["speed_kmh"] / 3.6) * interval * 3.0 + 2.0

    jumped = (travelled > plausible).fillna(False)
    suspect = jumped | jumped.shift(1, fill_value=False)

    if suspect.any():
        print("Dropped %d sample(s) around %d respawn(s)."
              % (suspect.sum(), jumped.sum()))

    return df[~suspect]


def build_correlation_matrix(df):
    """
    Pairwise correlation across the behavioural variables.

    Constant columns are dropped first: a column that never changes has zero
    variance, so its correlation is undefined and pandas returns NaN. Brake is
    the usual offender when a drive is recorded without ever slowing down.
    """
    columns = [c for c in ANALYSIS_COLUMNS if c in df.columns]
    columns += ["steer_magnitude", "yaw_rate_magnitude", "lateral_g"]

    frame = df[columns]

    constant = [c for c in frame.columns if frame[c].nunique() <= 1]
    if constant:
        print("Skipping constant column(s): %s" % ", ".join(constant))
        frame = frame.drop(columns=constant)

    matrix = frame.corr().round(2)

    print("Correlation matrix:")
    print(matrix.to_string())
    print()

    return matrix


def find_highest_correlation(matrix, exclude_pairs=None):
    """
    Returns the (column_a, column_b, r) with the largest absolute correlation.

    Self-correlations on the diagonal are always 1.00 and are skipped, as is
    each mirrored duplicate below it. Pairs listed in exclude_pairs are also
    skipped - used to leave out relationships that hold by construction rather
    than by driving behaviour.
    """
    exclude_pairs = exclude_pairs or []
    best = (None, None, 0.0)

    for i in range(len(matrix.columns)):
        for j in range(i + 1, len(matrix.columns)):     # upper triangle only
            a = matrix.columns[i]
            b = matrix.columns[j]

            if {a, b} in [set(p) for p in exclude_pairs]:
                continue

            r = matrix.iloc[i, j]
            if pd.isna(r):
                continue

            if abs(r) > abs(best[2]):
                best = (a, b, r)

    return best


# --------------------------------------------------------------------------- #
# 3. Figures
# --------------------------------------------------------------------------- #

def style():
    """Shared look: light grid, recessive axes, so the data is the loudest thing."""
    sns.set_theme(style="whitegrid")
    plt.rcParams.update({
        "figure.dpi": 110,
        "savefig.dpi": 150,
        "savefig.bbox": "tight",
        "axes.edgecolor": "#B8BCC2",
        "axes.labelcolor": "#2A2D31",
        "axes.titlesize": 12,
        "axes.titleweight": "bold",
        "grid.color": "#E4E6E9",
        "text.color": "#2A2D31",
        "xtick.color": "#5A5F66",
        "ytick.color": "#5A5F66",
        "font.size": 10,
    })


def save(fig, name):
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    path = os.path.join(OUTPUT_DIR, name)
    fig.savefig(path)
    print("Saved %s" % path)


def plot_correlation_heatmap(matrix):
    """
    Figure 1 - the correlation matrix as a heatmap.

    Correlation is polarity data: it runs from -1 through a meaningful zero to
    +1. That calls for a diverging colour scale with a neutral midpoint, scaled
    symmetrically (vmin=-1, vmax=+1) so that white always means "no linear
    relationship" and equal strengths in opposite directions look equally
    strong. A rainbow scale here would invent structure that is not in the data.
    """
    fig, ax = plt.subplots(figsize=(9, 7.5))

    labels = [nice(c) for c in matrix.columns]

    sns.heatmap(
        matrix,
        annot=True, fmt=".2f", annot_kws={"size": 8},
        cmap="RdBu_r", vmin=-1, vmax=1, center=0,
        square=True, linewidths=1.5, linecolor="white",
        cbar_kws={"label": "Pearson r", "shrink": 0.8},
        xticklabels=labels, yticklabels=labels,
        ax=ax,
    )

    ax.set_title("Correlation between driving variables\n"
                 "NC A&T campus simulator telemetry", pad=14)
    plt.setp(ax.get_xticklabels(), rotation=40, ha="right")
    plt.setp(ax.get_yticklabels(), rotation=0)

    fig.tight_layout()
    save(fig, "fig1_correlation_matrix.png")
    return fig


def plot_top_pair(df, col_a, col_b, r):
    """
    Figure 2 - scatter plot of the two most strongly correlated variables.

    The straight line is an ordinary least-squares fit. It is drawn to make the
    direction and tightness of the relationship visible at a glance; it is not a
    predictive model, and no claim is made about causation from it.
    """
    fig, ax = plt.subplots(figsize=(8, 6))

    ax.scatter(
        df[col_a], df[col_b],
        s=14, alpha=0.35, color=ACCENT,
        edgecolors="none", label="Telemetry sample",
    )

    # Least-squares trend line across the observed range.
    slope, intercept = np.polyfit(df[col_a], df[col_b], 1)
    x_line = np.linspace(df[col_a].min(), df[col_a].max(), 100)
    ax.plot(x_line, slope * x_line + intercept,
            color="#C4341E", linewidth=2, label="Least-squares fit")

    ax.set_xlabel(nice(col_a))
    ax.set_ylabel(nice(col_b))
    ax.set_title("Strongest relationship in the data:\n%s vs %s"
                 % (nice(col_a), nice(col_b)), pad=12)

    # The coefficient stated on the figure, so the plot stands on its own.
    ax.annotate(
        "Pearson r = %.2f\nn = %d samples" % (r, len(df)),
        xy=(0.03, 0.95), xycoords="axes fraction",
        va="top", ha="left", fontsize=10,
        bbox=dict(boxstyle="round,pad=0.5", facecolor="white",
                  edgecolor="#B8BCC2"),
    )

    ax.legend(loc="lower right", frameon=True)
    fig.tight_layout()
    save(fig, "fig2_top_correlation_scatter.png")
    return fig


def plot_speed_distribution(df):
    """
    Figure 3 - distribution of a single variable, speed.

    A histogram with a kernel density estimate over it. For a driving-safety
    project this is the figure that says how the vehicle was actually operated:
    where the mass of the distribution sits, and how long a tail there is above
    the campus limit.
    """
    fig, ax = plt.subplots(figsize=(8, 5))

    sns.histplot(data=df, x="speed_kmh", bins=30, kde=True,
                 color=ACCENT, alpha=0.65, edgecolor="white",
                 linewidth=1.2, ax=ax)

    mean_speed = df["speed_kmh"].mean()
    ax.axvline(mean_speed, color="#C4341E", linestyle="--", linewidth=2)
    ax.annotate("Mean %.1f km/h" % mean_speed,
                xy=(mean_speed, ax.get_ylim()[1] * 0.92),
                xytext=(6, 0), textcoords="offset points",
                color="#C4341E", fontsize=10, fontweight="bold")

    ax.set_xlabel(nice("speed_kmh"))
    ax.set_ylabel("Number of samples")
    ax.set_title("How fast the vehicle was driven\n"
                 "Distribution of recorded speed", pad=12)

    fig.tight_layout()
    save(fig, "fig3_speed_distribution.png")
    return fig


def plot_time_series(df):
    """
    Figure 4 - speed and cornering load over the course of the drive.

    Two stacked panels sharing one x axis, rather than one panel with two y
    axes. Two different scales on a single plot let the reader line up crossings
    that are an artefact of the scaling rather than anything in the data; the
    shared time axis keeps the comparison honest.

    Read together, the peaks in the lower panel that sit under high speed in the
    upper panel are the moments the vehicle was cornering hard while travelling
    fast - the events a driving-safety study is looking for.

    Cornering load is sampled ten times a second and swings hard between them, so
    the raw trace alone is a solid block of ink. It is drawn faintly underneath a
    two-second rolling average, which is what carries the shape of the drive.
    """
    fig, (top, bottom) = plt.subplots(2, 1, figsize=(10, 6.5), sharex=True)

    # Two seconds of samples, however the logger was configured.
    rate = 1.0 / np.median(np.diff(df["time_s"]))
    window = max(3, int(round(rate * 2)))

    top.plot(df["time_s"], df["speed_kmh"], color=ACCENT, linewidth=1.6)
    top.set_ylabel(nice("speed_kmh"))
    top.set_title("The drive over time", pad=12)

    bottom.plot(df["time_s"], df["lateral_g"],
                color="#C4341E", linewidth=0.7, alpha=0.25)
    bottom.plot(df["time_s"],
                df["lateral_g"].rolling(window, center=True, min_periods=1).mean(),
                color="#C4341E", linewidth=2.0)
    bottom.set_ylabel(nice("lateral_g"))
    bottom.set_xlabel("Time into session (s)")

    fig.subplots_adjust(hspace=0.18)
    save(fig, "fig4_drive_timeline.png")
    return fig


# --------------------------------------------------------------------------- #
# Main
# --------------------------------------------------------------------------- #

def main():
    parser = argparse.ArgumentParser(
        description="Visualise NCATMAPP driving telemetry.")
    parser.add_argument("--csv", help="analyse one specific CSV file")
    parser.add_argument("--all", action="store_true",
                        help="combine every session in TelemetryData/")
    parser.add_argument("--no-show", action="store_true",
                        help="save the figures without opening windows")
    args = parser.parse_args()

    style()

    df = prepare(load_data(args.csv, args.all))
    matrix = build_correlation_matrix(df)

    # lateral_g is derived from lateral_accel_ms2, and yaw_rate_magnitude from
    # yaw_rate_dps, so those pairs correlate almost perfectly by construction.
    # Excluding them means the reported "strongest relationship" is one that the
    # driving produced, not one the arithmetic guaranteed.
    derived_pairs = [
        ("lateral_accel_ms2", "lateral_g"),
        ("yaw_rate_dps", "yaw_rate_magnitude"),
        ("steer_input", "steer_magnitude"),
    ]

    col_a, col_b, r = find_highest_correlation(matrix, derived_pairs)

    if col_a is None:
        sys.exit("No usable correlations found - the data may be too short.")

    print("Columns with highest correlation: %s and %s (r = %.2f)\n"
          % (col_a, col_b, r))

    plot_correlation_heatmap(matrix)
    plot_top_pair(df, col_a, col_b, r)
    plot_speed_distribution(df)
    plot_time_series(df)

    # A short written summary, so the numbers quoted in the report come from the
    # script rather than being read off a chart by eye.
    print("\nSummary")
    print("-------")
    print("Samples analysed      : %d" % len(df))
    print("Mean speed            : %.1f km/h" % df["speed_kmh"].mean())
    print("Top speed             : %.1f km/h" % df["speed_kmh"].max())
    print("Peak cornering load   : %.2f g" % df["lateral_g"].max())
    print("Closest pedestrian    : %.1f m" % df["nearest_ped_m"].min())
    print("Strongest correlation : %s vs %s, r = %.2f"
          % (nice(col_a), nice(col_b), r))

    if not args.no_show:
        plt.show()


if __name__ == "__main__":
    main()
