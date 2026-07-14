using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure;

namespace MyoTrack.Api.Controllers;

[Route("api/workout-plans")]
public class WorkoutPlansController(AppDbContext db) : ApiControllerBase
{
    [HttpPost("generate")]
    public async Task<IActionResult> Generate()
    {
        var hasProfile = await db.UserProfiles.AnyAsync(p => p.UserId == CurrentUserId);
        if (!hasProfile)
            return BadRequest(new { error = "Complete o onboarding antes de gerar o treino." });

        var pending = await db.AnalysisJobs.AnyAsync(j =>
            j.UserId == CurrentUserId &&
            j.Type == AnalysisJobType.WorkoutGeneration &&
            (j.Status == JobStatus.Pending || j.Status == JobStatus.Processing));
        if (pending)
            return Conflict(new { error = "Já existe uma geração de treino em andamento." });

        var job = new AnalysisJob { UserId = CurrentUserId, Type = AnalysisJobType.WorkoutGeneration };
        db.AnalysisJobs.Add(job);
        await db.SaveChangesAsync();
        return Accepted(new { jobId = job.Id });
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var plan = await db.WorkoutPlans.AsNoTracking()
            .Include(p => p.Days.OrderBy(d => d.Order))
                .ThenInclude(d => d.Exercises.OrderBy(e => e.Order))
                    .ThenInclude(e => e.Exercise)
            .SingleOrDefaultAsync(p => p.UserId == CurrentUserId && p.Status == PlanStatus.Active);

        return plan is null ? NotFound() : Ok(ToDto(plan));
    }

    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await db.WorkoutPlans.AsNoTracking()
            .Where(p => p.UserId == CurrentUserId)
            .OrderByDescending(p => p.Version)
            .Select(p => new { p.Id, p.Name, p.Split, Status = p.Status.ToString(), p.Version, p.CreatedAt })
            .ToListAsync());

    private static object ToDto(WorkoutPlan plan) => new
    {
        plan.Id,
        plan.Name,
        plan.Split,
        Goal = plan.Goal.ToString(),
        plan.Version,
        plan.CreatedAt,
        ReviewStatus = plan.ReviewStatus.ToString(),
        plan.ReviewNote,
        plan.ReviewedAt,
        Days = plan.Days.Select(d => new
        {
            d.Id,
            d.Order,
            d.Label,
            Exercises = d.Exercises.Select(e => new
            {
                e.Id,
                e.ExerciseId,
                ExerciseName = e.Exercise.Name,
                MuscleGroup = e.Exercise.PrimaryMuscleGroup.ToString(),
                e.Exercise.TutorialVideoUrl,
                e.Sets,
                e.RepsMin,
                e.RepsMax,
                e.RestSeconds,
                e.Notes,
            }),
        }),
    };
}
