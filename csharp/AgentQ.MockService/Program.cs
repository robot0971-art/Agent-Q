using AgentQ.MockService;

Console.WriteLine("Starting AgentQ Mock Anthropic Service...");

var service = new MockAnthropicService();
await service.StartAsync("http://localhost:18080/");

Console.WriteLine($"Mock service listening on {service.BaseUrl}");
Console.WriteLine("Press Ctrl+C to stop the service...");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });

await service.StopAsync();
Console.WriteLine("Service stopped.");

