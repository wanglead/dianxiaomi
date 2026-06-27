using System.Text.Json;
using OrderAlert.Core.Messaging;

namespace OrderAlert.Core.Tests;

public sealed class NativeMessageProtocolTests
{
    [Fact]
    public async Task Writes_a_little_endian_length_and_round_trips_utf8_json()
    {
        var message = new NativeEnvelope(
            "请求-1",
            "scanAccount",
            JsonSerializer.SerializeToElement(new { store = "美国店" }),
            null);
        await using var stream = new MemoryStream();

        await NativeMessageProtocol.WriteAsync(stream, message);
        var bytes = stream.ToArray();
        Assert.Equal(bytes.Length - 4, BitConverter.ToInt32(bytes, 0));

        stream.Position = 0;
        var restored = await NativeMessageProtocol.ReadAsync(stream);
        Assert.Equal(message.RequestId, restored!.RequestId);
        Assert.Equal("美国店", restored.Payload!.Value.GetProperty("store").GetString());
    }

    [Fact]
    public async Task Clean_end_of_stream_returns_null()
    {
        await using var stream = new MemoryStream();

        Assert.Null(await NativeMessageProtocol.ReadAsync(stream));
    }

    [Fact]
    public async Task Rejects_messages_larger_than_four_megabytes()
    {
        var message = new NativeEnvelope(
            "large",
            "scan",
            JsonSerializer.SerializeToElement(new { value = new string('x', 4_300_000) }),
            null);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => NativeMessageProtocol.WriteAsync(new MemoryStream(), message));
    }
}
