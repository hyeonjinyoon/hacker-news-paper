using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>
/// 기사 점수는 조각 점수의 최댓값이다. 첫머리 조각만 쓰면 본문에서만 맞는 관련 기사를 놓쳤다(기사 300개 시험).
/// 조각마다 한국어 제목을 붙이는 것은 "핵심 내용" 같은 글머리표만으로는 무슨 기사인지 알 수 없어서다.
/// </summary>
public sealed class SemanticIndex(PaperOptions options, EditionStore store, ILogger<SemanticIndex> logger, string? model = null)
    : BackgroundService
{
    public const string DefaultModel = "snowflake-arctic-embed-l-v2.0-ko";

    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(10);
    // 한국어 900자는 최대 510토큰이라(시험 기사 101조각) 모델의 512토큰 안에 든다.
    private const int MaxChunkChars = 900;
    private const int SyncBatch = 32;
    private const int MaxCachedQueries = 256;

    private static readonly byte[] FileMagic = "HNVE"u8.ToArray();
    private const int FileVersion = 1;

    public sealed record Chunk(UInt128 Key, string Text, string Content);

    public sealed record Match(StoryView Story, float Score, string Content);

    private readonly Lazy<TextEmbedder?> _embedder = new(() => LoadEmbedder(options, logger, model ?? DefaultModel));
    private readonly ConcurrentDictionary<UInt128, Half[]> _vectors = new();
    // SyncAsync에서 _gate를 잡고만 쓴다.
    private readonly Dictionary<string, HashSet<UInt128>> _saved = new(StringComparer.Ordinal);
    // EditionStore는 파일이 바뀐 호만 새 StoryView로 다시 만들므로, 기사 객체에 붙여 두면 바뀐 기사만 다시 나눈다.
    private readonly ConditionalWeakTable<StoryView, Chunk[]> _chunks = new();
    private readonly ConcurrentDictionary<string, float[]> _queries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);
    // 저장된 벡터를 불러오기 전에는 Score가 빈 목록을 준다. 검색 요청이 모델 로딩(몇 초)을 기다리지 않게 하려는 것이다.
    private volatile bool _loaded;

    public string Model { get; } = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;

    public TextEmbedder? Embedder => _embedder.Value;

    private static TextEmbedder? LoadEmbedder(PaperOptions options, ILogger logger, string model)
    {
        var dir = options.ModelDirectory(string.IsNullOrWhiteSpace(model) ? DefaultModel : model);
        try
        {
            var embedder = TextEmbedder.Load(dir);
            if (embedder is null)
                logger.LogInformation("의미 검색 모델이 없어 키워드로만 검색합니다: {Directory}", dir);
            else
                logger.LogInformation("의미 검색 모델을 읽었습니다: {Directory} ({Source})", dir, embedder.Config.Source);
            return embedder;
        }
        catch (Exception e)
        {
            logger.LogError(e, "의미 검색 모델을 읽지 못해 키워드로만 검색합니다: {Directory}", dir);
            return null;
        }
    }

    // ── 검색 ──────────────────────────────────────────────

    public IReadOnlyList<Match> Similar(string query, IEnumerable<StoryView> stories, int max, float? minScore = null)
    {
        if (!_loaded || Embedder is not { } embedder)
            return [];
        var threshold = minScore ?? embedder.Config.MinScore;
        return Score(query, stories).Where(m => m.Score >= threshold).OrderByDescending(m => m.Score).Take(max).ToList();
    }

    public IReadOnlyList<Match> Score(string query, IEnumerable<StoryView> stories)
    {
        if (!_loaded || Embedder is not { } embedder)
            return [];

        var queryVector = QueryVector(embedder, query);
        var buffer = new float[embedder.Dimensions];
        var matches = new List<Match>();
        var missing = false;
        foreach (var story in stories)
        {
            Chunk? best = null;
            var bestScore = float.NegativeInfinity;
            foreach (var chunk in ChunksOf(embedder, story))
            {
                if (!_vectors.TryGetValue(chunk.Key, out var vector))
                {
                    missing = true;
                    continue;
                }
                TensorPrimitives.ConvertToSingle(vector, buffer);
                var score = TensorPrimitives.Dot(queryVector, buffer);
                if (score > bestScore)
                    (best, bestScore) = (chunk, score);
            }
            if (best is not null)
                matches.Add(new Match(story, bestScore, best.Content));
        }
        if (missing)
            Wake();
        return matches;
    }

    // 쪽을 넘기거나 정렬을 바꾸면 같은 검색어가 다시 온다.
    private float[] QueryVector(TextEmbedder embedder, string query)
    {
        if (_queries.TryGetValue(query, out var cached))
            return cached;
        var vector = embedder.EmbedQuery(query);
        if (_queries.Count >= MaxCachedQueries)
            _queries.Clear();
        _queries[query] = vector;
        return vector;
    }

    // ── 기사 조각 ──────────────────────────────────────────────

    private Chunk[] ChunksOf(TextEmbedder embedder, StoryView story) =>
        _chunks.GetValue(story, s => Split(s).Select(c => new Chunk(Key(embedder, c.Text), c.Text, c.Content)).ToArray());

    public static IReadOnlyList<(string Text, string Content)> Split(StoryView story)
    {
        var title = Nfc(story.Title).Trim();
        var original = Nfc(story.Raw.Title).Trim();
        var head = original == title ? title : title + "\n" + original;

        if (story.Body is null)
        {
            var summary = Nfc(story.Summary ?? story.Raw.Description ?? "").Trim();
            return [(summary.Length == 0 ? head : head + "\n" + summary, summary)];
        }

        var chunks = new List<(string Text, string Content)>();
        foreach (var (heading, blocks) in MarkdownRenderer.Sections(Nfc(story.Body)))
        {
            var header = heading is null ? head : title + "\n" + heading;
            foreach (var piece in Pieces(blocks))
                chunks.Add((header + "\n" + piece, piece));
        }
        return chunks.Count > 0 ? chunks : [(head, "")];
    }

    // 블록 하나가 MaxChunkChars보다 길면 그대로 두므로, 넘치는 뒷부분은 모델이 MaxTokens에서 잘라 버린다.
    private static IEnumerable<string> Pieces(IReadOnlyList<string> blocks)
    {
        var builder = new StringBuilder();
        foreach (var block in blocks)
        {
            if (builder.Length > 0 && builder.Length + 1 + block.Length > MaxChunkChars)
            {
                yield return builder.ToString();
                builder.Clear();
            }
            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(block);
        }
        if (builder.Length > 0)
            yield return builder.ToString();
    }

    // 모델 판·접두어·최대 토큰 수가 바뀌면 같은 글이라도 벡터가 달라지므로 키에 넣는다.
    private static UInt128 Key(TextEmbedder embedder, string text)
    {
        var config = embedder.Config;
        var bytes = Encoding.UTF8.GetBytes($"{config.Revision}\n{config.DocumentPrefix}\n{config.MaxTokens}\n{text}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return BinaryPrimitives.ReadUInt128LittleEndian(hash);
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

    // ── 벡터 만들기 ──────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 모델 로딩(몇 초)이 사이트 시작을 막지 않게 첫 await 전에 스레드 풀로 넘긴다.
        await Task.Yield();
        if (Embedder is null)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Run(() => SyncAsync(stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "의미 검색 벡터를 만들지 못했습니다.");
            }

            try
            {
                await _wake.WaitAsync(SyncInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Wake()
    {
        try
        {
            if (_wake.CurrentCount == 0)
                _wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public async Task<int> SyncAsync(CancellationToken cancellationToken)
    {
        if (Embedder is not { } embedder)
            return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_loaded)
            {
                LoadVectors(embedder);
                _loaded = true;
            }

            var editions = new List<(string Date, List<Chunk> Chunks, List<Chunk> Missing)>();
            foreach (var info in store.List())
            {
                if (store.Load(info.Date) is not { } edition)
                    continue;
                var chunks = edition.All.SelectMany(s => ChunksOf(embedder, s)).DistinctBy(c => c.Key).ToList();
                editions.Add((info.Date, chunks, chunks.Where(c => !_vectors.ContainsKey(c.Key)).ToList()));
            }

            var total = editions.SelectMany(e => e.Missing).Select(c => c.Key).Distinct().Count();
            if (total > 0)
                logger.LogInformation("의미 검색 벡터를 만듭니다: 조각 {Total}개", total);

            var made = 0;
            var watch = Stopwatch.StartNew();
            var lastLog = TimeSpan.Zero;
            foreach (var (date, chunks, missing) in editions)
            {
                foreach (var batch in missing.Where(c => !_vectors.ContainsKey(c.Key)).Chunk(SyncBatch))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var vectors = embedder.EmbedDocuments(batch.Select(c => c.Text).ToList());
                    for (var i = 0; i < batch.Length; i++)
                        _vectors[batch[i].Key] = ToHalf(vectors[i]);
                    made += batch.Length;
                    if (watch.Elapsed - lastLog > TimeSpan.FromMinutes(1))
                    {
                        lastLog = watch.Elapsed;
                        logger.LogInformation("의미 검색 벡터: {Made}/{Total}개 ({Minutes:0}분)", made, total, watch.Elapsed.TotalMinutes);
                    }
                }

                var keys = chunks.Select(c => c.Key).ToHashSet();
                if (!_saved.TryGetValue(date, out var saved) || !saved.SetEquals(keys))
                {
                    Save(embedder, date, chunks);
                    _saved[date] = keys;
                }
            }

            if (made > 0)
                logger.LogInformation("의미 검색 벡터를 만들었습니다: 조각 {Made}개, {Seconds:0.0}초", made, watch.Elapsed.TotalSeconds);
            return made;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Half[] ToHalf(float[] vector)
    {
        var half = new Half[vector.Length];
        TensorPrimitives.ConvertToHalf(vector, half);
        return half;
    }

    // ── 벡터 파일 ──────────────────────────────────────────────
    // 벡터는 fp16으로 두어 메모리와 파일을 반으로 줄인다. 내적 오차는 최대 0.00003이었다(시험 벡터 17개).
    // 파일은 실행 기기의 바이트 순서로 쓴다(x64, arm64 모두 리틀 엔디언).

    private void Save(TextEmbedder embedder, string date, IReadOnlyList<Chunk> chunks)
    {
        var dir = options.VectorDirectory(Model);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, date + ".bin");
        // 사이트와 semantic-index 명령이 같은 파일을 함께 쓸 수 있다.
        var temp = $"{path}.{Environment.ProcessId}.tmp";
        var present = chunks.Where(c => _vectors.ContainsKey(c.Key)).ToList();
        using (var stream = File.Create(temp))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(FileMagic);
            writer.Write(FileVersion);
            writer.Write(embedder.Dimensions);
            writer.Write(present.Count);
            foreach (var chunk in present)
            {
                writer.Write((ulong)chunk.Key);
                writer.Write((ulong)(chunk.Key >> 64));
                writer.Write(MemoryMarshal.AsBytes(_vectors[chunk.Key].AsSpan()));
            }
        }
        File.Move(temp, path, overwrite: true);
    }

    private void LoadVectors(TextEmbedder embedder)
    {
        var dir = options.VectorDirectory(Model);
        if (!Directory.Exists(dir))
            return;

        foreach (var path in Directory.EnumerateFiles(dir, "*.bin"))
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                if (!reader.ReadBytes(FileMagic.Length).AsSpan().SequenceEqual(FileMagic)
                    || reader.ReadInt32() != FileVersion
                    || reader.ReadInt32() != embedder.Dimensions)
                {
                    logger.LogWarning("형식이 다른 벡터 파일이라 다시 만듭니다: {Path}", path);
                    continue;
                }
                var count = reader.ReadInt32();
                var keys = new HashSet<UInt128>(count);
                for (var i = 0; i < count; i++)
                {
                    var low = reader.ReadUInt64();
                    var high = reader.ReadUInt64();
                    var key = new UInt128(high, low);
                    var vector = new Half[embedder.Dimensions];
                    stream.ReadExactly(MemoryMarshal.AsBytes(vector.AsSpan()));
                    _vectors.TryAdd(key, vector);
                    keys.Add(key);
                }
                _saved[Path.GetFileNameWithoutExtension(path)] = keys;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "벡터 파일을 읽지 못해 다시 만듭니다: {Path}", path);
            }
        }
        logger.LogInformation("저장해 둔 의미 검색 벡터를 불러왔습니다: 조각 {Count}개", _vectors.Count);
    }

    public override void Dispose()
    {
        base.Dispose();
        if (_embedder.IsValueCreated)
            _embedder.Value?.Dispose();
        _gate.Dispose();
        _wake.Dispose();
    }
}
