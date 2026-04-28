using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Seren.Modules.VoxMind.F5Tts;

/// <summary>
/// F5-TTS preprocess stage (<c>F5_Preprocess.onnx</c>).
/// </summary>
/// <remarks>
/// Contract (DakeQQ/F5-TTS-ONNX port) — I/O names are read from the ONNX
/// metadata at runtime to stay tolerant to re-exports:
/// <list type="bullet">
///   <item>audio: <c>float32 [1, n_samples]</c> reference voice (PCM 24 kHz mono).</item>
///   <item>prompt_ids: <c>int32 [1, P]</c> token IDs of the reference transcription.</item>
///   <item>target_ids: <c>int32 [1, T]</c> token IDs of the text to synthesise.</item>
/// </list>
/// Main output: conditioning embeddings <c>[1, L, D]</c> consumed by the transformer.
/// </remarks>
public sealed class F5TtsPreprocessor : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _audioInputName;
    private readonly string _promptInputName;
    private readonly string _targetInputName;
    private readonly string _outputName;

    public F5TtsPreprocessor(string modelPath, SessionOptions opts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(opts);

        _session = new InferenceSession(modelPath, opts);

        var inputNames = _session.InputMetadata.Keys.ToList();
        var outputNames = _session.OutputMetadata.Keys.ToList();

        if (inputNames.Count < 3)
        {
            throw new InvalidOperationException(
                $"F5_Preprocess.onnx expects at least 3 inputs (audio, prompt_ids, target_ids), got {inputNames.Count}.");
        }

        _audioInputName = inputNames[0];
        _promptInputName = inputNames[1];
        _targetInputName = inputNames[2];
        _outputName = outputNames[0];
    }

    public DenseTensor<float> Run(float[] referencePcm24kHz, int[] promptIds, int[] targetIds)
    {
        ArgumentNullException.ThrowIfNull(referencePcm24kHz);
        ArgumentNullException.ThrowIfNull(promptIds);
        ArgumentNullException.ThrowIfNull(targetIds);

        var audioTensor = new DenseTensor<float>(referencePcm24kHz, [1, referencePcm24kHz.Length]);
        var promptTensor = new DenseTensor<int>(promptIds, [1, promptIds.Length]);
        var targetTensor = new DenseTensor<int>(targetIds, [1, targetIds.Length]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_audioInputName, audioTensor),
            NamedOnnxValue.CreateFromTensor(_promptInputName, promptTensor),
            NamedOnnxValue.CreateFromTensor(_targetInputName, targetTensor),
        };

        using var results = _session.Run(inputs);
        var output = results.First(r => r.Name == _outputName).AsTensor<float>();

        // Copy out so the buffer doesn't depend on the (about-to-be-disposed) ORT result.
        var dims = output.Dimensions.ToArray();
        var data = output.ToArray();
        return new DenseTensor<float>(data, dims);
    }

    public void Dispose() => _session.Dispose();
}
