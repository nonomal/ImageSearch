using 以图搜图.Models;

namespace 以图搜图.Services;

internal sealed class HashCandidateIndex
{
    private static readonly int[] DctHashBucketOffsets = [0, 7, 15, 23];
    private static readonly int[] DctHash64BucketOffsets = [0, 11, 22, 33];
    private readonly Dictionary<byte, List<string>>[] _dctHashBuckets;
    private readonly Dictionary<byte, List<string>>[] _dctHash64Buckets;

    private HashCandidateIndex(Dictionary<byte, List<string>>[] dctHashBuckets, Dictionary<byte, List<string>>[] dctHash64Buckets)
    {
        _dctHashBuckets = dctHashBuckets;
        _dctHash64Buckets = dctHash64Buckets;
    }

    public static HashCandidateIndex Build(KeyValuePair<string, IndexItem>[] entries)
    {
        var dctHashBuckets = CreateBuckets();
        var dctHash64Buckets = CreateBuckets();

        foreach (var (path, item) in entries)
        {
            for (var table = 0; table < DctHashBucketOffsets.Length; table++)
            {
                Add(dctHashBuckets[table], GetBucket(item.DctHash, table, DctHashBucketOffsets), path);
                Add(dctHash64Buckets[table], GetBucket(item.DctHash64, table, DctHash64BucketOffsets), path);
            }
        }

        return new HashCandidateIndex(dctHashBuckets, dctHash64Buckets);
    }

    public HashSet<string> FindCandidates(ulong[] dctHashes, ulong[] dctHash64s)
    {
        var candidates = new HashSet<string>();
        AddCandidates(_dctHashBuckets, dctHashes, DctHashBucketOffsets, candidates);
        AddCandidates(_dctHash64Buckets, dctHash64s, DctHash64BucketOffsets, candidates);
        return candidates;
    }

    private static Dictionary<byte, List<string>>[] CreateBuckets()
    {
        return Enumerable.Range(0, DctHash64BucketOffsets.Length).Select(_ => new Dictionary<byte, List<string>>()).ToArray();
    }

    private static void Add(Dictionary<byte, List<string>> buckets, byte bucket, string path)
    {
        if (!buckets.TryGetValue(bucket, out var paths))
        {
            paths = [];
            buckets[bucket] = paths;
        }

        paths.Add(path);
    }

    private static void AddCandidates(Dictionary<byte, List<string>>[] buckets, ulong[] hashes, int[] bucketOffsets, HashSet<string> candidates)
    {
        foreach (var hash in hashes)
        {
            for (var table = 0; table < bucketOffsets.Length; table++)
            {
                if (buckets[table].TryGetValue(GetBucket(hash, table, bucketOffsets), out var paths))
                {
                    candidates.UnionWith(paths);
                }
            }
        }
    }

    private static byte GetBucket(ulong hash, int table, int[] bucketOffsets)
    {
        return (byte) (hash >> bucketOffsets[table]);
    }
}