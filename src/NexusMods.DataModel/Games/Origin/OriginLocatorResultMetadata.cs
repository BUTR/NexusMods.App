using JetBrains.Annotations;

namespace NexusMods.DataModel.Games;

/// <summary>
/// Metadata for games found that implement <see cref="IOriginGame"/>.
/// </summary>
[PublicAPI]
public record OriginLocatorResultMetadata : IGameLocatorResultMetadata
{
    /// <summary>
    /// The specific ID of the found game.
    /// </summary>
    public required string Id { get; init; }
}
