using System.Text.RegularExpressions;
using Seren.Application.Abstractions;
using Seren.Contracts.Characters;

namespace Seren.Application.Characters.Personas;

/// <summary>
/// Exact inverse of <see cref="PersonaTemplateComposer"/> : reads the
/// two markdown files currently driving OpenClaw's persona back into
/// the primitives Seren uses to create a <c>Character</c>. Pure
/// function — no I/O — so every branch is unit-testable with string
/// inputs alone.
/// </summary>
/// <remarks>
/// Round-trip invariant : for every <c>Character c</c> with a non-blank
/// name + system prompt,
/// <c>Extract(new(ComposeIdentity(c), ComposeSoul(c))).SystemPrompt</c>
/// is byte-equivalent to <c>c.SystemPrompt.Trim()</c>. The extractor
/// also degrades gracefully on "vanilla" OpenClaw markdown (no Seren
/// bandeau, no <c>— Soul</c> suffix, no protocol annex) — the raw body
/// becomes the system prompt.
/// </remarks>
public static partial class PersonaTemplateExtractor
{
    /// <summary>
    /// First line of <see cref="PersonaTemplateComposer.MarkerProtocolBlock"/>
    /// — the section header used as a cut point when stripping the
    /// auto-injected protocol annex. Single source of truth for both
    /// composer and extractor : changing the composer's header
    /// automatically flows to extraction.
    /// </summary>
    public static readonly string MarkerProtocolHeader =
        PersonaTemplateComposer.MarkerProtocolBlock.Split('\n', 2)[0].TrimEnd();

    // Suffix the composer appends to SOUL's first heading (e.g.
    // "# Cortana — Soul"). Stripped on round-trip so the Name stays
    // canonical. Em dash — not hyphen — mirrors the composer exactly.
    private const string SoulHeadingSuffix = " — Soul";

    /// <summary>
    /// Extract the character primitives from a workspace snapshot.
    /// Throws <see cref="PersonaCaptureException"/> with
    /// <see cref="PersonaCaptureError.InvalidPersona"/> when the
    /// markdown doesn't carry a usable <c># Name</c> or leaves an
    /// empty system prompt after stripping the Seren-managed scaffolding.
    /// </summary>
    public static ExtractedPersona Extract(WorkspacePersonaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var name = ExtractName(snapshot.IdentityMarkdown)
            ?? ExtractName(snapshot.SoulMarkdown, stripSoulSuffix: true)
            ?? throw new PersonaCaptureException(
                PersonaCaptureError.InvalidPersona,
                "No '# Name' heading found in IDENTITY.md or SOUL.md.");

        var systemPrompt = ExtractSystemPrompt(snapshot.SoulMarkdown, name);
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            throw new PersonaCaptureException(
                PersonaCaptureError.InvalidPersona,
                "SOUL.md has no usable system-prompt body after stripping the Seren scaffolding.");
        }

        return new ExtractedPersona(
            Name: name,
            SystemPrompt: systemPrompt,
            Description: ExtractSection(snapshot.IdentityMarkdown, "Description"),
            Greeting: ExtractGreeting(snapshot.IdentityMarkdown),
            Tags: ExtractTags(snapshot.IdentityMarkdown));
    }

    private static string? ExtractName(string markdown, bool stripSoulSuffix = false)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        var match = HeadingRegex().Match(markdown);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups[1].Value.Trim();
        if (stripSoulSuffix && name.EndsWith(SoulHeadingSuffix, StringComparison.Ordinal))
        {
            name = name[..^SoulHeadingSuffix.Length].TrimEnd();
        }
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string ExtractSystemPrompt(string soulMarkdown, string name)
    {
        if (string.IsNullOrWhiteSpace(soulMarkdown))
        {
            return string.Empty;
        }

        var body = StripSerenManagedHeader(soulMarkdown);
        body = StripSoulHeading(body, name);
        body = StripMarkerProtocolAnnex(body);
        return body.Trim();
    }

    private static string StripSerenManagedHeader(string markdown)
    {
        // The bandeau is emitted verbatim as an HTML comment by the
        // composer; it always sits at the very top of the file. Drop
        // it plus any leading whitespace that followed so we don't
        // keep a blank line at the start of the extracted prompt.
        var trimmed = markdown.TrimStart('﻿'); // BOM defence (paranoia).
        var commentEnd = trimmed.IndexOf("-->", StringComparison.Ordinal);
        if (trimmed.StartsWith("<!--", StringComparison.Ordinal) && commentEnd > 0)
        {
            return trimmed[(commentEnd + "-->".Length)..].TrimStart();
        }
        return trimmed.TrimStart();
    }

    private static string StripSoulHeading(string markdown, string name)
    {
        // Match only the first heading of the file and only if it
        // matches "# {name}" or "# {name} — Soul" — we never strip a
        // heading buried mid-document.
        var candidate = $"# {name}{SoulHeadingSuffix}";
        if (markdown.StartsWith(candidate, StringComparison.Ordinal))
        {
            return markdown[candidate.Length..].TrimStart();
        }

        var bare = $"# {name}";
        if (markdown.StartsWith(bare, StringComparison.Ordinal))
        {
            return markdown[bare.Length..].TrimStart();
        }
        return markdown;
    }

    private static string StripMarkerProtocolAnnex(string markdown)
    {
        var idx = markdown.IndexOf(MarkerProtocolHeader, StringComparison.Ordinal);
        return idx < 0 ? markdown : markdown[..idx].TrimEnd();
    }

    private static string? ExtractSection(string markdown, string heading)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        var pattern = $@"^##\s+{Regex.Escape(heading)}\s*$(?<body>.*?)(?=^##\s|\z)";
        var match = Regex.Match(
            markdown, pattern,
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant,
            matchTimeout: TimeSpan.FromSeconds(1));

        if (!match.Success)
        {
            return null;
        }

        var body = match.Groups["body"].Value.Trim();
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    private static string? ExtractGreeting(string identityMarkdown)
    {
        var raw = ExtractSection(identityMarkdown, "Greeting");
        if (raw is null)
        {
            return null;
        }

        var lines = raw.Split('\n');
        var stripped = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                stripped.Add(trimmed[2..]);
            }
            else if (trimmed.StartsWith('>'))
            {
                stripped.Add(trimmed[1..].TrimStart());
            }
            else
            {
                stripped.Add(trimmed);
            }
        }
        var joined = string.Join('\n', stripped).Trim();
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static List<string> ExtractTags(string identityMarkdown)
    {
        var raw = ExtractSection(identityMarkdown, "Tags");
        if (raw is null)
        {
            return [];
        }

        var tags = new List<string>();
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').TrimStart();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }
            var tag = trimmed[2..].Trim();
            if (!string.IsNullOrWhiteSpace(tag))
            {
                tags.Add(tag);
            }
        }
        return tags;
    }

    // First top-level heading on any line of the document.
    [GeneratedRegex(@"^#\s+(.+?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeadingRegex();
}

/// <summary>
/// Primitives reconstructed from a workspace persona — exact inverse
/// of the fields <see cref="PersonaTemplateComposer"/> consumes.
/// </summary>
public sealed record ExtractedPersona(
    string Name,
    string SystemPrompt,
    string? Description,
    string? Greeting,
    IReadOnlyList<string> Tags);
