# """Generer opdateret belastningsprofil og reelle resultatgrafer baseret på NBomber-rapporterne."""

# from pathlib import Path
# import matplotlib
# matplotlib.use("Agg")
# import matplotlib.pyplot as plt
# from matplotlib.lines import Line2D

# ROOT = Path(__file__).resolve().parent
# FIG = ROOT / "figurer"
# FIG.mkdir(parents=True, exist_ok=True)

# plt.rcParams.update({
#     "font.family": "DejaVu Sans",
#     "font.size": 10,
#     "axes.spines.top": False,
#     "axes.spines.right": False,
#     "axes.edgecolor": "#b7c0c9",
#     "axes.labelcolor": "#243746",
#     "xtick.color": "#455766",
#     "ytick.color": "#455766",
#     "pdf.fonttype": 42,
#     "ps.fonttype": 42,
# })

# NAVY, CYAN, GOLD, RED = "#15394f", "#178eae", "#d58b22", "#b64c4c"

# def save(fig, stem):
#     for ext in ("pdf", "png"):
#         fig.savefig(FIG / f"{stem}.{ext}", dpi=200, facecolor="white")
#     plt.close(fig)

# # -----------------------------------------------------------------------------
# # FIGUR 1: Load Profile Timeline (1.315 forbindelser)
# # -----------------------------------------------------------------------------
# fig, ax = plt.subplots(figsize=(11.5, 4.3))
# fig.subplots_adjust(left=.085, right=.975, top=.81, bottom=.23)

# fig.text(.085, .94, "Belastningsprofil for performance-test", fontsize=16, color=NAVY, weight="bold")
# fig.text(.085, .875, "Trinvist forløb med i alt 1.315 samtidige SignalR-forbindelser", color="#647888", fontsize=10)

# # Farvede faser
# for a, b, c in [(0, 1, "#eef1f4"), (1, 6, "#e5f3f7"), (6, 16, "#edf5ec"), (16, 17, "#fff2df")]:
#     ax.axvspan(a, b, color=c, zorder=0)

# for x in (1, 6, 16):
#     ax.axvline(x, color="#a5b4be", linewidth=.7, zorder=1)

# # Belastningskurve
# ax.plot([1, 6, 16, 17], [0, 1315, 1315, 0], color=NAVY, linewidth=2.8, zorder=4)
# ax.plot([0, 1], [30, 30], color="#697c8b", linewidth=2, linestyle="--")
# ax.axhline(1315, color=CYAN, linestyle=":", linewidth=1)

# ax.text(11, 1150, "P1: 1.315 samtidige forbindelser (1.285 studerende + 30 lærere)", ha="center", color=NAVY, fontsize=9.5, weight="bold")

# # Fase-tekster
# for x, label, ha in [(.5, "Warm-up\n1 min", "center"), (3.5, "Ramp-up\n5 min", "center"), 
#                      (11, "Steady-state\n10 min", "center"), (16.95, "Ramp-down\n1 min", "right")]:
#     ax.text(x, 1550, label, ha=ha, va="top", fontsize=9.3, color="#344c5d")

# ax.annotate("30 lærere forbundet", xy=(.5, 30), xytext=(.6, 320), ha="left", fontsize=8.5,
#             color="#596d7b", arrowprops={"arrowstyle": "-", "color": "#748692", "lw": .8})

# ax.set(xlim=(0, 17), ylim=(0, 1650), xlabel="Tid fra teststart (minutter)", ylabel="Antal samtidige forbindelser")
# ax.set_xticks([0, 1, 3, 6, 9, 12, 16, 17])
# ax.set_yticks([0, 300, 650, 1000, 1315], ["0", "300", "650", "1.000", "1.315"])
# ax.grid(axis="y", alpha=.22, zorder=0)

# fig.text(.085, .08, "Ekskl. forberedelse og session-oprydning. Målefasen udgør præcis 16 minutter (960 sekunder).", fontsize=8.5, color="#5c7180")
# save(fig, "LoadProfileTimeline")

# # -----------------------------------------------------------------------------
# # FIGUR 2: Faktiske måleresultater (Worst-case 100% aktivitet)
# # -----------------------------------------------------------------------------
# real_data = {
#     "K1: HelpRequest HTTP": [57.60, 76.46, 90.73, 134.63],
#     "K2: Lærer-notifikation": [627.51, 1126.89, 1126.89, 1126.89],
#     "K3: Svar-notifikation": [981.81, 1501.69, 1518.01, 1583.34],
# }

