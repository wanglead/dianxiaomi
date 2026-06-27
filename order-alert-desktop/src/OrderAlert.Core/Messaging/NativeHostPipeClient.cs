using System.Diagnostics;
using System.IO.Pipes;

namespace OrderAlert.Core.Messaging;

public sealed class NativeHostPipeClient
{
    public const string PipeName = "OrderAlert.NativeBridge";
    private readonly string? _applicationPath;

    public NativeHostPipeClient(string? applicationPath = null)
    {
        _applicationPath = applicationPath;
    }

    public async Task<NativeEnvelope> RelayAsync(
        NativeEnvelope request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await RelayOnceAsync(request, TimeSpan.FromSeconds(1), cancellationToken);
        }
        catch (TimeoutException) when (!string.IsNullOrWhiteSpace(_applicationPath))
        {
            Process.Start(new ProcessStartInfo(_applicationPath!) { UseShellExecute = true });
            try
            {
                return await RelayOnceAsync(request, TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (Exception error) when (error is TimeoutException or IOException)
            {
                return new NativeEnvelope(
                    request.RequestId,
                    "error",
                    null,
                    new NativeError("APP_UNAVAILABLE", error.Message));
            }
        }
    }

    private static async Task<NativeEnvelope> RelayOnceAsync(
        NativeEnvelope request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await pipe.ConnectAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Order Alert application pipe is unavailable.");
        }
        await NativeMessageProtocol.WriteAsync(pipe, request, cancellationToken);
        return await NativeMessageProtocol.ReadAsync(pipe, cancellationToken)
            ?? throw new EndOfStreamException("Application pipe closed without a response.");
    }
}
