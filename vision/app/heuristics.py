"""Heurísticas de execução por exercício (câmera lateral, corpo inteiro).

Motor declarativo: cada exercício é uma ExerciseSpec com um sinal principal
(que define as repetições) e uma lista de checagens por repetição. Checagem
que passa em todas as reps vira "ponto correto"; a que falha vira "ponto de
atenção" com os timestamps das reps em que falhou — assim toda análise
avaliável devolve os dois lados da execução.

Filosofia: score conservador — na dúvida (pouca pose detectada, nenhuma
repetição clara), devolver "não avaliável" em vez de feedback errado.
"""

from collections.abc import Callable
from dataclasses import dataclass, field

from .analysis import FrameSignals

# Faixa mínima de variação do sinal para considerarmos que houve movimento.
MIN_SIGNAL_RANGE_DEG = 25.0
MIN_WRIST_TRAVEL = 0.10   # coordenadas normalizadas (fração da altura da imagem)
MIN_SHRUG_TRAVEL = 0.025  # encolhimento tem amplitude pequena por natureza


@dataclass
class Issue:
    code: str
    message: str
    timestamps_sec: list[float] = field(default_factory=list)


@dataclass
class CorrectPoint:
    code: str
    message: str


@dataclass
class HeuristicResult:
    rep_count: int
    issues: list[Issue]
    correct_points: list[CorrectPoint]
    metrics: dict
    not_evaluable_reason: str | None = None


@dataclass
class Rep:
    """Uma repetição detectada.

    window: frames em torno do extremo do movimento (fundo do agachamento,
    topo do press...) — para checagens de posição no extremo.
    segment: frames do extremo desta rep até o extremo da próxima — cobre a
    volta do movimento (lockout do terra, extensão do braço entre roscas...).
    """
    t: float
    window: list[FrameSignals]
    segment: list[FrameSignals]


@dataclass
class Check:
    """Uma checagem por repetição, com as duas leituras do resultado."""
    code: str
    fail_message: str
    ok_message: str
    passes: Callable[[Rep], bool]


@dataclass
class ExerciseSpec:
    label: str                                   # em minúsculas, para mensagens
    signal: Callable[[FrameSignals], float]      # sinal que define a repetição
    signal_name: str                             # nome do sinal nas métricas
    extremum: str                                # "bottom" | "top" do sinal
    min_range: float                             # variação mínima = houve movimento
    checks: list[Check]


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


def analyze(spec: ExerciseSpec, frames: list[FrameSignals]) -> HeuristicResult:
    times = [f.t for f in frames]
    series = _smooth([spec.signal(f) for f in frames])
    if max(series) - min(series) < spec.min_range:
        return HeuristicResult(0, [], [], {}, f"Nenhuma repetição de {spec.label} detectada no vídeo.")

    # Extremos no topo (press, elevação...) = "fundos" do sinal invertido.
    oriented = series if spec.extremum == "bottom" else [-v for v in series]
    extremes = _find_bottoms(oriented, times)
    if not extremes:
        return HeuristicResult(0, [], [], {}, "Nenhuma repetição completa detectada.")

    boundaries = extremes[1:] + [times[-1] + 1]
    reps = [
        Rep(t=t, window=_window(frames, t), segment=_segment(frames, t, next_t))
        for t, next_t in zip(extremes, boundaries)
    ]
    reps = [r for r in reps if r.window]

    issues: list[Issue] = []
    correct_points: list[CorrectPoint] = []
    for check in spec.checks:
        failed_at = [round(r.t, 1) for r in reps if not check.passes(r)]
        if failed_at:
            issues.append(Issue(check.code, check.fail_message, failed_at))
        else:
            correct_points.append(CorrectPoint(check.code, check.ok_message))

    return HeuristicResult(
        rep_count=len(extremes),
        issues=issues,
        correct_points=correct_points,
        metrics={
            f"min_{spec.signal_name}": round(min(series), 2),
            f"max_{spec.signal_name}": round(max(series), 2),
            "max_trunk_lean_deg": round(max(f.trunk_angle for f in frames), 1),
        },
    )


# ---------------------------------------------------------------------------
# Sinais e condições reutilizados pelas specs.

def _knee(f: FrameSignals) -> float:
    return f.knee_angle


def _hip(f: FrameSignals) -> float:
    return f.hip_angle


def _elbow(f: FrameSignals) -> float:
    return f.elbow_angle


