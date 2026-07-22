using Microsoft.Extensions.Options;
using MyoTrack.Api.Services;
using MyoTrack.Infrastructure.Email;

namespace MyoTrack.Tests;

public class EmailTemplateTests
{
    private const string ResetUrl = "https://myotrack.app/redefinir-senha?uid=abc&token=CfDJ8%2Ba%2Bb";

    [Fact]
    public void PasswordReset_PutsTheLinkInBothBodies()
    {
        var email = EmailTemplates.PasswordReset(ResetUrl, 24);

        // O corpo em texto puro é o que muitos clientes mostram — sem o link ali,
        // quem lê em modo texto fica sem como redefinir.
        Assert.Contains(ResetUrl, email.TextBody);
        Assert.Contains("myotrack.app/redefinir-senha", email.HtmlBody);
        Assert.Contains("24 horas", email.TextBody);
    }

    [Fact]
    public void PasswordReset_EscapesTheUrlInHtml()
    {
        var email = EmailTemplates.PasswordReset(ResetUrl, 24);

        // O link tem "&" separando uid e token: cru no HTML, o cliente de e-mail
        // pode truncar o href e o link chega quebrado.
        Assert.Contains("uid=abc&amp;token=", email.HtmlBody);
        Assert.DoesNotContain("uid=abc&token=", email.HtmlBody);
    }

    [Fact]
    public void PasswordReset_HasSubject()
    {
        Assert.False(string.IsNullOrWhiteSpace(EmailTemplates.PasswordReset(ResetUrl, 24).Subject));
    }
}

public class GoogleOAuthServiceTests
{
    private static GoogleOAuthService Service(string clientId = "id-do-cliente", string secret = "segredo") =>
        new(Options.Create(new GoogleOAuthOptions { ClientId = clientId, ClientSecret = secret }),
            new DummyHttpClientFactory());

    [Theory]
    [InlineData("", "")]
    [InlineData("id-do-cliente", "")]
    [InlineData("", "segredo")]
    public void IsEnabled_RequiresBothCredentials(string clientId, string secret)
    {
        Assert.False(Service(clientId, secret).IsEnabled);
    }

    [Fact]
    public void IsEnabled_WhenBothCredentialsArePresent()
    {
        Assert.True(Service().IsEnabled);
    }

    [Fact]
    public void BuildAuthorizationUrl_CarriesStateAndRedirect()
    {
        const string redirect = "https://myotrack.app/api/auth/google/callback";
        var url = Service().BuildAuthorizationUrl(redirect, "estado-anti-csrf");

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        Assert.Contains("client_id=id-do-cliente", url);
        Assert.Contains("state=estado-anti-csrf", url);
        Assert.Contains("response_type=code", url);
        // O redirect precisa ir escapado: cru, os ":" e "/" quebram a querystring.
        Assert.Contains(Uri.EscapeDataString(redirect), url);
    }

    private class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
