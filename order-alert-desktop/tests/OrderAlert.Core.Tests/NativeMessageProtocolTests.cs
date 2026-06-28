using System.Text.Json;
using System.IO.Pipes;
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

    [Fact]
    public async Task Named_pipe_server_can_initiate_a_request_and_preserve_its_id()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = new NativeHostPipeServer();
        var run = server.RunAsync(cancellation.Token);
        await using var host = new NamedPipeClientStream(
            ".",
            NativeHostPipeClient.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await host.ConnectAsync(cancellation.Token);
        var hostPump = Task.Run(async () =>
        {
            var request = await NativeMessageProtocol.ReadAsync(host, cancellation.Token);
            await NativeMessageProtocol.WriteAsync(
                host,
                new NativeEnvelope(request!.RequestId, "scanResult", request.Payload, null),
                cancellation.Token);
        }, cancellation.Token);

        var response = await server.SendAsync(
            new NativeEnvelope(
                "pipe-request",
                "scanAccount",
                JsonSerializer.SerializeToElement(new { url = "https://example.invalid" }),
                null),
            cancellation.Token);

        Assert.Equal("pipe-request", response.RequestId);
        await hostPump;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }
}
