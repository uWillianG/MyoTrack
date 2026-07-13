using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure;

namespace MyoTrack.Api.Controllers;

public record SetLogRequest(int ExerciseId, int SetNumber, int Reps, decimal LoadKg, int? Rpe);
public record SessionRequest(DateOnly Date, Guid? WorkoutDayId, string? Notes, List<SetLogRequest> Sets);

[Route("api/sessions")]
public class WorkoutSessionsController(AppDbContext db) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(SessionRequest request)
    {
        if (request.Sets.Count == 0)
            return BadRequest(new { error = "Registre pelo menos uma série." });
        if (request.Sets.Any(s => s.Reps is < 1 or > 100 || s.LoadKg is < 0 or > 1000))
            return BadRequest(new { error = "Série com repetições ou carga fora da faixa válida." });

        var exerciseIds = request.Sets.Select(s => s.ExerciseId).Distinct().ToList();
        var validIds = await db.Exercises
            .Where(e => exerciseIds.Contains(e.Id))
            .Select(e => e.Id)
            .ToListAsync();
        if (validIds.Count != exerciseIds.Count)
            return BadRequest(new { error = "Exercício inexistente na sessão." });

        var session = new WorkoutSession
        {
            UserId = CurrentUserId,
            Date = request.Date,
            WorkoutDayId = request.WorkoutDayId,
            Notes = request.Notes,
            Sets = request.Sets.Select(s => new SetLog
            {
                ExerciseId = s.ExerciseId,
                SetNumber = s.SetNumber,
                Reps = s.Reps,
                LoadKg = s.LoadKg,
                Rpe = s.Rpe,
            }).ToList(),
        };

        db.WorkoutSessions.Add(session);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = session.Id }, new { session.Id });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var session = await db.WorkoutSessions.AsNoTracking()
            .Include(s => s.Sets)
            .ThenInclude(s => s.Exercise)
            .SingleOrDefaultAsync(s => s.Id == id && s.UserId == CurrentUserId);
        return session is null ? NotFound() : Ok(ToDto(session));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var query = db.WorkoutSessions.AsNoTracking()
            .Include(s => s.Sets)
            .ThenInclude(s => s.Exercise)
            .Where(s => s.UserId == CurrentUserId);
        if (from is not null) query = query.Where(s => s.Date >= from);
        if (to is not null) query = query.Where(s => s.Date <= to);

        var sessions = await query.OrderByDescending(s => s.Date).Take(100).ToListAsync();
        return Ok(sessions.Select(ToDto));
    }

    private static object ToDto(WorkoutSession session) => new
    {
        session.Id,
        session.Date,
        session.WorkoutDayId,
        session.Notes,
        TotalVolumeKg = session.Sets.Sum(s => s.Reps * s.LoadKg),
        Sets = session.Sets
            .OrderBy(s => s.ExerciseId).ThenBy(s => s.SetNumber)
            .Select(s => new
            {
                s.Id,
                s.ExerciseId,
                ExerciseName = s.Exercise.Name,
                s.SetNumber,
                s.Reps,
                s.LoadKg,
                s.Rpe,
            }),
    };
}
