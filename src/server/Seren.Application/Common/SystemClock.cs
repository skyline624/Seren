using Seren.Application.Abstractions;

namespace Seren.Application.Common;

/// <summary>
/// Default <see cref="IClock"/> implementation backed by <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
