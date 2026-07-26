using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FocusAI.Api.Endpoints;
using FocusAI.Api.Jobs;
using FocusAI.Api.Security;
using FocusAI.Application;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Infrastructure;
using FocusAI.Infrastructure.Configuration;
using FocusAI.Infrastructure.Persistence;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<PipelineJobs>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Enums travel as names, not ordinals. Numeric enums make the API opaque to
    // read and turn any future reordering into a silent breaking change.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
{
    // Failing at startup is the only safe option: a short or missing key would
    // silently produce forgeable tokens.
    throw new InvalidOperationException(
        "Jwt:SigningKey en az 32 bayt olmalı. Ortam değişkeni veya user-secrets ile sağlayın.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            // The default five-minute grace makes short-lived access tokens
            // meaningless; expiry should mean expiry.
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("admin", policy => policy.RequireRole("admin"));

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                  ?? ["http://localhost:3000"];

    policy.WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
}));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
    });

    // LLM calls cost real money per request, so they get their own tight budget.
    options.AddFixedWindowLimiter("ai", limiter =>
    {
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromMinutes(1);
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Focus AI API",
        Version = "v1",
        Description = "Yapay zekâ destekli teknoloji takip platformu."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHealthChecks();

var ingestionOptions = builder.Configuration.GetSection(IngestionOptions.SectionName).Get<IngestionOptions>()
                       ?? new IngestionOptions();

var hangfireEnabled = ingestionOptions.EnableBackgroundJobs &&
                      !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Postgres"));

if (hangfireEnabled)
{
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(pg =>
            pg.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Postgres"))));

    builder.Services.AddHangfireServer(options => options.WorkerCount = 4);
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Focus AI API v1"));
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/", () => Results.Ok(new { name = "Focus AI API", version = "1.0" })).AllowAnonymous();

app.MapAuthEndpoints();
app.MapStoryEndpoints();
app.MapUserEndpoints();
app.MapAdminEndpoints();

await InitializeDatabaseAsync(app);

if (hangfireEnabled)
{
    ScheduleRecurringJobs(app, ingestionOptions);
}

app.Run();

static string PartitionKey(HttpContext context) =>
    context.User.Identity?.IsAuthenticated == true
        ? context.User.Identity.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"
        : context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

static async Task InitializeDatabaseAsync(WebApplication app)
{
    // Skipping migrations is the right call for CI and for `--help`-style runs,
    // but it must be an explicit opt-out rather than a silent failure.
    if (app.Configuration.GetValue("SkipDatabaseInitialization", false))
    {
        return;
    }

    using var scope = app.Services.CreateScope();
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

    try
    {
        await initializer.InitializeAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "FocusAI database initialization failed — API cannot serve requests");
        throw;
    }
}

static void ScheduleRecurringJobs(WebApplication app, IngestionOptions options)
{
    var recurring = app.Services.GetRequiredService<IRecurringJobManager>();
    var interval = Math.Clamp(options.IngestCronMinutes, 5, 240);

    recurring.AddOrUpdate<PipelineJobs>(
        "focus-ai-pipeline",
        job => job.RunIngestionAsync(CancellationToken.None),
        $"*/{interval} * * * *");

    // Hourly, because "08:00" means 08:00 wherever the reader is; the job itself
    // filters down to the users whose local hour has just arrived.
    recurring.AddOrUpdate<PipelineJobs>(
        "focus-ai-daily-digest",
        job => job.BuildDailyDigestsAsync(CancellationToken.None),
        "0 * * * *");

    recurring.AddOrUpdate<PipelineJobs>(
        "focus-ai-weekly-digest",
        job => job.BuildWeeklyDigestsAsync(CancellationToken.None),
        "0 6 * * 1");

    recurring.AddOrUpdate<PipelineJobs>(
        "focus-ai-trends",
        job => job.RebuildTrendsAsync(CancellationToken.None),
        "30 3 * * *");
}

/// <summary>Exposed so integration tests can build the same host.</summary>
public partial class Program;
