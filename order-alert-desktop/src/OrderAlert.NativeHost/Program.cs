using OrderAlert.Core.Messaging;

var applicationPath = Path.Combine(
    AppContext.BaseDirectory,
    "..",
    "app",
    "OrderAlert.App.exe");
var pipeClient = new NativeHostPipeClient(applicationPath);

await pipeClient.RunRelayAsync(
    Console.OpenStandardInput(),
    Console.OpenStandardOutput());