def _wrist_height(f: FrameSignals) -> float:
    # Altura do punho acima do ombro (y de imagem cresce para baixo).
    return f.shoulder_y - f.wrist_y


def _shoulder_elevation(f: FrameSignals) -> float:
    # Distância ombro-quadril: relativa ao corpo, cancela balanço da câmera/tronco.
    return f.hip_y - f.shoulder_y


def _trunk_stable(rep: Rep, max_range: float) -> bool:
    angles = [f.trunk_angle for f in rep.segment or rep.window]
    return max(angles) - min(angles) <= max_range


def _squat_depth(rep: Rep) -> bool:
    # Profundidade: joelho fechando o suficiente OU quadril na linha do joelho.
    lowest = min(rep.window, key=lambda f: f.knee_angle)
    return lowest.knee_angle <= 100 or lowest.hip_y >= lowest.knee_y - 0.02


# ---------------------------------------------------------------------------
# Catálogo de exercícios suportados.

SPECS: dict[str, ExerciseSpec] = {
    "squat": ExerciseSpec(
        label="agachamento", signal=_knee, signal_name="knee_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("insufficient_depth",
                  "Profundidade insuficiente: desça até o quadril passar da linha do joelho.",
                  "Profundidade adequada — o quadril chegou à linha do joelho.",
                  _squat_depth),
            Check("excessive_trunk_lean",
                  "Inclinação excessiva do tronco na descida — mantenha o peito mais erguido.",
                  "Tronco firme na descida, sem inclinar demais.",
                  lambda rep: max(f.trunk_angle for f in rep.window) <= 55),
        ]),
    "lunge": ExerciseSpec(
        label="afundo", signal=_knee, signal_name="knee_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("insufficient_depth",
                  "Desça mais — o joelho da frente deve dobrar até cerca de 90°.",
                  "Boa amplitude na descida do afundo.",
                  lambda rep: min(f.knee_angle for f in rep.window) <= 100),
            Check("excessive_trunk_lean",
                  "Tronco inclinando demais para a frente — mantenha-o ereto.",
                  "Tronco ereto durante o movimento.",
                  lambda rep: max(f.trunk_angle for f in rep.window) <= 30),
        ]),
    "deadlift": ExerciseSpec(
        label="levantamento terra", signal=_hip, signal_name="hip_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("incomplete_lockout",
                  "Extensão de quadril incompleta no topo — finalize o movimento ereto.",
                  "Extensão completa de quadril no topo (lockout).",
                  lambda rep: max(f.hip_angle for f in rep.segment or rep.window) >= 160),
            Check("stiff_legs_at_start",
                  "Pernas quase esticadas na saída do chão — dobre mais os joelhos e use as pernas.",
                  "Boa flexão de pernas na saída do chão.",
                  lambda rep: min(f.knee_angle for f in rep.window) <= 145),
        ]),
    "romanian_deadlift": ExerciseSpec(
        label="terra romeno", signal=_hip, signal_name="hip_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("incomplete_lockout",
                  "Extensão de quadril incompleta no topo — finalize o movimento ereto.",
                  "Extensão completa do quadril no topo.",
                  lambda rep: max(f.hip_angle for f in rep.segment or rep.window) >= 160),
            Check("excessive_knee_bend",
                  "Joelhos dobrando demais — no terra romeno, mantenha-os quase estendidos.",
                  "Joelhos firmes, com dobra mínima — bom padrão de dobradiça de quadril.",
                  lambda rep: min(f.knee_angle for f in rep.window) >= 130),
        ]),
    "hip_thrust": ExerciseSpec(
        label="elevação de quadril", signal=_hip, signal_name="hip_angle_deg",
        extremum="top", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("incomplete_extension",
                  "Suba mais o quadril — estenda por completo no topo do movimento.",
                  "Extensão completa do quadril no topo.",
                  lambda rep: max(f.hip_angle for f in rep.window) >= 160),
            Check("short_range",
                  "Amplitude curta — desça mais o quadril entre as repetições.",
                  "Boa amplitude de movimento entre as repetições.",
                  lambda rep: min(f.hip_angle for f in rep.segment or rep.window) <= 120),
        ]),
    "bench_press": ExerciseSpec(
        label="supino", signal=_elbow, signal_name="elbow_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("short_range",
                  "Amplitude curta na descida — leve a barra até perto do peito.",
                  "Boa amplitude na descida da barra.",
                  lambda rep: min(f.elbow_angle for f in rep.window) <= 100),
            Check("incomplete_lockout",
                  "Cotovelos não estenderam por completo no topo do movimento.",
                  "Extensão completa dos cotovelos no topo.",
                  lambda rep: max(f.elbow_angle for f in rep.segment or rep.window) >= 160),
        ]),
    "push_up": ExerciseSpec(
        label="flexão de braço", signal=_elbow, signal_name="elbow_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("insufficient_depth",
                  "Desça mais — dobre os cotovelos até o peito se aproximar do chão.",
                  "Boa profundidade na descida.",
                  lambda rep: min(f.elbow_angle for f in rep.window) <= 100),
            Check("hip_sag",
                  "Corpo desalinhado — quadril caindo ou empinando; contraia o abdômen e o glúteo.",
                  "Corpo alinhado durante toda a flexão, como uma prancha.",
                  lambda rep: min(f.hip_angle for f in rep.segment or rep.window) >= 150),
        ]),
    "dips": ExerciseSpec(
        label="mergulho em paralelas", signal=_elbow, signal_name="elbow_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("insufficient_depth",
                  "Desça mais — dobre os cotovelos até cerca de 90°.",
                  "Boa profundidade na descida.",
                  lambda rep: min(f.elbow_angle for f in rep.window) <= 100),
            Check("incomplete_lockout",
                  "Estenda os cotovelos por completo no topo do movimento.",
                  "Extensão completa dos cotovelos no topo.",
                  lambda rep: max(f.elbow_angle for f in rep.segment or rep.window) >= 160),
        ]),
    "triceps_pushdown": ExerciseSpec(
        # O extremo da rep é a EXTENSÃO do cotovelo (empurrão até embaixo).
        label="tríceps na polia", signal=_elbow, signal_name="elbow_angle_deg",
        extremum="top", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("incomplete_extension",
                  "Estenda o cotovelo por completo no fim do empurrão.",
                  "Extensão completa do cotovelo no fim do empurrão.",
                  lambda rep: max(f.elbow_angle for f in rep.window) >= 160),
            Check("short_range",
                  "Amplitude curta — deixe o antebraço subir controlado até fechar o cotovelo.",
                  "Boa amplitude no retorno do movimento.",
                  lambda rep: min(f.elbow_angle for f in rep.segment or rep.window) <= 100),
            Check("torso_swing",
                  "Tronco debruçando sobre a polia para empurrar — mantenha o corpo parado.",
                  "Tronco estável — força só do tríceps.",
                  lambda rep: _trunk_stable(rep, 15)),
        ]),
    "overhead_press": ExerciseSpec(
        label="desenvolvimento", signal=_wrist_height, signal_name="wrist_height",
        extremum="top", min_range=MIN_WRIST_TRAVEL,
        checks=[
            Check("incomplete_lockout",
                  "Cotovelos não estenderam por completo no topo do movimento.",
                  "Cotovelos estendidos por completo no topo.",
                  lambda rep: max(f.elbow_angle for f in rep.window) >= 160),
            Check("excessive_back_lean",
                  "Tronco inclinando demais para trás — contraia o abdômen e o glúteo.",
                  "Tronco estável, sem inclinar para trás.",
                  lambda rep: max(f.trunk_angle for f in rep.window) <= 25),
        ]),
    "lat_pulldown": ExerciseSpec(
        label="puxada alta", signal=_elbow, signal_name="elbow_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("incomplete_pull",
                  "Puxada incompleta — traga a barra até a altura do queixo ou do peito.",
                  "Puxada completa, com a barra chegando à altura do peito.",
                  lambda rep: min(f.elbow_angle for f in rep.window) <= 90),
            Check("incomplete_extension",
                  "Estenda os braços por completo no retorno da barra.",
                  "Extensão completa dos braços no retorno.",
                  lambda rep: max(f.elbow_angle for f in rep.segment or rep.window) >= 150),
            Check("torso_swing",
                  "Tronco inclinando para trás para puxar — estabilize e puxe com as costas.",
                  "Tronco estável durante a puxada.",
                  lambda rep: _trunk_stable(rep, 20)),
        ]),
    "seated_cable_row": ExerciseSpec(
        label="remada baixa", signal=_elbow, signal_name="elbow_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("incomplete_pull",
                  "Puxada incompleta — leve o punho até o tronco.",
                  "Puxada completa, com o punho chegando ao tronco.",
                  lambda rep: min(f.elbow_angle for f in rep.window) <= 90),
            Check("incomplete_extension",
                  "Estenda os braços por completo no retorno.",
                  "Extensão completa dos braços no retorno.",
                  lambda rep: max(f.elbow_angle for f in rep.segment or rep.window) >= 150),
            Check("torso_swing",
                  "Tronco balançando para frente e para trás — puxe com as costas, não com o embalo.",
                  "Tronco estável, sem embalo.",
                  lambda rep: _trunk_stable(rep, 20)),
        ]),
    "dumbbell_row": ExerciseSpec(
        label="remada serrote", signal=_elbow, signal_name="elbow_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("incomplete_pull",
                  "Puxada incompleta — suba o halter até a linha do tronco.",
                  "Puxada completa, com o halter chegando ao tronco.",
                  lambda rep: min(f.elbow_angle for f in rep.window) <= 90),
            Check("incomplete_extension",
                  "Estenda o braço por completo na descida do halter.",
                  "Extensão completa do braço na descida.",
                  lambda rep: max(f.elbow_angle for f in rep.segment or rep.window) >= 150),
            Check("torso_swing",
                  "Tronco girando/balançando para ajudar — mantenha a posição apoiada e estável.",
                  "Tronco firme na posição apoiada.",
                  lambda rep: _trunk_stable(rep, 15)),
        ]),
    "barbell_row": ExerciseSpec(
        label="remada curvada", signal=_elbow, signal_name="elbow_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("incomplete_pull",
                  "Puxada incompleta — leve o cotovelo mais para trás, até a barra tocar o tronco.",
                  "Puxada completa, com a barra chegando ao tronco.",
                  lambda rep: min(f.elbow_angle for f in rep.window) <= 90),
            Check("torso_swing",
                  "Tronco balançando para ajudar a puxada — estabilize a posição curvada.",
                  "Tronco estável na posição curvada durante toda a série.",
                  lambda rep: _trunk_stable(rep, 20)),
        ]),
    "biceps_curl": ExerciseSpec(
        label="rosca bíceps", signal=_elbow, signal_name="elbow_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("incomplete_curl",
                  "Flexão incompleta — suba o peso até o fim do movimento.",
                  "Flexão completa no topo do movimento.",
                  lambda rep: min(f.elbow_angle for f in rep.window) <= 70),
            Check("incomplete_extension",
                  "Estenda o braço por completo entre as repetições.",
                  "Extensão completa do braço entre as repetições.",
                  lambda rep: max(f.elbow_angle for f in rep.segment or rep.window) >= 150),
            Check("torso_swing",
                  "Balanço de tronco (roubo) — mantenha o corpo parado e isole o bíceps.",
                  "Sem balanço de tronco — movimento isolado no bíceps.",
                  lambda rep: _trunk_stable(rep, 15)),
        ]),
    "hammer_curl": ExerciseSpec(
        label="rosca martelo", signal=_elbow, signal_name="elbow_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("incomplete_curl",
                  "Flexão incompleta — suba o peso até o fim do movimento.",
                  "Flexão completa no topo do movimento.",
                  lambda rep: min(f.elbow_angle for f in rep.window) <= 70),
            Check("incomplete_extension",
                  "Estenda o braço por completo entre as repetições.",
                  "Extensão completa do braço entre as repetições.",
                  lambda rep: max(f.elbow_angle for f in rep.segment or rep.window) >= 150),
            Check("torso_swing",
                  "Balanço de tronco (roubo) — mantenha o corpo parado e isole o braço.",
                  "Sem balanço de tronco — movimento isolado no braço.",
                  lambda rep: _trunk_stable(rep, 15)),
        ]),
    "preacher_curl": ExerciseSpec(
        # Braço apoiado no banco: sem checagem de balanço de tronco.
        label="rosca scott", signal=_elbow, signal_name="elbow_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("incomplete_curl",
                  "Flexão incompleta — suba o peso até o fim do movimento.",
                  "Flexão completa no topo do movimento.",
                  lambda rep: min(f.elbow_angle for f in rep.window) <= 70),
            Check("incomplete_extension",
                  "Estenda o braço por completo na descida — sem repetições pela metade.",
                  "Extensão completa do braço na descida.",
                  lambda rep: max(f.elbow_angle for f in rep.segment or rep.window) >= 150),
        ]),
    "pull_up": ExerciseSpec(
        label="barra fixa", signal=_elbow, signal_name="elbow_angle_deg",
        extremum="bottom", min_range=MIN_SIGNAL_RANGE_DEG,
        checks=[
            Check("incomplete_pull",
                  "Subida incompleta — puxe até o queixo passar da linha da barra.",
                  "Subida completa, com boa flexão dos cotovelos.",
                  lambda rep: min(f.elbow_angle for f in rep.window) <= 90),
            Check("incomplete_extension",
                  "Estenda os braços por completo na descida (dead hang).",
                  "Extensão completa dos braços na descida.",
                  lambda rep: max(f.elbow_angle for f in rep.segment or rep.window) >= 160),
        ]),
    "shrug": ExerciseSpec(
        label="encolhimento", signal=_shoulder_elevation, signal_name="shoulder_elevation",
        extremum="top", min_range=MIN_SHRUG_TRAVEL,
        checks=[
            Check("elbow_bend",
                  "Cotovelos dobrando para ajudar — mantenha os braços estendidos e suba apenas os ombros.",
                  "Braços estendidos — o movimento ficou por conta do trapézio.",
                  lambda rep: min(f.elbow_angle for f in rep.segment or rep.window) >= 150),
            Check("torso_swing",
                  "Impulso com o corpo — mantenha o tronco parado e encolha os ombros com controle.",
                  "Tronco estável durante o encolhimento.",
                  lambda rep: _trunk_stable(rep, 10)),
        ]),
    "front_raise": ExerciseSpec(
        # Elevação no plano sagital — a mais visível de todas na câmera lateral.
        label="elevação frontal", signal=_wrist_height, signal_name="wrist_height",
        extremum="top", min_range=MIN_WRIST_TRAVEL,
        checks=[
            Check("short_range",
                  "Suba os braços até a linha dos ombros.",
                  "Braços subindo até a linha dos ombros.",
                  lambda rep: min(f.wrist_y - f.shoulder_y for f in rep.window) <= 0.03),
            Check("torso_swing",
                  "Balanço de tronco para impulsionar o peso — mantenha o corpo parado.",
                  "Sem balanço de tronco — movimento controlado.",
                  lambda rep: _trunk_stable(rep, 12)),
        ]),
    "upright_row": ExerciseSpec(
        label="remada alta", signal=_wrist_height, signal_name="wrist_height",
        extremum="top", min_range=MIN_WRIST_TRAVEL,
        checks=[
            Check("short_range",
                  "Puxada curta — suba a barra até a linha do peitoral superior, cotovelos na altura dos ombros.",
                  "Barra subindo até a linha do peitoral superior.",
                  lambda rep: min(f.wrist_y - f.shoulder_y for f in rep.window) <= 0.10),
            Check("torso_swing",
                  "Balanço de tronco para impulsionar a barra — mantenha o corpo parado.",
                  "Sem balanço de tronco — puxada controlada.",
                  lambda rep: _trunk_stable(rep, 15)),
        ]),
    "lateral_raise": ExerciseSpec(
        label="elevação lateral", signal=_wrist_height, signal_name="wrist_height",
        extremum="top", min_range=MIN_WRIST_TRAVEL,
        checks=[
            Check("short_range",
                  "Suba os braços até a linha dos ombros.",
                  "Braços subindo até a linha dos ombros.",
                  lambda rep: min(f.wrist_y - f.shoulder_y for f in rep.window) <= 0.03),
            Check("torso_swing",
                  "Balanço de tronco para impulsionar o peso — mantenha o corpo parado.",
                  "Sem balanço de tronco — movimento controlado.",
                  lambda rep: _trunk_stable(rep, 12)),
        ]),
}

# Interface consumida pelo main.py: slug -> callable(frames) -> HeuristicResult.
HEURISTICS = {slug: (lambda frames, s=spec: analyze(s, frames)) for slug, spec in SPECS.items()}


def compute_score(result: HeuristicResult) -> int | None:
    """100 − 12 por ocorrência de erro (máx. 36 por tipo). Conservador e simples."""
    if result.not_evaluable_reason:
        return None
    penalty = sum(min(36, 12 * len(issue.timestamps_sec)) for issue in result.issues)
    return max(0, 100 - penalty)
