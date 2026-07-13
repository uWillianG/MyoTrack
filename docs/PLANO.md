# MyoTrack — Plano de Arquitetura e Roadmap

## Contexto

Sistema web de academia e saúde ("personal trainer + nutricionista digital") em C#/.NET, como **produto real (SaaS)** com usuários pagantes no horizonte. Gera treinos e dietas personalizados, analisa execução de exercícios por vídeo e refeições por foto usando IA, e acompanha progressão de carga e medidas.

**Restrições definidas com o usuário:**
- Orçamento de IA no MVP: **~US$ 50/mês** → APIs de LLM com controle rígido de custo; visão computacional pesada (vídeo) fica self-hosted/open-source.
- Deploy: **VPS com Docker Compose** (custo fixo baixo, agnóstico de nuvem).
- Front-end: recomendação minha (abaixo).
- LGPD é requisito real (dados de saúde = dados sensíveis, art. 5º, II).

---

## 1. Decisões de stack (com justificativa)

| Camada | Escolha | Por quê |
|---|---|---|
| Backend | **ASP.NET Core 9 Web API** (REST) | Requisito do usuário; maduro, ótimo para workers em background. |
| Banco | **PostgreSQL 16 + EF Core (Npgsql)** | Gratuito, JSONB para payloads de IA (planos gerados, resultados de análise), ótimo em VPS, extensões úteis (pg_trgm p/ busca de alimentos). SQL Server custaria licença; SQLite não serve para SaaS. |
| Front-end | **React + TypeScript + Vite** (SPA) | As telas críticas são gráficos (Recharts) e upload/preview de vídeo/foto com overlay de pose — o ecossistema JS é muito superior ao do Blazor nisso (canvas, players, MediaPipe roda até no browser se quisermos). Blazor manteria um stack só, mas travaria justamente as features diferenciais do produto. |
| Fila/async | **PostgreSQL como fila no MVP** (tabela `jobs` + `FOR UPDATE SKIP LOCKED`, consumida por `BackgroundService`) → migrar p/ RabbitMQ se o volume exigir | Menos uma peça de infra no VPS; padrão bem estabelecido; a interface `IJobQueue` abstrai a troca futura. |
| Storage de mídia | **MinIO** (S3-compatível, self-hosted no compose) | API S3 = migração trivial para nuvem depois; URLs pré-assinadas para upload direto do browser (mídia não passa pela API). |
| Auth | **ASP.NET Core Identity + JWT** (access + refresh) | Nativo, suficiente; perfis via roles (`Student`, `Trainer`, `Nutritionist`, `Admin`). |
| Worker de visão | **Serviço Python separado** (FastAPI + MediaPipe/Ultralytics) no mesmo compose | Pose estimation não tem ecossistema decente em .NET; isolar em container Python é o caminho padrão. Comunicação via fila + storage compartilhado. |
| LLM | **API externa (Claude ou OpenAI)** com prompts estruturados + validação | Ver seção 4. |

## 2. Arquitetura geral

```
Browser (React SPA)
  │  HTTPS/JSON            ┌────────────────────────────────────┐
  ▼                        │ VPS — Docker Compose               │
[Caddy/nginx (TLS)] ──────►│                                    │
  │                        │  api        ASP.NET Core Web API   │
  │ upload direto          │  worker     .NET BackgroundService │
  │ (URL pré-assinada)     │             (jobs LLM, notificações)│
  └───────────────────────►│  vision     Python FastAPI +       │
                           │             MediaPipe/YOLO         │
                           │  postgres   dados + fila de jobs   │
                           │  minio      vídeos/fotos           │
                           └────────────────────────────────────┘
                                    │ HTTPS (saída)
                                    ▼
                          LLM API (Claude/OpenAI) — geração de
                          treino/dieta e análise de refeição
```

**Fluxo assíncrono (vídeo/foto):**
1. SPA pede URL pré-assinada à API → sobe mídia direto no MinIO.
2. API cria `AnalysisJob` (status `Pending`) na tabela de jobs e responde 202.
3. Worker (vision p/ vídeo, .NET worker p/ refeição via LLM) consome o job, processa, grava resultado em JSONB, marca `Completed`/`Failed`.
4. SPA acompanha via polling do endpoint de status (SSE/SignalR fica como melhoria futura).

Geração de treino/dieta também roda como job assíncrono (chamada LLM leva 10–30s).

## 3. Modelo de dados (entidades principais)

**Identidade e perfil**
- `User` (Identity) → `UserProfile`: data nasc., sexo, altura, biotipo (enum), nível de experiência, objetivo (enum), dias/semana, lesões/limitações (texto + tags), equipamentos disponíveis, restrições e preferências alimentares.
- `ConsentRecord`: tipo de consentimento (dados de saúde, mídia para IA), versão do termo, timestamp, revogação — LGPD exige trilha.