# fig, (ax1, ax2) = plt.subplots(2, 1, figsize=(11.5, 5.8), gridspec_kw={"height_ratios": [1, 2]})
# fig.subplots_adjust(left=.22, right=.96, top=.80, bottom=.18, hspace=.55)

# fig.text(.055, .94, "Målte svartider under spidsbelastning (Worst-case, N = 1.285)", fontsize=16, color=NAVY, weight="bold")
# fig.text(.055, .88, "Percentiler og maksima holdt op imod kravgrænserne (SLA).", fontsize=9.5, color="#5c7180")

# markers = ["o", "D", "s", "^"]
# colors = [CYAN, GOLD, NAVY, RED]

# # Plot K1 (HTTP)
# ax1.set_xlim(0, 1100)
# ax1.set_xticks([0, 200, 400, 600, 800, 1000])
# ax1.set_xlabel("Svartid (ms)")
# ax1.grid(axis="x", alpha=.22)
# vals1 = real_data["K1: HelpRequest HTTP"]
# ax1.plot([vals1[0], vals1[-1]], [0, 0], color="#c6d2da", lw=2, zorder=1)
# for v, m, c in zip(vals1, markers, colors):
#     ax1.scatter(v, 0, marker=m, color=c, s=55, zorder=3)
# ax1.annotate(f"P95: {vals1[1]:.1f} ms | Maks: {vals1[3]:.1f} ms", xy=(vals1[3], 0), xytext=(vals1[3] + 25, 0.15),
#              fontsize=8.5, color="#455766")
# ax1.set_yticks([0], ["K1: HelpRequest (HTTP)"])
# ax1.set_ylim(-.4, .6)
# ax1.axvline(1000, color=RED, linestyle="--", linewidth=1.2)
# ax1.text(990, .45, "Grænse: 1.000 ms", ha="right", va="top", fontsize=8.5, color=RED, weight="bold")
# ax1.set_title("K1: Synkron HTTP REST (Grænse: 1.0 s)", loc="left", fontsize=10.5, weight="bold", color=NAVY)

# # Plot K2 & K3 (E2E Notifikationer)
# ax2.set_xlim(0, 10500)
# ax2.set_xticks([0, 2000, 4000, 6000, 8000, 10000])
# ax2.set_xlabel("Svartid (ms)")
# ax2.grid(axis="x", alpha=.22)

# for y_idx, name in enumerate(["K3: Svar-notifikation", "K2: Lærer-notifikation"]):
#     vals = real_data[name]
#     ax2.plot([vals[0], vals[-1]], [y_idx, y_idx], color="#c6d2da", lw=2, zorder=1)
#     for v, m, c in zip(vals, markers, colors):
#         ax2.scatter(v, y_idx, marker=m, color=c, s=55, zorder=3)
#     ax2.annotate(f"P95: {vals[1]:.1f} ms | Maks: {vals[3]:.1f} ms", xy=(vals[3], y_idx), xytext=(vals[3] + 200, y_idx + 0.15),
#                  fontsize=8.5, color="#455766")

# ax2.set_yticks([1, 0], ["K2: Lærer-notifikation", "K3: Svar-notifikation"])
# ax2.set_ylim(-.4, 1.6)
# ax2.axvline(10000, color=RED, linestyle="--", linewidth=1.2)
# ax2.text(9900, 1.45, "Grænse: 10.000 ms", ha="right", va="top", fontsize=8.5, color=RED, weight="bold")
# ax2.set_title("K2 & K3: Asynkron E2E SignalR via RabbitMQ Outbox (Grænse: 10.0 s)", loc="left", fontsize=10.5, weight="bold", color=NAVY)

# # Legend
# handles = [Line2D([0], [0], marker=m, color="none", markerfacecolor=c, markeredgecolor=c, label=l, markersize=6.5)
#            for m, c, l in zip(markers, colors, ["P50 (median)", "P95", "P99", "Maksimum"])]
# fig.legend(handles=handles, loc="upper left", bbox_to_anchor=(.215, .86), frameon=False, ncol=4, fontsize=8.5)

# save(fig, "Resultatfigur_Real")
# print("Begge figurer er genereret i /figurer (PDF og PNG).")

# """Generer opdateret LoadProfileTimeline med 300s request-spredning og drain phase."""
# from pathlib import Path
# import matplotlib
# matplotlib.use("Agg")
# import matplotlib.pyplot as plt

# ROOT = Path(__file__).resolve().parent
# FIG = ROOT / "figurer"
# FIG.mkdir(parents=True, exist_ok=True)

