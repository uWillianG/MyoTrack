using Microsoft.AspNetCore.Identity;
using MyoTrack.Infrastructure.Identity;

namespace MyoTrack.Tests;

/// <summary>
/// Os validadores não usam o UserManager — daí o null! nas chamadas.
/// </summary>
public class CommonPasswordValidatorTests
{
    private static readonly CommonPasswordValidator Validator = new();

    private static IdentityResult Validate(string password) =>
        Validator.ValidateAsync(null!, new ApplicationUser(), password).GetAwaiter().GetResult();

    [Theory]
    [InlineData("password")]
    [InlineData("123456789")]
    [InlineData("qwerty")]
    [InlineData("senha123")]
    // A lista é comparada em minúsculas e sem espaços nas pontas, como no Django.
    [InlineData("PassWord")]
    [InlineData("  password  ")]
    public void RejectsPasswordsFromTheLeakedList(string password)
    {
        Assert.False(Validate(password).Succeeded);
    }

    [Theory]
    // Ausentes da lista anglófona do Django, óbvias para o público brasileiro.
    [InlineData("futebol")]
    [InlineData("corinthians")]
    [InlineData("senha1234")]
    [InlineData("supino")]
    public void RejectsBrazilianCommonPasswords(string password)
    {
        Assert.False(Validate(password).Succeeded);
    }

    [Theory]
    [InlineData("Tr0vao!Verde9")]
    [InlineData("Cachorro#Azul42")]
    public void AcceptsPasswordsOutsideTheList(string password)
    {
        Assert.True(Validate(password).Succeeded);
    }

    [Fact]
    public void RejectionExplainsTheReason()
    {
        var error = Assert.Single(Validate("password").Errors);
        Assert.Equal("PasswordTooCommon", error.Code);
        Assert.Contains("comum", error.Description);
    }
}

public class UserAttributeSimilarityValidatorTests
{
    private static readonly UserAttributeSimilarityValidator Validator = new();

    private static IdentityResult Validate(string password, string? email = null, string? displayName = null) =>
        Validator.ValidateAsync(null!, new ApplicationUser { Email = email, DisplayName = displayName }, password)
            .GetAwaiter().GetResult();

    // Valores conferidos contra o difflib.SequenceMatcher.quick_ratio do Python,
    // que é a referência usada pelo validador equivalente do Django.
    [Theory]
    [InlineData("willian2024", "willian", 0.777778)]
    [InlineData("gmail123", "gmail", 0.769231)]
    [InlineData("tr0vao!verde9", "willian", 0.100000)]
    [InlineData("joaosilva", "joao", 0.615385)]
    // Multiconjunto de caracteres: a ordem não conta.
    [InlineData("abc123", "321cba", 1.000000)]
    [InlineData("x", "abcdefghij", 0.000000)]
    [InlineData("", "", 1.000000)]
    public void QuickRatio_MatchesPythonDifflib(string a, string b, double expected)
    {
        Assert.Equal(expected, UserAttributeSimilarityValidator.QuickRatio(a, b), 6);
    }

    [Fact]
    public void RejectsPasswordBuiltFromTheEmailLocalPart()
    {
        var result = Validate("Willian2024!", email: "willian@gmail.com");

        Assert.False(result.Succeeded);
        Assert.Equal("PasswordTooSimilarToUser", Assert.Single(result.Errors).Code);
        Assert.Contains("e-mail", Assert.Single(result.Errors).Description);
    }

    [Fact]
    public void RejectsPasswordBuiltFromTheDisplayName()
    {
        var result = Validate("Cardoso#12", displayName: "Cardoso");

        Assert.False(result.Succeeded);
        Assert.Contains("nome", Assert.Single(result.Errors).Description);
    }

    [Fact]
    public void AcceptsPasswordUnrelatedToTheAccount()
    {
        Assert.True(Validate("Tr0vao!Verde9", email: "willian@gmail.com", displayName: "Willian").Succeeded);
    }

    [Fact]
    public void IgnoresAccountsWithoutEmailOrName()
    {
        Assert.True(Validate("Tr0vao!Verde9").Succeeded);
    }
}
