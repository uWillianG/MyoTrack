using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyoTrack.Api.Services;
using MyoTrack.Infrastructure;
using MyoTrack.Infrastructure.Email;
using MyoTrack.Infrastructure.Identity;

namespace MyoTrack.Api.Controllers;

public record RegisterRequest(string Email, string Password, string? DisplayName);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record AuthResponse(string AccessToken, string RefreshToken);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string UserId, string Token, string Password);
public record ExchangeCodeRequest(string Code);

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> userManager,
    TokenService tokenService,
    AppDbContext db,
    IEmailSender emailSender,
    GoogleOAuthService google,
    IOptions<AppOptions> appOptions,
    IWebHostEnvironment environment,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Validade do link de redefinição — casada com o DataProtectionTokenProvider.</summary>
    private const int PasswordResetValidHours = 24;
    private const string GoogleProvider = "Google";
    private const string StateCookie = "myotrack_oauth_state";

    private readonly AppOptions _app = appOptions.Value;

    /// <summary>Quais formas de login a SPA deve oferecer nesta instalação.</summary>
    [HttpGet("providers")]
    public ActionResult<object> Providers() => Ok(new
    {
        google = google.IsEnabled,
        // Sem SMTP o link de redefinição só vai para o log do servidor: em
        // produção a tela esconde a opção para não prometer um e-mail que não chega.
        passwordReset = emailSender.IsConfigured || environment.IsDevelopment(),
    });

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
            // UserName e Email guardam o mesmo valor: sem Distinct, o e-mail
            // duplicado aparece repetido na tela.
            return BadRequest(new { errors = result.Errors.Select(e => e.Description).Distinct() });

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

    /// <summary>
    /// Envia o link de redefinição. Responde sempre igual, exista ou não a conta:
    /// a resposta não pode revelar quem tem cadastro no sistema.
    /// </summary>
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.AuthEmail)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : await userManager.FindByEmailAsync(request.Email.Trim());

        if (user?.Email is not null)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var url = $"{_app.PublicBaseUrl.TrimEnd('/')}/redefinir-senha" +
                      $"?uid={Uri.EscapeDataString(user.Id.ToString())}" +
                      $"&token={Uri.EscapeDataString(token)}";

            var email = EmailTemplates.PasswordReset(url, PasswordResetValidHours);
            try
            {
                await emailSender.SendAsync(user.Email, email.Subject, email.HtmlBody, email.TextBody, ct);
            }
            catch (Exception ex)
            {
                // Falha de SMTP não pode virar erro na tela (revelaria que a conta existe).
                logger.LogError(ex, "Falha ao enviar e-mail de redefinição de senha.");
            }
        }

        return Ok(new { message = "Se houver uma conta com esse e-mail, o link de redefinição foi enviado." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = Guid.TryParse(request.UserId, out var id)
            ? await userManager.FindByIdAsync(id.ToString())
            : null;

        if (user is null)
            return BadRequest(new { error = "Link inválido ou expirado. Peça um novo." });

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.Password);
        if (!result.Succeeded)
        {
            // Erros de token são genéricos; os de política de senha ajudam o usuário.
            var passwordErrors = result.Errors
                .Where(e => !e.Code.Contains("Token", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Description)
                .ToArray();
            return BadRequest(passwordErrors.Length > 0
                ? new { error = string.Join(" ", passwordErrors) }
                : new { error = "Link inválido ou expirado. Peça um novo." });
        }

        // Trocar a senha derruba as sessões abertas — inclusive a de quem roubou a conta.
        await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow));

        return Ok(new { message = "Senha redefinida. Entre com a nova senha." });
    }

    /// <summary>Passo 1 do OAuth: gera o <c>state</c> anti-CSRF e manda ao Google.</summary>
    [HttpGet("google/start")]
    public IActionResult GoogleStart()
    {
        if (!google.IsEnabled)
            return RedirectToSpa("erro=google-indisponivel");

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        Response.Cookies.Append(StateCookie, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax, // o retorno do Google é navegação top-level
            Path = "/api/auth/google",
            MaxAge = TimeSpan.FromMinutes(10),
        });

        return Redirect(google.BuildAuthorizationUrl(GoogleRedirectUri(), state));
    }

    /// <summary>
    /// Passo 2/3: valida o <c>state</c>, troca o code por token, busca o perfil e
    /// devolve o usuário à SPA com um código de uso único (nunca com os tokens).
    /// </summary>
    [HttpGet("google/callback")]
    public async Task<IActionResult> GoogleCallback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        var expectedState = Request.Cookies[StateCookie];
        Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/api/auth/google" });

        if (!google.IsEnabled) return RedirectToSpa("erro=google-indisponivel");
        if (!string.IsNullOrEmpty(error)) return RedirectToSpa("erro=google-cancelado");

        if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(expectedState) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expectedState)))
            return RedirectToSpa("erro=google-state");

        if (string.IsNullOrEmpty(code)) return RedirectToSpa("erro=google-falhou");

        GoogleUserInfo info;
        try
        {
            var accessToken = await google.ExchangeCodeAsync(code, GoogleRedirectUri(), ct);
            info = await google.FetchUserInfoAsync(accessToken, ct);
        }
        catch (Exception ex) when (ex is GoogleOAuthException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Falha no fluxo OAuth do Google.");
            return RedirectToSpa("erro=google-falhou");
        }

        if (string.IsNullOrWhiteSpace(info.Email) || !info.EmailVerified)
            return RedirectToSpa("erro=google-email");

        var user = await FindOrCreateGoogleUserAsync(info);
        var loginCode = await CreateLoginCodeAsync(user);

        return RedirectToSpa($"oauth={Uri.EscapeDataString(loginCode)}");
    }

    /// <summary>Troca o código de uso único pelo par de tokens.</summary>
    [HttpPost("google/exchange")]
    public async Task<ActionResult<AuthResponse>> GoogleExchange(ExchangeCodeRequest request)
    {
        var hash = TokenService.HashRefreshToken(request.Code);
        var stored = await db.LoginCodes.SingleOrDefaultAsync(c => c.CodeHash == hash);
        if (stored is null || !stored.IsUsable)
            return Unauthorized(new { error = "Código de login inválido ou expirado." });

        var user = await userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null)
            return Unauthorized(new { error = "Usuário não encontrado." });

        stored.UsedAt = DateTimeOffset.UtcNow;
        return Ok(await IssueTokensAsync(user));
    }

    /// <summary>
    /// Casa a identidade do Google com uma conta existente (pelo <c>sub</c> e,
    /// na falta dele, pelo e-mail verificado) ou cria uma nova sem senha.
    /// </summary>
    private async Task<ApplicationUser> FindOrCreateGoogleUserAsync(GoogleUserInfo info)
    {
        var user = await userManager.FindByLoginAsync(GoogleProvider, info.Sub);
        if (user is not null) return user;

        user = await userManager.FindByEmailAsync(info.Email!);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = info.Email,
                Email = info.Email,
                EmailConfirmed = true, // o Google já verificou este endereço
                DisplayName = string.IsNullOrWhiteSpace(info.GivenName) ? info.Name : info.GivenName,
            };
            // Sem senha: a conta só entra pelo Google até o usuário definir uma
            // senha pelo fluxo de "esqueci minha senha".
            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded)
                throw new InvalidOperationException(
                    "Falha ao criar usuário do Google: " + string.Join("; ", created.Errors.Select(e => e.Description)));

            await userManager.AddToRoleAsync(user, AppRoles.Student);
        }

        await userManager.AddLoginAsync(user, new UserLoginInfo(GoogleProvider, info.Sub, GoogleProvider));
        return user;
    }

    private async Task<string> CreateLoginCodeAsync(ApplicationUser user)
    {
        var code = Base64Url(RandomNumberGenerator.GetBytes(32));

        await db.LoginCodes.Where(c => c.ExpiresAt < DateTimeOffset.UtcNow).ExecuteDeleteAsync();
        db.LoginCodes.Add(new LoginCode
        {
            UserId = user.Id,
            CodeHash = TokenService.HashRefreshToken(code),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
        });
        await db.SaveChangesAsync();

        return code;
    }

    /// <summary>Deve bater exatamente com o redirect autorizado no Google Cloud Console.</summary>
    private string GoogleRedirectUri() =>
        $"{_app.PublicBaseUrl.TrimEnd('/')}/api/auth/google/callback";

    private RedirectResult RedirectToSpa(string query) =>
        Redirect($"{_app.PublicBaseUrl.TrimEnd('/')}/login?{query}");

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

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
