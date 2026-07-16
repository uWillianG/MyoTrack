using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure.Identity;

namespace MyoTrack.Infrastructure.Seed;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, RoleManager<IdentityRole<Guid>> roleManager)
    {
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }

        // Idempotente por nome: itens novos do catálogo entram também em bancos já
        // populados, e a classificação muscular dos existentes é sincronizada com o
        // seed (fonte da verdade do catálogo — ex.: encolhimento migrou Back → Traps).
        var existingByName = (await db.Exercises.ToListAsync())
            .ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var seed in ExerciseSeed.Items)
        {
            if (!existingByName.TryGetValue(seed.Name, out var existing))
            {
                db.Exercises.Add(seed);
            }
            else if (existing.PrimaryMuscleGroup != seed.PrimaryMuscleGroup
                || !existing.SecondaryMuscleGroups.SequenceEqual(seed.SecondaryMuscleGroups))
            {
                existing.PrimaryMuscleGroup = seed.PrimaryMuscleGroup;
                existing.SecondaryMuscleGroups = seed.SecondaryMuscleGroups;
            }
        }
        await db.SaveChangesAsync();

        if (!await db.FoodItems.AnyAsync())
        {
            db.FoodItems.AddRange(FoodSeed.Items);
            await db.SaveChangesAsync();
        }
    }
}
