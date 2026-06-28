using System.Buffers.Binary;
using System.Text.Json;

namespace OrderAlert.Core.Messaging;

public sealed record NativeError(string Code, string Message);

public sealed record NativeEnvelope(
    string? RequestId,
    string Type,
    JsonElement? Payload,
    NativeError? Error);

public static class NativeMessageProtocol
{
    public const int MaximumMessageBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task WriteAsync(
        Stream stream,
        NativeEnvelope message,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length > MaximumMessageBytes)
            throw new InvalidDataException("Native message exceeds the 4 MiB limit.");

        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<NativeEnvelope?> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var header = new byte[4];
        var firstRead = await stream.ReadAsync(header.AsMemory(0, 4), cancellationToken);
        if (firstRead == 0) return null;
        await ReadRemainderAsync(stream, header, firstRead, cancellationToken);

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaximumMessageBytes)
            throw new InvalidDataException("Native message length is invalid.");

        var payload = new byte[length];
        await ReadRemainderAsync(stream, payload, 0, cancellationToken);
        return JsonSerializer.Deserialize<NativeEnvelope>(payload, JsonOptions)
            ?? throw new InvalidDataException("Native message JSON is empty.");
    }

    private static async Task ReadRemainderAsync(
        Stream stream,
        byte[] buffer,
        int offset,
        CancellationToken cancellationToken)
    {
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);
            if (read == 0) throw new EndOfStreamException("Native message ended unexpectedly.");
            offset += read;
        }
    }
}
