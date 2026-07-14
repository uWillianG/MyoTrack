"""Acesso ao MinIO (S3) — mesmas credenciais dos serviços .NET."""

import os

import boto3

BUCKET = os.environ.get("S3_BUCKET", "myotrack-media")

_client = None


def client():
    global _client
    if _client is None:
        _client = boto3.client(
            "s3",
            endpoint_url=os.environ.get("S3_ENDPOINT", "http://localhost:9000"),
            aws_access_key_id=os.environ.get("S3_ACCESS_KEY", "myotrack"),
            aws_secret_access_key=os.environ.get("S3_SECRET_KEY", "dev-only-password"),
        )
    return _client


def download(key: str, path: str) -> None:
    client().download_file(BUCKET, key, path)


def upload(path: str, key: str, content_type: str) -> None:
    client().upload_file(path, BUCKET, key, ExtraArgs={"ContentType": content_type})
