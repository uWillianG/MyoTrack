using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;

namespace MyoTrack.Infrastructure.Ai;

/// <summary>
/// Chat com o coach IA: responde dúvidas de treino/nutrição usando o contexto
/// real do usuário (perfil, planos ativos, últimas sessões). Roda como job na
/// fila — toda chamada de LLM fica no Worker, com a chave em um lugar só.
/// </summary>
public class CoachChatService(AppDbContext db, ILlmJsonClient llm)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Mensagens recentes enviadas ao LLM como transcrição da conversa.</summary>
    private const int HistoryLimit = 20;

    public async Task<Guid> ReplyAsync(AnalysisJob job, CancellationToken ct = default)
    {
        if (!llm.IsConfigured)
            throw new InvalidOperationException("Coach indisponível: chave da API de IA não configurada no servidor.");

        var history = await db.CoachMessages.AsNoTracking()
            .Where(m => m.UserId == job.UserId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(HistoryLimit)
            .ToListAsync(ct);
        history.Reverse();
        if (history.Count == 0 || !history[^1].FromUser)
            throw new InvalidOperationException("Não há pergunta do usuário para responder.");

        var system = """
            Você é o coach virtual do MyoTrack, um app de treino e nutrição. Responda como um
            personal trainer e nutricionista: direto, motivador e prático, em português do Brasil.
            Personalize usando o contexto fornecido (perfil, planos e progressão do usuário).
            Regras:
            - Responda apenas sobre treino, nutrição, recuperação, hábitos e uso do app; fora
              disso, redirecione com bom humor para os temas do coach.
            - Não diagnostique condições de saúde nem prescreva medicamentos ou suplementos;
              em caso de dor persistente, lesão ou condição médica, recomende procurar um
              profissional de saúde.
            - Não invente dados que não estejam no contexto; se não souber, diga que não sabe.
            - Seja conciso: no máximo ~150 palavras, texto corrido ou listas curtas, sem markdown.
            """;

        var user = JsonSerializer.Serialize(new
        {
            contexto = await BuildContextAsync(job.UserId, ct),
            conversa = history.Select(m => new
            {
                de = m.FromUser ? "usuario" : "coach",
                texto = m.Content,
            }),
        }, JsonOptions);

        var result = await llm.GenerateJsonAsync(system, user, ReplySchema(), ct)
            ?? throw new TransientAiException("O coach não conseguiu responder agora. Tente novamente.");

        db.AiUsageLogs.Add(new AiUsageLog
        {
            UserId = job.UserId,
            Operation = AnalysisJobType.CoachChat,
            Model = llm.Model,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
        });

        var parsed = JsonSerializer.Deserialize<CoachReply>(result.Json, JsonOptions);
        if (string.IsNullOrWhiteSpace(parsed?.Reply))
            throw new TransientAiException("O coach não conseguiu responder agora. Tente novamente.");

        var message = new CoachMessage
        {
            UserId = job.UserId,
            FromUser = false,
            Content = parsed.Reply.Trim(),
        };
        db.CoachMessages.Add(message);
        await db.SaveChangesAsync(ct);
        return message.Id;
    }

    /// <summary>Snapshot compacto do usuário — o suficiente para personalizar sem estourar tokens.</summary>
    private async Task<object> BuildContextAsync(Guid userId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == userId, ct);

        var workout = await db.WorkoutPlans.AsNoTracking()
            .Include(p => p.Days.OrderBy(d => d.Order))
                .ThenInclude(d => d.Exercises.OrderBy(e => e.Order))
                    .ThenInclude(e => e.Exercise)
            .SingleOrDefaultAsync(p => p.UserId == userId && p.Status == PlanStatus.Active, ct);

        var diet = await db.DietPlans.AsNoTracking()
            .Include(p => p.Meals.OrderBy(m => m.Order))
            .SingleOrDefaultAsync(p => p.UserId == userId && p.Status == PlanStatus.Active, ct);

        var recentSessions = await db.WorkoutSessions.AsNoTracking()
            .Include(s => s.Sets)
                .ThenInclude(s => s.Exercise)
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.Date)
            .Take(5)
            .ToListAsync(ct);

        return new
        {
            dataAtual = DateOnly.FromDateTime(DateTime.UtcNow),
            perfil = profile is null ? null : new
            {
                objetivo = profile.Goal.ToString(),
                experiencia = profile.ExperienceLevel.ToString(),
                diasDeTreinoPorSemana = profile.TrainingDaysPerWeek,
                lesoes = profile.InjuryTags,
                observacoesLesoes = profile.InjuryNotes,
                restricoesAlimentares = profile.DietaryRestrictions,
                preferenciasAlimentares = profile.FoodPreferences,
            },
            treinoAtivo = workout is null ? null : new
            {
                split = workout.Split,
                dias = workout.Days.Select(d => new
                {
                    d.Label,
                    exercicios = d.Exercises.Select(e =>
                        $"{e.Exercise.Name} {e.Sets}x{e.RepsMin}-{e.RepsMax}"),
                }),
            },
            dietaAtiva = diet is null ? null : new
            {
                metas = new { kcal = diet.TargetKcal, proteinaG = diet.TargetProteinG, carboG = diet.TargetCarbsG, gorduraG = diet.TargetFatG },
                refeicoes = diet.Meals.Select(m => m.Name),
            },
            ultimasSessoes = recentSessions.Select(s => new
            {
                data = s.Date,
                volumeKg = Math.Round(s.Sets.Sum(x => x.Reps * x.LoadKg)),
                melhoresSeries = s.Sets
                    .GroupBy(x => x.Exercise.Name)
                    .Select(g => $"{g.Key}: {g.Max(x => x.LoadKg)} kg"),
            }),
        };
    }

    private static Dictionary<string, JsonElement> ReplySchema()
    {
        const string schema = """
            {
              "type": "object",
              "properties": {
                "reply": { "type": "string", "description": "Resposta do coach ao usuário, em português" }
              },
              "required": ["reply"],
              "additionalProperties": false
            }
            """;
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schema)!;
    }

    private sealed record CoachReply(string Reply);
}
