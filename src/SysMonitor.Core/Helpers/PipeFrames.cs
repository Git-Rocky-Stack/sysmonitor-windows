using System.Buffers.Binary;

namespace SysMonitor.Core.Helpers;

/// <summary>
/// Messages over a byte stream - the named pipes the app talks to its elevated helpers through - as a length
/// and then that many bytes, since a pipe in byte mode has no end-of-message of its own.
/// </summary>
public static class PipeFrames
{
    /// <summary>The largest frame either side accepts: far beyond any list or chunk, far short of exhausting memory.</summary>
    public const int MaxFrameBytes = 16 * 1024 * 1024;

    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, body.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>The next frame, or null when the other side closed without sending one.</summary>
    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var length = new byte[4];
        if (await stream.ReadAtLeastAsync(length, 4, throwOnEndOfStream: false, cancellationToken) < 4)
            return null;

        var size = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (size is < 0 or > MaxFrameBytes)
            throw new IOException("The other side sent a frame of an impossible size.");

        var body = new byte[size];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return body;
    }
}
