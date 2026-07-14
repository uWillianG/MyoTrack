# MediaPipe suporta oficialmente Python 3.9–3.11.
FROM python:3.11-slim

# ffmpeg: transcodificação do overlay para H.264 (o OpenCV não embute encoder H.264).
# libgl1/libglib2.0-0: dependências nativas do OpenCV/MediaPipe.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg libgl1 libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /srv
COPY requirements.txt .
RUN pip install --no-cache-dir --timeout 120 --retries 10 -r requirements.txt

COPY app ./app

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