**Treino**
- `Exercise` (catálogo global): nome, grupo muscular, equipamento, instruções, contraindicações, `MediaUrl`.
- `WorkoutPlan`: usuário, objetivo, split (ABC, PPL...), status (ativo/arquivado), `GenerationInput` e `RawLlmOutput` (JSONB, p/ auditoria e regeneração), versão.
- `WorkoutDay` → `WorkoutExercise`: exercício, ordem, séries, faixa de reps, carga sugerida, descanso (s), observações.
- `WorkoutSession` (execução real): data → `SetLog`: exercício, série nº, reps feitas, carga usada, RPE. **É daqui que sai a progressão de carga.**

**Dieta**
- `FoodItem` (catálogo nutricional): nome, kcal/proteína/carbo/gordura por 100g, fonte (TACO/TBCA — tabelas brasileiras abertas).
- `DietPlan` (versão, JSONB de geração, análoga a WorkoutPlan) → `Meal` → `MealItem` (food, quantidade em g).

**Progresso**
- `BodyMeasurement`: data, peso, % gordura (opcional), circunferências.
- Volume/carga por exercício são derivados de `SetLog` (queries agregadas, não tabela própria).

**IA**
- `AnalysisJob`: tipo (`ExerciseVideo` | `MealPhoto`), usuário, `MediaKey` (MinIO), status, tentativas, erro.
- `ExerciseVideoAnalysis`: job, exercício alegado, resultado JSONB (ângulos por frame, erros detectados com timestamps), score, `OverlayVideoKey`.
- `MealPhotoAnalysis`: job, alimentos detectados + porções estimadas + macros (JSONB), total estimado, flag `UserAdjusted` (usuário corrige a estimativa — vira dado de qualidade).

## 4. Estratégia de IA por módulo

### 4.1 Geração de treino — Híbrido: regras + LLM (recomendado)
- **Regras em C#** garantem o esqueleto válido: split conforme dias/semana, volume por grupo muscular conforme nível, exclusão de exercícios contraindicados pelas lesões/equipamentos (filtro no catálogo `Exercise`).
- **LLM** preenche/personaliza dentro desse esqueleto e gera as observações, retornando **JSON com schema fixo** (tool use / structured output), validado no backend (exercícios têm que existir no catálogo; volumes dentro de faixas seguras).
- Por quê híbrido: LLM puro alucina exercícios e volumes inseguros; regras puras geram treinos genéricos. Custo: ~US$ 0,01–0,03 por geração → irrelevante no orçamento.
- Regeneração: mesma pipeline recebendo histórico de `SetLog` resumido (progressão real) como contexto.

### 4.2 Geração de dieta — Determinístico primeiro, LLM para montagem
- **Cálculo de TDEE/macros é matemática** (Mifflin-St Jeor + fator de atividade + ajuste do objetivo) — fazer em C#, nunca delegar ao LLM.
- LLM monta as refeições escolhendo itens do catálogo `FoodItem` (TACO/TBCA) respeitando restrições/preferências; backend recalcula os macros dos itens escolhidos e ajusta quantidades para bater as metas (o LLM sugere, o código garante os números).

### 4.3 Análise de exercício por vídeo — MediaPipe Pose self-hosted
- **Recomendação: MediaPipe Pose (BlazePose)** no serviço Python: gratuito, roda em CPU (~tempo real em vídeo 720p), 33 keypoints 3D — suficiente para ângulos articulares.
- Pipeline: extrair pose por frame → calcular ângulos relevantes ao exercício → **regras heurísticas por exercício** (ex.: agachamento: profundidade do quadril vs. joelho, valgo de joelho, inclinação de tronco) → gerar lista de erros com timestamps + vídeo com overlay do esqueleto (OpenCV) salvo no MinIO.
- Alternativas descartadas: OpenPose (pesado, licença restritiva p/ uso comercial), APIs de terceiros de análise de movimento (caras e raras), modelo próprio (precisa de dataset rotulado — inviável agora).
- **Ponto de atenção**: as heurísticas por exercício são o trabalho de verdade deste módulo. Começar com 3–5 exercícios compostos (agachamento, supino, levantamento terra, remada, desenvolvimento) e expandir.
- Limitar vídeos: ≤ 60s, ≤ 100 MB, processar a 10–15 fps (não precisa de todos os frames).

