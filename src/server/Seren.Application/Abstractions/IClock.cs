namespace Seren.Application.Abstractions;

/// <summary>
/// Abstraction over the system clock so that time-dependent behaviour
/// can be deterministically tested.
/// </summary>
public interface IClock
{
    /// <summary>Current wall-clock time with UTC offset.</summary>
    DateTimeOffset UtcNow { get; }
}
