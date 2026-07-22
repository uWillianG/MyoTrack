namespace MyoTrack.Api.Services;

/// <summary>
/// URL pública por onde o usuário acessa o sistema. É a base dos links enviados
/// por e-mail e do redirect do OAuth — em desenvolvimento aponta para o Vite
/// (que faz proxy de <c>/api</c>), em produção para o domínio servido pelo Caddy.
/// </summary>
public class AppOptions
{
    public const string SectionName = "App";

    public string PublicBaseUrl { get; set; } = "http://localhost:5173";
}

public static class RateLimitPolicies
{
    /// <summary>Endpoints que disparam e-mail — alvo natural de abuso.</summary>
    public const string AuthEmail = "auth-email";
}
