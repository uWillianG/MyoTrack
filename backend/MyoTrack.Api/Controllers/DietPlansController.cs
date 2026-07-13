using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure;

namespace MyoTrack.Api.Controllers;

[Route("api/diet-plans")]
public class DietPlansController(AppDbContext db) : ApiControllerBase
{
    [HttpPost("generate")]
    public async Task<IActionResult> Generate()
    {
        var profile = await db.UserProfiles.AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == CurrentUserId);
        if (profile is null)
            return BadRequest(new { error = "Complete o onboarding antes de gerar a dieta." });
        if (profile.BirthDate is null || profile.Sex is null || profile.HeightCm is null)
            return BadRequest(new { error = "Perfil incompleto: informe data de nascimento, sexo e altura." });

        var hasWeight = await db.BodyMeasurements.AnyAsync(m => m.UserId == CurrentUserId && m.WeightKg != null);
        if (!hasWeight)
            return BadRequest(new { error = "Registre seu peso corporal antes de gerar a dieta." });

        var pending = await db.AnalysisJobs.AnyAsync(j =>
            j.UserId == CurrentUserId &&
            j.Type == AnalysisJobType.DietGeneration &&
            (j.Status == JobStatus.Pending || j.Status == JobStatus.Processing));
        if (pending)
            return Conflict(new { error = "Já existe uma geração de dieta em andamento." });

        var job = new AnalysisJob { UserId = CurrentUserId, Type = AnalysisJobType.DietGeneration };
        db.AnalysisJobs.Add(job);
        await db.SaveChangesAsync();
        return Accepted(new { jobId = job.Id });
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var plan = await db.DietPlans.AsNoTracking()
            .Include(p => p.Meals.OrderBy(m => m.Order))
                .ThenInclude(m => m.Items)
                    .ThenInclude(i => i.FoodItem)
            .SingleOrDefaultAsync(p => p.UserId == CurrentUserId && p.Status == PlanStatus.Active);

        return plan is null ? NotFound() : Ok(ToDto(plan));
    }

    private static object ToDto(DietPlan plan)
    {
        var meals = plan.Meals.Select(m => new
        {
            m.Id,
            m.Order,
            m.Name,
            Items = m.Items.Select(i => new
            {
                i.Id,
                i.FoodItemId,
                FoodName = i.FoodItem.Name,
                i.QuantityG,
                Kcal = Math.Round(i.FoodItem.KcalPer100g * i.QuantityG / 100m, 0),
                ProteinG = Math.Round(i.FoodItem.ProteinPer100g * i.QuantityG / 100m, 1),
                CarbsG = Math.Round(i.FoodItem.CarbsPer100g * i.QuantityG / 100m, 1),
                FatG = Math.Round(i.FoodItem.FatPer100g * i.QuantityG / 100m, 1),
            }).ToList(),
        }).ToList();

        return new
        {
            plan.Id,
            plan.Name,
            CalorieGoal = plan.CalorieGoal.ToString(),
            plan.Version,
            plan.CreatedAt,
            Targets = new { plan.TargetKcal, plan.TargetProteinG, plan.TargetCarbsG, plan.TargetFatG },
            Totals = new
            {
                Kcal = meals.SelectMany(m => m.Items).Sum(i => i.Kcal),
                ProteinG = meals.SelectMany(m => m.Items).Sum(i => i.ProteinG),
                CarbsG = meals.SelectMany(m => m.Items).Sum(i => i.CarbsG),
                FatG = meals.SelectMany(m => m.Items).Sum(i => i.FatG),
            },
            Meals = meals,
        };
    }
}
