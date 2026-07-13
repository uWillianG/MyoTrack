using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Infrastructure;

namespace MyoTrack.Api.Controllers;

[Route("api/jobs")]
public class JobsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var job = await db.AnalysisJobs.AsNoTracking()
            .SingleOrDefaultAsync(j => j.Id == id && j.UserId == CurrentUserId);
        if (job is null)
            return NotFound();

        return Ok(new
        {
            job.Id,
            Type = job.Type.ToString(),
            Status = job.Status.ToString(),
            job.ResultJson,
            job.LastError,
            job.CreatedAt,
            job.CompletedAt,
        });
    }
}
