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


def make_squat(reps: int, min_knee: float, trunk_at_bottom: float, deep: bool):
    """Série senoidal de agachamentos: joelho oscila entre 175° e min_knee."""
    frames = []
    duration = reps * 3.0  # 3 s por rep
    n = int(duration * FPS)
    for i in range(n):
        t = i / FPS
        phase = (t % 3.0) / 3.0  # 0..1 dentro da rep
        depth = math.sin(math.pi * phase)  # 0 no topo, 1 no fundo
        knee = 175 - (175 - min_knee) * depth
        trunk = 10 + (trunk_at_bottom - 10) * depth
        hip_y = 0.5 + (0.25 if deep else 0.12) * depth  # joelho fica em ~0.7
        frames.append(FrameSignals(t, knee, 170 - 80 * depth, 170, trunk, hip_y, 0.7, 0.25, 0.8))
    return frames


def make_deadlift(reps: int, top_hip: float):
    frames = []
    n = int(reps * 3.0 * FPS)
    for i in range(n):
        t = i / FPS
        phase = (t % 3.0) / 3.0
        flex = math.sin(math.pi * phase)  # 1 = curvado (fundo)
        hip = top_hip - (top_hip - 90) * flex
        frames.append(FrameSignals(t, 175 - 40 * flex, hip, 170, 15 + 55 * flex, 0.5, 0.7, 0.25, 0.8))
    return frames


def make_press(reps: int, top_elbow: float, trunk_lean: float):
    frames = []
    n = int(reps * 3.0 * FPS)
    for i in range(n):
        t = i / FPS
        phase = (t % 3.0) / 3.0
        lift = math.sin(math.pi * phase)  # 1 = topo
        wrist_y = 0.45 - 0.30 * lift  # começa na altura do ombro (0.45), sobe
        elbow = 90 + (top_elbow - 90) * lift
        trunk = 5 + (trunk_lean - 5) * lift
        frames.append(FrameSignals(t, 175, 170, elbow, trunk, 0.55, 0.75, 0.45, wrist_y))
    return frames


def check(name, condition, detail=""):
    status = "OK " if condition else "FALHOU"
    print(f"[{status}] {name} {detail}")
    return condition


ok = True

# Agachamento profundo e ereto: sem issues, 5 reps
r = heuristics.analyze_squat(make_squat(5, min_knee=70, trunk_at_bottom=35, deep=True))
ok &= check("squat bom: 5 reps", r.rep_count == 5, f"(reps={r.rep_count})")
ok &= check("squat bom: sem issues", not r.issues, f"(issues={[i.code for i in r.issues]})")
ok &= check("squat bom: score 100", heuristics.compute_score(r) == 100)

# Agachamento raso (joelho só até 120°, quadril acima do joelho)
r = heuristics.analyze_squat(make_squat(4, min_knee=120, trunk_at_bottom=30, deep=False))
codes = [i.code for i in r.issues]
ok &= check("squat raso: detecta insufficient_depth", "insufficient_depth" in codes, f"(issues={codes})")
ok &= check("squat raso: score < 100", (heuristics.compute_score(r) or 100) < 100)

# Agachamento com tronco muito inclinado
r = heuristics.analyze_squat(make_squat(4, min_knee=75, trunk_at_bottom=70, deep=True))
codes = [i.code for i in r.issues]
ok &= check("squat inclinado: detecta excessive_trunk_lean", "excessive_trunk_lean" in codes, f"(issues={codes})")

# Vídeo parado (sem movimento) → não avaliável
r = heuristics.analyze_squat([FrameSignals(i / FPS, 175, 170, 170, 10, 0.5, 0.7, 0.25, 0.8) for i in range(60)])
ok &= check("squat parado: não avaliável", r.not_evaluable_reason is not None and r.rep_count == 0)
ok &= check("squat parado: score None", heuristics.compute_score(r) is None)

# Terra com lockout completo
r = heuristics.analyze_deadlift(make_deadlift(3, top_hip=175))
ok &= check("terra bom: 3 reps", r.rep_count == 3, f"(reps={r.rep_count})")
ok &= check("terra bom: sem issues", not r.issues, f"(issues={[i.code for i in r.issues]})")

# Terra sem finalizar (quadril só até 150°)
r = heuristics.analyze_deadlift(make_deadlift(3, top_hip=150))
codes = [i.code for i in r.issues]
ok &= check("terra sem lockout: detecta incomplete_lockout", "incomplete_lockout" in codes, f"(issues={codes})")

# Desenvolvimento com lockout e tronco ereto
r = heuristics.analyze_overhead_press(make_press(4, top_elbow=175, trunk_lean=8))
ok &= check("press bom: 4 reps", r.rep_count == 4, f"(reps={r.rep_count})")
ok &= check("press bom: sem issues", not r.issues, f"(issues={[i.code for i in r.issues]})")

# Desenvolvimento sem estender + inclinando para trás
r = heuristics.analyze_overhead_press(make_press(4, top_elbow=140, trunk_lean=35))
codes = [i.code for i in r.issues]
ok &= check("press ruim: detecta incomplete_lockout", "incomplete_lockout" in codes, f"(issues={codes})")
ok &= check("press ruim: detecta excessive_back_lean", "excessive_back_lean" in codes, f"(issues={codes})")

print()
print("TODOS OS TESTES PASSARAM" if ok else "HÁ FALHAS")
sys.exit(0 if ok else 1)
