using MyoTrack.Infrastructure.Ai;

namespace MyoTrack.Tests;

public class TikTokVideoServiceTests
{
    [Fact]
    public void ExtractFirstVideoUrl_PlainHtml()
    {
        const string html = """
            <a href="https://www.tiktok.com/@personal.fit/video/7301234567890123456">agachamento</a>
            <a href="https://www.tiktok.com/@outro/video/9999999999999999999">outro</a>
            """;
        Assert.Equal(
            "https://www.tiktok.com/@personal.fit/video/7301234567890123456",
            TikTokVideoService.ExtractFirstVideoUrl(html));
    }

    [Fact]
    public void ExtractFirstVideoUrl_JsonWithEscapedSlashes()
    {
        const string html =
            """{"shareUrl":"https://www.tiktok.com/@treino.certo/video/7311111111111111111"}""";
        Assert.Equal(
            "https://www.tiktok.com/@treino.certo/video/7311111111111111111",
            TikTokVideoService.ExtractFirstVideoUrl(html));
    }

    [Fact]
    public void ExtractFirstVideoUrl_JsonWithBackslashSlashes()
    {
        const string html =
            """{"url":"https:\/\/www.tiktok.com\/@coach_br\/video\/7322222222222222222"}""";
        Assert.Equal(
            "https://www.tiktok.com/@coach_br/video/7322222222222222222",
            TikTokVideoService.ExtractFirstVideoUrl(html));
    }

    [Fact]
    public void ExtractFirstVideoUrl_NoMatch_ReturnsNull()
    {
        Assert.Null(TikTokVideoService.ExtractFirstVideoUrl("<html><body>login required</body></html>"));
        Assert.Null(TikTokVideoService.ExtractFirstVideoUrl(
            "https://www.tiktok.com/@user/photo/123 https://www.youtube.com/watch?v=abc"));
    }

    [Fact]
    public void BuildSearchUrl_EncodesExerciseName()
    {
        var url = TikTokVideoService.BuildSearchUrl("Agachamento Búlgaro");
        Assert.StartsWith("https://www.tiktok.com/search?q=", url);
        Assert.Contains("como%20fazer%20Agachamento%20B%C3%BAlgaro%20academia", url);
    }
}
