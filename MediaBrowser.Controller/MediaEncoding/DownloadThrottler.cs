using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// Media Download throttler.
/// </summary>
///
    public class DownloadThrottler
    {
        private readonly int _maxBytesPerSecond;
        private readonly ILogger<DownloadThrottler> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DownloadThrottler"/> class.
        /// </summary>
        /// <param name="maxBytesPerSecond">Download Bandwidth max bps.</param>
        /// <param name="logger">Logger instance.</param>
        public DownloadThrottler(int maxBytesPerSecond, ILogger<DownloadThrottler> logger)
        {
            _maxBytesPerSecond = maxBytesPerSecond;
            _logger = logger;
        }

    /// <summary>
    /// Throttle the download.
    /// </summary>
    /// <returns><stream>A <see cref="Task"/> representing the asynchronous operation.</stream></returns>
    /// <param name="inputStream">Input stream of type <see cref="Stream"/>.</param>
    /// <param name="outputStream">Output stream of type <see cref="Stream"/>.</param>
    /// <param name="cancellationToken">Cancellation token of type <see cref="CancellationToken"/>.</param>
        public async Task ThrottleAsync(Stream inputStream, Stream outputStream, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Throttling download to {MaxBytesPerSecond} bytes per second", _maxBytesPerSecond);

            var buffer = new byte[8192];
            var bytesRead = 0;
            var totalBytesRead = 0;
            var stopwatch = Stopwatch.StartNew();

            while ((bytesRead = await inputStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await outputStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                totalBytesRead += bytesRead;

                var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                if (elapsedSeconds > 0)
                {
                    var currentRate = totalBytesRead / elapsedSeconds;
                    if (currentRate > _maxBytesPerSecond)
                    {
                        var delay = (int)(((totalBytesRead / (double)_maxBytesPerSecond) - elapsedSeconds) * 1000);
                        if (delay > 0)
                        {
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
        }
}
