using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Domain.Services;

namespace MyoTrack.Infrastructure.Ai;

/// <summary>
/// Pipeline híbrida do plano: o motor de regras gera o esqueleto válido;
/// o LLM (quando configurado) personaliza dentro dele; o backend valida
/// tudo contra o catálogo e faixas seguras antes de persistir.
/// </summary>
public class WorkoutGenerationService(
    AppDbContext db,
    ILlmJsonClient llm,
    TikTokVideoService tikTok,
    ILogger<WorkoutGenerationService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> GenerateAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await db.UserProfiles.SingleOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException("Perfil não encontrado. Complete o onboarding antes de gerar o treino.");

        var catalog = await db.Exercises.AsNoTracking().ToListAsync(ct);
        var input = new WorkoutGenerationInput(
            profile.Goal,
            profile.ExperienceLevel,
            profile.TrainingDaysPerWeek,
            profile.PriorityMuscleGroups,
            profile.InjuryTags,
            profile.AvailableEquipment);

        var skeleton = WorkoutRuleEngine.Generate(input, catalog);
        var plan = skeleton;
        string? rawLlmOutput = null;

        if (llm.IsConfigured)
        {
            var progression = await SummarizeProgressionAsync(userId, ct);
            var llmResult = await PersonalizeWithLlmAsync(profile, skeleton, catalog, progression, ct);
            if (llmResult is not null)
            {
                rawLlmOutput = llmResult.Json;
                db.AiUsageLogs.Add(new AiUsageLog
                {
                    UserId = userId,
                    Operation = AnalysisJobType.WorkoutGeneration,
                    Model = llm.Model,
                    InputTokens = llmResult.InputTokens,
                    OutputTokens = llmResult.OutputTokens,
                });

                if (TryParseAndValidate(rawLlmOutput, skeleton, catalog, input, out var refined))
                    plan = refined;
                else
                    logger.LogWarning("Saída do LLM inválida para o usuário {UserId}; mantendo esqueleto por regras.", userId);
            }
        }

        // Arquiva planos ativos anteriores.
        var previous = await db.WorkoutPlans
            .Where(p => p.UserId == userId && p.Status == PlanStatus.Active)
            .ToListAsync(ct);
        foreach (var old in previous)
            old.Status = PlanStatus.Archived;

        var version = await db.WorkoutPlans.CountAsync(p => p.UserId == userId, ct) + 1;
        var entity = new WorkoutPlan
        {
            UserId = userId,
            Name = $"Treino {plan.Split} v{version}",
            Goal = profile.Goal,
            Split = plan.Split,
            Status = PlanStatus.Active,
            Version = version,
            GenerationInputJson = JsonSerializer.Serialize(input, JsonOptions),
            RawLlmOutputJson = rawLlmOutput,
            Days = plan.Days.Select(d => new WorkoutDay
            {
                Order = d.Order,
                Label = d.Label,
                Exercises = d.Exercises.Select(e => new WorkoutExercise
                {
                    ExerciseId = e.ExerciseId,
                    Order = d.Exercises.IndexOf(e) + 1,
                    Sets = e.Sets,
                    RepsMin = e.RepsMin,
                    RepsMax = e.RepsMax,
                    RestSeconds = e.RestSeconds,
                    Notes = e.Notes,
                }).ToList(),
            }).ToList(),
        };

        db.WorkoutPlans.Add(entity);
        await db.SaveChangesAsync(ct);

        // Vídeos explicativos do TikTok: resolvidos uma vez por exercício e
        // reaproveitados por todos os usuários. Best-effort — não falha o job.
        try
        {
            var exerciseIds = plan.Days.SelectMany(d => d.Exercises).Select(e => e.ExerciseId).Distinct().ToList();
            await tikTok.ResolveMissingAsync(exerciseIds, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falha ao resolver vídeos do TikTok para o plano {PlanId}.", entity.Id);
        }

        return entity.Id;
    }

    /// <summary>
    /// Resume as últimas 8 semanas de SetLogs por exercício (melhor carga, volume e frequência)
    /// para o LLM ajustar a progressão em vez de gerar um plano "do zero" a cada regeneração.
    /// </summary>
    private async Task<List<ExerciseProgression>> SummarizeProgressionAsync(Guid userId, CancellationToken ct)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-56));
        return await db.SetLogs.AsNoTracking()
            .Where(s => s.WorkoutSession.UserId == userId && s.WorkoutSession.Date >= since)
            .GroupBy(s => new { s.ExerciseId, s.Exercise.Name })
            .Select(g => new ExerciseProgression(
                g.Key.Name,
                g.Max(s => s.LoadKg),
                Math.Round(g.Sum(s => s.LoadKg * s.Reps), 0),
                g.Select(s => s.WorkoutSessionId).Distinct().Count()))
            .OrderByDescending(p => p.VolumeTotalKg)
            .Take(20)
            .ToListAsync(ct);
    }

    private async Task<LlmJsonResult?> PersonalizeWithLlmAsync(
        UserProfile profile, GeneratedWorkout skeleton, List<Exercise> catalog,
        List<ExerciseProgression> progression, CancellationToken ct)
    {
        var eligibleIds = skeleton.Days.SelectMany(d => d.Exercises).Select(e => e.ExerciseId).ToHashSet();
        var eligible = catalog
            .Where(e => !e.ContraindicationTags.Intersect(profile.InjuryTags, StringComparer.OrdinalIgnoreCase).Any())
            .Select(e => new { e.Id, e.Name, Grupo = e.PrimaryMuscleGroup.ToString(), Composto = e.IsCompound })
            .ToList();

        var system = """
            Você é um personal trainer experiente. Você recebe um esqueleto de treino gerado por regras
            e pode personalizá-lo: trocar exercícios por equivalentes da lista permitida, ajustar a ordem
            e escrever observações curtas e úteis em português para cada exercício.
            Regras invioláveis: use apenas exerciseId presentes na lista permitida; mantenha o mesmo número
            de dias; séries entre 2 e 5; repetições entre 5 e 30; descanso entre 30 e 240 segundos.
            Quando houver histórico de progressão, prefira manter os exercícios que o aluno já pratica com
            boa frequência e use as melhores cargas como referência nas observações (progressão gradual,
            nunca saltos maiores que ~10% de carga).
            """;

        var user = JsonSerializer.Serialize(new
        {
            perfil = new
            {
                objetivo = profile.Goal.ToString(),
                nivel = profile.ExperienceLevel.ToString(),
                diasPorSemana = profile.TrainingDaysPerWeek,
                biotipo = profile.Biotype?.ToString(),
                lesoes = profile.InjuryNotes,
                gruposPriorizados = profile.PriorityMuscleGroups.Select(g => g.ToString()),
            },
            // Últimas 8 semanas de treino real: melhor carga, volume total e nº de sessões por exercício.
            progressaoRecente = progression.Select(p => new
            {
                exercicio = p.ExerciseName,
                melhorCargaKg = p.BestLoadKg,
                volumeTotalKg = p.VolumeTotalKg,
                sessoes = p.Sessions,
            }),
            esqueleto = skeleton,
            exerciciosPermitidos = eligible,
        }, JsonOptions);

        return await llm.GenerateJsonAsync(system, user, WorkoutSchema(), ct);
    }

    private static bool TryParseAndValidate(
        string json, GeneratedWorkout skeleton, List<Exercise> catalog,
        WorkoutGenerationInput input, out GeneratedWorkout result)
    {
        result = skeleton;
        try
        {
            var parsed = JsonSerializer.Deserialize<LlmWorkout>(json, JsonOptions);
            if (parsed?.Days is null || parsed.Days.Count != skeleton.Days.Count)
                return false;

            var allowed = catalog
                .Where(e => !e.ContraindicationTags.Intersect(input.InjuryTags, StringComparer.OrdinalIgnoreCase).Any())
                .ToDictionary(e => e.Id);

            var days = new List<GeneratedDay>();
            foreach (var day in parsed.Days.OrderBy(d => d.Order))
            {
                if (day.Exercises is null || day.Exercises.Count == 0)
                    return false;

                var exercises = new List<GeneratedExercise>();
                foreach (var e in day.Exercises)
                {
                    if (!allowed.TryGetValue(e.ExerciseId, out var exercise)) return false;
                    if (e.Sets is < 2 or > 5) return false;
                    if (e.RepsMin < 5 || e.RepsMax > 30 || e.RepsMin > e.RepsMax) return false;
                    if (e.RestSeconds is < 30 or > 240) return false;

                    exercises.Add(new GeneratedExercise(
                        e.ExerciseId, exercise.Name, e.Sets, e.RepsMin, e.RepsMax, e.RestSeconds, e.Notes));
                }
                days.Add(new GeneratedDay(day.Order, day.Label ?? $"Dia {day.Order}", exercises));
            }

            result = new GeneratedWorkout(skeleton.Split, days);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Dictionary<string, JsonElement> WorkoutSchema()
    {
        const string schema = """
            {
              "type": "object",
              "properties": {
                "days": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "order": { "type": "integer" },
                      "label": { "type": "string" },
                      "exercises": {
                        "type": "array",
                        "items": {
                          "type": "object",
                          "properties": {
                            "exerciseId": { "type": "integer" },
                            "sets": { "type": "integer" },
                            "repsMin": { "type": "integer" },
                            "repsMax": { "type": "integer" },
                            "restSeconds": { "type": "integer" },
                            "notes": { "type": "string" }
                          },
                          "required": ["exerciseId", "sets", "repsMin", "repsMax", "restSeconds"],
                          "additionalProperties": false
                        }
                      }
                    },
                    "required": ["order", "label", "exercises"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["days"],
              "additionalProperties": false
            }
            """;
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schema)!;
    }

    private sealed record ExerciseProgression(string ExerciseName, decimal BestLoadKg, decimal VolumeTotalKg, int Sessions);
    private sealed record LlmWorkout(List<LlmDay> Days);
    private sealed record LlmDay(int Order, string? Label, List<LlmExercise> Exercises);
    private sealed record LlmExercise(int ExerciseId, int Sets, int RepsMin, int RepsMax, int RestSeconds, string? Notes);
}
