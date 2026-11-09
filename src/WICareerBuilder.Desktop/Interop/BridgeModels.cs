using System.Text.Json;

namespace WICareerBuilder.Desktop.Interop;

public sealed class BridgeRequest
{
    public string Type { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public JsonElement Payload { get; set; }
}

public sealed record BridgeResponse(string Type, string RequestId, bool Ok, object? Data = null, string? Error = null);
