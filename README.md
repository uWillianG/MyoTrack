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
vision/     Serviço Python (FastAPI + MediaPipe) de análise de vídeo
deploy/     Caddyfile
docs/       Plano de arquitetura e roadmap
```

## Como rodar

Pré-requisitos: Docker Desktop em execução (no Windows, abra o app e aguarde "Engine running" — o CLI sozinho não basta). Para desenvolvimento, também .NET 9 SDK e Node 22+.

### Tudo via Docker (mais próximo de produção)

```bash
cp .env.example .env   # preencha ANTHROPIC_API_KEY para habilitar a IA
docker compose up -d --build
```

Acesse **http://localhost** — o Caddy serve o frontend e faz proxy de `/api`. A API aplica as migrations e semeia o banco (exercícios + alimentos TACO) sozinha no startup; não há passo manual de banco.

### Desenvolvimento (infra no Docker, serviços na mão)

```bash
# Infra (Postgres + MinIO + vision para análise de vídeo)
docker compose up -d postgres minio vision

# API (migra e semeia o banco no startup) — http://localhost:5000/swagger
cd backend
dotnet run --project MyoTrack.Api --urls http://localhost:5000

# Worker (fila de jobs de IA) — em outro terminal
dotnet run --project MyoTrack.Worker

# Frontend — http://localhost:5173 (proxy de /api para :5000)
cd ../frontend
npm install
npm run dev
```

Os `appsettings.json` da API e do Worker já apontam para `localhost:5433` (Postgres — o compose expõe 5433 no host para não conflitar com um PostgreSQL nativo na 5432) e `localhost:9000` (MinIO) com as credenciais de desenvolvimento do compose. Para habilitar a IA no Worker, defina a variável de ambiente `Llm__AnthropicApiKey` (ou edite `backend/MyoTrack.Worker/appsettings.json`); sem a chave, treino/dieta usam só o motor de regras e a análise de foto falha com mensagem amigável.

Para testar o **e-mail de recuperação de senha** sem SMTP, basta rodar em `Development`: o link aparece no log da API. Para enviar de verdade, preencha `Email__User`/`Email__Password` (no Gmail, uma *senha de app* — a senha da conta não funciona com 2FA). Para o **login com Google**, crie um OAuth client tipo *Web application* no Google Cloud Console e autorize o redirect `http://localhost:5173/api/auth/google/callback` (o Vite faz proxy de `/api`); em produção, o mesmo caminho sob o domínio do Caddy. As credenciais vão em `Auth__Google__ClientId`/`Auth__Google__ClientSecret`.

> **Windows**: se o `dotnet` não estiver no PATH (instalação por usuário), prefixe os comandos com:
> `$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"`

Solução de problemas: erro `unable to get image ... open //./pipe/dockerDesktopLinuxEngine` significa que o Docker Desktop não está rodando — abra o aplicativo, aguarde o engine iniciar e rode o comando novamente.

Erro de build `MSB3027/MSB3021 ... O arquivo é bloqueado por: "MyoTrack.Api"`: a API ou o Worker ainda estão rodando e travam as DLLs (comportamento do Windows). Pare-os antes de compilar — Ctrl+C nos terminais ou, para garantir:

```powershell
Get-Process MyoTrack.Api, MyoTrack.Worker -ErrorAction SilentlyContinue | Stop-Process -Force
```

Como o `Directory.Build.props` desativa o apphost `.exe` em Debug, a API e o Worker normalmente rodam como `dotnet.exe` — se o comando acima não achar nada, procure pelo comando completo:

