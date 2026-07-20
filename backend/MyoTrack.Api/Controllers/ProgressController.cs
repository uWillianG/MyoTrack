using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Domain.Services;
using MyoTrack.Infrastructure;

namespace MyoTrack.Api.Controllers;

[Route("api/progress")]
public class ProgressController(AppDbContext db) : ApiControllerBase
{
    /// <summary>Progressão de carga (máxima e volume) por sessão para um exercício.</summary>
    [HttpGet("exercises/{exerciseId:int}")]
    public async Task<IActionResult> ByExercise(int exerciseId)
    {
        var points = await db.SetLogs.AsNoTracking()
            .Where(s => s.ExerciseId == exerciseId && s.WorkoutSession.UserId == CurrentUserId)
            .GroupBy(s => s.WorkoutSession.Date)
            .Select(g => new
            {
                Date = g.Key,
                MaxLoadKg = g.Max(s => s.LoadKg),
                VolumeKg = g.Sum(s => s.Reps * s.LoadKg),
            })
            .OrderBy(p => p.Date)
            .ToListAsync();

        return Ok(points);
    }

    /// <summary>Exercícios que o usuário já registrou (para popular o seletor de gráfico).</summary>
    [HttpGet("exercises")]
    public async Task<IActionResult> LoggedExercises() =>
        Ok(await db.SetLogs.AsNoTracking()
            .Where(s => s.WorkoutSession.UserId == CurrentUserId)
            .GroupBy(s => new { s.ExerciseId, s.Exercise.Name })
            .Select(g => new { g.Key.ExerciseId, g.Key.Name, Sessions = g.Select(x => x.WorkoutSessionId).Distinct().Count() })
            .OrderByDescending(e => e.Sessions)
            .ToListAsync());

    /// <summary>Volume total de treino por semana (ISO, aproximada pela segunda-feira).</summary>
    [HttpGet("volume")]
    public async Task<IActionResult> WeeklyVolume()
    {
        var sets = await db.SetLogs.AsNoTracking()
            .Where(s => s.WorkoutSession.UserId == CurrentUserId)
            .Select(s => new { s.WorkoutSession.Date, Volume = s.Reps * s.LoadKg })
            .ToListAsync();

        var weekly = sets
            .GroupBy(s => s.Date.AddDays(-(((int)s.Date.DayOfWeek + 6) % 7)))
            .Select(g => new { WeekStart = g.Key, VolumeKg = g.Sum(s => s.Volume) })
            .OrderBy(w => w.WeekStart);

        return Ok(weekly);
    }

    /// <summary>Série temporal de peso corporal.</summary>
    [HttpGet("weight")]
    public async Task<IActionResult> Weight() =>
        Ok(await db.BodyMeasurements.AsNoTracking()
            .Where(m => m.UserId == CurrentUserId && m.WeightKg != null)
            .OrderBy(m => m.Date)
            .Select(m => new { m.Date, m.WeightKg })
            .ToListAsync());

    /// <summary>
    /// Sugestão de progressão (dupla progressão) para cada exercício do plano
    /// ativo, com base na última sessão registrada. Determinístico, sem LLM.
    /// </summary>
    [HttpGet("suggestions")]
    public async Task<IActionResult> Suggestions()
    {
        var plan = await db.WorkoutPlans.AsNoTracking()
            .Include(p => p.Days.OrderBy(d => d.Order))
                .ThenInclude(d => d.Exercises.OrderBy(e => e.Order))
                    .ThenInclude(e => e.Exercise)
            .SingleOrDefaultAsync(p => p.UserId == CurrentUserId && p.Status == PlanStatus.Active);
        if (plan is null)
            return Ok(Array.Empty<object>());

        var exerciseIds = plan.Days.SelectMany(d => d.Exercises).Select(e => e.ExerciseId).Distinct().ToList();
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-6);
        var logs = await db.SetLogs.AsNoTracking()
            .Where(s => s.WorkoutSession.UserId == CurrentUserId &&
                        exerciseIds.Contains(s.ExerciseId) &&
                        s.WorkoutSession.Date >= since)
            .Select(s => new { s.ExerciseId, s.WorkoutSession.Date, s.SetNumber, s.Reps, s.LoadKg })
            .ToListAsync();
        var logsByExercise = logs.GroupBy(l => l.ExerciseId).ToDictionary(g => g.Key, g => g.ToList());

        var result = plan.Days.SelectMany(day => day.Exercises.Select(e =>
        {
            var history = logsByExercise.GetValueOrDefault(e.ExerciseId);
            var lastDate = history?.Max(l => l.Date);
            var lastSets = history?
                .Where(l => l.Date == lastDate)
                .OrderBy(l => l.SetNumber)
                .Select(l => new SetPerformance(l.Reps, l.LoadKg))
                .ToList() ?? [];

            var incrementKg = ProgressionCalculator.IncrementFor(e.Exercise.PrimaryMuscleGroup);
            var suggestion = ProgressionCalculator.Suggest(lastSets, e.RepsMin, e.RepsMax, incrementKg);

            return new
            {
                WorkoutDayId = day.Id,
                DayLabel = day.Label,
                e.ExerciseId,
                ExerciseName = e.Exercise.Name,
                e.Sets,
                e.RepsMin,
                e.RepsMax,
                e.RestSeconds,
                LastSessionDate = lastDate,
                LastSets = lastSets.Select(s => new { s.Reps, s.LoadKg }),
                Action = suggestion.Action.ToString(),
                // Sem histórico, o ponto de partida é a carga sugerida do plano.
                NextLoadKg = suggestion.NextLoadKg ?? e.SuggestedLoadKg,
                suggestion.TargetReps,
                IncrementKg = incrementKg,
            };
        }));
        return Ok(result);
    }

    /// <summary>Recordes pessoais por exercício: carga máxima e melhor 1RM estimado (Epley).</summary>
    [HttpGet("records")]
    public async Task<IActionResult> Records()
    {
        var logs = await db.SetLogs.AsNoTracking()
            .Where(s => s.WorkoutSession.UserId == CurrentUserId)
            .Select(s => new { s.ExerciseId, s.Exercise.Name, s.WorkoutSession.Date, s.Reps, s.LoadKg })
            .ToListAsync();

        var records = logs
            .GroupBy(l => new { l.ExerciseId, l.Name })
            .Select(g =>
            {
                var maxLoad = g.Max(l => l.LoadKg);
                var maxLoadDate = g.Where(l => l.LoadKg == maxLoad).Min(l => l.Date);

                var best = g
                    .Select(l => new { l.Reps, l.LoadKg, l.Date, E1Rm = ProgressionCalculator.EstimateOneRepMax(l.Reps, l.LoadKg) })
                    .Where(l => l.E1Rm is not null)
                    .OrderByDescending(l => l.E1Rm)
                    .ThenBy(l => l.Date)
                    .FirstOrDefault();

                return new
                {
                    g.Key.ExerciseId,
                    g.Key.Name,
                    MaxLoadKg = maxLoad,
                    MaxLoadDate = maxLoadDate,
                    BestE1RmKg = best?.E1Rm,
                    E1RmReps = best?.Reps,
                    E1RmLoadKg = best?.LoadKg,
                    E1RmDate = best?.Date,
                };
            })
            .OrderByDescending(r => r.BestE1RmKg ?? r.MaxLoadKg)
            .ToList();

        return Ok(records);
    }
}
