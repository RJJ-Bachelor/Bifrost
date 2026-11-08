"""Generer ren, professionel benchmark-figur uden indbygget overskrift (til LaTeX caption)."""
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.lines import Line2D
from matplotlib.patches import Patch

ROOT = Path(__file__).resolve().parent
FIG = ROOT / "figurer"
FIG.mkdir(parents=True, exist_ok=True)

plt.rcParams.update({
    "font.family": "DejaVu Sans",
    "font.size": 9.5,
    "axes.spines.top": False,
    "axes.spines.right": False,
    "axes.edgecolor": "#b7c0c9",
    "axes.labelcolor": "#243746",
    "xtick.color": "#455766",
    "ytick.color": "#455766",
})

NAVY = "#15394f"
CYAN_LIGHT = "#9bc4db"   # Normalprofil (129)
CYAN_DARK = "#178eae"    # Worst-case (1.285)
RED = "#b64c4c"

# Data: [P50, P95, P99, Maks]
data_normal = {
    "K1": [51.65, 67.81, 75.87, 80.82],
    "K2": [932.21, 1283.82, 1283.82, 1283.82],
    "K3": [989.22, 1500.72, 1506.40, 1512.75]
}

data_worst = {
    "K1": [57.60, 76.46, 90.73, 134.63],
    "K2": [627.51, 1126.89, 1126.89, 1126.89],
    "K3": [981.81, 1501.69, 1518.01, 1583.34]
}

# Mere kompakt figur, da overskriften er væk
fig, (ax1, ax2) = plt.subplots(2, 1, figsize=(9.5, 4.4), gridspec_kw={"height_ratios": [1, 1.8]})
fig.subplots_adjust(left=0.27, right=0.88, top=0.86, bottom=0.12, hspace=0.52)

h = 0.22

# -----------------------------------------------------------------------------
# PANEL 1: K1 (HTTP)
# -----------------------------------------------------------------------------
y1 = np.array([0])

# Normal K1
p50_n, p95_n, max_n = data_normal["K1"][0], data_normal["K1"][1], data_normal["K1"][3]
ax1.barh(y1 + h/2, p50_n, h, color=CYAN_LIGHT)
ax1.plot([p50_n, p95_n], [y1[0] + h/2, y1[0] + h/2], color=NAVY, lw=1.8, zorder=4)
ax1.scatter(max_n, y1 + h/2, color=RED, s=30, zorder=5, marker="D")
ax1.text(max_n + 12, y1[0] + h/2, f"P95: {p95_n:.0f} ms", va="center", fontsize=8.5, color=NAVY)

# Worst K1
p50_w, p95_w, max_w = data_worst["K1"][0], data_worst["K1"][1], data_worst["K1"][3]
ax1.barh(y1 - h/2, p50_w, h, color=CYAN_DARK)
ax1.plot([p50_w, p95_w], [y1[0] - h/2, y1[0] - h/2], color=NAVY, lw=1.8, zorder=4)
ax1.scatter(max_w, y1 - h/2, color=RED, s=30, zorder=5, marker="D")
ax1.text(max_w + 12, y1[0] - h/2, f"P95: {p95_w:.0f} ms", va="center", fontsize=8.5, color=NAVY, weight="bold")

ax1.set_yticks(y1)
ax1.set_yticklabels(["K1: Opret anmodning\n(Synkron HTTP)"], fontsize=9)
ax1.set_xlim(0, 220)
ax1.set_xticks([0, 50, 100, 150, 200])
ax1.set_xlabel("Svartid (millisekunder)", fontsize=8.5)
ax1.set_title("K1: HTTP REST (SLA-krav: $\leq$ 1.000 ms)", loc="left", fontsize=9.5, weight="bold", color=NAVY, pad=6)
ax1.grid(axis="x", alpha=0.2)

# -----------------------------------------------------------------------------
# PANEL 2: K2 og K3 (SignalR E2E)
# -----------------------------------------------------------------------------
y2 = np.array([1, 0])

for idx, k in enumerate(["K2", "K3"]):
    # Normal
    p50_n, p95_n, max_n = data_normal[k][0], data_normal[k][1], data_normal[k][3]
    ax2.barh(y2[idx] + h/2, p50_n, h, color=CYAN_LIGHT)
    ax2.plot([p50_n, p95_n], [y2[idx] + h/2, y2[idx] + h/2], color=NAVY, lw=1.8, zorder=4)
    ax2.scatter(max_n, y2[idx] + h/2, color=RED, s=30, zorder=5, marker="D")
    ax2.text(max_n + 40, y2[idx] + h/2, f"P95: {p95_n/1000:.2f} s", va="center", fontsize=8.5, color=NAVY)

    # Worst
    p50_w, p95_w, max_w = data_worst[k][0], data_worst[k][1], data_worst[k][3]
    ax2.barh(y2[idx] - h/2, p50_w, h, color=CYAN_DARK)
    ax2.plot([p50_w, p95_w], [y2[idx] - h/2, y2[idx] - h/2], color=NAVY, lw=1.8, zorder=4)
    ax2.scatter(max_w, y2[idx] - h/2, color=RED, s=30, zorder=5, marker="D")
    # Rettet tastefejl: Nu udskrives p95_w korrekt!
    ax2.text(max_w + 40, y2[idx] - h/2, f"P95: {p95_w/1000:.2f} s", va="center", fontsize=8.5, color=NAVY, weight="bold")

ax2.set_yticks(y2)
ax2.set_yticklabels(["K2: Ny anmodn. $\\rightarrow$ Lærer\n(SignalR broadcast)", "K3: Svar $\\rightarrow$ Studerende\n(SignalR direct)"], fontsize=9)
ax2.set_xlim(0, 2200)
ax2.set_xticks([0, 500, 1000, 1500, 2000])
ax2.set_xticklabels(["0,0 s", "0,5 s", "1,0 s", "1,5 s", "2,0 s"])
ax2.set_xlabel("Svartid (sekunder)", fontsize=8.5)
ax2.set_title("K2 & K3: Asynkron SignalR E2E (SLA-krav: $\leq$ 10,0 s)", loc="left", fontsize=9.5, weight="bold", color=NAVY, pad=6)
ax2.grid(axis="x", alpha=0.2)

# -----------------------------------------------------------------------------
# FÆLLES LEGEND (Øverst, helt fri for tekst)
# -----------------------------------------------------------------------------
legend_elements = [
    Patch(facecolor=CYAN_LIGHT, edgecolor="none", label="Normalprofil (129 reqs) — Søjle: P50"),
    Patch(facecolor=CYAN_DARK, edgecolor="none", label="Worst-case (1.285 reqs) — Søjle: P50"),
    Line2D([0], [0], color=NAVY, lw=2, label="Spænd til P95"),
    Line2D([0], [0], marker="D", color="w", markerfacecolor=RED, markersize=6, label="Maksimum")
]

fig.legend(handles=legend_elements, loc="upper center", bbox_to_anchor=(0.55, 0.98), 
           frameon=False, ncol=4, fontsize=8.5)

for ext in ("pdf", "png"):
    fig.savefig(FIG / f"Perf_Benchmark_FullOverview.{ext}", dpi=200, facecolor="white")
plt.close(fig)
print("Færdig! Perf_Benchmark_FullOverview er nu knivskarp og uden overskrift.")