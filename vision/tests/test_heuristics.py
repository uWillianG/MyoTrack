"""Testa as heurísticas do vision com séries sintéticas de FrameSignals.

Importa heuristics.py isoladamente (sem mediapipe/cv2, que não estão instalados
localmente) simulando o módulo analysis com um FrameSignals equivalente.
"""

import math
import sys
import types
from dataclasses import dataclass
from pathlib import Path

VISION = Path(__file__).resolve().parents[1]


@dataclass
class FrameSignals:
    t: float
    knee_angle: float
    hip_angle: float
    elbow_angle: float
    trunk_angle: float
    hip_y: float
    knee_y: float
    shoulder_y: float
    wrist_y: float


# Injeta um pacote "app" fake com app.analysis.FrameSignals para satisfazer o import relativo.
app_pkg = types.ModuleType("app")
app_pkg.__path__ = [str(VISION / "app")]
analysis_mod = types.ModuleType("app.analysis")
analysis_mod.FrameSignals = FrameSignals
sys.modules["app"] = app_pkg
sys.modules["app.analysis"] = analysis_mod

import importlib

heuristics = importlib.import_module("app.heuristics")

FPS = 12.0


def series(reps: int, frame_at):
    """Gera `reps` ciclos senoidais de 3 s chamando frame_at(t, phase 0..1)."""
    frames = []
    for i in range(int(reps * 3.0 * FPS)):
        t = i / FPS
        phase = math.sin(math.pi * ((t % 3.0) / 3.0))  # 0 no início/fim, 1 no meio da rep
        frames.append(frame_at(t, phase))
    return frames


def neutral(t, *, knee=175, hip=170, elbow=170, trunk=10, hip_y=0.5, knee_y=0.7, shoulder_y=0.25, wrist_y=0.8):
    return FrameSignals(t, knee, hip, elbow, trunk, hip_y, knee_y, shoulder_y, wrist_y)


def make_squat(reps, min_knee, trunk_at_bottom, deep):
    return series(reps, lambda t, d: neutral(
        t, knee=175 - (175 - min_knee) * d, hip=170 - 80 * d,
        trunk=10 + (trunk_at_bottom - 10) * d, hip_y=0.5 + (0.25 if deep else 0.12) * d))


def make_deadlift(reps, top_hip, knee_at_bottom=135):
    return series(reps, lambda t, d: neutral(
        t, knee=175 - (175 - knee_at_bottom) * d, hip=top_hip - (top_hip - 90) * d, trunk=15 + 55 * d))


def make_press(reps, top_elbow, trunk_lean):
    return series(reps, lambda t, d: neutral(
        t, elbow=90 + (top_elbow - 90) * d, trunk=5 + (trunk_lean - 5) * d,
        shoulder_y=0.45, wrist_y=0.45 - 0.30 * d, hip_y=0.55, knee_y=0.75))


def make_curl(reps, top_flex, bottom_ext, trunk_swing):
    return series(reps, lambda t, d: neutral(
        t, elbow=bottom_ext - (bottom_ext - top_flex) * d, trunk=8 + trunk_swing * d))


def make_pushup(reps, bottom_elbow, hip_line):
    return series(reps, lambda t, d: neutral(
        t, elbow=175 - (175 - bottom_elbow) * d, hip=180 - (180 - hip_line) * d, trunk=80))


def make_hip_thrust(reps, top_hip, bottom_hip):
    return series(reps, lambda t, d: neutral(t, hip=bottom_hip + (top_hip - bottom_hip) * d, trunk=60))


def make_lateral_raise(reps, top_wrist_above_shoulder, trunk_swing):
    # Punho parte da altura do quadril (0.55) e sobe até perto da linha do ombro (0.30).
    return series(reps, lambda t, d: neutral(
        t, trunk=5 + trunk_swing * d, shoulder_y=0.30,
        wrist_y=0.55 - (0.25 + top_wrist_above_shoulder) * d))


def run(exercise, frames):
    return heuristics.HEURISTICS[exercise](frames)


ok = True


def check(name, condition, detail=""):
    global ok
    status = "OK " if condition else "FALHOU"
    print(f"[{status}] {name} {detail}")
    ok &= condition
    return condition


