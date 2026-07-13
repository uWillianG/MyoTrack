using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
}
