namespace Habitat.Models;

public sealed record OutgoingMessage(string Id, string Text);

public sealed record BridgeEvent(string Type, string? Id, string? Text, string? Model);
