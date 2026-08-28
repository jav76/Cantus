using System.CommandLine;
using System.Text.RegularExpressions;
using Cantus.Core.Logging;
using Cantus.Infrastructure;
using Cantus.Infrastructure.Logging;
using Cantus.Infrastructure.Persistence;
using Cantus.Server.BackgroundServices;
using Cantus.Server.Endpoints;
using Cantus.Server.Hubs;
using Cantus.Server.Middleware;
using Cantus.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scalar.AspNetCore;

// 1. CLI Parsing via System.CommandLine
Option<string> logConfigOption = new("--log-configuration", "-l")
{
    Description = "Specify logging configuration level (none, debug, trace)."
};

RootCommand rootCommand = new("Cantus - Real-Time Spotify Synced Lyrics Server")
{
    logConfigOption
};
rootCommand.TreatUnmatchedTokensAsErrors = false;

ParseResult parseResult = rootCommand.Parse(args);
string? logConfigValue = parseResult.GetValue(logConfigOption);

string? logConfigRaw = !string.IsNullOrWhiteSpace(logConfigValue)
    ? logConfigValue
    : Environment.GetEnvironmentVariable("CANTUS_LOG_CONFIGURATION");

LoggingConfiguration loggingConfig = CantusLoggingManager.ParseConfiguration(logConfigRaw);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// 2. Logging Framework Configuration (log4net & Microsoft.Extensions.Logging)
string connStr = builder.Configuration.GetConnectionString("CantusDatabase") ?? "Data Source=cantus.db";
CantusLoggingManager.InitializeServer(loggingConfig, connStr);

builder.Logging.ClearProviders();
builder.Logging.AddProvider(new Log4NetLoggerProvider());

// 3. Exception Handling Services
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// 4. Data Directory & Data Protection Key Persistence
string? dataDir = Environment.GetEnvironmentVariable("DATA_DIR")
    ?? (Directory.Exists("/app/data") ? "/app/data" : null);

if (!string.IsNullOrEmpty(dataDir))
{
    Directory.CreateDirectory(dataDir);
    string keysDir = Path.Combine(dataDir, "keys");
    Directory.CreateDirectory(keysDir);

    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
        .SetApplicationName("Cantus");
}
else
{
    builder.Services.AddDataProtection()
        .SetApplicationName("Cantus");
}

// 5. Core & Infrastructure Services
builder.Services.AddCantusInfrastructure(builder.Configuration);

// 6. Server Options, URL Resolver & In-Memory Registry
builder.Services.Configure<PlaybackPollerOptions>(
    builder.Configuration.GetSection(PlaybackPollerOptions.SECTION_NAME));
builder.Services.AddSingleton<IPlaybackSessionRegistry, PlaybackSessionRegistry>();
builder.Services.AddSingleton<IHostUrlResolver, HostUrlResolver>();

// 7. Reverse Proxy & Forwarded Headers
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.All;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// 8. SignalR & Background Services
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

builder.Services.AddHostedService<ActiveUsersPlaybackMonitor>();

// 9. CORS Policy (Local network & dev clients)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true);
    });
});

// 10. OpenAPI & Documentation
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// 11. Exception Handler & Reverse Proxy Middleware
app.UseExceptionHandler();
app.UseForwardedHeaders();

// 12. Ensure Database Directory Exists & Run Migrations
using (IServiceScope scope = app.Services.CreateScope())
{
    IConfiguration config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    string dbConnStr = config.GetConnectionString("CantusDatabase") ?? "Data Source=cantus.db";
    Match match = Regex.Match(dbConnStr, @"Data Source=([^;]+)", RegexOptions.IgnoreCase);
    if (match.Success)
    {
        string dbPath = match.Groups[1].Value.Trim();
        string? dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }
    }

    CantusDbContext dbContext = scope.ServiceProvider.GetRequiredService<CantusDbContext>();
    dbContext.Database.Migrate();
}

// 13. OpenAPI Endpoints
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "Cantus Real-Time Lyrics API";
        options.Theme = ScalarTheme.Purple;
    });
}

// 14. Static Files & Routing
app.UseCors();
app.UseDefaultFiles();

FileExtensionContentTypeProvider contentTypeProvider = new();
contentTypeProvider.Mappings[".dat"] = "application/octet-stream";
contentTypeProvider.Mappings[".wasm"] = "application/wasm";
contentTypeProvider.Mappings[".dll"] = "application/octet-stream";
contentTypeProvider.Mappings[".pdb"] = "application/octet-stream";
contentTypeProvider.Mappings[".uprimarker"] = "text/plain";
contentTypeProvider.Mappings[".manifest"] = "text/plain";
contentTypeProvider.Mappings[".blat"] = "application/octet-stream";
contentTypeProvider.Mappings[".woff"] = "font/woff";
contentTypeProvider.Mappings[".woff2"] = "font/woff2";
contentTypeProvider.Mappings[".ttf"] = "font/ttf";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider,
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream"
});

// 15. SignalR Hub Mapping
app.MapHub<PlaybackHub>("/hubs/playback");

// 16. REST API Route Groups
app.MapAuthEndpoints();
app.MapLyricsEndpoints();

// 17. SPA Fallback
app.MapFallbackToFile("index.html");

app.Run();

// Make Program accessible to WebApplicationFactory in tests
public partial class Program { }
