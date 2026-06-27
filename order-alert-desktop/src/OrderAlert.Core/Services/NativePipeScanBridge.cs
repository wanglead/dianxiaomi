using System.Globalization;
using System.Text.Json;
using OrderAlert.Core.Messaging;
using OrderAlert.Core.Models;

namespace OrderAlert.Core.Services;

public sealed class NativePipeScanBridge : IScanBridge
{
    private readonly INativeCommandChannel _channel;

    public NativePipeScanBridge(INativeCommandChannel channel)
    {
        _channel = channel;
    }

    public async Task<ScanBatch> ScanAsync(
        StoreAccount account,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var orders = new Dictionary<string, OrderSnapshot>();
        var pageCount = 0;
        foreach (var url in urls)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var response = await _channel.SendAsync(
                new NativeEnvelope(
                    requestId,
                    "scanAccount",
                    JsonSerializer.SerializeToElement(new
                    {
                        url,
                        account = new
                        {
                            id = account.Id,
                            platform = account.Platform.ToString(),
                            displayName = account.DisplayName
                        }
                    }),
                    null),
                cancellationToken);
            if (response.Error is not null)
                throw new InvalidOperationException(
                    $"{response.Error.Code}: {response.Error.Message}");
            var payload = response.Payload
                ?? throw new InvalidDataException("Extension response has no payload.");
            if (!payload.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                var message = payload.TryGetProperty("error", out var error)
                    && error.TryGetProperty("message", out var errorMessage)
                        ? errorMessage.GetString()
                        : "Extension scan failed.";
                throw new InvalidOperationException(message);
            }

            var batch = payload.GetProperty("batch");
            if (!batch.GetProperty("complete").GetBoolean())
                throw new InvalidOperationException("Extension returned an incomplete scan.");
            pageCount += batch.GetProperty("pageCount").GetInt32();
            foreach (var item in batch.GetProperty("orders").EnumerateArray())
            {
                var orderId = item.GetProperty("orderId").GetString()
                    ?? throw new InvalidDataException("Order ID is missing.");
                var snapshot = new OrderSnapshot(
                    account.Platform,
                    account.Id,
                    orderId,
                    item.GetProperty("sourceUrl").GetString() ?? url,
                    GetString(item, "rawStatus") ?? "",
                    ParseDate(GetString(item, "assessmentAt")),
                    GetNullableInt(item, "shippingSeconds"),
                    DateTimeOffset.UtcNow);
                orders[snapshot.OrderKey] = snapshot;
            }
        }

        return new ScanBatch(
            account.Id,
            true,
            startedAt,
            DateTimeOffset.UtcNow,
            orders.Values.ToArray(),
            pageCount);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static int? GetNullableInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
}
