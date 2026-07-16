"""Serviço vision: análise de execução de exercício por vídeo (MediaPipe Pose).

Exposto apenas na rede interna do compose; o worker .NET é o único cliente.
"""

import logging
import os
import tempfile

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

from . import storage
from .analysis import BusinessError, process_video
from .heuristics import HEURISTICS, compute_score

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("vision")

app = FastAPI(title="MyoTrack Vision", version="1.0")


class AnalyzeRequest(BaseModel):
    media_key: str
    exercise: str
    overlay_key: str | None = None


@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/analyze")
def analyze(request: AnalyzeRequest):
    heuristic = HEURISTICS.get(request.exercise)
    if heuristic is None:
        raise HTTPException(400, f"Exercício não suportado: {request.exercise}")

    suffix = os.path.splitext(request.media_key)[1] or ".mp4"
    video_path = tempfile.mktemp(suffix=suffix)
    overlay_path = tempfile.mktemp(suffix=".mp4") if request.overlay_key else None
    try:
        try:
            storage.download(request.media_key, video_path)
        except Exception:
            raise HTTPException(400, "Vídeo não encontrado no storage.")

        logger.info("Analisando %s (%s)", request.media_key, request.exercise)
        try:
            frames, coverage, duration = process_video(video_path, overlay_path)
        except BusinessError as error:
            raise HTTPException(400, str(error))

        result = heuristic(frames)
        if result.not_evaluable_reason is None and coverage < 0.5:
            result.not_evaluable_reason = (
                "Pose detectada em poucos frames — grave em um local iluminado, "
                "de lado e com o corpo inteiro no enquadramento.")

        overlay_key = None
        if overlay_path is not None and os.path.exists(overlay_path):
            storage.upload(overlay_path, request.overlay_key, "video/mp4")
            overlay_key = request.overlay_key

        return {
            "score": compute_score(result),
            "rep_count": result.rep_count,
            "issues": [vars(issue) for issue in result.issues],
            "correct_points": [vars(point) for point in result.correct_points],
            "metrics": {
                **result.metrics,
                "duration_sec": round(duration, 1),
                "pose_coverage": round(coverage, 2),
            },
            "not_evaluable_reason": result.not_evaluable_reason,
            "overlay_key": overlay_key,
        }
    finally:
        for path in (video_path, overlay_path):
            if path and os.path.exists(path):
                os.unlink(path)
