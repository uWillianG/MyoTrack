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

        // Idempotente por nome: itens novos do catálogo entram também em bancos já populados.
        var existingNames = (await db.Exercises.Select(e => e.Name).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingExercises = ExerciseSeed.Items.Where(e => !existingNames.Contains(e.Name)).ToList();
        if (missingExercises.Count > 0)
        {
            db.Exercises.AddRange(missingExercises);
            await db.SaveChangesAsync();
        }

        if (!await db.FoodItems.AnyAsync())
        {
            db.FoodItems.AddRange(FoodSeed.Items);
            await db.SaveChangesAsync();
        }
    }
}
