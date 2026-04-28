using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Seren.Modules.VoxMind.Parakeet;

/// <summary>
/// Mel spectrogram feature extractor wrapping nemo128.onnx.
/// Uses NVIDIA NeMo AudioToMelSpectrogramPreprocessor ONNX export.
/// Input: float[] audio samples (16kHz mono, normalized -1..1)
/// Output: (float[] mel features flattened [1, 128, T], long frame count T)
/// </summary>
public sealed class AudioPreprocessor : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _audioInputName;
    private readonly string _lengthInputName;
    private readonly string _melOutputName;
    private readonly string _lenOutputName;
    private readonly bool _lengthIsInt64;

    public AudioPreprocessor(string modelPath, SessionOptions opts)
    {
        _session = new InferenceSession(modelPath, opts);

        var inputNames = _session.InputMetadata.Keys.ToList();
        var outputNames = _session.OutputMetadata.Keys.ToList();

        if (inputNames.Count == 0)
        {
            throw new InvalidOperationException("nemo128.onnx has no inputs.");
        }

        _audioInputName = inputNames[0];
        _lengthInputName = inputNames.Count > 1 ? inputNames[1] : inputNames[0];
        _melOutputName = outputNames[0];
        _lenOutputName = outputNames.Count > 1 ? outputNames[1] : outputNames[0];

        _lengthIsInt64 = _session.InputMetadata[_lengthInputName].ElementType == typeof(long);
    }

    public (float[] Features, long Frames) ComputeMelSpectrogram(float[] audioSamples)
    {
        var audioTensor = new DenseTensor<float>(audioSamples, [1, audioSamples.Length]);

        NamedOnnxValue lengthInput;
        if (_lengthIsInt64)
        {
            var lengthTensor = new DenseTensor<long>(new long[] { audioSamples.Length }, [1]);
            lengthInput = NamedOnnxValue.CreateFromTensor(_lengthInputName, lengthTensor);
        }
        else
        {
            var lengthTensor = new DenseTensor<int>(new[] { audioSamples.Length }, [1]);
            lengthInput = NamedOnnxValue.CreateFromTensor(_lengthInputName, lengthTensor);
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_audioInputName, audioTensor),
            lengthInput
        };

        using var results = _session.Run(inputs);

        var melResult = results.First(r => r.Name == _melOutputName);
        var lenResult = results.First(r => r.Name == _lenOutputName);

        float[] features = [.. melResult.AsTensor<float>()];
        long frames = lenResult.AsTensor<long>()[0];

        return (features, frames);
    }

    public void Dispose() => _session.Dispose();
}
