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

    public async Task RunRelayAsync(
        Stream browserInput,
        Stream browserOutput,
        CancellationToken cancellationToken = default)
    {
        await using var pipe = await ConnectAsync(cancellationToken);
        var appToBrowser = PumpAsync(pipe, browserOutput, cancellationToken);
        var browserToApp = PumpAsync(browserInput, pipe, cancellationToken);
        await Task.WhenAny(appToBrowser, browserToApp);
    }

    private async Task<NamedPipeClientStream> ConnectAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await ConnectOnceAsync(TimeSpan.FromSeconds(1), cancellationToken);
        }
        catch (TimeoutException) when (!string.IsNullOrWhiteSpace(_applicationPath))
        {
            Process.Start(new ProcessStartInfo(_applicationPath!) { UseShellExecute = true });
            return await ConnectOnceAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    private static async Task<NamedPipeClientStream> ConnectOnceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(
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
            return pipe;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            pipe.Dispose();
            throw new TimeoutException("Order Alert application pipe is unavailable.");
        }
    }

    private static async Task PumpAsync(
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await NativeMessageProtocol.ReadAsync(input, cancellationToken);
            if (message is null) return;
            await NativeMessageProtocol.WriteAsync(output, message, cancellationToken);
        }
    }
}
