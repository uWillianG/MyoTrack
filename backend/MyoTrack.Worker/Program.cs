using Microsoft.EntityFrameworkCore;
using MyoTrack.Infrastructure;
using MyoTrack.Infrastructure.Ai;
using MyoTrack.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.AddSingleton<ILlmJsonClient, AnthropicJsonClient>();
builder.Services.AddScoped<WorkoutGenerationService>();
builder.Services.AddScoped<DietGenerationService>();

builder.Services.AddHostedService<JobPollerService>();

var host = builder.Build();
host.Run();