def codes(result):
    return [i.code for i in result.issues]


def ok_codes(result):
    return [p.code for p in result.correct_points]


# --- Agachamento -----------------------------------------------------------
r = run("squat", make_squat(5, min_knee=70, trunk_at_bottom=35, deep=True))
check("squat bom: 5 reps", r.rep_count == 5, f"(reps={r.rep_count})")
check("squat bom: sem issues", not r.issues, f"(issues={codes(r)})")
check("squat bom: 2 pontos corretos", len(r.correct_points) == 2, f"(ok={ok_codes(r)})")
check("squat bom: score 100", heuristics.compute_score(r) == 100)

r = run("squat", make_squat(4, min_knee=120, trunk_at_bottom=30, deep=False))
check("squat raso: detecta insufficient_depth", "insufficient_depth" in codes(r), f"(issues={codes(r)})")
check("squat raso: tronco ainda e ponto correto", "excessive_trunk_lean" in ok_codes(r), f"(ok={ok_codes(r)})")
check("squat raso: score < 100", (heuristics.compute_score(r) or 100) < 100)

r = run("squat", make_squat(4, min_knee=75, trunk_at_bottom=70, deep=True))
check("squat inclinado: detecta excessive_trunk_lean", "excessive_trunk_lean" in codes(r), f"(issues={codes(r)})")

r = run("squat", [neutral(i / FPS) for i in range(60)])
check("squat parado: nao avaliavel", r.not_evaluable_reason is not None and r.rep_count == 0)
check("squat parado: score None", heuristics.compute_score(r) is None)
check("squat parado: sem pontos corretos", not r.correct_points)

# --- Levantamento terra ----------------------------------------------------
r = run("deadlift", make_deadlift(3, top_hip=175))
check("terra bom: 3 reps", r.rep_count == 3, f"(reps={r.rep_count})")
check("terra bom: sem issues", not r.issues, f"(issues={codes(r)})")
check("terra bom: lockout como ponto correto", "incomplete_lockout" in ok_codes(r), f"(ok={ok_codes(r)})")

r = run("deadlift", make_deadlift(3, top_hip=150))
check("terra sem lockout: detecta incomplete_lockout", "incomplete_lockout" in codes(r), f"(issues={codes(r)})")

r = run("deadlift", make_deadlift(3, top_hip=175, knee_at_bottom=170))
check("terra de pernas retas: detecta stiff_legs_at_start", "stiff_legs_at_start" in codes(r), f"(issues={codes(r)})")

# --- Terra romeno (o inverso: joelhos DEVEM ficar quase retos) -------------
r = run("romanian_deadlift", make_deadlift(3, top_hip=175, knee_at_bottom=160))
check("RDL bom: sem issues", not r.issues, f"(issues={codes(r)})")

r = run("romanian_deadlift", make_deadlift(3, top_hip=175, knee_at_bottom=100))
check("RDL com joelho dobrando: detecta excessive_knee_bend", "excessive_knee_bend" in codes(r), f"(issues={codes(r)})")

# --- Desenvolvimento -------------------------------------------------------
r = run("overhead_press", make_press(4, top_elbow=175, trunk_lean=8))
check("press bom: 4 reps", r.rep_count == 4, f"(reps={r.rep_count})")
check("press bom: sem issues", not r.issues, f"(issues={codes(r)})")
check("press bom: 2 pontos corretos", len(r.correct_points) == 2, f"(ok={ok_codes(r)})")

r = run("overhead_press", make_press(4, top_elbow=140, trunk_lean=35))
check("press ruim: detecta incomplete_lockout", "incomplete_lockout" in codes(r), f"(issues={codes(r)})")
check("press ruim: detecta excessive_back_lean", "excessive_back_lean" in codes(r), f"(issues={codes(r)})")
check("press ruim: sem pontos corretos", not r.correct_points, f"(ok={ok_codes(r)})")

# --- Supino ----------------------------------------------------------------
r = run("bench_press", make_curl(4, top_flex=80, bottom_ext=175, trunk_swing=0))
check("supino bom: sem issues", not r.issues, f"(issues={codes(r)})")

