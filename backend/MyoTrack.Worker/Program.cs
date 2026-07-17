using Microsoft.EntityFrameworkCore;
using MyoTrack.Infrastructure;
using MyoTrack.Infrastructure.Ai;
using MyoTrack.Infrastructure.Storage;
using MyoTrack.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Segredos de desenvolvimento local (gitignored) — ex.: chaves de LLM para dotnet run.
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<VisionOptions>(builder.Configuration.GetSection(VisionOptions.SectionName));
// Provider de LLM selecionado por Llm:Provider (ou autodetectado pela chave presente).
builder.Services.AddHttpClient(GeminiJsonClient.HttpClientName,
    client => client.Timeout = TimeSpan.FromMinutes(3));
builder.Services.AddSingleton<AnthropicJsonClient>();
builder.Services.AddSingleton<GeminiJsonClient>();
// Edição de imagem (análise ilustrada de refeição) — sempre Gemini, o único
// provider com geração de imagem; sem chave/cota o serviço degrada sozinho.
builder.Services.AddSingleton<GeminiImageClient>();
builder.Services.AddSingleton<ILlmJsonClient>(sp =>
{
    var llmOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmOptions>>().Value;
    return llmOptions.EffectiveProvider == "gemini"
        ? sp.GetRequiredService<GeminiJsonClient>()
        : sp.GetRequiredService<AnthropicJsonClient>();
});
builder.Services.AddSingleton<IMediaStorage, MinioMediaStorage>();
builder.Services.AddHttpClient(VideoAnalysisService.HttpClientName);
// A busca no TikTok exige um User-Agent de browser; sem ele a resposta vem vazia.
builder.Services.AddHttpClient(TikTokVideoService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/json");
});
builder.Services.AddScoped<TikTokVideoService>();
builder.Services.AddScoped<WorkoutGenerationService>();
builder.Services.AddScoped<DietGenerationService>();
builder.Services.AddScoped<MealAnalysisService>();
builder.Services.AddScoped<VideoAnalysisService>();

builder.Services.AddHostedService<JobPollerService>();
builder.Services.AddHostedService<MediaRetentionService>();

var host = builder.Build();
host.Run();