```powershell
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
  Where-Object CommandLine -match 'MyoTrack' | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

Um `MyoTrack.Api.exe`/`MyoTrack.Worker.exe` antigo pode sobrar no `bin` (de builds anteriores ao props) e voltar a ser executado; se aparecer no erro de lock, apague o `.exe` do `bin` além de parar o processo.

Iniciar API e Worker ao mesmo tempo também pode falhar com `CS2012`/arquivo em uso (os dois `dotnet run` compilam os mesmos projetos em paralelo) — espere a API imprimir "Now listening" antes de subir o Worker, ou compile antes (`dotnet build`) e rode com `dotnet run --no-build`.

Erro `Uma política de Controle de Aplicativo bloqueou este arquivo` (Smart App Control) ao rodar ou testar: o SAC avalia cada binário novo por hash e às vezes bloqueia uma DLL recém-compilada. O `backend/Directory.Build.props` já desativa o apphost `.exe` e o build determinístico em Debug/Windows — se o bloqueio acontecer, um rebuild completo gera hashes novos e destrava:

```powershell
dotnet build /t:Rebuild
```

## Deploy (VPS)

Mesmo fluxo do "Tudo via Docker" acima, mas preencha também as senhas e a chave JWT no `.env`. Para TLS automático, troque `:80` pelo domínio no `deploy/Caddyfile`.

## Status do roadmap

- [x] **Fase 0 — Fundação**: solução .NET, auth (Identity + JWT), modelo de dados + migration, seeds (exercícios e alimentos TACO), fila de jobs no Postgres, compose, CI. Autenticação completa: **recuperação de senha por e-mail** (link válido por 24 h, resposta idêntica exista ou não a conta para não revelar cadastros, sessões abertas encerradas ao trocar a senha, limite de 10 pedidos/15 min por IP) e **login com Google** (OAuth2 authorization-code servidor-a-servidor; a SPA recebe um código de uso único válido por 2 min em vez dos tokens na URL). Sem `EMAIL_USER`/`EMAIL_PASSWORD` o e-mail só vai para o log e a opção some da tela; sem `GOOGLE_OAUTH_CLIENT_ID`/`SECRET` o botão do Google não aparece.
- [x] **Fase 1 — MVP**: perfil/onboarding com consentimento LGPD, geração de treino (regras + Claude API com fallback) e dieta (TDEE determinístico + LLM), registro de treinos e medidas, dashboards de progressão. Modo treino guiado (`/treinar`): execução série a série com timer de descanso (som + vibração), cargas pré-preenchidas pela sugestão de progressão e salvamento da sessão ao finalizar. Progressão sugerida determinística (dupla progressão: fechou o teto de reps em todas as séries → sobe 2,5 kg tronco / 5 kg pernas) e recordes pessoais com 1RM estimado (Epley, séries ≤ 12 reps) no dashboard. Defina `ANTHROPIC_API_KEY` no `.env` para habilitar a personalização via LLM (sem a chave, o motor de regras gera os planos).
- [x] **Fase 2 — Análise de refeição por foto**: upload de foto (JPEG/PNG/WebP até 10 MB) → job assíncrono → Claude vision identifica itens e porções, macros oficiais vêm do catálogo TACO quando há correspondência. Modo "análise ilustrada" opcional: a IA anota itens e macros na própria foto (modelo de imagem do Gemini, `GEMINI_IMAGE_MODEL`; requer chave com billing — sem cota, a análise cai no modo padrão automaticamente). Edição manual das quantidades pelo usuário, limite diário configurável por plano (`Limits:Free:MaxMealAnalysesPerDay`, padrão 10; Pro 50) e log de tokens por usuário (`AiUsageLog`). Requer `ANTHROPIC_API_KEY` (sem a chave o job falha com mensagem amigável). Diário alimentar (`/diario`): as análises do dia somam contra as metas do plano de dieta ativo (consumido vs. meta de kcal e macros, no fuso do usuário), com navegação por dia, gráfico dos últimos 7 dias e opção "não contar" para fotos repetidas ou pratos não consumidos.
- [x] **Fase 3 — Análise de vídeo de exercício**: upload direto no MinIO via URL pré-assinada (até 100 MB / 60 s), serviço `vision` (Python + FastAPI + MediaPipe Pose) processa a ~12 fps, heurísticas declarativas para 24 exercícios (agachamento, afundo, terra, terra romeno, hip thrust, panturrilha em pé, extensão lombar, supino, flexão, mergulho em paralelas, desenvolvimento, remadas — curvada, baixa, serrote e alta —, puxada alta, roscas — direta, martelo e Scott —, tríceps na polia, barra fixa, elevações lateral e frontal e encolhimento; câmera lateral), contagem de repetições, feedback com pontos corretos e pontos de atenção da execução, score conservador ("não avaliável" em vez de feedback errado), vídeo com overlay do esqueleto (H.264) e erros com timestamps clicáveis no player. Limite diário configurável por plano (`Limits:Free:MaxVideoAnalysesPerDay`, padrão 5; Pro 20).
- [x] **Fase 4 — Produto/SaaS**: hardening LGPD (export completo dos dados em JSON e exclusão de conta em `/api/privacy`, retenção de mídia com expiração automática — vídeos 30 dias, fotos 90 dias, configurável em `Retention:*`), status de jobs em tempo real via SSE (com fallback para polling), supervisão humana dos planos (Trainer revisa treinos, Nutritionist revisa dietas, página `/revisao` + selo de revisão nos planos), billing com Stripe Checkout (plano Pro amplia limites diários de IA; sem `STRIPE_SECRET_KEY` os pagamentos ficam desabilitados e todos usam o plano gratuito) e regeneração de treino informada pelo histórico real de progressão (SetLogs das últimas 8 semanas entram no prompt do LLM). Cada exercício do treino tem link "ver como fazer" para um vídeo do TikTok — resolvido uma única vez por exercício na geração do plano (busca + validação via oEmbed, salvo no catálogo e compartilhado por todos os usuários; sem vídeo salvo, o link cai na busca do TikTok). Engajamento por IA: **relatório semanal** ("Sua semana em revisão" no dashboard) com métricas calculadas em código (treinos vs. planejado, volume e delta, recordes, aderência à dieta, peso) e narrativa curta do LLM — gerado automaticamente pelo Worker toda semana (1 chamada por usuário/semana) ou sob demanda; e **chat com o coach IA** (botão flutuante em todas as telas) que responde com o contexto real do usuário (perfil, planos ativos, últimas sessões), com guarda-corpos (sem diagnóstico/prescrição) e limite diário por plano (`Limits:Free:MaxCoachMessagesPerDay`, padrão 10; Pro 50).
