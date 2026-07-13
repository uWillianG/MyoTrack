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

        if (!await db.Exercises.AnyAsync())
        {
            db.Exercises.AddRange(ExerciseSeed.Items);
            await db.SaveChangesAsync();
        }

        if (!await db.FoodItems.AnyAsync())
        {
            db.FoodItems.AddRange(FoodSeed.Items);
            await db.SaveChangesAsync();
        }
    }
}
