using System.Security.Cryptography;

namespace WICareerBuilder.Core.Discovery;

/// <summary>
/// Creates a deliberately light-weight content fingerprint for staged scanning.
/// It reads at most three 64 KiB windows (start/middle/end) plus the file length,
/// rather than hashing an entire multi-gigabyte archive during discovery.
/// This is a duplicate/move candidate signal, not cryptographic proof that two files are identical.
/// </summary>
public sealed class ContentFingerprintService
{
    public const string FingerprintKind = "SHA256-window-v1";
    private const int WindowSize = 64 * 1024;

    public Task<string> ComputeQuickFingerprintAsync(string fullPath, CancellationToken cancellationToken = default)
        => Task.Run(() => ComputeQuickFingerprint(fullPath, cancellationToken), cancellationToken);

    private static string ComputeQuickFingerprint(string fullPath, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, WindowSize, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        BitConverter.TryWriteBytes(lengthBytes, stream.Length);
        hash.AppendData(lengthBytes);

        var positions = SamplingPositions(stream.Length);
        var buffer = new byte[WindowSize];
        Span<byte> positionBytes = stackalloc byte[sizeof(long)];
        foreach (var position in positions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stream.Position = position;
            var remaining = (int)Math.Min(WindowSize, stream.Length - position);
            var readTotal = 0;
            while (readTotal < remaining)
            {
                var read = stream.Read(buffer, readTotal, remaining - readTotal);
                if (read <= 0) break;
                readTotal += read;
            }

            BitConverter.TryWriteBytes(positionBytes, position);
            hash.AppendData(positionBytes);
            if (readTotal > 0) hash.AppendData(buffer.AsSpan(0, readTotal));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static IReadOnlyList<long> SamplingPositions(long length)
    {
        if (length <= 0) return [0];
        if (length <= WindowSize) return [0];

        var last = Math.Max(0, length - WindowSize);
        if (length <= WindowSize * 2L) return [0, last];

        var middle = Math.Max(0, Math.Min(last, (length / 2L) - (WindowSize / 2L)));
        return new[] { 0L, middle, last }.Distinct().OrderBy(x => x).ToArray();
    }
}
