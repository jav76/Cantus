using Cantus.Core.Interfaces;
using Cantus.Infrastructure.Clock;
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
        services.Configure<LrclibOptions>(
            configuration.GetSection(LrclibOptions.SECTION_NAME));
        services.Configure<PlaybackInterpolatorOptions>(
            configuration.GetSection(PlaybackInterpolatorOptions.SECTION_NAME));

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
        services.AddTransient<IPlaybackInterpolator, PlaybackInterpolator>();

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
