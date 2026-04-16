namespace Seren.Application.Abstractions;

/// <summary>
/// A single viseme frame for lip sync animation, describing a mouth shape over a time window.
/// </summary>
/// <param name="Viseme">Viseme identifier (e.g. "aa", "O", "E").</param>
/// <param name="StartTime">Start time in seconds relative to the audio start.</param>
/// <param name="Duration">Duration of this viseme frame in seconds.</param>
/// <param name="Weight">Blend weight between 0 and 1 (default 1).</param>
public sealed record VisemeFrame(string Viseme, float StartTime, float Duration, float Weight = 1f);
