namespace Seren.Modules.VoxMind.Parakeet;

/// <summary>
/// Vocabulary-based token decoder for Parakeet TDT (vocab.txt).
/// Handles SentencePiece-style tokens ( prefix = word boundary).
/// </summary>
public sealed class TokenDecoder
{
    private readonly string[] _vocab;

    public int VocabSize => _vocab.Length;

    public int BlankIndex { get; }
    public int BosIndex { get; }
    public int EosIndex { get; }

    public TokenDecoder(string vocabPath)
    {
        _vocab = [.. File.ReadAllLines(vocabPath)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .Select(static l => { var i = l.LastIndexOf(' '); return i > 0 ? l[..i] : l; })];

        if (_vocab.Length == 0)
        {
            throw new InvalidOperationException($"vocab.txt is empty: {vocabPath}");
        }

        BlankIndex = FindTokenIndex("<blk>", "<blank>", "⁇blk") ?? _vocab.Length - 1;
        BosIndex = FindTokenIndex("<s>", "<bos>", "[CLS]", "<sos>") ?? 0;
        EosIndex = FindTokenIndex("</s>", "<eos>", "[SEP]", "<pad>") ?? _vocab.Length - 2;
    }

    private int? FindTokenIndex(params string[] candidates)
    {
        for (int i = 0; i < _vocab.Length; i++)
        {
            if (candidates.Any(c => string.Equals(c, _vocab[i], StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return null;
    }

    public string DecodeTokens(IEnumerable<int> tokenIds)
    {
        var parts = tokenIds
            .Where(t => t >= 0 && t < _vocab.Length && t != BlankIndex && t != BosIndex && t != EosIndex)
            .Select(t => _vocab[t]);

        var text = string.Concat(parts);
        text = text.Replace('▁', ' ').Trim();
        return text;
    }

    public string GetToken(int index) =>
        index >= 0 && index < _vocab.Length ? _vocab[index] : $"<{index}>";
}
