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
            configuration.GetSection(SpotifyOptions.SectionName));
        services.Configure<LrclibOptions>(
            configuration.GetSection(LrclibOptions.SectionName));
        services.Configure<PlaybackInterpolatorOptions>(
            configuration.GetSection(PlaybackInterpolatorOptions.SectionName));

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
        services.AddScoped<ILyricsCacheRepository, SqliteLyricsCacheRepository>();

        services.AddHttpClient<LrclibLyricsProvider>((sp, client) =>
        {
            LrclibOptions options = sp.GetRequiredService<IOptions<LrclibOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.AddScoped<ILyricsProvider, CachedLyricsService>();

        // 6. Spotify Services
        services.AddScoped<ISpotifyAuthService, SpotifyAuthService>();
        services.AddScoped<ISpotifyPlayerClient, SpotifyPlayerClient>();

        return services;
    }
}
