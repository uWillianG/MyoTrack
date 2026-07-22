namespace MyoTrack.Infrastructure.Email;

/// <summary>
/// SMTP de saída. Sem <see cref="User"/>/<see cref="Password"/> o envio fica
/// desligado e as mensagens são apenas registradas no log — o mesmo "modo mock"
/// dos demais serviços externos, para desenvolver sem credenciais.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>Remetente exibido. Vazio = usa <see cref="User"/>.</summary>
    public string From { get; set; } = string.Empty;
    public string FromName { get; set; } = "MyoTrack";
}
