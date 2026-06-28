using System.IO.Pipes;

namespace OrderAlert.Core.Messaging;

public interface INativeCommandChannel
{
    Task<NativeEnvelope> SendAsync(
        NativeEnvelope request,
        CancellationToken cancellationToken);
}

public sealed class NativeHostPipeServer : INativeCommandChannel, IAsyncDisposable
{
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private TaskCompletionSource<NamedPipeServerStream> _connected =
        NewConnectionSource();
    private NamedPipeServerStream? _pipe;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeServerStream(
            NativeHostPipeClient.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        _pipe = pipe;
        try
        {
            await pipe.WaitForConnectionAsync(cancellationToken);
            _connected.TrySetResult(pipe);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        finally
        {
            _connected.TrySetCanceled(cancellationToken);
            pipe.Dispose();
            _pipe = null;
        }
    }

    public async Task<NativeEnvelope> SendAsync(
        NativeEnvelope request,
        CancellationToken cancellationToken = default)
    {
        var pipe = await _connected.Task.WaitAsync(cancellationToken);
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            await NativeMessageProtocol.WriteAsync(pipe, request, cancellationToken);
            var response = await NativeMessageProtocol.ReadAsync(pipe, cancellationToken)
                ?? throw new EndOfStreamException("Native host disconnected.");
            if (response.RequestId != request.RequestId)
                throw new InvalidDataException("Native response request ID does not match.");
            return response;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _pipe?.Dispose();
        _requestLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private static TaskCompletionSource<NamedPipeServerStream> NewConnectionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
