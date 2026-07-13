using Microsoft.EntityFrameworkCore;
using MyoTrack.Infrastructure;
using MyoTrack.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHostedService<JobPollerService>();

var host = builder.Build();
host.Run();
