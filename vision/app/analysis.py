"""Extração de pose (MediaPipe) e sinais articulares por frame.

O vídeo é amostrado a ~12 fps — suficiente para exercícios de força e ~2,5x
mais barato que processar todos os frames de um vídeo 30 fps.
"""

import math
import os
import subprocess
import tempfile
from dataclasses import dataclass

import cv2
import mediapipe as mp

MAX_DURATION_SEC = 60
TARGET_FPS = 12
MIN_POSE_COVERAGE = 0.5  # fração mínima de frames com pose para o vídeo ser avaliável


class BusinessError(Exception):
    """Erro que o usuário consegue corrigir (vídeo longo demais, sem pessoa visível...)."""


@dataclass
class FrameSignals:
    t: float                # segundos desde o início
    knee_angle: float       # quadril-joelho-tornozelo
    hip_angle: float        # ombro-quadril-joelho
    elbow_angle: float      # ombro-cotovelo-punho
    trunk_angle: float      # inclinação do tronco vs. vertical (0 = ereto)
    hip_y: float            # coordenadas normalizadas de imagem (y cresce para baixo)
    knee_y: float
    shoulder_y: float
    wrist_y: float


def _angle(a, b, c) -> float:
    """Ângulo em graus no vértice b, formado pelos pontos a-b-c (2D)."""
    v1 = (a[0] - b[0], a[1] - b[1])
    v2 = (c[0] - b[0], c[1] - b[1])
    dot = v1[0] * v2[0] + v1[1] * v2[1]
    n1 = math.hypot(*v1)
    n2 = math.hypot(*v2)
    if n1 < 1e-6 or n2 < 1e-6:
        return 180.0
    cos = max(-1.0, min(1.0, dot / (n1 * n2)))
    return math.degrees(math.acos(cos))


# Índices dos landmarks do BlazePose (33 pontos): [lado esquerdo, lado direito].
SHOULDER, ELBOW, WRIST, HIP, KNEE, ANKLE = (11, 12), (13, 14), (15, 16), (23, 24), (25, 26), (27, 28)


def _side_visibility(landmarks, side: int) -> float:
    ids = [SHOULDER[side], ELBOW[side], WRIST[side], HIP[side], KNEE[side], ANKLE[side]]
    return sum(landmarks[i].visibility for i in ids) / len(ids)


def _extract_signals(landmarks, t: float) -> FrameSignals:
    # Vídeo lateral: usa o lado mais visível para a câmera.
    side = 0 if _side_visibility(landmarks, 0) >= _side_visibility(landmarks, 1) else 1
    p = lambda i: (landmarks[i].x, landmarks[i].y)
    shoulder, elbow, wrist = p(SHOULDER[side]), p(ELBOW[side]), p(WRIST[side])
    hip, knee, ankle = p(HIP[side]), p(KNEE[side]), p(ANKLE[side])

    dx, dy = shoulder[0] - hip[0], shoulder[1] - hip[1]
    trunk = math.degrees(math.atan2(abs(dx), abs(dy))) if abs(dy) > 1e-6 else 90.0

    return FrameSignals(
        t=t,
        knee_angle=_angle(hip, knee, ankle),
        hip_angle=_angle(shoulder, hip, knee),
        elbow_angle=_angle(shoulder, elbow, wrist),
        trunk_angle=trunk,
        hip_y=hip[1],
        knee_y=knee[1],
        shoulder_y=shoulder[1],
        wrist_y=wrist[1],
    )


def process_video(video_path: str, overlay_path: str | None):
    """Extrai sinais por frame e, opcionalmente, grava o vídeo com o esqueleto.

    Retorna (frames: list[FrameSignals], pose_coverage: float, duration_sec: float).
    """
    capture = cv2.VideoCapture(video_path)
    if not capture.isOpened():
        raise BusinessError("Não foi possível ler o vídeo. Use MP4, MOV ou WebM.")

    fps = capture.get(cv2.CAP_PROP_FPS) or 30.0
    total_frames = int(capture.get(cv2.CAP_PROP_FRAME_COUNT) or 0)
    duration = total_frames / fps if fps > 0 else 0.0
    if duration > MAX_DURATION_SEC:
        capture.release()
        raise BusinessError(f"Vídeo com {duration:.0f}s — o limite é {MAX_DURATION_SEC}s. Grave apenas a série.")

    step = max(1, round(fps / TARGET_FPS))
    effective_fps = fps / step

    writer = None
    raw_overlay = None
    if overlay_path is not None:
        width = int(capture.get(cv2.CAP_PROP_FRAME_WIDTH))
        height = int(capture.get(cv2.CAP_PROP_FRAME_HEIGHT))
        raw_overlay = tempfile.mktemp(suffix=".mp4")
        writer = cv2.VideoWriter(raw_overlay, cv2.VideoWriter_fourcc(*"mp4v"), effective_fps, (width, height))

    drawing = mp.solutions.drawing_utils
    pose_connections = mp.solutions.pose.POSE_CONNECTIONS

    frames: list[FrameSignals] = []
    sampled = 0
    with mp.solutions.pose.Pose(model_complexity=1, min_detection_confidence=0.5) as pose:
        index = 0
        while True:
            ok, frame = capture.read()
            if not ok:
                break
            if index % step != 0:
                index += 1
                continue
            index += 1
            sampled += 1
            t = (index - 1) / fps

            result = pose.process(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
            if result.pose_landmarks:
                frames.append(_extract_signals(result.pose_landmarks.landmark, t))
                if writer is not None:
                    drawing.draw_landmarks(frame, result.pose_landmarks, pose_connections)
            if writer is not None:
                writer.write(frame)

    capture.release()
    if writer is not None:
        writer.release()
        _transcode_h264(raw_overlay, overlay_path)
        os.unlink(raw_overlay)

    if sampled == 0:
        raise BusinessError("Vídeo vazio ou corrompido.")
    coverage = len(frames) / sampled
    if not frames:
        raise BusinessError(
            "Não foi possível detectar uma pessoa no vídeo. Grave de lado, com o corpo inteiro no enquadramento.")
    return frames, coverage, duration


def _transcode_h264(source: str, destination: str) -> None:
    """OpenCV grava MPEG-4 Part 2, que browsers não reproduzem — transcodifica para H.264."""
    subprocess.run(
        ["ffmpeg", "-y", "-loglevel", "error", "-i", source,
         "-c:v", "libx264", "-pix_fmt", "yuv420p", "-movflags", "+faststart", destination],
        check=True,
    )
