using Microsoft.EntityFrameworkCore;
using MyoTrack.Infrastructure;
using MyoTrack.Infrastructure.Ai;
using MyoTrack.Infrastructure.Storage;
using MyoTrack.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<VisionOptions>(builder.Configuration.GetSection(VisionOptions.SectionName));
builder.Services.AddSingleton<ILlmJsonClient, AnthropicJsonClient>();
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