r = run("bench_press", make_curl(4, top_flex=120, bottom_ext=175, trunk_swing=0))
check("supino raso: detecta short_range", "short_range" in codes(r), f"(issues={codes(r)})")

# --- Flexão de braço -------------------------------------------------------
r = run("push_up", make_pushup(4, bottom_elbow=80, hip_line=175))
check("flexao boa: sem issues", not r.issues, f"(issues={codes(r)})")

r = run("push_up", make_pushup(4, bottom_elbow=80, hip_line=130))
check("flexao com quadril caido: detecta hip_sag", "hip_sag" in codes(r), f"(issues={codes(r)})")

# --- Rosca bíceps ----------------------------------------------------------
r = run("biceps_curl", make_curl(4, top_flex=50, bottom_ext=170, trunk_swing=0))
check("rosca boa: sem issues", not r.issues, f"(issues={codes(r)})")
check("rosca boa: 3 pontos corretos", len(r.correct_points) == 3, f"(ok={ok_codes(r)})")

r = run("biceps_curl", make_curl(4, top_flex=90, bottom_ext=130, trunk_swing=25))
check("rosca ruim: detecta incomplete_curl", "incomplete_curl" in codes(r), f"(issues={codes(r)})")
check("rosca ruim: detecta incomplete_extension", "incomplete_extension" in codes(r), f"(issues={codes(r)})")
check("rosca ruim: detecta torso_swing", "torso_swing" in codes(r), f"(issues={codes(r)})")

# --- Barra fixa ------------------------------------------------------------
r = run("pull_up", make_curl(4, top_flex=60, bottom_ext=175, trunk_swing=0))
check("barra fixa boa: sem issues", not r.issues, f"(issues={codes(r)})")

r = run("pull_up", make_curl(4, top_flex=110, bottom_ext=140, trunk_swing=0))
check("barra fixa curta: detecta incomplete_pull e incomplete_extension",
      "incomplete_pull" in codes(r) and "incomplete_extension" in codes(r), f"(issues={codes(r)})")

# --- Remada curvada --------------------------------------------------------
r = run("barbell_row", make_curl(4, top_flex=70, bottom_ext=170, trunk_swing=0))
check("remada boa: sem issues", not r.issues, f"(issues={codes(r)})")

r = run("barbell_row", make_curl(4, top_flex=110, bottom_ext=170, trunk_swing=30))
check("remada ruim: detecta incomplete_pull", "incomplete_pull" in codes(r), f"(issues={codes(r)})")
check("remada ruim: detecta torso_swing", "torso_swing" in codes(r), f"(issues={codes(r)})")

# --- Elevação de quadril ---------------------------------------------------
r = run("hip_thrust", make_hip_thrust(4, top_hip=175, bottom_hip=100))
check("hip thrust bom: sem issues", not r.issues, f"(issues={codes(r)})")

r = run("hip_thrust", make_hip_thrust(4, top_hip=145, bottom_hip=100))
check("hip thrust sem extensao: detecta incomplete_extension", "incomplete_extension" in codes(r), f"(issues={codes(r)})")

# --- Elevação lateral ------------------------------------------------------
r = run("lateral_raise", make_lateral_raise(4, top_wrist_above_shoulder=0.0, trunk_swing=0))
check("elevacao lateral boa: sem issues", not r.issues, f"(issues={codes(r)})")

r = run("lateral_raise", make_lateral_raise(4, top_wrist_above_shoulder=-0.10, trunk_swing=20))
check("elevacao lateral curta: detecta short_range", "short_range" in codes(r), f"(issues={codes(r)})")
check("elevacao lateral com balanco: detecta torso_swing", "torso_swing" in codes(r), f"(issues={codes(r)})")

# --- Catálogo completo -----------------------------------------------------
check("catalogo: 12 exercicios", len(heuristics.HEURISTICS) == 12, f"(n={len(heuristics.HEURISTICS)})")

print()
print("TODOS OS TESTES PASSARAM" if ok else "HA FALHAS")
sys.exit(0 if ok else 1)
