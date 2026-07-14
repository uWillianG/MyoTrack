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
builder.Services.AddScoped<WorkoutGenerationService>();
builder.Services.AddScoped<DietGenerationService>();
builder.Services.AddScoped<MealAnalysisService>();
builder.Services.AddScoped<VideoAnalysisService>();

builder.Services.AddHostedService<JobPollerService>();

var host = builder.Build();
host.Run();
