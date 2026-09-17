using System.Collections;
using Masuit.Tools;
using Masuit.Tools.Media;
using SkiaSharp;
using System.Collections.Concurrent;
using System.IO;
using 以图搜图.Models;

namespace 以图搜图.Services;

public class ImageSearchService
{
    private readonly object _candidateIndexLock = new();
    private HashCandidateIndex? _candidateIndex;
    private ConcurrentDictionary<string, IndexItem>? _candidateIndexSource;
    private int _candidateIndexSourceCount;

    public async Task<List<SearchResult>> SearchAsync(string filename, ConcurrentDictionary<string, IndexItem> index, ConcurrentDictionary<string, FrameIndexItem> frameIndex, MatchAlgorithm algorithm, float similarity, bool checkRotated, bool checkFlipped)
    {
        var parallelism = Environment.ProcessorCount * 4;
        return await Task.Run(() =>
        {
            var defHashs = new ConcurrentBag<ulong[]>();
            var dctHashs = new ConcurrentBag<ulong>();
            var pHashs = new ConcurrentBag<ulong>();
            var actions = new List<Action>();

            if (filename.EndsWith("gif", StringComparison.OrdinalIgnoreCase))
            {
                using var frames = new DisposeCollection<SKBitmap>(SkiaImageHelper.DecodeGrayFrames(filename, 160));
                foreach (var frame in frames.Items)
                {
                    actions.Add(() =>
                    {
                        if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                        {
                            defHashs.Add(frame.DifferenceHash256());
                        }

                        if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                        {
                            dctHashs.Add(frame.DctHash());
                        }

                        if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                        {
                            pHashs.Add(frame.DctHash64());
                        }

                        frame.Dispose();
                    });
                }

                Parallel.Invoke(actions.ToArray());
            }
            else
            {
                using (var image = SkiaImageHelper.DecodeGrayThumb(filename, 160))
                {
                    if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                    {
                        actions.Add(() => defHashs.Add(image.DifferenceHash256()));
                    }

                    if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                    {
                        actions.Add(() => dctHashs.Add(image.DctHash()));
                    }

                    if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                    {
                        actions.Add(() => pHashs.Add(image.DctHash64()));
                    }

                    if (checkRotated)
                    {
                        actions.Add(() =>
                        {
                            using var clone = image.Rotate(90);
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                            {
                                dctHashs.Add(clone.DctHash());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                            {
                                pHashs.Add(clone.DctHash64());
                            }
                        });
                        actions.Add(() =>
                        {
                            using var clone = image.Rotate(180);
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                            {
                                dctHashs.Add(clone.DctHash());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                            {
                                pHashs.Add(clone.DctHash64());
                            }
                        });
                        actions.Add(() =>
                        {
                            using var clone = image.Rotate(270);
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                            {
                                dctHashs.Add(clone.DctHash());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                            {
                                pHashs.Add(clone.DctHash64());
                            }
                        });
                    }

                    if (checkFlipped)
                    {
                        actions.Add(() =>
                        {
                            using var clone = image.FlipHorizontal();
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                            {
                                dctHashs.Add(clone.DctHash());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                            {
                                pHashs.Add(clone.DctHash64());
                            }
                        });
                        actions.Add(() =>
                        {
                            using var clone = image.FlipVertical();
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                            {
                                dctHashs.Add(clone.DctHash());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                            {
                                pHashs.Add(clone.DctHash64());
                            }
                        });
                    }

                    Parallel.Invoke(actions.ToArray());
                }
            }

            var list = new List<SearchResult>();
            var queryDifferenceHashes = defHashs.ToArray();
            var queryDctHashes = dctHashs.ToArray();
            var queryDctHash64s = pHashs.ToArray();
            var useDifferenceHash = algorithm.HasFlag(MatchAlgorithm.DifferenceHash);
            var useDctHash32 = algorithm.HasFlag(MatchAlgorithm.DctHash32);
            var useDctHash64 = algorithm.HasFlag(MatchAlgorithm.DctHash64);
            var frameSearchParallelism = Math.Max(1, Environment.ProcessorCount);

            if (filename.EndsWith("gif", StringComparison.OrdinalIgnoreCase))
            {
                list.AddRange(frameIndex.AsParallel().WithDegreeOfParallelism(frameSearchParallelism).SelectMany(x =>
                {
                    var items = new List<SearchResult>(4);
                    if (useDifferenceHash)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = x.Key,
                            匹配度 = Top10Average(x.Value.DifferenceHash, queryDifferenceHashes, similarity),
                            匹配算法 = "Difference Hash"
                        });
                    }

                    var sim = Math.Max(0.85, similarity);
                    if (useDctHash64)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = x.Key,
                            匹配度 = Top10Average(x.Value.DctHash64, queryDctHash64s, (float) sim),
                            匹配算法 = "DCT Hash 64"
                        });
                    }

                    if (useDctHash32)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = x.Key,
                            匹配度 = Top10Average(x.Value.DctHash, queryDctHashes, (float) sim),
                            匹配算法 = "DCT Hash 32"
                        });
                    }

                    return items;
                }).Where(x => x.匹配度 >= similarity));
            }
            else
            {
                var sim = Math.Max(0.85, similarity);
                list.AddRange(frameIndex.AsParallel().WithDegreeOfParallelism(frameSearchParallelism).SelectMany(x =>
                {
                    var items = new List<SearchResult>(4);
                    if (useDctHash64)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = x.Key,
                            匹配度 = MaxCompare(x.Value.DctHash64, queryDctHash64s, (float) sim),
                            匹配算法 = "DCT Hash 64"
                        });
                    }

                    if (useDifferenceHash)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = x.Key,
                            匹配度 = MaxCompare(x.Value.DifferenceHash, queryDifferenceHashes),
                            匹配算法 = "Difference Hash"
                        });
                    }

                    if (useDctHash32)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = x.Key,
                            匹配度 = MaxCompare(x.Value.DctHash, queryDctHashes, (float) sim),
                            匹配算法 = "DCT Hash 32"
                        });
                    }

                    return items;
                }).Where(x => x.匹配度 >= similarity));

                var indexSearchParallelism = Math.Max(1, Environment.ProcessorCount*2);
                var indexSearchOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = indexSearchParallelism
                };
                var resultBatches = new ConcurrentBag<List<SearchResult>>();
                IEnumerable<KeyValuePair<string, IndexItem>> searchEntries = index;

                if (!useDifferenceHash && (useDctHash32 || useDctHash64))
                {
                    var candidateIndex = GetCandidateIndex(index);
                    var candidatePaths = candidateIndex.FindCandidates(queryDctHashes, queryDctHash64s);
                    searchEntries = candidatePaths.Select(path => index.TryGetValue(path, out var item) ? (KeyValuePair<string, IndexItem>?) new KeyValuePair<string, IndexItem>(path, item) : null).Where(entry => entry.HasValue).Select(entry => entry.GetValueOrDefault());
                }

                Parallel.ForEach(searchEntries, indexSearchOptions, () => new List<SearchResult>(), (entry, _, items) =>
                {
                    var key = entry.Key;
                    var value = entry.Value;

                    if (useDctHash64)
                    {
                        var match = MaxCompare(value.DctHash64, queryDctHash64s);
                        if (match > sim)
                        {
                            items.Add(new SearchResult
                            {
                                路径 = key,
                                匹配度 = match,
                                匹配算法 = "DCT Hash 64"
                            });
                        }
                    }

                    if (useDifferenceHash)
                    {
                        var match = MaxCompare(value.DifferenceHash, queryDifferenceHashes);
                        if (match > similarity)
                        {
                            items.Add(new SearchResult
                            {
                                路径 = key,
                                匹配度 = match,
                                匹配算法 = "Difference Hash"
                            });
                        }
                    }

                    if (useDctHash32)
                    {
                        var match = MaxCompare(value.DctHash, queryDctHashes);
                        if (match > sim)
                        {
                            items.Add(new SearchResult
                            {
                                路径 = key,
                                匹配度 = match,
                                匹配算法 = "DCT Hash 32"
                            });
                        }
                    }

                    return items;
                }, items => resultBatches.Add(items));
                list.AddRange(resultBatches.SelectMany(items => items));
            }

            list = list.OrderByDescending(a => a.匹配度).DistinctBy(e => e.路径).ToList();
            var dic = list.Where(e => File.Exists(e.路径)).GroupBy(r => new FileInfo(r.路径).DirectoryName).Where(g => g.Key != null).AsParallel().WithDegreeOfParallelism(parallelism).Select(g =>
            {
                var files = new DirectoryInfo(g.Key!).GetFiles("*.*", SearchOption.AllDirectories);
                return new
                {
                    Key = g.Key!,
                    files.Length,
                    Size = files.Sum(s => s.Length) / 1048576f
                };
            }).ToDictionary(a => a.Key);

            list.Where(e => File.Exists(e.路径)).OrderBy(e => e.路径).ForEach(result =>
            {
                var file = new FileInfo(result.路径);
                result.大小 = $"{file.Length / 1024}KB";
                var dirName = file.DirectoryName!;
                if (dic.ContainsKey(dirName))
                {
                    result.所属文件夹文件数 = dic[dirName].Length;
                    result.所属文件夹大小 = $"{dic[dirName].Size:F2}MB";
                }
            });

            return list;
        });
    }

    private HashCandidateIndex GetCandidateIndex(ConcurrentDictionary<string, IndexItem> index)
    {
        lock (_candidateIndexLock)
        {
            if (_candidateIndex != null && ReferenceEquals(_candidateIndexSource, index) && _candidateIndexSourceCount == index.Count)
            {
                return _candidateIndex;
            }

            var snapshot = index.ToArray();
            _candidateIndex = HashCandidateIndex.Build(snapshot);
            _candidateIndexSource = index;
            _candidateIndexSourceCount = snapshot.Length;
            return _candidateIndex;
        }
    }

    private static float MaxCompare(ulong value, ulong[] queryHashes)
    {
        var max = 0f;
        foreach (var queryHash in queryHashes)
        {
            var match = ImageHasher.Compare(value, queryHash);
            if (match > max)
            {
                max = match;
            }
        }

        return max;
    }

    private static float MaxCompare(ulong[] value, ulong[][] queryHashes)
    {
        var max = 0f;
        foreach (var queryHash in queryHashes)
        {
            var match = ImageHasher.Compare(value, queryHash);
            if (match > max)
            {
                max = match;
            }
        }

        return max;
    }

    private static float MaxCompare(List<ulong> values, ulong[] queryHashes)
    {
        var max = 0f;
        foreach (var value in values)
        {
            foreach (var queryHash in queryHashes)
            {
                var match = ImageHasher.Compare(value, queryHash);
                if (match > max)
                {
                    max = match;
                }
            }
        }

        return max;
    }

    private static float MaxCompare(List<ulong> values, ulong[] queryHashes, float threshold)
    {
        var max = 0f;
        foreach (var value in values)
        {
            for (var index = 0; index < queryHashes.Length; index++)
            {
                var match = ImageHasher.Compare(value, queryHashes[index]);
                if (match >= threshold && match > max)
                {
                    max = match;
                }
            }
        }

        return max;
    }

    private static float MaxCompare(List<ulong[]> values, ulong[][] queryHashes)
    {
        var max = 0f;
        foreach (var value in values)
        {
            for (var index = 0; index < queryHashes.Length; index++)
            {
                var match = ImageHasher.Compare(value, queryHashes[index]);
                if (match > max)
                {
                    max = match;
                }
            }
        }

        return max;
    }

    private static float Top10Average(List<ulong> values, ulong[] queryHashes, float threshold)
    {
        Span<float> topMatches = stackalloc float[10];
        var count = 0;

        foreach (var value in values)
        {
            foreach (var queryHash in queryHashes)
            {
                var match = ImageHasher.Compare(value, queryHash);
                if (match >= threshold)
                {
                    AddTopMatch(topMatches, ref count, match);
                }
            }
        }

        return Average(topMatches, count);
    }

    private static float Top10Average(List<ulong[]> values, ulong[][] queryHashes, float threshold)
    {
        Span<float> topMatches = stackalloc float[10];
        var count = 0;

        foreach (var value in values)
        {
            foreach (var queryHash in queryHashes)
            {
                var match = ImageHasher.Compare(value, queryHash);
                if (match >= threshold)
                {
                    AddTopMatch(topMatches, ref count, match);
                }
            }
        }

        return Average(topMatches, count);
    }

    private static void AddTopMatch(Span<float> topMatches, ref int count, float match)
    {
        if (count == topMatches.Length && match <= topMatches[^1])
        {
            return;
        }

        var position = Math.Min(count, topMatches.Length - 1);
        if (count < topMatches.Length)
        {
            count++;
        }

        while (position > 0 && topMatches[position - 1] < match)
        {
            topMatches[position] = topMatches[position - 1];
            position--;
        }

        topMatches[position] = match;
    }

    private static float Average(Span<float> values, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        var total = 0f;
        for (var i = 0; i < count; i++)
        {
            total += values[i];
        }

        return total / count;
    }
}

internal sealed class DisposeCollection<T>(IEnumerable<T> items) : IEnumerable<T>, IDisposable where T : IDisposable
{
    public List<T> Items { get; } = items.ToList();

    public void Dispose()
    {
        foreach (var item in Items)
        {
            item.Dispose();
        }
    }

    /// <summary>Returns an enumerator that iterates through the collection.</summary>
    /// <returns>An enumerator that can be used to iterate through the collection.</returns>
    public IEnumerator<T> GetEnumerator()
    {
        return Items.GetEnumerator();
    }

    /// <summary>Returns an enumerator that iterates through a collection.</summary>
    /// <returns>An <see cref="T:System.Collections.IEnumerator" /> object that can be used to iterate through the collection.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}