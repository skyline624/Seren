using FluentValidation;

namespace Seren.Infrastructure.Persistence.Json;

/// <summary>
/// Options for <see cref="JsonCharacterRepository"/> and the companion
/// <see cref="FileSystemCharacterAvatarStore"/>. Bound from the
/// <c>Seren:Characters</c> section of <c>appsettings.json</c> or any
/// ASP.NET configuration provider (env vars, command-line, etc.).
/// </summary>
public sealed class CharacterStoreOptions
{
    public const string SectionName = "Seren:Characters";

    /// <summary>
    /// Absolute or relative path of the JSON file used as a flat-file
    /// character store. Defaults to a relative <c>characters.json</c>
    /// next to the running binary — production deployments should point
    /// this at a mounted volume (e.g. <c>/data/characters.json</c>).
    /// </summary>
    public string StorePath { get; set; } = "characters.json";

    /// <summary>
    /// Directory where 2D avatars extracted from imported Character Card v3
    /// files are stored. Defaults to a sibling <c>avatars/</c> next to
    /// <see cref="StorePath"/>; can be overridden to point at a separate
    /// mount (e.g. object-storage-backed FUSE). The validator ensures
    /// the resolved absolute path stays inside the configured
    /// <see cref="StorePath"/>'s parent directory, blocking any
    /// mis-configuration that would let avatars escape the data root.
    /// </summary>
    public string AvatarDirectory { get; set; } = "avatars";
}

/// <summary>
/// Validates <see cref="CharacterStoreOptions"/> at startup. Beyond
/// non-empty checks, ensures the resolved <see cref="CharacterStoreOptions.AvatarDirectory"/>
/// stays inside the data root implied by <see cref="CharacterStoreOptions.StorePath"/> —
/// defense-in-depth against a config file that would try to redirect
/// avatar writes outside the expected tree.
/// </summary>
public sealed class CharacterStoreOptionsValidator : AbstractValidator<CharacterStoreOptions>
{
    public CharacterStoreOptionsValidator()
    {
        RuleFor(x => x.StorePath)
            .NotEmpty()
            .WithMessage("Seren:Characters:StorePath is required.");

        RuleFor(x => x.AvatarDirectory)
            .NotEmpty()
            .WithMessage("Seren:Characters:AvatarDirectory is required.");

        RuleFor(x => x)
            .Must(StayInsideDataRoot)
            .WithMessage("Seren:Characters:AvatarDirectory must stay inside the directory that contains Seren:Characters:StorePath.");
    }

    private static bool StayInsideDataRoot(CharacterStoreOptions options)
    {
        // Resolve both to absolute paths using the current working dir as
        // the base for relative values. Path.GetFullPath canonicalises
        // away any ".." segments, so the comparison is robust.
        var storeFull = Path.GetFullPath(options.StorePath);
        var storeDir = Path.GetDirectoryName(storeFull);
        if (string.IsNullOrEmpty(storeDir))
        {
            return true; // StorePath at filesystem root — skip the check.
        }

        var avatarBase = Path.IsPathRooted(options.AvatarDirectory)
            ? options.AvatarDirectory
            : Path.Combine(storeDir, options.AvatarDirectory);
        var avatarFull = Path.GetFullPath(avatarBase);

        // Append a separator to avoid false positives against paths that
        // start with the same prefix (e.g. /data vs /data_bad).
        var normalizedRoot = storeDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
        var normalizedAvatar = avatarFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                               + Path.DirectorySeparatorChar;

        return normalizedAvatar.StartsWith(normalizedRoot, StringComparison.Ordinal)
            || normalizedAvatar.Equals(normalizedRoot, StringComparison.Ordinal);
    }
}
