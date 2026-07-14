using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Infrastructure;
using MyoTrack.Infrastructure.Identity;

namespace MyoTrack.Api.Controllers;

public record ReviewDecision(ReviewStatus Status, string? Note);

/// <summary>
/// Fila de supervisão humana: Trainers revisam treinos gerados por IA,
/// Nutritionists revisam dietas. Admin revisa ambos.
/// </summary>
[Route("api/reviews")]
public class ReviewsController(AppDbContext db) : ApiControllerBase
{
    private const string WorkoutReviewers = $"{AppRoles.Trainer},{AppRoles.Admin}";
    private const string DietReviewers = $"{AppRoles.Nutritionist},{AppRoles.Admin}";

    [HttpGet("workout-plans")]
    [Authorize(Roles = WorkoutReviewers)]
    public async Task<IActionResult> PendingWorkouts() =>
        Ok(await db.WorkoutPlans.AsNoTracking()
            .Where(p => p.Status == PlanStatus.Active && p.ReviewStatus == ReviewStatus.NotReviewed)
            .OrderBy(p => p.CreatedAt)
            .Take(50)
            .Join(db.Users, p => p.UserId, u => u.Id, (p, u) => new
            {
                p.Id, p.Name, p.Split, Goal = p.Goal.ToString(), p.Version, p.CreatedAt,
                Student = u.Email,
            })
            .ToListAsync());

    [HttpGet("workout-plans/{id:guid}")]
    [Authorize(Roles = WorkoutReviewers)]
    public async Task<IActionResult> GetWorkout(Guid id)
    {
        var plan = await db.WorkoutPlans.AsNoTracking()
            .Include(p => p.Days.OrderBy(d => d.Order))
                .ThenInclude(d => d.Exercises.OrderBy(e => e.Order))
                    .ThenInclude(e => e.Exercise)
            .SingleOrDefaultAsync(p => p.Id == id);
        if (plan is null)
            return NotFound();

        return Ok(new
        {
            plan.Id, plan.Name, plan.Split, Goal = plan.Goal.ToString(), plan.Version, plan.CreatedAt,
            ReviewStatus = plan.ReviewStatus.ToString(),
            Days = plan.Days.Select(d => new
            {
                d.Order, d.Label,
                Exercises = d.Exercises.Select(e => new
                {
                    ExerciseName = e.Exercise.Name, e.Sets, e.RepsMin, e.RepsMax, e.RestSeconds, e.Notes,
                }),
            }),
        });
    }

    [HttpPost("workout-plans/{id:guid}")]
    [Authorize(Roles = WorkoutReviewers)]
    public async Task<IActionResult> ReviewWorkout(Guid id, ReviewDecision decision)
    {
        if (decision.Status == ReviewStatus.NotReviewed)
            return BadRequest(new { error = "Decisão inválida." });

        var plan = await db.WorkoutPlans.SingleOrDefaultAsync(p => p.Id == id);
        if (plan is null)
            return NotFound();

        plan.ReviewStatus = decision.Status;
        plan.ReviewNote = decision.Note;
        plan.ReviewedByUserId = CurrentUserId;
        plan.ReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("diet-plans")]
    [Authorize(Roles = DietReviewers)]
    public async Task<IActionResult> PendingDiets() =>
        Ok(await db.DietPlans.AsNoTracking()
            .Where(p => p.Status == PlanStatus.Active && p.ReviewStatus == ReviewStatus.NotReviewed)
            .OrderBy(p => p.CreatedAt)
            .Take(50)
            .Join(db.Users, p => p.UserId, u => u.Id, (p, u) => new
            {
                p.Id, p.Name, CalorieGoal = p.CalorieGoal.ToString(), p.Version, p.CreatedAt,
                p.TargetKcal, Student = u.Email,
            })
            .ToListAsync());

    [HttpGet("diet-plans/{id:guid}")]
    [Authorize(Roles = DietReviewers)]
    public async Task<IActionResult> GetDiet(Guid id)
    {
        var plan = await db.DietPlans.AsNoTracking()
            .Include(p => p.Meals.OrderBy(m => m.Order))
                .ThenInclude(m => m.Items)
                    .ThenInclude(i => i.FoodItem)
            .SingleOrDefaultAsync(p => p.Id == id);
        if (plan is null)
            return NotFound();

        return Ok(new
        {
            plan.Id, plan.Name, CalorieGoal = plan.CalorieGoal.ToString(), plan.Version, plan.CreatedAt,
            ReviewStatus = plan.ReviewStatus.ToString(),
            Targets = new { plan.TargetKcal, plan.TargetProteinG, plan.TargetCarbsG, plan.TargetFatG },
            Meals = plan.Meals.Select(m => new
            {
                m.Order, m.Name,
                Items = m.Items.Select(i => new { FoodName = i.FoodItem.Name, i.QuantityG }),
            }),
        });
    }

    [HttpPost("diet-plans/{id:guid}")]
    [Authorize(Roles = DietReviewers)]
    public async Task<IActionResult> ReviewDiet(Guid id, ReviewDecision decision)
    {
        if (decision.Status == ReviewStatus.NotReviewed)
            return BadRequest(new { error = "Decisão inválida." });

        var plan = await db.DietPlans.SingleOrDefaultAsync(p => p.Id == id);
        if (plan is null)
            return NotFound();

        plan.ReviewStatus = decision.Status;
        plan.ReviewNote = decision.Note;
        plan.ReviewedByUserId = CurrentUserId;
        plan.ReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