# plt.rcParams.update({
#     "font.family": "DejaVu Sans",
#     "font.size": 9.5,
#     "axes.spines.top": False,
#     "axes.spines.right": False,
#     "axes.edgecolor": "#b7c0c9",
#     "axes.labelcolor": "#243746",
#     "xtick.color": "#455766",
#     "ytick.color": "#455766",
# })

# NAVY = "#15394f"
# CYAN = "#178eae"
# GREEN = "#2d8a4e"

# fig, ax = plt.subplots(figsize=(10.5, 4.2))
# fig.subplots_adjust(left=0.08, right=0.96, top=0.86, bottom=0.18)

# # Farvede fase-zoner
# ax.axvspan(0, 1, color="#eef1f4", zorder=0)
# ax.axvspan(1, 6, color="#e5f3f7", zorder=0)
# ax.axvspan(6, 16, color="#edf5ec", zorder=0)
# ax.axvspan(16, 18.65, color="#fff2df", zorder=0)

# # Lodrette skillevægge mellem faser
# for x in (1, 6, 16):
#     ax.axvline(x, color="#a5b4be", linewidth=0.8, linestyle="--", zorder=1)

# # Belastningskurver
# ax.plot([1, 6, 16, 18.65], [0, 1315, 1315, 0], color=NAVY, linewidth=2.6, zorder=4)
# ax.plot([16, 17.2], [1315, 0], color="#748692", linewidth=1.8, linestyle=":", zorder=3)
# ax.plot([0, 1], [30, 30], color="#697c8b", linewidth=2, linestyle="--", zorder=3)
# ax.axhline(1315, color=CYAN, linestyle=":", linewidth=1, zorder=2)

# # Fasetekster helt i toppen (y = 1580)
# phases = [
#     (0.5, "Warm-up\n1 min"),
#     (3.5, "Ramp-up\n5 min"),
#     (11.0, "Steady-state\n10 min"),
#     (17.3, "Drain & Clean\n1-2,5 min")
# ]
# for x, label in phases:
#     ax.text(x, 1580, label, ha="center", va="top", fontsize=8.8, color="#344c5d", weight="bold")

# # P1 tekst placeret med GOD LUFT under overskrifterne (y = 1120)
# ax.text(11, 1140, "P1: 1.315 samtidige forbindelser (1.285 studerende + 30 lærere)", 
#         ha="center", color=NAVY, fontsize=9.2, weight="bold")

# # -----------------------------------------------------------------------------
# # NYE INDIKATORER FOR 300s OPRETTELSE OG BESVARELSER
# # -----------------------------------------------------------------------------
# # 1. 300 sekunders (5 min) oprettelse: fra min 6 til min 11
# ax.annotate("", xy=(11, 750), xytext=(6, 750), 
#             arrowprops=dict(arrowstyle="<->", color=NAVY, lw=1.6))
# ax.text(8.5, 810, "1.285 HelpRequests oprettes\n(spredt over 300 s / 5 min)", 
#         ha="center", va="bottom", fontsize=8.2, color=NAVY, weight="bold")

# # 2. Besvarelsesforløb: fra min 6 til min 18,6 (steady-state + drain)
# ax.annotate("", xy=(18.65, 420), xytext=(6, 420), 
#             arrowprops=dict(arrowstyle="->", color=GREEN, lw=1.6))
# ax.text(12.3, 470, "Løbende besvarelser og tømning af restkø (steady-state + drain phase)", 
#         ha="center", va="bottom", fontsize=8.2, color=GREEN, weight="bold")

# # 30 lærere annotering
# ax.annotate("30 lærere forbundet", xy=(0.5, 30), xytext=(0.6, 260), ha="left", fontsize=8,
#             color="#596d7b", arrowprops={"arrowstyle": "-", "color": "#748692", "lw": 0.8})

# # Akser og ticks
# ax.set(xlim=(0, 19.5), ylim=(0, 1720), xlabel="Tid fra teststart (minutter)", ylabel="Antal forbindelser")
# ax.set_xticks([0, 1, 3, 6, 9, 11, 12, 16, 17.2, 18.65])
# ax.set_xticklabels(["0", "1", "3", "6", "9", "11", "12", "16", "16,5", "18,6"])
# ax.set_yticks([0, 300, 650, 1000, 1315], ["0", "300", "650", "1.000", "1.315"])
# ax.grid(axis="y", alpha=0.22, zorder=0)

# # Fodnote
# fig.text(0.08, 0.03, "* Forbindelser holdes åbne efter steady-state under dræningsfasen (drain phase), indtil alle svar er leveret.", 
#          fontsize=8, color="#5c7180")

