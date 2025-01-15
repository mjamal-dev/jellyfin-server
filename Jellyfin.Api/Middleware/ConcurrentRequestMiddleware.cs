using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;

namespace Jellyfin.Api.Middleware;

/// <summary>
/// Middleware to limit concurrent requests from a single client.
/// </summary>
public class ConcurrentRequestsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ConcurrentRequestsMiddleware> _logger;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _clientSemaphores = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrentRequestsMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    public ConcurrentRequestsMiddleware(RequestDelegate next, ILogger<ConcurrentRequestsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        string clientId = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var semaphore = _clientSemaphores.GetOrAdd(clientId, _ => new SemaphoreSlim(1, 1));

        try
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
