using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;

namespace HnPaper.Web.Services;

/// <summary>
/// scripts/export-embedding-model.py가 만든 모델을 돌린다. 그래프 안에서 문장 벡터를 고르고 정규화까지 하므로 두 벡터의 내적이 코사인 유사도다.
/// InferenceSession.Run이 스레드에 안전해서 여러 요청이 함께 불러도 된다.
/// </summary>
public sealed class TextEmbedder : IDisposable
{
    /// <param name="MinScore">모델마다 점수 분포가 달라 모델 폴더(embedding.json)에 둔다.</param>
    public sealed record Settings(
        string Source,
        string Revision,
        int Dimensions,
        int MaxTokens,
        string QueryPrefix,
        string DocumentPrefix,
        float MinScore,
        int BosId,
        int EosId,
        int PadId);

    private static readonly string[] InputNames = ["input_ids", "attention_mask"];
    private static readonly string[] OutputNames = ["embedding"];

    private const int BatchTokens = 4096;

    private readonly InferenceSession _session;
    private readonly SentencePieceTokenizer _tokenizer;

    private TextEmbedder(Settings settings, SentencePieceTokenizer tokenizer, InferenceSession session)
    {
        Config = settings;
        _tokenizer = tokenizer;
        _session = session;
    }

    public Settings Config { get; }
    public int Dimensions => Config.Dimensions;

    public static TextEmbedder? Load(string directory, int threads = 0)
    {
        var modelPath = Path.Combine(directory, "model.onnx");
        var settingsPath = Path.Combine(directory, "embedding.json");
        var tokenizerPath = Path.Combine(directory, "tokenizer.json");
        if (!File.Exists(modelPath) || !File.Exists(settingsPath) || !File.Exists(tokenizerPath))
            return null;

        var settings = PaperJson.Read<Settings>(settingsPath)
            ?? throw new InvalidDataException($"embedding.json을 읽지 못했습니다: {settingsPath}");

        SentencePieceTokenizer tokenizer;
        using (var json = File.OpenRead(tokenizerPath))
            tokenizer = SentencePieceTokenizer.CreateFromTokenizerJson(json, addBeginningOfSentence: false, addEndOfSentence: false);

        using var options = new Microsoft.ML.OnnxRuntime.SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            // 코어를 다 쓰면 백그라운드에서 벡터를 만드는 동안 사이트 응답이 밀린다.
            IntraOpNumThreads = threads > 0 ? threads : Math.Max(1, Environment.ProcessorCount / 2),
        };
        var session = new InferenceSession(modelPath, options);
        return new TextEmbedder(settings, tokenizer, session);
    }

    // Hugging Face 토크나이저는 자모로 풀어 쓴 한글(NFD)을 완성형과 다르게 나누므로 먼저 NFC로 맞춘다.
    public int[] Tokenize(string text)
    {
        var pieces = _tokenizer.EncodeToIds(Nfc(text), addBeginningOfSentence: false, addEndOfSentence: false);
        var count = Math.Min(pieces.Count, Config.MaxTokens - 2);
        var ids = new int[count + 2];
        ids[0] = Config.BosId;
        for (var i = 0; i < count; i++)
            ids[i + 1] = pieces[i];
        ids[^1] = Config.EosId;
        return ids;
    }

    public float[] EmbedQuery(string query) => EmbedTokens([Tokenize(Config.QueryPrefix + query)])[0];

    // 길이가 비슷한 글끼리 묶어 빈 토큰(패딩)을 줄인다.
    public float[][] EmbedDocuments(IReadOnlyList<string> texts)
    {
        var tokens = texts.Select(t => Tokenize(Config.DocumentPrefix + t)).ToArray();
        var result = new float[texts.Count][];
        var order = Enumerable.Range(0, texts.Count).OrderBy(i => tokens[i].Length).ToArray();

        var start = 0;
        while (start < order.Length)
        {
            var end = start + 1;
            while (end < order.Length && (end - start + 1) * tokens[order[end]].Length <= BatchTokens)
                end++;
            var batch = order[start..end];
            var vectors = EmbedTokens(batch.Select(i => tokens[i]).ToArray());
            for (var j = 0; j < batch.Length; j++)
                result[batch[j]] = vectors[j];
            start = end;
        }
        return result;
    }

    public float[][] EmbedTokens(IReadOnlyList<int[]> batch)
    {
        var length = batch.Max(t => t.Length);
        var ids = new long[batch.Count * length];
        var mask = new long[batch.Count * length];
        Array.Fill(ids, Config.PadId);
        for (var i = 0; i < batch.Count; i++)
        {
            for (var j = 0; j < batch[i].Length; j++)
            {
                ids[i * length + j] = batch[i][j];
                mask[i * length + j] = 1;
            }
        }

        long[] shape = [batch.Count, length];
        using var idsValue = OrtValue.CreateTensorValueFromMemory(ids, shape);
        using var maskValue = OrtValue.CreateTensorValueFromMemory(mask, shape);
        using var runOptions = new RunOptions();
        using var outputs = _session.Run(runOptions, InputNames, [idsValue, maskValue], OutputNames);

        var data = outputs[0].GetTensorDataAsSpan<float>();
        var vectors = new float[batch.Count][];
        for (var i = 0; i < batch.Count; i++)
            vectors[i] = data.Slice(i * Dimensions, Dimensions).ToArray();
        return vectors;
    }

    private static string Nfc(string text)
    {
        try
        {
            return text.Normalize();
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    public void Dispose() => _session.Dispose();
}
