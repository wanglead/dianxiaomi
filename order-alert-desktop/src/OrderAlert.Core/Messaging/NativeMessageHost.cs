namespace OrderAlert.Core.Messaging;

public static class NativeMessageHost
{
    public static async Task RunAsync(
        Stream input,
        Stream output,
        Func<NativeEnvelope, CancellationToken, Task<NativeEnvelope>> handler,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var request = await NativeMessageProtocol.ReadAsync(input, cancellationToken);
            if (request is null) return;
            var response = await handler(request, cancellationToken);
            await NativeMessageProtocol.WriteAsync(output, response, cancellationToken);
        }
    }
}
