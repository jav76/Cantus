using Cantus.Core.Interfaces;
using Cantus.Infrastructure.Lyrics;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Security;
using Cantus.Infrastructure.Spotify;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCantusInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. Configure Options
        services.Configure<SpotifyOptions>(
            configuration.GetSection(SpotifyOptions.SECTION_NAME));

        // Configuration binding APPENDS to collection defaults instead of
        // replacing them, so every scope listed in both SpotifyOptions.Scopes
        // and appsettings.json ends up in the authorize URL twice. Dedupe once
        // here so every consumer sees a clean list.
        services.PostConfigure<SpotifyOptions>(options =>
        {
            options.Scopes = options.Scopes.Distinct(StringComparer.Ordinal).ToList();
        });

        services.Configure<LrclibOptions>(
            configuration.GetSection(LrclibOptions.SECTION_NAME));
        services.Configure<NeteaseOptions>(
            configuration.GetSection(NeteaseOptions.SECTION_NAME));
        services.Configure<LyricsCacheOptions>(
            configuration.GetSection(LyricsCacheOptions.SECTION_NAME));

        // 2. Persistence (SQLite EF Core)
        string connectionString = configuration.GetConnectionString("CantusDatabase")
            ?? "Data Source=cantus.db";

        services.AddDbContext<CantusDbContext>(options =>
            options.UseSqlite(connectionString));

        // 3. Security & Data Protection
        services.AddDataProtection();
        services.AddSingleton<ITokenEncryptionService, DataProtectionTokenEncryptionService>();

        // 4. Clock & Interpolation
        services.AddSingleton(TimeProvider.System);

        // 5. Lyrics Services
        services.AddScoped<SqliteLyricsCacheRepository>();
        services.AddScoped<ILyricsCacheRepository>(sp =>
            new TraceLoggingLyricsCacheRepositoryDecorator(
                sp.GetRequiredService<SqliteLyricsCacheRepository>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TraceLoggingLyricsCacheRepositoryDecorator>>()));

        services.AddHttpClient<LrclibLyricsProvider>((sp, client) =>
        {
            LrclibOptions options = sp.GetRequiredService<IOptions<LrclibOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.AddHttpClient<NeteaseLyricsProvider>((sp, client) =>
        {
            NeteaseOptions options = sp.GetRequiredService<IOptions<NeteaseOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            // NetEase's unofficial endpoints expect browser-like headers.
            client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", options.BaseUrl);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        // Ordered lyrics fallback chain: LRCLIB first, then any enabled
        // fallback providers. CachedLyricsService walks this list in order.
        services.AddScoped<IReadOnlyList<ILyricsFetchProvider>>(sp =>
        {
            List<ILyricsFetchProvider> providers = new()
            {
                sp.GetRequiredService<LrclibLyricsProvider>()
            };

            NeteaseOptions neteaseOptions = sp.GetRequiredService<IOptions<NeteaseOptions>>().Value;
            if (neteaseOptions.Enabled)
            {
                providers.Add(sp.GetRequiredService<NeteaseLyricsProvider>());
            }

            return providers;
        });

        services.AddScoped<CachedLyricsService>();
        services.AddScoped<ILyricsProvider>(sp =>
            new TraceLoggingLyricsProviderDecorator(
                sp.GetRequiredService<CachedLyricsService>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TraceLoggingLyricsProviderDecorator>>()));

        // 6. Spotify Services
        services.AddScoped<SpotifyAuthService>();
        services.AddScoped<ISpotifyAuthService>(sp =>
            new TraceLoggingSpotifyAuthServiceDecorator(
                sp.GetRequiredService<SpotifyAuthService>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TraceLoggingSpotifyAuthServiceDecorator>>()));

        services.AddScoped<SpotifyPlayerClient>();
        services.AddScoped<ISpotifyPlayerClient>(sp =>
            new TraceLoggingSpotifyPlayerClientDecorator(
                sp.GetRequiredService<SpotifyPlayerClient>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TraceLoggingSpotifyPlayerClientDecorator>>()));

        return services;
    }
}
