using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Api.Services;
using MyoTrack.Infrastructure;
using MyoTrack.Infrastructure.Identity;

namespace MyoTrack.Api.Controllers;

public record RegisterRequest(string Email, string Password, string? DisplayName);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record AuthResponse(string AccessToken, string RefreshToken);

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> userManager,
    TokenService tokenService,
    AppDbContext db) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        await userManager.AddToRoleAsync(user, AppRoles.Student);

        return Ok(await IssueTokensAsync(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
            return Unauthorized(new { error = "Credenciais inválidas." });

        return Ok(await IssueTokensAsync(user));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request)
    {
        var hash = TokenService.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash);
        if (stored is null || !stored.IsActive)
            return Unauthorized(new { error = "Refresh token inválido ou expirado." });

        var user = await userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null)
            return Unauthorized(new { error = "Usuário não encontrado." });

        stored.RevokedAt = DateTimeOffset.UtcNow;
        var response = await IssueTokensAsync(user);
        return Ok(response);
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var pair = tokenService.CreateTokenPair(user, roles);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenService.HashRefreshToken(pair.RefreshToken),
            ExpiresAt = pair.RefreshTokenExpiresAt,
        });
        await db.SaveChangesAsync();

        return new AuthResponse(pair.AccessToken, pair.RefreshToken);
    }
}
