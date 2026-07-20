using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Domain.Services;

namespace MyoTrack.Infrastructure.Ai;

/// <summary>
/// Relatório semanal: os números (treinos, volume, recordes, dieta, peso) são
/// calculados em código; o LLM só escreve a narrativa curta em cima deles.
/// Uma chamada de IA por usuário por semana — custo marginal irrisório.
/// Semanas em UTC (segunda a domingo).
/// </summary>
public class WeeklyReportService(
    AppDbContext db,
    ILlmJsonClient llm,
    ILogger<WeeklyReportService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ReportJobInput(DateOnly WeekStart);

    public async Task<Guid> GenerateAsync(AnalysisJob job, CancellationToken ct = default)
    {
        var input = string.IsNullOrEmpty(job.InputJson)
            ? null
            : JsonSerializer.Deserialize<ReportJobInput>(job.InputJson, JsonOptions);
        var weekStart = input?.WeekStart
            ?? throw new InvalidOperationException("Job de relatório sem a semana de referência (weekStart).");
        var weekEnd = weekStart.AddDays(7);

        var metrics = await ComputeMetricsAsync(job.UserId, weekStart, weekEnd, ct);
        var metricsJson = JsonSerializer.Serialize(metrics, JsonOptions);

        string? narrativeJson = null;
        if (llm.IsConfigured)
        {
            var system = """
                Você é o coach do MyoTrack, um app de treino e nutrição. Com base nas métricas
                da semana do usuário (calculadas pelo sistema), escreva o resumo da semana em
                português do Brasil:
                - summary: 2 a 3 frases, tom positivo e honesto (sem exageros quando a semana foi fraca).
                - highlights: 2 a 4 conquistas concretas tiradas dos dados.
                - recommendations: 2 a 3 ações objetivas e realistas para a próxima semana.
                Não invente números que não estejam nas métricas. Campos null significam "sem dados" —
                nesse caso, incentive a registrar. Sem conselhos médicos.
                """;

            var result = await llm.GenerateJsonAsync(system, metricsJson, NarrativeSchema(), ct)
                ?? throw new TransientAiException("A geração do relatório falhou. Tente novamente.");

            db.AiUsageLogs.Add(new AiUsageLog
            {
                UserId = job.UserId,
                Operation = AnalysisJobType.WeeklyReport,
                Model = llm.Model,
                InputTokens = result.InputTokens,
                OutputTokens = result.OutputTokens,
            });
            narrativeJson = result.Json;
        }
        else
        {
            logger.LogInformation("LLM não configurado — relatório da semana {WeekStart} sai só com métricas.", weekStart);
        }

        // Upsert: regerar a mesma semana substitui o conteúdo (índice único UserId+WeekStart).
        var report = await db.WeeklyReports
            .SingleOrDefaultAsync(r => r.UserId == job.UserId && r.WeekStart == weekStart, ct);
        if (report is null)
        {
            report = new WeeklyReport { UserId = job.UserId, WeekStart = weekStart };
            db.WeeklyReports.Add(report);
        }
        report.MetricsJson = metricsJson;
        report.NarrativeJson = narrativeJson;
        report.CreatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return report.Id;
    }

    private async Task<object> ComputeMetricsAsync(Guid userId, DateOnly weekStart, DateOnly weekEnd, CancellationToken ct)
    {
        var prevWeekStart = weekStart.AddDays(-7);

        // --- Treinos: sessões da semana e volume; semana anterior para o delta.
        var sessions = await db.WorkoutSessions.AsNoTracking()
            .Include(s => s.Sets)
            .Where(s => s.UserId == userId && s.Date >= prevWeekStart && s.Date < weekEnd)
            .ToListAsync(ct);
        var weekSessions = sessions.Where(s => s.Date >= weekStart).ToList();
        var volumeKg = Math.Round(weekSessions.SelectMany(s => s.Sets).Sum(x => x.Reps * x.LoadKg));
        var prevVolumeKg = Math.Round(sessions.Where(s => s.Date < weekStart)
            .SelectMany(s => s.Sets).Sum(x => x.Reps * x.LoadKg));

        var plannedPerWeek = await db.WorkoutPlans.AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == PlanStatus.Active)
            .Select(p => (int?)p.Days.Count)
            .SingleOrDefaultAsync(ct);

        // --- Recordes: carga máxima da semana acima de todo o histórico anterior.
        var trainedExerciseIds = weekSessions.SelectMany(s => s.Sets).Select(x => x.ExerciseId).Distinct().ToList();
        var prs = new List<string>();
        if (trainedExerciseIds.Count > 0)
        {
            var allLogs = await db.SetLogs.AsNoTracking()
                .Where(x => x.WorkoutSession.UserId == userId &&
                            trainedExerciseIds.Contains(x.ExerciseId) &&
                            x.WorkoutSession.Date < weekEnd)
                .Select(x => new { x.ExerciseId, x.Exercise.Name, x.WorkoutSession.Date, x.LoadKg })
                .ToListAsync(ct);
            prs = allLogs
                .GroupBy(x => new { x.ExerciseId, x.Name })
                .Where(g =>
                {
                    var before = g.Where(x => x.Date < weekStart).ToList();
                    // Primeira vez no exercício não é recorde — precisa de histórico para superar.
                    return before.Count > 0 &&
                           g.Where(x => x.Date >= weekStart).Max(x => (decimal?)x.LoadKg) > before.Max(x => x.LoadKg);
                })
                .Select(g => g.Key.Name)
                .Take(5)
                .ToList();
        }

        // --- Nutrição: dias com diário e médias vs. metas do plano ativo.
        var weekStartUtc = new DateTimeOffset(weekStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var weekEndUtc = new DateTimeOffset(weekEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var meals = await db.MealPhotoAnalyses.AsNoTracking()
            .Where(a => a.UserId == userId && !a.ExcludedFromDiary &&
                        a.CreatedAt >= weekStartUtc && a.CreatedAt < weekEndUtc)
            .Select(a => new { a.CreatedAt, a.TotalKcal, a.TotalProteinG })
            .ToListAsync(ct);
        var mealDays = meals
            .GroupBy(m => DateOnly.FromDateTime(m.CreatedAt.UtcDateTime))
            .Select(g => new { Kcal = g.Sum(m => m.TotalKcal), ProteinG = g.Sum(m => m.TotalProteinG) })
            .ToList();

        var dietTargets = await db.DietPlans.AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == PlanStatus.Active)
            .Select(p => new { p.TargetKcal, p.TargetProteinG })
            .SingleOrDefaultAsync(ct);

        // --- Peso: última medição da semana vs. última anterior à semana.
        var weights = await db.BodyMeasurements.AsNoTracking()
            .Where(m => m.UserId == userId && m.WeightKg != null && m.Date < weekEnd)
            .OrderBy(m => m.Date)
            .Select(m => new { m.Date, m.WeightKg })
            .ToListAsync(ct);
        var endWeight = weights.LastOrDefault(w => w.Date >= weekStart);
        var startWeight = weights.LastOrDefault(w => w.Date < weekStart);

        if (weekSessions.Count == 0 && mealDays.Count == 0 && endWeight is null)
            throw new InvalidOperationException(
                "Sem atividade registrada na semana — treine, registre refeições ou o peso para ter um relatório.");

        return new
        {
            WeekStart = weekStart,
            WeekEnd = weekEnd.AddDays(-1),
            Workouts = new
            {
                Sessions = weekSessions.Count,
                PlannedPerWeek = plannedPerWeek,
                VolumeKg = volumeKg,
                PreviousVolumeKg = prevVolumeKg > 0 ? (decimal?)prevVolumeKg : null,
                Prs = prs,
            },
            Nutrition = new
            {
                DaysLogged = mealDays.Count,
                AvgKcal = mealDays.Count > 0 ? (decimal?)Math.Round(mealDays.Average(d => d.Kcal)) : null,
                TargetKcal = dietTargets?.TargetKcal,
                AvgProteinG = mealDays.Count > 0 ? (decimal?)Math.Round(mealDays.Average(d => d.ProteinG)) : null,
                TargetProteinG = dietTargets?.TargetProteinG,
            },
            Weight = new
            {
                StartKg = startWeight?.WeightKg,
                EndKg = endWeight?.WeightKg,
            },
        };
    }

    private static Dictionary<string, JsonElement> NarrativeSchema()
    {
        const string schema = """
            {
              "type": "object",
              "properties": {
                "summary": { "type": "string", "description": "Resumo da semana em 2-3 frases" },
                "highlights": { "type": "array", "items": { "type": "string" }, "description": "Conquistas concretas da semana" },
                "recommendations": { "type": "array", "items": { "type": "string" }, "description": "Ações objetivas para a próxima semana" }
              },
              "required": ["summary", "highlights", "recommendations"],
              "additionalProperties": false
            }
            """;
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schema)!;
    }
}
