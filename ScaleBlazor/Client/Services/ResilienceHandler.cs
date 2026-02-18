using System.Net;
using Microsoft.Extensions.Logging;

namespace ScaleBlazor.Client.Services;

public sealed class ResilienceHandler : DelegatingHandler
{
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1)
    };

    private readonly ILogger<ResilienceHandler> _logger;

    public ResilienceHandler(ILogger<ResilienceHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken);

                if (IsTransientStatusCode(response.StatusCode) && attempt < RetryDelays.Length)
                {
                    var delay = RetryDelays[attempt];
                    _logger.LogWarning("Transient HTTP {StatusCode} for {Method} {Uri}. Retrying in {Delay}.",
                        (int)response.StatusCode,
                        request.Method,
                        request.RequestUri,
                        delay);
                    response.Dispose();
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (attempt < RetryDelays.Length)
            {
                var delay = RetryDelays[attempt];
                _logger.LogWarning(ex, "HTTP request failed for {Method} {Uri}. Retrying in {Delay}.",
                    request.Method,
                    request.RequestUri,
                    delay);
                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < RetryDelays.Length)
            {
                var delay = RetryDelays[attempt];
                _logger.LogWarning(ex, "HTTP request timed out for {Method} {Uri}. Retrying in {Delay}.",
                    request.Method,
                    request.RequestUri,
                    delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        var numericCode = (int)statusCode;
        return numericCode == 408 || numericCode == 429 || numericCode >= 500;
    }
}
