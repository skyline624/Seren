namespace Seren.Modules.VoxMind.F5Tts;

/// <summary>
/// ONNX paths for an F5-TTS fine-tune for one language.
/// </summary>
/// <remarks>
/// Export reference: <see href="https://github.com/DakeQQ/F5-TTS-ONNX"/>.
/// The pipeline always produces three ONNX files per checkpoint plus a
/// SentencePiece BPE <c>tokens.txt</c> and a default reference voice WAV.
/// </remarks>
public sealed class F5LanguageCheckpoint
{
    /// <summary>ISO 639-1 language code (e.g. <c>"fr"</c>, <c>"en"</c>).</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Preprocess model (<c>F5_Preprocess.onnx</c>): text + audio ref → input embeddings.</summary>
    public string PreprocessModelPath { get; set; } = string.Empty;

    /// <summary>DiT transformer (<c>F5_Transformer.onnx</c>): flow-matching loop.</summary>
    public string TransformerModelPath { get; set; } = string.Empty;

    /// <summary>Decoder (<c>F5_Decode.onnx</c>): Vocos 24 kHz vocoder, mel → PCM.</summary>
    public string DecodeModelPath { get; set; } = string.Empty;

    /// <summary>Phonemic BPE vocabulary (<c>tokens.txt</c>).</summary>
    public string TokensPath { get; set; } = string.Empty;

    /// <summary>Default reference WAV for cloning (PCM 24 kHz mono, &lt; 30 s).</summary>
    public string DefaultReferenceWav { get; set; } = string.Empty;

    /// <summary>Exact transcription of <see cref="DefaultReferenceWav"/>.</summary>
    public string DefaultReferenceText { get; set; } = string.Empty;
}
