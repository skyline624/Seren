namespace Seren.Modules.VoxMind.Models;

/// <summary>
/// Static description of one downloadable STT bundle: which engine family
/// it belongs to, the files it expects on disk, the HuggingFace repository
/// to pull them from, and an approximate disk footprint for the UI.
/// </summary>
/// <remarks>
/// <para>
/// <c>Files</c> are resolved against
/// <c>https://huggingface.co/{HfRepo}/resolve/main/{file}</c> when downloading,
/// and against <see cref="ModelCatalog.LocalDirFor"/> when checking on-disk
/// presence. A bundle is considered "downloaded" iff every file in
/// <c>Files</c> exists with a non-zero size.
/// </para>
/// <para>
/// System-managed variants (Parakeet today) carry empty <c>HfRepo</c> and
/// <c>Files</c> arrays — they are never downloaded by this service; the UI
/// hides their download/delete buttons.
/// </para>
/// </remarks>
/// <param name="Id">Stable string identifier used both as URL slug and as the active-engine localStorage value (e.g., <c>"whisper-tiny"</c>).</param>
/// <param name="EngineFamily">Engine family the variant belongs to (<c>"parakeet"</c> | <c>"whisper"</c>) — used by the router to dispatch.</param>
/// <param name="DisplayKey">Suffix used by the UI for i18n lookup (<c>modules.voxmind.models.{DisplayKey}.name</c>).</param>
/// <param name="ApproxSizeMb">Rough disk footprint in MB — surfaced to the UI for the cosmetic "(~470 MB)" label.</param>
/// <param name="IsSystemManaged">When <c>true</c>, the variant is not downloadable nor deletable from the UI (e.g., Parakeet is shipped via deployment).</param>
/// <param name="HfRepo">HuggingFace repository slug (<c>org/repo</c>) — empty when <see cref="IsSystemManaged"/> is true.</param>
/// <param name="Files">File names within the bundle directory — empty when <see cref="IsSystemManaged"/> is true.</param>
public sealed record ModelVariant(
    string Id,
    string EngineFamily,
    string DisplayKey,
    int ApproxSizeMb,
    bool IsSystemManaged,
    string HfRepo,
    IReadOnlyList<string> Files);

/// <summary>
/// Hard-coded catalog of STT bundles known to VoxMind. Adding a new
/// Whisper variant means appending one entry here — the storage, download,
/// and routing layers pick it up automatically.
/// </summary>
public static class ModelCatalog
{
    /// <summary>Every variant exposed in <c>GET /api/voxmind/models</c>, in display order.</summary>
    public static IReadOnlyList<ModelVariant> All { get; } =
    [
        new ModelVariant(
            Id: "parakeet",
            EngineFamily: "parakeet",
            DisplayKey: "parakeet",
            ApproxSizeMb: 600,
            IsSystemManaged: true,
            HfRepo: string.Empty,
            Files: Array.Empty<string>()),

        new ModelVariant(
            Id: "whisper-tiny",
            EngineFamily: "whisper",
            DisplayKey: "whisperTiny",
            ApproxSizeMb: 75,
            IsSystemManaged: false,
            HfRepo: "csukuangfj/sherpa-onnx-whisper-tiny",
            Files: new[] { "tiny-encoder.int8.onnx", "tiny-decoder.int8.onnx", "tiny-tokens.txt" }),

        new ModelVariant(
            Id: "whisper-base",
            EngineFamily: "whisper",
            DisplayKey: "whisperBase",
            ApproxSizeMb: 140,
            IsSystemManaged: false,
            HfRepo: "csukuangfj/sherpa-onnx-whisper-base",
            Files: new[] { "base-encoder.int8.onnx", "base-decoder.int8.onnx", "base-tokens.txt" }),

        new ModelVariant(
            Id: "whisper-small",
            EngineFamily: "whisper",
            DisplayKey: "whisperSmall",
            ApproxSizeMb: 470,
            IsSystemManaged: false,
            HfRepo: "csukuangfj/sherpa-onnx-whisper-small",
            Files: new[] { "small-encoder.int8.onnx", "small-decoder.int8.onnx", "small-tokens.txt" }),

        new ModelVariant(
            Id: "whisper-medium",
            EngineFamily: "whisper",
            DisplayKey: "whisperMedium",
            ApproxSizeMb: 1500,
            IsSystemManaged: false,
            HfRepo: "csukuangfj/sherpa-onnx-whisper-medium",
            Files: new[] { "medium-encoder.int8.onnx", "medium-decoder.int8.onnx", "medium-tokens.txt" }),

        new ModelVariant(
            Id: "whisper-large",
            EngineFamily: "whisper",
            DisplayKey: "whisperLarge",
            ApproxSizeMb: 3000,
            IsSystemManaged: false,
            HfRepo: "csukuangfj/sherpa-onnx-whisper-large",
            Files: new[] { "large-encoder.int8.onnx", "large-decoder.int8.onnx", "large-tokens.txt" }),
    ];

    /// <summary>Looks up a variant by id; returns <c>null</c> when not in the catalog.</summary>
    public static ModelVariant? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i].Id, id, StringComparison.Ordinal))
            {
                return All[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the local install directory for <paramref name="variant"/>:
    /// Parakeet uses its dedicated <c>Stt:Parakeet:ModelDir</c>; Whisper
    /// variants use <c>{Stt:Whisper:RootDir}/{variant.Id}</c>.
    /// </summary>
    /// <remarks>
    /// Returns <see cref="string.Empty"/> for Parakeet when its ModelDir is
    /// not configured — callers must treat empty as "engine not deployable".
    /// </remarks>
    public static string LocalDirFor(VoxMindOptions options, ModelVariant variant)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(variant);

        if (string.Equals(variant.EngineFamily, "parakeet", StringComparison.Ordinal))
        {
            return options.Stt.Parakeet.ModelDir ?? string.Empty;
        }

        var root = options.Stt.Whisper.RootDir;
        if (string.IsNullOrWhiteSpace(root))
        {
            return string.Empty;
        }

        return Path.Combine(root, variant.Id);
    }
}
