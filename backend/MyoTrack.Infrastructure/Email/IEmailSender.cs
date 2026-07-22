namespace MyoTrack.Infrastructure.Email;

public interface IEmailSender
{
    /// <summary>Há credenciais SMTP configuradas (envio real, não só log).</summary>
    bool IsConfigured { get; }

    Task SendAsync(string to, string subject, string htmlBody, string textBody, CancellationToken ct = default);
}
