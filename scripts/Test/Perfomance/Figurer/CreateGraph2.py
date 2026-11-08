"""Generer et rent og lækkert SLA-budget diagram til rapporten."""
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

ROOT = Path(__file__).resolve().parent
FIG = ROOT / "figurer"
FIG.mkdir(parents=True, exist_ok=True)

plt.rcParams.update({
    "font.family": "DejaVu Sans",
    "font.size": 10,
    "axes.spines.top": False,
    "axes.spines.right": False,
    "axes.edgecolor": "#b7c0c9",
    "axes.labelcolor": "#243746",
    "xtick.color": "#455766",
    "ytick.color": "#455766",
})

NAVY, CYAN, GREEN, LIGHT_GRAY = "#15394f", "#178eae", "#2d8a4e", "#e2e8f0"

fig, ax = plt.subplots(figsize=(9, 3.2))
fig.subplots_adjust(left=0.28, right=0.92, top=0.82, bottom=0.22)

labels = [
    "K3: Svar-notifikation (E2E)\n[Grænse: 10,0 s]",
    "K2: Lærer-notifikation (E2E)\n[Grænse: 10,0 s]",
    "K1: HelpRequest HTTP\n[Grænse: 1,0 s]"
]

# Procentvis udnyttelse af SLA ved P95 og Maks
# K1: P95 = 76.5 ms / 1000 = 7.6% | Maks = 134.6 ms / 1000 = 13.5%
# K2: P95 = 1126.9 ms / 10000 = 11.3% | Maks = 1126.9 ms = 11.3%
# K3: P95 = 1501.7 ms / 10000 = 15.0% | Maks = 1583.3 ms = 15.8%
p95_pct = [15.0, 11.3, 7.6]
max_pct = [15.8, 11.3, 13.5]
y_pos = [2, 1, 0]

# Baggrund: 100% SLA-budget
ax.barh(y_pos, [100, 100, 100], color=LIGHT_GRAY, height=0.45, label="Tilladt SLA-budget (100 %)")

# Maksimal observeret tid
ax.barh(y_pos, max_pct, color="#8bbec9", height=0.45, label="Maksimal målt tid")

# P95 tid
bars = ax.barh(y_pos, p95_pct, color=CYAN, height=0.45, label="P95 svartid")

# Annoteringer med faktiske tider
ax.text(17.0, 2, "P95: 1,50 s (Maks: 1,58 s)", va="center", fontsize=8.5, color=NAVY, weight="bold")
ax.text(13.0, 1, "P95: 1,13 s (Maks: 1,13 s)", va="center", fontsize=8.5, color=NAVY, weight="bold")
ax.text(15.0, 0, "P95: 76 ms (Maks: 135 ms)", va="center", fontsize=8.5, color=NAVY, weight="bold")

# SLA grænselinje
ax.axvline(100, color="#b64c4c", linestyle="--", linewidth=1.5)
ax.text(99, 2.55, "SLA-grænse (100 %)", color="#b64c4c", ha="right", va="bottom", fontsize=8.5, weight="bold")

ax.set_yticks(y_pos)
ax.set_yticklabels(labels, fontsize=9)
ax.set_xlim(0, 108)
ax.set_xlabel("Udnyttelse af tilladt svartidsbudget (%)", fontsize=9.5)
ax.set_title("SLA-budgetudnyttelse under spidsbelastning (Worst-case, N = 1.285)", 
             loc="left", fontsize=11, weight="bold", color=NAVY, pad=15)

ax.legend(loc="lower right", bbox_to_anchor=(1.0, -0.02), frameon=False, ncol=3, fontsize=8)

for ext in ("pdf", "png"):
    fig.savefig(FIG / f"SLA_Budget_Overview.{ext}", dpi=200, facecolor="white")
plt.close(fig)
print("SLA-budget figur genereret!")