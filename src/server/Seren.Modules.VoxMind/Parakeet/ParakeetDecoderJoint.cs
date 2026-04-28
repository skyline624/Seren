using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Seren.Modules.VoxMind.Parakeet;

/// <summary>
/// ONNX decoder-joint for Parakeet TDT (decoder_joint-model.int8.onnx).
/// Implements TDT (Token and Duration Transducer) greedy decoding.
/// </summary>
public sealed class ParakeetDecoderJoint : IDisposable
{
    private readonly InferenceSession _session;
    private readonly TokenDecoder _tokenDecoder;
    private readonly string _encoderInputName;
    private readonly string _targetInputName;
    private readonly string _targetLengthInputName;
    private readonly string _inputState1Name;
    private readonly string _inputState2Name;
    private readonly string _logitsOutputName;
    private readonly string _outputState1Name;
    private readonly string _outputState2Name;
    private const int HiddenDim = 1024;
    private const int StateDim = 640;
    private const int MaxTokensPerFrame = 8;

    public ParakeetDecoderJoint(string modelPath, TokenDecoder tokenDecoder, SessionOptions opts)
    {
        _tokenDecoder = tokenDecoder;
        _session = new InferenceSession(modelPath, opts);

        var inputNames = _session.InputMetadata.Keys.ToList();
        var outputNames = _session.OutputMetadata.Keys.ToList();

        _encoderInputName = inputNames.Count > 0 ? inputNames[0] : "encoder_outputs";
        _targetInputName = inputNames.Count > 1 ? inputNames[1] : "targets";
        _targetLengthInputName = inputNames.Count > 2 ? inputNames[2] : "target_length";
        _inputState1Name = inputNames.Count > 3 ? inputNames[3] : "input_states_1";
        _inputState2Name = inputNames.Count > 4 ? inputNames[4] : "input_states_2";

        _logitsOutputName = outputNames.Count > 0 ? outputNames[0] : "outputs";
        _outputState1Name = outputNames.Count > 2 ? outputNames[2] : "output_states_1";
        _outputState2Name = outputNames.Count > 3 ? outputNames[3] : "output_states_2";
    }

    public int[] DecodeGreedy(float[] encoderOutput, long encodedFrames, int hiddenDim)
    {
        var result = new List<int>();
        int prevToken = _tokenDecoder.BosIndex;
        int blankId = _tokenDecoder.BlankIndex;
        int eosId = _tokenDecoder.EosIndex;
        int vocabSize = _tokenDecoder.VocabSize;

        float[] state1 = new float[2 * 1 * StateDim];
        float[] state2 = new float[2 * 1 * StateDim];

        for (long t = 0; t < encodedFrames; t++)
        {
            for (int step = 0; step < MaxTokensPerFrame; step++)
            {
                float[] logits = RunDecoderStep(
                    encoderOutput, (int)encodedFrames, (int)t,
                    prevToken, state1, state2,
                    out float[] newState1, out float[] newState2);

                state1 = newState1;
                state2 = newState2;

                int token = ArgMax(logits, vocabSize);

                if (token == blankId || token == eosId)
                {
                    break;
                }

                result.Add(token);
                prevToken = token;
            }
        }

        return [.. result];
    }

    private float[] RunDecoderStep(
        float[] encoderOutput, int totalFrames, int frameIdx,
        int prevToken,
        float[] state1, float[] state2,
        out float[] newState1, out float[] newState2)
    {
        var frameSlice = new float[HiddenDim];
        for (int h = 0; h < HiddenDim; h++)
        {
            frameSlice[h] = encoderOutput[h * totalFrames + frameIdx];
        }

        var encoderTensor = new DenseTensor<float>(frameSlice, [1, HiddenDim, 1]);
        var targetTensor = new DenseTensor<int>(new int[] { prevToken }, [1, 1]);
        var targetLengthTensor = new DenseTensor<int>(new int[] { 1 }, [1]);
        var state1Tensor = new DenseTensor<float>(state1, [2, 1, StateDim]);
        var state2Tensor = new DenseTensor<float>(state2, [2, 1, StateDim]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_encoderInputName, encoderTensor),
            NamedOnnxValue.CreateFromTensor(_targetInputName, targetTensor),
            NamedOnnxValue.CreateFromTensor(_targetLengthInputName, targetLengthTensor),
            NamedOnnxValue.CreateFromTensor(_inputState1Name, state1Tensor),
            NamedOnnxValue.CreateFromTensor(_inputState2Name, state2Tensor),
        };

        using var results = _session.Run(inputs);

        float[] logits = [.. results.First(r => r.Name == _logitsOutputName).AsTensor<float>()];
        newState1 = [.. results.First(r => r.Name == _outputState1Name).AsTensor<float>()];
        newState2 = [.. results.First(r => r.Name == _outputState2Name).AsTensor<float>()];

        return logits;
    }

    private static int ArgMax(float[] logits, int vocabSize)
    {
        int count = Math.Min(logits.Length, vocabSize);
        int maxIdx = 0;
        float maxVal = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            if (logits[i] > maxVal)
            {
                maxVal = logits[i];
                maxIdx = i;
            }
        }
        return maxIdx;
    }

    public void Dispose() => _session.Dispose();
}
