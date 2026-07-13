using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure;

namespace MyoTrack.Api.Controllers;

public record ProfileRequest(
    DateOnly? BirthDate,
    string? Sex,
    decimal? HeightCm,
    Biotype? Biotype,
    ExperienceLevel ExperienceLevel,
    FitnessGoal Goal,
    int TrainingDaysPerWeek,
    List<MuscleGroup>? PriorityMuscleGroups,
    string? InjuryNotes,
    List<string>? InjuryTags,
    List<Equipment>? AvailableEquipment,
    List<string>? DietaryRestrictions,
    List<string>? FoodPreferences);

public record ConsentRequest(ConsentType Type, string TermsVersion);

[Route("api/profile")]
public class ProfileController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserProfile>> Get()
    {
        var profile = await db.UserProfiles.AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == CurrentUserId);
        return profile is null ? NotFound() : Ok(profile);
    }

    [HttpPut]
    public async Task<ActionResult<UserProfile>> Upsert(ProfileRequest request)
    {
        if (request.TrainingDaysPerWeek is < 1 or > 7)
            return BadRequest(new { error = "Dias de treino por semana deve estar entre 1 e 7." });
        if (request.Sex is not (null or "M" or "F"))
            return BadRequest(new { error = "Sexo deve ser 'M' ou 'F'." });

        var profile = await db.UserProfiles.SingleOrDefaultAsync(p => p.UserId == CurrentUserId);
        var isNew = profile is null;
        profile ??= new UserProfile { UserId = CurrentUserId };

        profile.BirthDate = request.BirthDate;
        profile.Sex = request.Sex;
        profile.HeightCm = request.HeightCm;
        profile.Biotype = request.Biotype;
        profile.ExperienceLevel = request.ExperienceLevel;
        profile.Goal = request.Goal;
        profile.TrainingDaysPerWeek = request.TrainingDaysPerWeek;
        profile.PriorityMuscleGroups = request.PriorityMuscleGroups ?? [];
        profile.InjuryNotes = request.InjuryNotes;
        profile.InjuryTags = request.InjuryTags ?? [];
        profile.AvailableEquipment = request.AvailableEquipment ?? [];
        profile.DietaryRestrictions = request.DietaryRestrictions ?? [];
        profile.FoodPreferences = request.FoodPreferences ?? [];
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        if (isNew)
            db.UserProfiles.Add(profile);
        await db.SaveChangesAsync();
        return Ok(profile);
    }

    [HttpPost("consents")]
    public async Task<IActionResult> RecordConsents(List<ConsentRequest> consents)
    {
        foreach (var consent in consents)
        {
            db.ConsentRecords.Add(new ConsentRecord
            {
                UserId = CurrentUserId,
                Type = consent.Type,
                TermsVersion = consent.TermsVersion,
            });
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("consents")]
    public async Task<ActionResult<List<ConsentRecord>>> GetConsents() =>
        Ok(await db.ConsentRecords.AsNoTracking()
            .Where(c => c.UserId == CurrentUserId)
            .OrderByDescending(c => c.GrantedAt)
            .ToListAsync());
}
