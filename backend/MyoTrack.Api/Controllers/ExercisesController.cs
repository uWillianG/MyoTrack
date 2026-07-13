using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Infrastructure;

namespace MyoTrack.Api.Controllers;

[Route("api/exercises")]
public class ExercisesController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await db.Exercises.AsNoTracking()
            .OrderBy(e => e.PrimaryMuscleGroup).ThenBy(e => e.Name)
            .Select(e => new
            {
                e.Id,
                e.Name,
                MuscleGroup = e.PrimaryMuscleGroup.ToString(),
                Equipment = e.Equipment.ToString(),
                e.IsCompound,
            })
            .ToListAsync());
}
