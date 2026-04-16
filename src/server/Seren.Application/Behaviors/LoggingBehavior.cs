using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Seren.Application.Behaviors;

/// <summary>
/// Mediator pipeline behavior that logs the start, end and duration of every
/// request handler. Uses structured logging so that fields show up in Serilog sinks
/// and OpenTelemetry traces.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IMessage
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> Handle(
        TRequest message,
        CancellationToken cancellationToken,
        MessageHandlerDelegate<TRequest, TResponse> next)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug("Handling {RequestName}", requestName);

        try
        {
            var response = await next(message, cancellationToken);
            stopwatch.Stop();
            _logger.LogDebug(
                "Handled {RequestName} in {ElapsedMs} ms",
                requestName, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Request {RequestName} failed after {ElapsedMs} ms",
                requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
