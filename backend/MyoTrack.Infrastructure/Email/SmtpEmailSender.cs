using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MyoTrack.Infrastructure.Email;

/// <summary>
/// Envia por SMTP quando há credenciais; sem elas, escreve a mensagem no log
/// (equivalente ao console backend do Django) para permitir testar os fluxos de
/// e-mail em desenvolvimento sem configurar nada.
/// </summary>
public class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.User) && !string.IsNullOrWhiteSpace(_options.Password);

    public async Task SendAsync(string to, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            logger.LogInformation(
                "SMTP não configurado — e-mail não enviado.\nPara: {To}\nAssunto: {Subject}\n{Body}",
                to, subject, textBody);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, string.IsNullOrWhiteSpace(_options.From) ? _options.User : _options.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
            ct);
        await client.AuthenticateAsync(_options.User, _options.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);

        logger.LogInformation("E-mail enviado para {To}: {Subject}", to, subject);
    }
}
