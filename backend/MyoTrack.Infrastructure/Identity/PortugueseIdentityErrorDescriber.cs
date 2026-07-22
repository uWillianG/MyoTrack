using Microsoft.AspNetCore.Identity;

namespace MyoTrack.Infrastructure.Identity;

/// <summary>
/// Mensagens do Identity em pt-BR — elas chegam direto na tela de cadastro e na
/// de redefinição de senha, então não podem ficar em inglês.
/// </summary>
public class PortugueseIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) => new()
    {
        Code = nameof(PasswordTooShort),
        Description = $"A senha precisa ter pelo menos {length} caracteres.",
    };

    public override IdentityError PasswordRequiresDigit() => new()
    {
        Code = nameof(PasswordRequiresDigit),
        Description = "A senha precisa ter pelo menos um número.",
    };

    public override IdentityError PasswordRequiresUpper() => new()
    {
        Code = nameof(PasswordRequiresUpper),
        Description = "A senha precisa ter pelo menos uma letra maiúscula.",
    };

    public override IdentityError PasswordRequiresLower() => new()
    {
        Code = nameof(PasswordRequiresLower),
        Description = "A senha precisa ter pelo menos uma letra minúscula.",
    };

    public override IdentityError PasswordRequiresNonAlphanumeric() => new()
    {
        Code = nameof(PasswordRequiresNonAlphanumeric),
        Description = "A senha precisa ter pelo menos um símbolo (ex.: !, @, #).",
    };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => new()
    {
        Code = nameof(PasswordRequiresUniqueChars),
        Description = $"A senha precisa ter pelo menos {uniqueChars} caracteres diferentes.",
    };

    public override IdentityError DuplicateEmail(string email) => new()
    {
        Code = nameof(DuplicateEmail),
        Description = "Já existe uma conta com esse e-mail.",
    };

    public override IdentityError DuplicateUserName(string userName) => new()
    {
        Code = nameof(DuplicateUserName),
        Description = "Já existe uma conta com esse e-mail.",
    };

    public override IdentityError InvalidEmail(string? email) => new()
    {
        Code = nameof(InvalidEmail),
        Description = "E-mail inválido.",
    };

    public override IdentityError InvalidToken() => new()
    {
        Code = nameof(InvalidToken),
        Description = "Link inválido ou expirado. Peça um novo.",
    };

    public override IdentityError PasswordMismatch() => new()
    {
        Code = nameof(PasswordMismatch),
        Description = "Senha incorreta.",
    };
}
