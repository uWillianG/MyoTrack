using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace MyoTrack.Infrastructure.Identity;

/// <summary>
/// Recusa senhas parecidas demais com os dados da própria conta (e-mail e nome).
/// Quem usa "willian2024" com o e-mail willian@… entrega a senha junto com o
/// login em qualquer vazamento de e-mails.
/// </summary>
/// <remarks>
/// Mesma regra do <c>UserAttributeSimilarityValidator</c> do Django: compara a
/// senha com cada atributo inteiro e com cada pedaço dele (separado por
/// pontuação), usando a razão do <c>SequenceMatcher.quick_ratio</c> do Python —
/// 2 × (caracteres em comum, contados como multiconjunto) ÷ (soma dos tamanhos).
/// </remarks>
public partial class UserAttributeSimilarityValidator : IPasswordValidator<ApplicationUser>
{
    private const double MaxSimilarity = 0.7;

    public Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password))
            return Task.FromResult(IdentityResult.Success);

        var lowered = password.ToLowerInvariant();

        foreach (var (attribute, label) in new[]
                 {
                     (user.Email, "o seu e-mail"),
                     (user.DisplayName, "o seu nome"),
                 })
        {
            if (string.IsNullOrWhiteSpace(attribute)) continue;

            var value = attribute.ToLowerInvariant();
            foreach (var part in NonWord().Split(value).Append(value))
            {
                if (part.Length == 0 || SkipByLength(lowered, part)) continue;
                if (QuickRatio(lowered, part) < MaxSimilarity) continue;

                return Task.FromResult(IdentityResult.Failed(new IdentityError
                {
                    Code = "PasswordTooSimilarToUser",
                    Description = $"A senha é muito parecida com {label}.",
                }));
            }
        }

        return Task.FromResult(IdentityResult.Success);
    }

    /// <summary>
    /// Pedaço curto demais para alcançar a similaridade máxima diante de uma
    /// senha longa — não há como passar do limite, então nem calcula.
    /// </summary>
    private static bool SkipByLength(string password, string value) =>
        password.Length >= 10 * value.Length && value.Length < MaxSimilarity / 2 * password.Length;

    /// <summary>
    /// Porta do <c>difflib.SequenceMatcher.quick_ratio</c>: trata as strings como
    /// multiconjuntos de caracteres, então "abc123" e "321cba" batem 100%.
    /// </summary>
    public static double QuickRatio(string a, string b)
    {
        if (a.Length + b.Length == 0) return 1.0;

        var available = new Dictionary<char, int>();
        foreach (var c in b)
            available[c] = available.GetValueOrDefault(c) + 1;

        var matches = 0;
        foreach (var c in a)
        {
            if (available.TryGetValue(c, out var remaining) && remaining > 0)
            {
                available[c] = remaining - 1;
                matches++;
            }
        }

        return 2.0 * matches / (a.Length + b.Length);
    }

    [GeneratedRegex(@"\W+")]
    private static partial Regex NonWord();
}