# for ext in ("pdf", "png"):
#     fig.savefig(FIG / f"LoadProfileTimeline.{ext}", dpi=200, facecolor="white")
# plt.close(fig)
# print("Færdig! Tekst-overlap er løst, og 300s-oprettelsen er nu tydeligt illustreret.")

"""Generer LoadProfileTimeline med k6-terminologi (Ramp-down), 300s request-spredning og drain phase."""
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

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
CYAN = "#178eae"
GREEN = "#2d8a4e"

fig, ax = plt.subplots(figsize=(10.5, 4.2))
fig.subplots_adjust(left=0.08, right=0.96, top=0.86, bottom=0.18)

# Farvede fase-zoner
ax.axvspan(0, 1, color="#eef1f4", zorder=0)
ax.axvspan(1, 6, color="#e5f3f7", zorder=0)
ax.axvspan(6, 16, color="#edf5ec", zorder=0)
ax.axvspan(16, 18.65, color="#fff2df", zorder=0)

# Lodrette skillevægge mellem faser
for x in (1, 6, 16):
    ax.axvline(x, color="#a5b4be", linewidth=0.8, linestyle="--", zorder=1)

# Belastningskurver
ax.plot([1, 6, 16, 18.65], [0, 1315, 1315, 0], color=NAVY, linewidth=2.6, zorder=4)
ax.plot([16, 17.2], [1315, 0], color="#748692", linewidth=1.8, linestyle=":", zorder=3)
ax.plot([0, 1], [30, 30], color="#697c8b", linewidth=2, linestyle="--", zorder=3)
ax.axhline(1315, color=CYAN, linestyle=":", linewidth=1, zorder=2)

# Fasetekster helt i toppen (y = 1580)
phases = [
    (0.5, "Warm-up\n1 min"),
    (3.5, "Ramp-up\n5 min"),
    (11.0, "Steady-state\n10 min"),
    (17.3, "Ramp-down\n(Drain & Clean)")
]
for x, label in phases:
    ax.text(x, 1580, label, ha="center", va="top", fontsize=8.8, color="#344c5d", weight="bold")

# P1 tekst placeret med GOD LUFT under overskrifterne (y = 1140)
ax.text(11, 1140, "P1: 1.315 samtidige forbindelser (1.285 studerende + 30 lærere)", 
        ha="center", color=NAVY, fontsize=9.2, weight="bold")

# 1. 300 sekunders (5 min) oprettelse: fra min 6 til min 11
ax.annotate("", xy=(11, 750), xytext=(6, 750), 
            arrowprops=dict(arrowstyle="<->", color=NAVY, lw=1.6))
ax.text(8.5, 810, "1.285 HelpRequests oprettes\n(spredt over 300 s / 5 min)", 
        ha="center", va="bottom", fontsize=8.2, color=NAVY, weight="bold")

# 2. Besvarelsesforløb: fra min 6 til min 18,6 (steady-state + drain)
ax.annotate("", xy=(18.65, 420), xytext=(6, 420), 
            arrowprops=dict(arrowstyle="->", color=GREEN, lw=1.6))
ax.text(12.3, 470, "Løbende besvarelser og tømning af restkø (steady-state + drain phase)", 
        ha="center", va="bottom", fontsize=8.2, color=GREEN, weight="bold")

# 30 lærere annotering
ax.annotate("30 lærere forbundet", xy=(0.5, 30), xytext=(0.6, 260), ha="left", fontsize=8,
            color="#596d7b", arrowprops={"arrowstyle": "-", "color": "#748692", "lw": 0.8})

# Akser og ticks
ax.set(xlim=(0, 19.5), ylim=(0, 1720), xlabel="Tid fra teststart (minutter)", ylabel="Antal forbindelser")
ax.set_xticks([0, 1, 3, 6, 9, 11, 12, 16, 17.2, 18.65])
ax.set_xticklabels(["0", "1", "3", "6", "9", "11", "12", "16", "16,5", "18,6"])
ax.set_yticks([0, 300, 650, 1000, 1315], ["0", "300", "650", "1.000", "1.315"])
ax.grid(axis="y", alpha=0.22, zorder=0)

# Fodnote
fig.text(0.08, 0.03, "* Forbindelser holdes åbne efter steady-state under dræningsfasen (drain phase), indtil alle svar er leveret.", 
         fontsize=8, color="#5c7180")

for ext in ("pdf", "png"):
    fig.savefig(FIG / f"LoadProfileTimeline.{ext}", dpi=200, facecolor="white")
plt.close(fig)
print("Færdig! LoadProfileTimeline er opdateret med 'Ramp-down (Drain & Clean)'.")