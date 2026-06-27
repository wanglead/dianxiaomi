using System.IO.Pipes;

namespace OrderAlert.Core.Messaging;

public sealed class NativeHostPipeServer
{
    private readonly Func<NativeEnvelope, CancellationToken, Task<NativeEnvelope>> _handler;

    public NativeHostPipeServer(
        Func<NativeEnvelope, CancellationToken, Task<NativeEnvelope>> handler)
    {
        _handler = handler;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                NativeHostPipeClient.PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(cancellationToken);
            var request = await NativeMessageProtocol.ReadAsync(pipe, cancellationToken);
            if (request is null) continue;
            var response = await _handler(request, cancellationToken);
            if (response.RequestId != request.RequestId)
                response = response with { RequestId = request.RequestId };
            await NativeMessageProtocol.WriteAsync(pipe, response, cancellationToken);
        }
    }
}
