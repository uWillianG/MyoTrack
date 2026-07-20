using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Infrastructure;

namespace MyoTrack.Api.Controllers;

public record DiaryEntryUpdate(bool Included);

/// <summary>
/// Diário alimentar: consolida as análises de refeição do dia e compara o
/// consumido com as metas do plano de dieta ativo. O dia é definido no fuso do
/// cliente (parâmetro tz), não em UTC — refeição das 22h conta no dia certo.
/// </summary>
[Route("api/diary")]
public class DiaryController(AppDbContext db) : ApiControllerBase
{
    /// <param name="date">Dia local desejado; ausente = hoje no fuso do cliente.</param>
    /// <param name="tz">Offset do fuso em minutos, como o getTimezoneOffset() do JS (Brasil = 180).</param>
    [HttpGet]
    public async Task<IActionResult> Get(DateOnly? date, int tz = 0)
    {
        if (tz is < -840 or > 840)
            return BadRequest(new { error = "Fuso horário inválido." });

        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(-tz));
        DateTimeOffset StartUtc(DateOnly d) =>
            new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddMinutes(tz);
        var dayStart = StartUtc(day);
        var dayEnd = dayStart.AddDays(1);
        var weekStart = dayStart.AddDays(-6); // 7 dias terminando no dia pedido

        var analyses = await db.MealPhotoAnalyses.AsNoTracking()
            .Where(a => a.UserId == CurrentUserId && a.CreatedAt >= weekStart && a.CreatedAt < dayEnd)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id, a.CreatedAt, a.TotalKcal, a.TotalProteinG, a.TotalCarbsG, a.TotalFatG,
                a.UserAdjusted, a.ExcludedFromDiary,
            })
            .ToListAsync();

        var targets = await db.DietPlans.AsNoTracking()
            .Where(p => p.UserId == CurrentUserId && p.Status == PlanStatus.Active)
            .Select(p => new { Kcal = p.TargetKcal, ProteinG = p.TargetProteinG, CarbsG = p.TargetCarbsG, FatG = p.TargetFatG })
            .SingleOrDefaultAsync();

        var entries = analyses.Where(a => a.CreatedAt >= dayStart).ToList();
        var counted = entries.Where(a => !a.ExcludedFromDiary).ToList();

        var week = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var d = day.AddDays(offset - 6);
                var start = StartUtc(d);
                var end = start.AddDays(1);
                return new
                {
                    Date = d,
                    Kcal = Math.Round(analyses
                        .Where(a => !a.ExcludedFromDiary && a.CreatedAt >= start && a.CreatedAt < end)
                        .Sum(a => a.TotalKcal)),
                };
            })
            .ToList();

        return Ok(new
        {
            Date = day,
            Targets = targets,
            Consumed = new
            {
                Kcal = Math.Round(counted.Sum(a => a.TotalKcal)),
                ProteinG = Math.Round(counted.Sum(a => a.TotalProteinG)),
                CarbsG = Math.Round(counted.Sum(a => a.TotalCarbsG)),
                FatG = Math.Round(counted.Sum(a => a.TotalFatG)),
            },
            Entries = entries,
            Week = week,
        });
    }

    /// <summary>Inclui/exclui uma análise do diário (ex.: foto repetida ou prato não consumido).</summary>
    [HttpPut("entries/{id:guid}")]
    public async Task<IActionResult> SetIncluded(Guid id, DiaryEntryUpdate body)
    {
        var analysis = await db.MealPhotoAnalyses
            .SingleOrDefaultAsync(a => a.Id == id && a.UserId == CurrentUserId);
        if (analysis is null)
            return NotFound();

        analysis.ExcludedFromDiary = !body.Included;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
