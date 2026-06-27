using OrderAlert.Core.Messaging;

var applicationPath = Path.Combine(
    AppContext.BaseDirectory,
    "OrderAlert.App.exe");
var pipeClient = new NativeHostPipeClient(applicationPath);

await NativeMessageHost.RunAsync(
    Console.OpenStandardInput(),
    Console.OpenStandardOutput(),
    (request, cancellationToken) =>
        pipeClient.RelayAsync(request, cancellationToken));
