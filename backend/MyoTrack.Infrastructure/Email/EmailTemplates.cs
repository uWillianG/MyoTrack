using System.Net;

namespace MyoTrack.Infrastructure.Email;

public record EmailContent(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Modelos de e-mail transacional (pt-BR). HTML inline e tabelas simples porque
/// clientes de e-mail não suportam CSS moderno nem folhas externas.
/// </summary>
public static class EmailTemplates
{
    public static EmailContent PasswordReset(string resetUrl, int validHours)
    {
        const string subject = "Redefinição de senha — MyoTrack";

        var text =
            $"""
            Recebemos um pedido para redefinir a senha da sua conta no MyoTrack.

            Abra o link abaixo para criar uma senha nova (válido por {validHours} horas):
            {resetUrl}

            Se não foi você que pediu, ignore este e-mail — sua senha continua a mesma.

            MyoTrack — seu personal trainer e nutricionista digital
            """;

        var html = Layout(
            title: "Redefinir sua senha",
            bodyHtml: $"""
                <p style="margin:0 0 16px">Recebemos um pedido para redefinir a senha da sua conta no MyoTrack.</p>
                <p style="margin:0 0 24px">Clique no botão abaixo para criar uma senha nova. O link vale por <strong>{validHours} horas</strong>.</p>
                {Button("Criar nova senha", resetUrl)}
                <p style="margin:24px 0 0;font-size:13px;color:#64748b">
                  Se o botão não funcionar, copie e cole este endereço no navegador:<br>
                  <span style="word-break:break-all">{WebUtility.HtmlEncode(resetUrl)}</span>
                </p>
                <p style="margin:16px 0 0;font-size:13px;color:#64748b">
                  Se não foi você que pediu, ignore este e-mail — sua senha continua a mesma.
                </p>
                """);

        return new EmailContent(subject, html, text);
    }

    private static string Button(string label, string url) =>
        $"""
        <table role="presentation" cellpadding="0" cellspacing="0" style="margin:0">
          <tr><td style="border-radius:12px;background:#059669">
            <a href="{WebUtility.HtmlEncode(url)}"
               style="display:inline-block;padding:12px 24px;font-family:Helvetica,Arial,sans-serif;
                      font-size:15px;font-weight:bold;color:#ffffff;text-decoration:none">{label}</a>
          </td></tr>
        </table>
        """;

    private static string Layout(string title, string bodyHtml) =>
        $"""
        <!doctype html>
        <html lang="pt-BR"><body style="margin:0;padding:24px;background:#f4f6f5;
          font-family:Helvetica,Arial,sans-serif;color:#334155;font-size:15px;line-height:1.6">
          <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="max-width:520px;margin:0 auto">
            <tr><td style="padding:0 0 20px">
              <span style="font-size:20px;font-weight:bold;color:#0f172a">Myo<span style="color:#10b981">Track</span></span>
            </td></tr>
            <tr><td style="background:#ffffff;border:1px solid #e2e8f0;border-radius:16px;padding:28px">
              <h1 style="margin:0 0 16px;font-size:19px;color:#0f172a">{title}</h1>
              {bodyHtml}
            </td></tr>
            <tr><td style="padding:20px 0;font-size:12px;color:#94a3b8">
              MyoTrack — seu personal trainer e nutricionista digital.<br>
              Este é um e-mail automático, não responda.
            </td></tr>
          </table>
        </body></html>
        """;
}
