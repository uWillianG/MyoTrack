"""Heurísticas de execução por exercício (câmera lateral, corpo inteiro).

Filosofia: score conservador — na dúvida (pouca pose detectada, nenhuma
repetição clara), devolver "não avaliável" em vez de feedback errado.
"""

from dataclasses import dataclass, field

from .analysis import FrameSignals

# Faixa mínima de variação do sinal para considerarmos que houve movimento.
MIN_SIGNAL_RANGE_DEG = 25.0
MIN_WRIST_TRAVEL = 0.10  # coordenadas normalizadas (fração da altura da imagem)


@dataclass
class Issue:
    code: str
    message: str
    timestamps_sec: list[float] = field(default_factory=list)


@dataclass
class HeuristicResult:
    rep_count: int
    issues: list[Issue]
    metrics: dict
    not_evaluable_reason: str | None = None


def _smooth(values: list[float], window: int = 5) -> list[float]:
    half = window // 2
    return [
        sum(values[max(0, i - half):i + half + 1]) / len(values[max(0, i - half):i + half + 1])
        for i in range(len(values))
    ]


def _find_bottoms(values: list[float], times: list[float], low: float = 0.35, high: float = 0.65) -> list[float]:
    """Máquina de estados com histerese sobre o sinal normalizado.

    Conta um ciclo a cada descida abaixo de `low` seguida de subida acima de
    `high`, devolvendo o instante do ponto mais baixo de cada ciclo.
    """
    vmin, vmax = min(values), max(values)
    if vmax - vmin < 1e-6:
        return []
    norm = [(v - vmin) / (vmax - vmin) for v in values]

    bottoms: list[float] = []
    state = "up"
    bottom_t = bottom_v = 0.0
    for t, v in zip(times, norm):
        if state == "up" and v < low:
            state, bottom_t, bottom_v = "down", t, v
        elif state == "down":
            if v < bottom_v:
                bottom_t, bottom_v = t, v
            if v > high:
                bottoms.append(bottom_t)
                state = "up"
    return bottoms


def _window(frames: list[FrameSignals], center: float, radius: float = 0.5) -> list[FrameSignals]:
    return [f for f in frames if abs(f.t - center) <= radius]


def _segment(frames: list[FrameSignals], start: float, end: float) -> list[FrameSignals]:
    return [f for f in frames if start <= f.t < end]


def analyze_squat(frames: list[FrameSignals]) -> HeuristicResult:
    times = [f.t for f in frames]
    knee = _smooth([f.knee_angle for f in frames])
    if max(knee) - min(knee) < MIN_SIGNAL_RANGE_DEG:
        return HeuristicResult(0, [], {}, "Nenhuma repetição de agachamento detectada no vídeo.")

    bottoms = _find_bottoms(knee, times)
    if not bottoms:
        return HeuristicResult(0, [], {}, "Nenhuma repetição completa detectada.")

    depth = Issue("insufficient_depth", "Profundidade insuficiente: desça até o quadril passar da linha do joelho.")
    lean = Issue("excessive_trunk_lean", "Inclinação excessiva do tronco na descida — mantenha o peito mais erguido.")
    min_knee_angles = []
    for bottom_t in bottoms:
        window = _window(frames, bottom_t)
        if not window:
            continue
        lowest = min(window, key=lambda f: f.knee_angle)
        min_knee_angles.append(lowest.knee_angle)
        # Profundidade: joelho fechando pouco E quadril acima da linha do joelho.
        if lowest.knee_angle > 100 and lowest.hip_y < lowest.knee_y - 0.02:
            depth.timestamps_sec.append(round(bottom_t, 1))
        if max(f.trunk_angle for f in window) > 55:
            lean.timestamps_sec.append(round(bottom_t, 1))

    return HeuristicResult(
        rep_count=len(bottoms),
        issues=[i for i in (depth, lean) if i.timestamps_sec],
        metrics={
            "min_knee_angle_deg": round(min(min_knee_angles), 1) if min_knee_angles else None,
            "max_trunk_lean_deg": round(max(f.trunk_angle for f in frames), 1),
        },
    )


def analyze_deadlift(frames: list[FrameSignals]) -> HeuristicResult:
    times = [f.t for f in frames]
    hip = _smooth([f.hip_angle for f in frames])
    if max(hip) - min(hip) < MIN_SIGNAL_RANGE_DEG:
        return HeuristicResult(0, [], {}, "Nenhuma repetição de levantamento terra detectada no vídeo.")

    bottoms = _find_bottoms(hip, times)
    if not bottoms:
        return HeuristicResult(0, [], {}, "Nenhuma repetição completa detectada.")

    lockout = Issue("incomplete_lockout", "Extensão de quadril incompleta no topo — finalize o movimento ereto.")
    boundaries = bottoms[1:] + [times[-1] + 1]
    top_angles = []
    for bottom_t, next_t in zip(bottoms, boundaries):
        ascent = _segment(frames, bottom_t, next_t)
        if not ascent:
            continue
        top = max(f.hip_angle for f in ascent)
        top_angles.append(top)
        if top < 160:
            lockout.timestamps_sec.append(round(bottom_t, 1))

    return HeuristicResult(
        rep_count=len(bottoms),
        issues=[lockout] if lockout.timestamps_sec else [],
        metrics={
            "min_hip_angle_deg": round(min(hip), 1),
            "max_hip_angle_deg": round(max(top_angles), 1) if top_angles else None,
        },
    )


def analyze_overhead_press(frames: list[FrameSignals]) -> HeuristicResult:
    times = [f.t for f in frames]
    # Altura do punho acima do ombro (y de imagem cresce para baixo).
    height = _smooth([f.shoulder_y - f.wrist_y for f in frames])
    if max(height) - min(height) < MIN_WRIST_TRAVEL:
        return HeuristicResult(0, [], {}, "Nenhuma repetição de desenvolvimento detectada no vídeo.")

    # Topos da elevação = "fundos" do sinal invertido.
    tops = _find_bottoms([-h for h in height], times)
    if not tops:
        return HeuristicResult(0, [], {}, "Nenhuma repetição completa detectada.")

    lockout = Issue("incomplete_lockout", "Cotovelos não estenderam por completo no topo do movimento.")
    lean = Issue("excessive_back_lean", "Tronco inclinando demais para trás — contraia o abdômen e o glúteo.")
    for top_t in tops:
        window = _window(frames, top_t)
        if not window:
            continue
        if max(f.elbow_angle for f in window) < 160:
            lockout.timestamps_sec.append(round(top_t, 1))
        if max(f.trunk_angle for f in window) > 25:
            lean.timestamps_sec.append(round(top_t, 1))

    return HeuristicResult(
        rep_count=len(tops),
        issues=[i for i in (lockout, lean) if i.timestamps_sec],
        metrics={
            "max_elbow_angle_deg": round(max(f.elbow_angle for f in frames), 1),
            "max_trunk_lean_deg": round(max(f.trunk_angle for f in frames), 1),
        },
    )


HEURISTICS = {
    "squat": analyze_squat,
    "deadlift": analyze_deadlift,
    "overhead_press": analyze_overhead_press,
}


def compute_score(result: HeuristicResult) -> int | None:
    """100 − 12 por ocorrência de erro (máx. 36 por tipo). Conservador e simples."""
    if result.not_evaluable_reason:
        return None
    penalty = sum(min(36, 12 * len(issue.timestamps_sec)) for issue in result.issues)
    return max(0, 100 - penalty)
