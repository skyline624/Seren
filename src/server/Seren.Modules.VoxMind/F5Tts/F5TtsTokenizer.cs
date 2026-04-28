using System.Globalization;

namespace Seren.Modules.VoxMind.F5Tts;

/// <summary>
/// Phoneme/char tokenizer for F5-TTS (sentencepiece-style).
/// </summary>
/// <remarks>
/// The DakeQQ F5-TTS-ONNX export ships a <c>tokens.txt</c> file with one
/// <c>token&lt;TAB&gt;id</c> entry per line. For Latin-alphabet languages
/// (FR/EN) char-level tokenisation is good enough to validate the pipeline
/// end-to-end; a proper phonemizer can be plugged in later without breaking
/// the public API.
/// </remarks>
public sealed class F5TtsTokenizer
{
    public const int PadToken = 0;
    public const int UnkToken = 1;

    private readonly Dictionary<string, int> _tokenToId;
    private readonly string[] _idToToken;

    public int VocabSize => _idToToken.Length;

    public F5TtsTokenizer(string tokensPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokensPath);

        if (!File.Exists(tokensPath))
        {
            throw new FileNotFoundException($"tokens.txt not found: {tokensPath}", tokensPath);
        }

        var lines = File.ReadAllLines(tokensPath);
        _tokenToId = new Dictionary<string, int>(lines.Length, StringComparer.Ordinal);
        var list = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(['\t', ' '], 2, StringSplitOptions.RemoveEmptyEntries);
            var token = parts[0];
            if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                while (list.Count <= id)
                {
                    list.Add(string.Empty);
                }

                list[id] = token;
            }
            else
            {
                id = list.Count;
                list.Add(token);
            }
            _tokenToId[token] = id;
        }

        _idToToken = [.. list];
    }

    /// <summary>
    /// Encodes a text into a sequence of token IDs (char-level fallback).
    /// Unknown characters are mapped to <see cref="UnkToken"/>; spaces are mapped
    /// to the SentencePiece word-boundary token (▁) when present.
    /// </summary>
    public int[] Encode(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        var ids = new List<int>(text.Length);
        while (enumerator.MoveNext())
        {
            var ch = (string)enumerator.Current;
            if (_tokenToId.TryGetValue(ch, out int id))
            {
                ids.Add(id);
            }
            else if (ch == " " && _tokenToId.TryGetValue("▁", out int spaceId))
            {
                ids.Add(spaceId);
            }
            else
            {
                ids.Add(UnkToken);
            }
        }
        return [.. ids];
    }
}
