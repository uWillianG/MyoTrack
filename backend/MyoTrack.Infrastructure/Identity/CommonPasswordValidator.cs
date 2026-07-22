using System.Collections.Frozen;
using System.IO.Compression;
using System.Reflection;
using Microsoft.AspNetCore.Identity;

namespace MyoTrack.Infrastructure.Identity;

/// <summary>
/// Recusa senhas da lista das ~20 mil mais usadas em vazamentos. As regras de
/// composição (maiúscula, número, símbolo) não pegam "Senha@123": é justamente
/// esse tipo de senha que os ataques de dicionário tentam primeiro.
/// </summary>
/// <remarks>
/// A lista vem do Django (<c>django/contrib/auth/common-passwords.txt.gz</c>,
/// licença BSD-3), compilada por Royce Williams a partir de vazamentos públicos.
/// </remarks>
public class CommonPasswordValidator : IPasswordValidator<ApplicationUser>
{
    private const string ResourceName = "MyoTrack.Infrastructure.Assets.common-passwords.txt.gz";

    // ~20 mil entradas: carregadas uma vez, no primeiro cadastro.
    private static readonly Lazy<FrozenSet<string>> Passwords = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (password is not null && Passwords.Value.Contains(password.Trim().ToLowerInvariant()))
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordTooCommon",
                Description = "Essa senha é muito comum — escolha outra, menos previsível.",
            }));

        return Task.FromResult(IdentityResult.Success);
    }

    /// <summary>
    /// Complemento em pt-BR: a lista do Django é majoritariamente anglófona e
    /// deixa passar senhas óbvias para o público daqui (times, "senha1234") e
    /// para o contexto do produto ("treino", "supino").
    /// </summary>
    private static readonly string[] BrazilianExtras =
    [
        "futebol", "corinthians", "vasco", "cruzeiro", "botafogo", "internacional", "fluminense",
        "brasil123", "senha1234", "senha12345", "mudar123", "joao", "deus", "meuamor", "familia123",
        "saudade", "obrigado", "naosei", "naosei123", "dinheiro", "liberdade",
        "treino", "musculacao", "halteres", "supino", "agachamento", "malhar",
    ];

    private static FrozenSet<string> Load()
    {
        using var stream = typeof(CommonPasswordValidator).GetTypeInfo().Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Recurso embutido '{ResourceName}' não encontrado.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, System.Text.Encoding.UTF8);

        var set = new HashSet<string>(BrazilianExtras, StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var entry = line.Trim();
            if (entry.Length > 0) set.Add(entry);
        }
        return set.ToFrozenSet(StringComparer.Ordinal);
    }
}
