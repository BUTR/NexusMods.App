using TransparentValueObjects;

namespace NexusMods.Networking.NexusWebApi.Types;

/// <summary>
/// Unique identifier for a given site user.
/// </summary>
[ValueObject<ulong>]
public readonly partial struct UserId : IAugmentWith<DefaultValueAugment>
{
    /// <inheritdoc/>
    public static UserId DefaultValue => From(default);
}
