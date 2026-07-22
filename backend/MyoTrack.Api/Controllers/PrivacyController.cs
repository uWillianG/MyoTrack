using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Infrastructure;
using MyoTrack.Infrastructure.Identity;
using MyoTrack.Infrastructure.Storage;

namespace MyoTrack.Api.Controllers;

public record DeleteAccountRequest(string Password);

/// <summary>
/// Direitos do titular (LGPD): portabilidade (export) e eliminação (exclusão de conta).
/// </summary>
[Route("api/privacy")]
public class PrivacyController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IMediaStorage storage,
    ILogger<PrivacyController> logger) : ApiControllerBase
{
    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Export completo dos dados do titular em JSON (art. 18, LGPD).</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var userId = CurrentUserId;
        var user = await userManager.FindByIdAsync(userId.ToString());

        var export = new
        {
            ExportedAt = DateTimeOffset.UtcNow,
            Account = new { user?.Email, user?.CreatedAt },
            Profile = await db.UserProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId),
            Consents = await db.ConsentRecords.AsNoTracking().Where(c => c.UserId == userId).ToListAsync(),
            WorkoutPlans = await db.WorkoutPlans.AsNoTracking()
                .Where(p => p.UserId == userId)
                .Include(p => p.Days).ThenInclude(d => d.Exercises)
                .ToListAsync(),
            DietPlans = await db.DietPlans.AsNoTracking()
                .Where(p => p.UserId == userId)
                .Include(p => p.Meals).ThenInclude(m => m.Items)
                .ToListAsync(),
            WorkoutSessions = await db.WorkoutSessions.AsNoTracking()
                .Where(s => s.UserId == userId)
                .Include(s => s.Sets)
                .ToListAsync(),
            BodyMeasurements = await db.BodyMeasurements.AsNoTracking().Where(m => m.UserId == userId).ToListAsync(),
            MealPhotoAnalyses = await db.MealPhotoAnalyses.AsNoTracking().Where(a => a.UserId == userId).ToListAsync(),
            ExerciseVideoAnalyses = await db.ExerciseVideoAnalyses.AsNoTracking().Where(a => a.UserId == userId).ToListAsync(),
            AnalysisJobs = await db.AnalysisJobs.AsNoTracking().Where(j => j.UserId == userId).ToListAsync(),
            AiUsage = await db.AiUsageLogs.AsNoTracking().Where(l => l.UserId == userId).ToListAsync(),
            CoachMessages = await db.CoachMessages.AsNoTracking().Where(m => m.UserId == userId).ToListAsync(),
            WeeklyReports = await db.WeeklyReports.AsNoTracking().Where(r => r.UserId == userId).ToListAsync(),
            Subscription = await db.UserSubscriptions.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId),
        };

        return File(
            JsonSerializer.SerializeToUtf8Bytes(export, ExportJson),
            "application/json",
            $"myotrack-dados-{DateTime.UtcNow:yyyyMMdd}.json");
    }

    /// <summary>
    /// Exclusão definitiva da conta e de todos os dados e mídias do titular.
    /// Exige a senha atual como confirmação.
    /// </summary>
    [HttpDelete("account")]
    public async Task<IActionResult> DeleteAccount(DeleteAccountRequest request)
    {
        var userId = CurrentUserId;
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return NotFound();

        // Contas criadas pelo Google não têm senha: a confirmação é digitar o
        // próprio e-mail — sem isso o titular ficaria sem como exercer a exclusão.
        if (await userManager.HasPasswordAsync(user))
        {
            if (!await userManager.CheckPasswordAsync(user, request.Password))
                return BadRequest(new { error = "Senha incorreta." });
        }
        else if (!string.Equals(request.Password?.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Para confirmar, digite o e-mail da sua conta." });
        }

        // Coleta as chaves de mídia antes de apagar as linhas.
        var mediaKeys = new List<string>();
        mediaKeys.AddRange(await db.MealPhotoAnalyses
            .Where(a => a.UserId == userId && a.MediaExpiredAt == null).Select(a => a.MediaKey).ToListAsync());
        mediaKeys.AddRange(await db.ExerciseVideoAnalyses
            .Where(a => a.UserId == userId && a.MediaExpiredAt == null).Select(a => a.MediaKey).ToListAsync());
        mediaKeys.AddRange(await db.ExerciseVideoAnalyses
            .Where(a => a.UserId == userId && a.OverlayVideoKey != null && a.MediaExpiredAt == null)
            .Select(a => a.OverlayVideoKey!).ToListAsync());

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.SetLogs.Where(s => db.WorkoutSessions
                .Where(ws => ws.UserId == userId).Select(ws => ws.Id).Contains(s.WorkoutSessionId)).ExecuteDeleteAsync();
            await db.WorkoutSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync();
            await db.WorkoutPlans.Where(p => p.UserId == userId).ExecuteDeleteAsync();
            await db.DietPlans.Where(p => p.UserId == userId).ExecuteDeleteAsync();
            await db.BodyMeasurements.Where(m => m.UserId == userId).ExecuteDeleteAsync();
            await db.MealPhotoAnalyses.Where(a => a.UserId == userId).ExecuteDeleteAsync();
            await db.ExerciseVideoAnalyses.Where(a => a.UserId == userId).ExecuteDeleteAsync();
            await db.AnalysisJobs.Where(j => j.UserId == userId).ExecuteDeleteAsync();
            await db.AiUsageLogs.Where(l => l.UserId == userId).ExecuteDeleteAsync();
            await db.CoachMessages.Where(m => m.UserId == userId).ExecuteDeleteAsync();
            await db.WeeklyReports.Where(r => r.UserId == userId).ExecuteDeleteAsync();
            await db.UserSubscriptions.Where(s => s.UserId == userId).ExecuteDeleteAsync();
            await db.ConsentRecords.Where(c => c.UserId == userId).ExecuteDeleteAsync();
            await db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await db.LoginCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync();
            await db.UserProfiles.Where(p => p.UserId == userId).ExecuteDeleteAsync();
            await tx.CommitAsync();
        }

        await userManager.DeleteAsync(user);

        // Mídia é melhor-esforço após o commit: uma falha aqui não pode ressuscitar a conta.
        foreach (var key in mediaKeys.Distinct())
        {
            try
            {
                await storage.DeleteAsync(key);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao apagar mídia {Key} na exclusão da conta {UserId}.", key, userId);
            }
        }

        logger.LogInformation("Conta {UserId} excluída a pedido do titular (LGPD).", userId);
        return NoContent();
    }
}
