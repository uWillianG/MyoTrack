using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MyoTrack.Api.Services;
using MyoTrack.Infrastructure;
using MyoTrack.Infrastructure.Identity;
using MyoTrack.Infrastructure.Seed;

var builder = WebApplication.CreateBuilder(args);

// Segredos de desenvolvimento local (gitignored) — ex.: chaves Stripe para dotnet run.
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddErrorDescriber<PortugueseIdentityErrorDescriber>()
    .AddDefaultTokenProviders(); // tokens de redefinição de senha

// Validade do link de redefinição — o texto do e-mail promete o mesmo prazo.
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(24));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<TokenService>();

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));

// E-mail transacional (redefinição de senha). Sem credenciais SMTP, o remetente
// apenas registra a mensagem no log — dá para testar o fluxo sem configurar nada.
builder.Services.Configure<MyoTrack.Infrastructure.Email.EmailOptions>(
    builder.Configuration.GetSection(MyoTrack.Infrastructure.Email.EmailOptions.SectionName));
builder.Services.AddScoped<MyoTrack.Infrastructure.Email.IEmailSender,
    MyoTrack.Infrastructure.Email.SmtpEmailSender>();

// Login social com Google (desligado sem ClientId/ClientSecret).
builder.Services.Configure<GoogleOAuthOptions>(
    builder.Configuration.GetSection(GoogleOAuthOptions.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddScoped<GoogleOAuthService>();

builder.Services.Configure<MyoTrack.Api.Controllers.BillingOptions>(
    builder.Configuration.GetSection(MyoTrack.Api.Controllers.BillingOptions.SectionName));
builder.Services.AddScoped<EntitlementService>();

builder.Services.Configure<MyoTrack.Infrastructure.Storage.StorageOptions>(
    builder.Configuration.GetSection(MyoTrack.Infrastructure.Storage.StorageOptions.SectionName));
builder.Services.AddSingleton<MyoTrack.Infrastructure.Storage.IMediaStorage,
    MyoTrack.Infrastructure.Storage.MinioMediaStorage>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Seção de configuração 'Jwt' ausente.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        options.Events = new JwtBearerEvents
        {
            // EventSource (SSE) não envia headers — aceita o token via query só nesse caminho.
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.Request.Path.StartsWithSegments("/api/jobs"))
                    context.Token = token;
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

// Disparar e-mail é o endpoint mais fácil de abusar (spam para terceiros): limita
// por IP de origem. Atrás do Caddy o IP real vem no X-Forwarded-For (abaixo).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.AuthEmail, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15),
            }));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // O proxy é o Caddy do compose; o IP dele muda a cada recriação do container.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "MyoTrack API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            []
        },
    });
});

const string CorsPolicy = "Frontend";
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Migra e semeia no startup — adequado para instância única em VPS.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        await DbSeeder.SeedAsync(db, roleManager);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
