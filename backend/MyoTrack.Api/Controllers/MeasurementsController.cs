using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure;

namespace MyoTrack.Api.Controllers;

public record MeasurementRequest(
    DateOnly Date,
    decimal? WeightKg,
    decimal? BodyFatPercent,
    decimal? WaistCm,
    decimal? ChestCm,
    decimal? HipCm,
    decimal? ArmCm,
    decimal? ThighCm,
    decimal? CalfCm);

[Route("api/measurements")]
public class MeasurementsController(AppDbContext db) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(MeasurementRequest request)
    {
        if (request.WeightKg is <= 0 or > 500)
            return BadRequest(new { error = "Peso fora da faixa válida." });

        var measurement = new BodyMeasurement
        {
            UserId = CurrentUserId,
            Date = request.Date,
            WeightKg = request.WeightKg,
            BodyFatPercent = request.BodyFatPercent,
            WaistCm = request.WaistCm,
            ChestCm = request.ChestCm,
            HipCm = request.HipCm,
            ArmCm = request.ArmCm,
            ThighCm = request.ThighCm,
            CalfCm = request.CalfCm,
        };
        db.BodyMeasurements.Add(measurement);
        await db.SaveChangesAsync();
        return Ok(measurement);
    }

    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await db.BodyMeasurements.AsNoTracking()
            .Where(m => m.UserId == CurrentUserId)
            .OrderBy(m => m.Date)
            .ToListAsync());
}
