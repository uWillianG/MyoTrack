namespace MyoTrack.Domain.Entities;

/// <summary>
/// Mensagem do chat com o coach IA. O histórico persiste para dar contexto às
/// próximas respostas e sobreviver ao reload da página.
/// </summary>
public class CoachMessage
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>True = mensagem do usuário; false = resposta do coach.</summary>
    public bool FromUser { get; set; }

    public string Content { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
