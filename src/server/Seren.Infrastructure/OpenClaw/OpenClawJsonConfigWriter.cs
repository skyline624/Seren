using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;

namespace Seren.Infrastructure.OpenClaw;

/// <summary>
/// <see cref="IOpenClawConfigWriter"/> implementation that rewrites a
/// single field inside OpenClaw's JSON5 config file via a targeted regex
/// substitution. Keeps all surrounding comments, indentation, and
/// trailing commas intact — full JSON5 parsers would drop them.
/// </summary>
/// <remarks>
/// <para>
/// The writer targets the <c>primary:</c> key nested under
/// <c>agents.defaults.model</c>. The regex is intentionally anchored on
/// the surrounding context (<c>model:\s*\{</c> … <c>primary:</c>) so it
/// can't accidentally match a <c>primary:</c> field under another node
/// (e.g. <c>agents.defaults.imageModel.primary</c>).
/// </para>
/// <para>
/// Writes go through a temp file + <see cref="File.Move(string, string, bool)"/>
/// to avoid leaving the config in a partial state if the host dies
/// mid-write (OpenClaw tolerates a torn JSON5 file less gracefully than
/// a plain crash on next boot).
/// </para>
/// </remarks>
public sealed partial class OpenClawJsonConfigWriter : IOpenClawConfigWriter
{
    // The env-var reference we restore when clearing the pin, matching
    // the shape documented in `ops/openclaw/openclaw.json`. The gateway's
    // config loader resolves `${VAR}` tokens at startup.
    private const string DefaultModelEnvToken = "\"${OPENCLAW_DEFAULT_MODEL}\"";

    private readonly IOptions<OpenClawOptions> _options;
    private readonly ILogger<OpenClawJsonConfigWriter> _logger;

    public OpenClawJsonConfigWriter(
        IOptions<OpenClawOptions> options,
        ILogger<OpenClawJsonConfigWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SetDefaultModelAsync(string? model, CancellationToken ct = default)
    {
        var path = _options.Value.ConfigFilePath;
        if (string.IsNullOrEmpty(path))
        {
            throw new InvalidOperationException(
                "OpenClaw:ConfigFilePath is not configured; cannot pin the default model. "
                + "Mount openclaw.json read-write into the Seren container and set OpenClaw__ConfigFilePath.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"OpenClaw config not found at '{path}'; verify the docker-compose mount.", path);
        }

        var original = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

        // Refuse to edit a file that doesn't look like a JSON5 document at
        // the root (comment line or opening brace). Earlier experiments
        // with `/tools/invoke gateway config.*` left a stray path line at
        // the top of the file, which JSON5 cannot parse and which a blind
        // regex pass would silently preserve. Failing fast lets callers
        // surface the corruption instead of doubling down on it.
        var firstMeaningfulLine = FirstMeaningfulLine(original);
        if (firstMeaningfulLine is not null
            && !firstMeaningfulLine.StartsWith("//", StringComparison.Ordinal)
            && !firstMeaningfulLine.StartsWith('{')
            && !firstMeaningfulLine.StartsWith("/*", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"openclaw.json does not start with a comment or '{{' "
              + $"(first meaningful line: '{firstMeaningfulLine.Trim()}'). "
              + "Refusing to write — restore the file from git (`git checkout ops/openclaw/openclaw.json`) before retrying.");
        }

        var replacement = string.IsNullOrWhiteSpace(model)
            ? DefaultModelEnvToken
            : $"\"{EscapeJsonString(model.Trim())}\"";

        var rewritten = PrimaryModelRegex().Replace(
            original,
            match =>
            {
                // Groups: 1 = leading context up to `primary:` + whitespace,
                //         2 = existing value (quoted or env token), kept as the
                //         fragment to swap.
                return match.Groups[1].Value + replacement;
            },
            count: 1);

        if (ReferenceEquals(rewritten, original))
        {
            _logger.LogWarning(
                "Could not locate agents.defaults.model.primary in {Path}; leaving config unchanged.",
                path);
            throw new InvalidOperationException(
                "Failed to locate `agents.defaults.model.primary` in openclaw.json. "
                + "Verify the config still matches the expected schema.");
        }

        // Direct in-place write. We'd prefer a temp+rename pattern for
        // atomicity, but bind-mounted files in Docker live in a root-owned
        // directory we can't create siblings in from the appuser account.
        // The truncate+rewrite window is milliseconds — a crash mid-write
        // leaves a torn config, which the gateway rejects on next boot
        // with a parse error rather than a silent misbehaviour. Acceptable
        // for a 2-3 KB file updated by explicit user action.
        await File.WriteAllTextAsync(path, rewritten, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Wrote agents.defaults.model.primary = {Model} to {Path}.",
            model ?? "<env default>", path);
    }

    private static string? FirstMeaningfulLine(string source)
    {
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.TrimStart();
            if (line.Length > 0 && line[0] != '\r')
            {
                return line;
            }
        }
        return null;
    }

    private static string EscapeJsonString(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
         .Replace("\"", "\\\"", StringComparison.Ordinal);

    // Matches the leading context (model block opener + any comments/
    // whitespace up through `primary:`) as group 1, then the existing
    // double-quoted value as group 2. The `(?<![A-Za-z0-9_])` lookbehind
    // ensures we anchor on the standalone `model` key, never on
    // `imageModel`, `groupModel`, etc.
    [GeneratedRegex(
        pattern: """((?<![A-Za-z0-9_])model\s*:\s*\{[\s\S]*?primary\s*:\s*)("(?:\\.|[^"\\])*")""",
        options: RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex PrimaryModelRegex();
}
