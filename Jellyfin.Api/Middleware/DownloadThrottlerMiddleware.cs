using MediaBrowser.Controller.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Api.Middleware;

/// <summary>
/// Middleware for throttling download speeds.
/// </summary>
public class DownloadThrottlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DownloadThrottlingMiddleware> _logger;
    private readonly IServerConfigurationManager _serverConfigurationManager;


    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadThrottlingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The current HTTP context.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="serverConfigurationManager">The server configuration manager.</param>
    /// <returns>The async task.</returns>
    public DownloadThrottlingMiddleware(
        RequestDelegate next,
        ILogger<DownloadThrottlingMiddleware> logger,
        IServerConfigurationManager serverConfigurationManager)
    {
        _next = next;
        _logger = logger;
        _serverConfigurationManager = serverConfigurationManager;
    }

    /// <summary>
    /// Executes the middleware action.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>The async task.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsLocalRequest(context.Connection))
        {
            long bytesPerSecond = _serverConfigurationManager.Configuration.RemoteClientDownloadLimit;

            if (bytesPerSecond > 0)
            {
                _logger.LogInformation("Throttling download to {MaxBytesPerSecond} bytes per second for remote client", bytesPerSecond);

                long bucketSize = (long)(bytesPerSecond * 2.0);
                var originalBodyStream = context.Response.Body;
                using var throttledStream = new ThrottledResponseStream(
                originalBodyStream,
                bytesPerSecond,
                bucketSize);

                context.Response.Body = throttledStream;

                try
                {
                    await _next(context).ConfigureAwait(false);
                }
                finally
                {
                    context.Response.Body = originalBodyStream;
                }
            }
        }
        else
        {
            _logger.LogDebug("Request from localhost, skipping throttling.");
            await _next(context).ConfigureAwait(false);
        }
    }

    private static bool IsLocalRequest(ConnectionInfo connection)
    {
        if (connection.RemoteIpAddress != null)
        {
            if (connection.LocalIpAddress != null)
            {
                return connection.RemoteIpAddress.Equals(connection.LocalIpAddress);
            }
            return IPAddress.IsLoopback(connection.RemoteIpAddress);
        }

        // Handle scenarios where RemoteIpAddress is null (e.g., test server)
        return true; // Consider it local by default for safety in such cases
    }
}



/// <summary>
/// Options for download throttling.
/// </summary>
/// <summary>
/// Initializes a new instance of the <see cref="ThrottledResponseStream"/> class.
/// </summary>
public class ThrottledResponseStream : Stream
{
    private readonly Stream _innerStream;
    private readonly double _tokensPerSecond;
    private readonly double _bucketSize;
    private double _currentTokens;
    private long _lastTimestamp;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottledResponseStream"/> class.
    /// </summary>
    /// <param name="bytesPerSecond">The bytes per second.</param>
    /// <param name="innerStream">The inner stream.</param>
    /// <param name="bucketSize">The bucket size multiplier.</param>
    public ThrottledResponseStream(
        Stream innerStream,
        long bytesPerSecond,
        long bucketSize)
    {
        _innerStream = innerStream;
        _tokensPerSecond = bytesPerSecond;
        _bucketSize = bucketSize;
        _currentTokens = bucketSize;
        _lastTimestamp = DateTime.UtcNow.Ticks;
    }

    private void RefillTokens()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow.Ticks;
            var elapsedSeconds = (now - _lastTimestamp) / (double)TimeSpan.TicksPerSecond;
            _lastTimestamp = now;

            _currentTokens = Math.Min(
                _bucketSize,
                _currentTokens + (elapsedSeconds * _tokensPerSecond)
            );
        }
    }

    private async Task ConsumeTokensAsync(int tokens, CancellationToken cancellationToken)
    {
        while (tokens > 0)
        {
            lock (_lock)
            {
                RefillTokens();

                if (_currentTokens >= tokens)
                {
                    _currentTokens -= tokens;
                    return;
                }

                if (_currentTokens > 0)
                {
                    tokens -= (int)_currentTokens;
                    _currentTokens = 0;
                }
            }

            var requiredSeconds = tokens / _tokensPerSecond;
            var delayMs = (int)(requiredSeconds * 1000);
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }
    }
    /// <inheritdoc/>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await ConsumeTokensAsync(buffer.Length, cancellationToken).ConfigureAwait(false);
        await _innerStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }
    /// <inheritdoc/>

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await ConsumeTokensAsync(count, cancellationToken).ConfigureAwait(false);
        await _innerStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }
    /// <inheritdoc/>

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteAsync(buffer, offset, count).GetAwaiter().GetResult();
    }
    /// <inheritdoc/>

    public override bool CanRead => _innerStream.CanRead;
    /// <inheritdoc/>

    public override bool CanSeek => _innerStream.CanSeek;
    /// <inheritdoc/>

    public override bool CanWrite => _innerStream.CanWrite;
    /// <inheritdoc/>

    public override long Length => _innerStream.Length;
    /// <inheritdoc/>

    public override long Position
    {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }
    /// <inheritdoc/>

    public override void Flush() => _innerStream.Flush();
    /// <inheritdoc/>

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _innerStream.FlushAsync(cancellationToken);
    /// <inheritdoc/>

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("Read operations are not supported.");
    /// <inheritdoc/>

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("Seek operations are not supported.");
    /// <inheritdoc/>

    public override void SetLength(long value) =>
        throw new NotSupportedException("SetLength operations are not supported.");
    /// <inheritdoc/>

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _innerStream.Dispose();
        }
        base.Dispose(disposing);
    }
}

// Extension methods for easy registration
// public static class DownloadThrottlingMiddlewareExtensions
// {
//     public static IApplicationBuilder UseBandwidthThrottling(
//         this IApplicationBuilder builder,
//         BandwidthThrottlingOptions options)
//     {
//         return builder.UseMiddleware<DownloadThrottlingMiddleware>(options);
//     }

//     public static IApplicationBuilder UseBandwidthThrottling(
//         this IApplicationBuilder builder,
//         long bytesPerSecond,
//         double bucketSizeMultiplier = 2.0)
//     {
//         var options = new DownloadThrottlingOptions(bytesPerSecond, bucketSizeMultiplier);
//         return builder.UseMiddleware<DownloadThrottlingMiddleware>(options);
//     }
// }
