using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Cantus.Server.Middleware;

/// <summary>
/// Serves the <c>.br</c> or <c>.gz</c> file that the WASM publish writes next to each asset, when the
/// browser accepts that encoding. The response keeps the original file's content type (the browser
/// compiles <c>.wasm</c> while streaming only when it is <c>application/wasm</c>).
/// </summary>
public static class PrecompressedStaticFiles
{
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
            previousOnPrepare?.Invoke(context);
        };

        app.Use((context, next) =>
        {
            TryRewriteToCompressed(context, fileProvider, contentTypeProvider);
            return next(context);
        });

        return app.UseStaticFiles(options);
    }

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
