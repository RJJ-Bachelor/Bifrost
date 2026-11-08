"""Sammenlign normalbelastning mod worst-case spidsbelastning."""
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

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

NAVY, CYAN = "#15394f", "#178eae"

fig, ax = plt.subplots(figsize=(8.5, 3.2))
fig.subplots_adjust(left=0.25, right=0.92, top=0.82, bottom=0.18)

metrics = [
    "K3: Svar-notifikation (E2E)",
    "K2: Lærer-notifikation (E2E)",
    "K1: HelpRequest (HTTP)"
]

# P95 svartider i sekunder
normal_p95 = [1.50, 1.28, 0.068]
worst_p95  = [1.50, 1.13, 0.076]

y = np.arange(len(metrics))
height = 0.32

rects1 = ax.barh(y + height/2, normal_p95, height, label="Normalbelastning (129 reqs)", color="#9bc4db")
rects2 = ax.barh(y - height/2, worst_p95, height, label="Worst-case (1.285 reqs)", color=CYAN)

# Tekst på bjælkerne
for rect in rects1:
    w = rect.get_width()
    ax.text(w + 0.04, rect.get_y() + rect.get_height()/2, f"{w*1000:.0f} ms" if w < 0.2 else f"{w:.2f} s",
            va="center", ha="left", fontsize=8.5, color=NAVY)

for rect in rects2:
    w = rect.get_width()
    ax.text(w + 0.04, rect.get_y() + rect.get_height()/2, f"{w*1000:.0f} ms" if w < 0.2 else f"{w:.2f} s",
            va="center", ha="left", fontsize=8.5, color=NAVY, weight="bold")

ax.set_yticks(y)
ax.set_yticklabels(metrics, fontsize=9.5)
ax.set_xlim(0, 2.0)
ax.set_xlabel("P95 Svartid (sekunder)", fontsize=9.5)
ax.set_title("P95 Svartider: Skalerbarhed ved 10x forøgelse i anmodninger", 
             loc="left", fontsize=11, weight="bold", color=NAVY, pad=12)

ax.legend(loc="upper right", frameon=False, fontsize=8.5)

for ext in ("pdf", "png"):
    fig.savefig(FIG / f"Skalerbarhed_Sammenligning.{ext}", dpi=200, facecolor="white")
plt.close(fig)
print("Skalerbarhedsfigur genereret!")