using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure;
using MyoTrack.Infrastructure.Ai;
using MyoTrack.Infrastructure.Storage;

namespace MyoTrack.Api.Controllers;

public record MealItemAdjustment(
    string Description,
    int? FoodItemId,
    decimal QuantityG,
    decimal KcalPer100g,
    decimal ProteinPer100g,
    decimal CarbsPer100g,
    decimal FatPer100g);

[Route("api/meal-analyses")]
public class MealAnalysesController(
    AppDbContext db,
    IMediaStorage storage,
    MyoTrack.Api.Services.EntitlementService entitlements) : ApiControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxFileBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan PhotoUrlExpiry = TimeSpan.FromHours(1);

    /// <summary>Sobe a foto e enfileira a análise em uma única chamada.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxFileBytes + 1024)]
    public async Task<IActionResult> Create(IFormFile photo)
    {
        if (photo.Length is 0 or > MaxFileBytes)
            return BadRequest(new { error = "Envie uma imagem de até 10 MB." });
        if (!AllowedContentTypes.Contains(photo.ContentType))
            return BadRequest(new { error = "Formato não suportado. Use JPEG, PNG ou WebP." });

        var entitlement = await entitlements.GetAsync(CurrentUserId);
        // Meia-noite UTC como DateTimeOffset explícito — a conversão implícita de
        // DateTime usaria o fuso local, que o Npgsql rejeita em timestamptz.
        var since = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var usedToday = await db.AnalysisJobs.CountAsync(j =>
            j.UserId == CurrentUserId &&
            j.Type == AnalysisJobType.MealPhoto &&
            j.CreatedAt >= since);
        if (usedToday >= entitlement.MaxMealAnalysesPerDay)
            return StatusCode(429, new
            {
                error = entitlement.Plan == SubscriptionPlanType.Free
                    ? $"Limite diário de {entitlement.MaxMealAnalysesPerDay} análises atingido. Assine o Pro para ampliar."
                    : $"Limite diário de {entitlement.MaxMealAnalysesPerDay} análises de refeição atingido.",
            });

        var extension = photo.ContentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg",
        };
        var key = $"meals/{CurrentUserId}/{Guid.NewGuid():N}{extension}";

        await using (var stream = photo.OpenReadStream())
        {
            await storage.UploadAsync(key, stream, photo.ContentType);
        }

        var job = new AnalysisJob
        {
            UserId = CurrentUserId,
            Type = AnalysisJobType.MealPhoto,
            MediaKey = key,
        };
        db.AnalysisJobs.Add(job);
        await db.SaveChangesAsync();
        return Accepted(new { jobId = job.Id });
    }

    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await db.MealPhotoAnalyses.AsNoTracking()
            .Where(a => a.UserId == CurrentUserId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(50)
            .Select(a => new
            {
                a.Id, a.CreatedAt, a.TotalKcal, a.TotalProteinG, a.TotalCarbsG, a.TotalFatG, a.UserAdjusted,
            })
            .ToListAsync());

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var analysis = await db.MealPhotoAnalyses.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == id && a.UserId == CurrentUserId);
        return analysis is null ? NotFound() : Ok(await ToDtoAsync(analysis));
    }

    /// <summary>Ajuste manual pelo usuário — quantidades/itens editados viram a estimativa oficial.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Adjust(Guid id, List<MealItemAdjustment> items)
    {
        var analysis = await db.MealPhotoAnalyses
            .SingleOrDefaultAsync(a => a.Id == id && a.UserId == CurrentUserId);
        if (analysis is null)
            return NotFound();
        if (items.Count == 0)
            return BadRequest(new { error = "A análise precisa de pelo menos um item." });
        if (items.Any(i => i.QuantityG is <= 0 or > 2000))
            return BadRequest(new { error = "Quantidade fora da faixa válida (1–2000 g)." });

        var foodIds = items.Where(i => i.FoodItemId is > 0).Select(i => i.FoodItemId!.Value).Distinct().ToList();
        var foods = await db.FoodItems.Where(f => foodIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id);

        var dtos = items.Select(i =>
        {
            // Itens do catálogo sempre usam os macros oficiais, não os enviados.
            if (i.FoodItemId is > 0 && foods.TryGetValue(i.FoodItemId.Value, out var food))
                return new MealItemDto(food.Name, food.Id, Math.Round(i.QuantityG),
                    food.KcalPer100g, food.ProteinPer100g, food.CarbsPer100g, food.FatPer100g);

            return new MealItemDto(i.Description, null, Math.Round(i.QuantityG),
                Math.Clamp(i.KcalPer100g, 0, 900),
                Math.Clamp(i.ProteinPer100g, 0, 100),
                Math.Clamp(i.CarbsPer100g, 0, 100),
                Math.Clamp(i.FatPer100g, 0, 100));
        }).ToList();

        analysis.ItemsJson = JsonSerializer.Serialize(dtos, JsonOptions);
        analysis.UserAdjusted = true;
        MealAnalysisService.ApplyTotals(analysis, dtos);
        await db.SaveChangesAsync();
        return Ok(await ToDtoAsync(analysis));
    }

    private async Task<object> ToDtoAsync(MealPhotoAnalysis analysis)
    {
        // Foto expirada pela política de retenção não tem mais arquivo no storage.
        string? photoUrl = null;
        if (analysis.MediaExpiredAt is null)
            photoUrl = await storage.GetPresignedDownloadUrlAsync(analysis.MediaKey, PhotoUrlExpiry);

        return new
        {
            analysis.Id,
            analysis.CreatedAt,
            analysis.UserAdjusted,
            analysis.TotalKcal,
            analysis.TotalProteinG,
            analysis.TotalCarbsG,
            analysis.TotalFatG,
            PhotoUrl = photoUrl,
            MediaExpired = analysis.MediaExpiredAt is not null,
            Items = JsonSerializer.Deserialize<List<MealItemDto>>(analysis.ItemsJson, JsonOptions),
        };
    }
}
