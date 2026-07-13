# MyoTrack

Personal trainer + nutricionista digital: gera treinos e dietas personalizados via IA, analisa execução de exercícios por vídeo e refeições por foto, e acompanha a progressão de carga.

O plano completo de arquitetura e o roadmap em fases estão em [`docs/PLANO.md`](docs/PLANO.md).

## Stack

- **Backend**: ASP.NET Core 9 (Web API + Worker), EF Core + PostgreSQL 16
- **Frontend**: React + TypeScript + Vite + Tailwind + Recharts
- **Infra**: Docker Compose (Caddy, MinIO, Postgres) em VPS
- **IA**: LLM via API (geração de treino/dieta, análise de refeição) + MediaPipe self-hosted (análise de vídeo, Fase 3)

## Estrutura

```
backend/    Solução .NET (Domain, Infrastructure, Api, Worker)
frontend/   SPA React
deploy/     Caddyfile
docs/       Plano de arquitetura e roadmap
```

## Desenvolvimento local

Pré-requisitos: .NET 9 SDK, Node 22+, Docker (para Postgres/MinIO).

```bash
# Infra (Postgres + MinIO)
docker compose up -d postgres minio

# API (migra e semeia o banco no startup) — http://localhost:5000/swagger
cd backend
dotnet run --project MyoTrack.Api --urls http://localhost:5000

# Worker (fila de jobs de IA)
dotnet run --project MyoTrack.Worker

# Frontend — http://localhost:5173 (proxy de /api para :5000)
cd ../frontend
npm install
npm run dev
```

## Deploy (VPS)

```bash
cp .env.example .env   # preencha senhas e chave JWT
docker compose up -d --build
```

O Caddy serve o frontend e faz proxy de `/api` para a API. Para TLS automático, troque `:80` pelo domínio no `deploy/Caddyfile`.

## Status do roadmap

- [x] **Fase 0 — Fundação**: solução .NET, auth (Identity + JWT), modelo de dados + migration, seeds (exercícios e alimentos TACO), fila de jobs no Postgres, compose, CI
- [ ] **Fase 1 — MVP**: perfil/onboarding, geração de treino e dieta (regras + LLM), registro de treinos, dashboards de progressão
- [ ] **Fase 2 — Análise de refeição por foto** (LLM multimodal)
- [ ] **Fase 3 — Análise de vídeo de exercício** (MediaPipe)
- [ ] **Fase 4 — Produto/SaaS**: supervisão humana, billing, hardening LGPD