### 4.4 Análise de refeição por foto — LLM multimodal + catálogo nutricional
- **Recomendação: LLM de visão via API** (Claude/GPT-4o): prompt estruturado retorna alimentos identificados + porção estimada em gramas → backend cruza com `FoodItem` (TACO) para os macros oficiais, em vez de confiar nos números do LLM.
- Custo ~US$ 0,01–0,02/foto → dentro do orçamento com limite de análises/dia por usuário no plano gratuito.
- Alternativa descartada: modelo próprio de food recognition (Food-101 etc.) — precisão ruim em comida brasileira e estimar porção por foto é problema em aberto; o LLM multimodal é hoje o melhor custo-benefício.
- **Transparência obrigatória na UI**: estimativa com margem de erro declarada (±20–30%) + edição manual pelo usuário (que alimenta `UserAdjusted`).

### Controle de custos (transversal)
- Tabela `AiUsageLog` (usuário, operação, tokens, custo) + limites por usuário/dia configuráveis + kill-switch global por teto mensal.
- Cache de gerações: mesmo input de perfil ⇒ não regera.

## 5. Roadmap faseado

**Fase 0 — Fundação (1–2 semanas de esforço)**
Solução .NET (API + Worker + Domain + Infrastructure), docker-compose (api, worker, postgres, minio, caddy), Identity + JWT, CI básico, esqueleto React (Vite + TS + Tailwind + Recharts), seed do catálogo `Exercise` (~150 exercícios) e `FoodItem` (importar TACO/TBCA).

**Fase 1 — MVP sem visão computacional**
Onboarding/perfil + consentimentos; geração de treino (regras + LLM); geração de dieta (TDEE em código + LLM); registro de sessões de treino (`SetLog`) e medidas; dashboards de progressão (carga por exercício, volume semanal, peso corporal). *Critério de saída: usuário completa o ciclo perfil → treino+dieta → registra treinos → vê evolução.*

**Fase 2 — Análise de refeição por foto**
Upload pré-assinado + `AnalysisJob` + worker LLM de visão; UI de resultado (macros em gráfico, edição manual); limites de uso e `AiUsageLog`. *É a feature de IA de menor risco técnico — vem antes do vídeo.*

**Fase 3 — Análise de vídeo de exercício**
Serviço Python (MediaPipe) no compose; heurísticas para 3–5 exercícios compostos; overlay de esqueleto + lista de erros com timestamps na UI; player com marcadores.

**Fase 4 — Produto/SaaS**
Regeneração automática de treino baseada em progressão; papel Trainer/Nutritionist (supervisão humana dos planos gerados); billing (Stripe); SSE/SignalR para status de jobs; hardening LGPD (export/exclusão de dados do titular, retenção de mídia com expiração).

## 6. Riscos e pontos de atenção

1. **Responsabilidade sobre saúde (maior risco)**: dieta/treino gerados por IA para pessoa com condição não declarada. Mitigação: disclaimers explícitos + PAR-Q no onboarding + guard-rails de volume/calorias no código (nunca déficit < TMB, etc.) + Fase 4 introduz revisão humana. Avaliar parecer jurídico antes de cobrar.
2. **Precisão da análise de refeição**: ±20–30% no melhor caso. Mitigar com transparência e edição manual — vender como "estimativa assistida", não como medição.
3. **Heurísticas de vídeo por exercício** são artesanais e sensíveis a ângulo de câmera. Mitigar: instruir enquadramento (câmera lateral, corpo inteiro), começar com poucos exercícios, score conservador ("não foi possível avaliar" > feedback errado).
4. **Custo de LLM em escala**: US$ 50/mês ≈ ~2.500–5.000 operações de IA. Limites por usuário desde a Fase 2; precificação do plano pago deve cobrir o custo marginal de IA.
5. **LGPD**: dados de saúde exigem consentimento específico e destacado; mídia com rosto é biométrico em potencial → retenção curta de vídeos (ex.: 30 dias), criptografia at-rest no MinIO, e direito de exclusão implementado de verdade (Fase 4, mas modelado desde a Fase 0 via `ConsentRecord`).
6. **VPS único é ponto único de falha**: backups automatizados do Postgres + MinIO desde a Fase 0; a arquitetura (S3 API, fila abstraída) permite migrar para nuvem sem reescrita.

## 7. Verificação (por fase)

- **Fase 0**: `docker compose up` sobe tudo; healthchecks da API respondem; registro/login via Swagger; seeds visíveis no banco.
- **Fase 1**: fluxo E2E manual — criar perfil, gerar treino e dieta (validar JSON do LLM contra o schema em testes de integração com respostas gravadas/mock), registrar 2 semanas fictícias de `SetLog`, conferir gráficos. Testes unitários para TDEE/macros e regras de split (são determinísticos — cobrir bem).
- **Fase 2**: subir fotos de pratos conhecidos (ex.: prato com quantidades pesadas) e comparar estimativa vs. real; testar job com falha (API fora) → retry e status `Failed` visível na UI.
- **Fase 3**: vídeos de referência com erros propositais (agachamento raso, valgo) → sistema aponta os erros certos; vídeo correto → não gera falso positivo.
