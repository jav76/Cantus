using System;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Cantus.Server.Middleware;

/// <summary>
/// Serves the <c>.br</c> or <c>.gz</c> file that the WASM publish writes next to each asset, when the
/// browser accepts that encoding. The response keeps the original file's content type (the browser
/// compiles <c>.wasm</c> while streaming only when it is <c>application/wasm</c>).
/// <para>
/// Every static response also gets a <c>Cache-Control</c> header. Without one, browsers keep a file
/// for a share of its age, so a deploy could leave a stale <c>index.html</c> pointing at files that
/// are gone. Files with a content hash in their name are cached for a year; the rest are revalidated.
/// </para>
/// </summary>
public static partial class PrecompressedStaticFiles
{
    public const string IMMUTABLE_CACHE_CONTROL = "public, max-age=31536000, immutable";
    public const string REVALIDATE_CACHE_CONTROL = "no-cache";

    private const string ORIGINAL_PATH_KEY = "Cantus.PrecompressedStaticFiles.Path";
    private const string ORIGINAL_CONTENT_TYPE_KEY = "Cantus.PrecompressedStaticFiles.ContentType";
    private const string CONTENT_ENCODING_KEY = "Cantus.PrecompressedStaticFiles.ContentEncoding";

    // Server preference order: brotli is smaller than gzip for every asset the publish compresses.
    private static readonly (string Encoding, string Extension)[] ENCODINGS =
    [
        ("br", ".br"),
        ("gzip", ".gz"),
    ];

    public static IApplicationBuilder UsePrecompressedStaticFiles(
        this IApplicationBuilder app,
        IFileProvider fileProvider,
        StaticFileOptions options)
    {
        IContentTypeProvider contentTypeProvider = options.ContentTypeProvider ?? new FileExtensionContentTypeProvider();
        options.FileProvider ??= fileProvider;

        Action<StaticFileResponseContext>? previousOnPrepare = options.OnPrepareResponse;
        options.OnPrepareResponse = context =>
        {
            ApplyEncodingHeaders(context.Context);
            ApplyCacheHeaders(context.Context);
            previousOnPrepare?.Invoke(context);
        };

        app.Use((context, next) =>
        {
            TryRewriteToCompressed(context, fileProvider, contentTypeProvider);
            return next(context);
        });

        return app.UseStaticFiles(options);
    }

    /// <summary>
    /// Serves <paramref name="filePath"/> for any request path without a file extension, like
    /// <c>MapFallbackToFile</c>, but through the same compression and cache rules as other files.
    /// </summary>
    public static IEndpointConventionBuilder MapPrecompressedFallbackToFile(
        this IEndpointRouteBuilder endpoints,
        string filePath,
        IFileProvider fileProvider,
        StaticFileOptions options)
    {
        PathString fallbackPath = new("/" + filePath.TrimStart('/'));

        IApplicationBuilder branch = endpoints.CreateApplicationBuilder();
        branch.Use((context, next) =>
        {
            context.Request.Path = fallbackPath;
            // The static file middleware does nothing while an endpoint is selected.
            context.SetEndpoint(null);
            return next(context);
        });
        branch.UsePrecompressedStaticFiles(fileProvider, options);

        return endpoints.MapFallback("{*path:nonfile}", branch.Build());
    }

    // The .NET SDK puts a 10-character content hash before the extension of every file in _framework.
    [GeneratedRegex(@"^/_framework/[^/]+\.[a-z0-9]{10}\.[^/.]+$", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintedPath();

    public static string GetCacheControl(string path) =>
        FingerprintedPath().IsMatch(path) ? IMMUTABLE_CACHE_CONTROL : REVALIDATE_CACHE_CONTROL;

    private static void TryRewriteToCompressed(
        HttpContext context,
        IFileProvider fileProvider,
        IContentTypeProvider contentTypeProvider)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        string? path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path) || path.EndsWith('/'))
        {
            return;
        }

        context.Items[ORIGINAL_PATH_KEY] = path;

        if (!contentTypeProvider.TryGetContentType(path, out string? contentType))
        {
            return;
        }

        if (!fileProvider.GetFileInfo(path).Exists)
        {
            return;
        }

        bool hasCompressedSibling = false;
        foreach ((string encoding, string extension) in ENCODINGS)
        {
            if (!fileProvider.GetFileInfo(path + extension).Exists)
            {
                continue;
            }

            hasCompressedSibling = true;
            if (!AcceptsEncoding(context.Request, encoding))
            {
                continue;
            }

            context.Items[ORIGINAL_CONTENT_TYPE_KEY] = contentType;
            context.Items[CONTENT_ENCODING_KEY] = encoding;
            context.Request.Path = new PathString(path + extension);
            break;
        }

        // Caches must key on Accept-Encoding whenever the response could differ by it,
        // including the uncompressed response sent to a client that accepts neither encoding.
        if (hasCompressedSibling)
        {
            context.Response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);
        }
    }

    private static void ApplyEncodingHeaders(HttpContext context)
    {
        if (context.Items[CONTENT_ENCODING_KEY] is not string encoding
            || context.Items[ORIGINAL_CONTENT_TYPE_KEY] is not string contentType)
        {
            return;
        }

        context.Response.ContentType = contentType;
        context.Response.Headers[HeaderNames.ContentEncoding] = encoding;
    }

    private static void ApplyCacheHeaders(HttpContext context)
    {
        string path = context.Items[ORIGINAL_PATH_KEY] as string ?? context.Request.Path.Value ?? string.Empty;
        context.Response.Headers.CacheControl = GetCacheControl(path);
    }

    private static bool AcceptsEncoding(HttpRequest request, string encoding)
    {
        StringValues header = request.Headers.AcceptEncoding;
        if (StringValues.IsNullOrEmpty(header)
            || !StringWithQualityHeaderValue.TryParseList(header, out IList<StringWithQualityHeaderValue>? values))
        {
            return false;
        }

        bool wildcardAccepted = false;
        foreach (StringWithQualityHeaderValue value in values)
        {
            bool accepted = value.Quality is null or > 0;
            if (StringSegment.Equals(value.Value, encoding, StringComparison.OrdinalIgnoreCase))
            {
                return accepted;
            }

            if (StringSegment.Equals(value.Value, "*", StringComparison.Ordinal))
            {
                wildcardAccepted = accepted;
            }
        }

        return wildcardAccepted;
    }
}
