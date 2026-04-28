using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Seren.Modules.VoxMind.F5Tts;

/// <summary>
/// F5-TTS DiT transformer (<c>F5_Transformer.onnx</c>).
/// </summary>
/// <remarks>
/// F5-TTS is a flow-matching model: starting from gaussian noise we integrate
/// to the target mel via N Euler steps. At each step we call the transformer
/// with (current state, conditioning embeddings, t).
/// Contract (DakeQQ port): three inputs in canonical order
/// <list type="bullet">
///   <item>x: <c>float32 [1, L, D]</c> current flow state.</item>
///   <item>cond: <c>float32 [1, L, D]</c> conditioning from preprocess stage.</item>
///   <item>t: <c>float32 [1]</c> normalised time in [0, 1].</item>
/// </list>
/// Output: velocity prediction <c>[1, L, D]</c>.
/// </remarks>
public sealed class F5TtsTransformer : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _xInputName;
    private readonly string _condInputName;
    private readonly string _timeInputName;
    private readonly string _outputName;

    public F5TtsTransformer(string modelPath, SessionOptions opts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(opts);

        _session = new InferenceSession(modelPath, opts);
        var inputs = _session.InputMetadata.Keys.ToList();
        var outputs = _session.OutputMetadata.Keys.ToList();

        if (inputs.Count < 3)
        {
            throw new InvalidOperationException(
                $"F5_Transformer.onnx expects 3 inputs (x, cond, t), got {inputs.Count}.");
        }

        _xInputName = inputs[0];
        _condInputName = inputs[1];
        _timeInputName = inputs[2];
        _outputName = outputs[0];
    }

    /// <summary>
    /// Integrates the flow-matching ODE over <paramref name="numSteps"/> Euler steps.
    /// Returns the target mel-spectrogram <c>[1, L, D]</c> consumed by the decoder.
    /// </summary>
    /// <remarks>
    /// The ODE integration is CPU-bound and can run for several seconds. The
    /// supplied <paramref name="ct"/> is checked at the start of every Euler
    /// step so that a cancelled request stops accumulating CPU within at most
    /// one ONNX <c>Run()</c> latency (~30-100 ms per step) instead of running
    /// the full 32-step loop to completion.
    /// </remarks>
    public DenseTensor<float> Sample(DenseTensor<float> conditioning, int numSteps, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conditioning);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numSteps);

        var dims = conditioning.Dimensions.ToArray();
        int total = 1;
        foreach (var d in dims)
        {
            total *= d;
        }

        // Box-Muller from a fixed seed — keeps synthesis deterministic for tests.
        var rng = new Random(42);
        var x = new DenseTensor<float>(dims);
        for (int i = 0; i < total; i++)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            x.Buffer.Span[i] = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        float dt = 1.0f / numSteps;

        for (int step = 0; step < numSteps; step++)
        {
            ct.ThrowIfCancellationRequested();

            float t = step * dt;
            var velocity = RunStep(x, conditioning, t);

            for (int i = 0; i < total; i++)
            {
                x.Buffer.Span[i] += dt * velocity.Buffer.Span[i];
            }
        }

        return x;
    }

    private DenseTensor<float> RunStep(DenseTensor<float> x, DenseTensor<float> cond, float t)
    {
        var tTensor = new DenseTensor<float>(new[] { t }, [1]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_xInputName, x),
            NamedOnnxValue.CreateFromTensor(_condInputName, cond),
            NamedOnnxValue.CreateFromTensor(_timeInputName, tTensor),
        };

        using var results = _session.Run(inputs);
        var velocity = results.First(r => r.Name == _outputName).AsTensor<float>();

        var dims = velocity.Dimensions.ToArray();
        var data = velocity.ToArray();
        return new DenseTensor<float>(data, dims);
    }

    public void Dispose() => _session.Dispose();
}
