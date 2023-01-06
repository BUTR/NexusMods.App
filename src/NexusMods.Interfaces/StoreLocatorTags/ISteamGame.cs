﻿using NexusMods.Interfaces.Components;

namespace NexusMods.Interfaces.StoreLocatorTags;

/// <summary>
/// Marker interface for the steam store locator.
/// </summary>
public interface ISteamGame : IGame
{
    /// <summary>
    /// Returns one or more steam app ids for the game.
    /// </summary>
    IEnumerable<int> SteamIds { get; }
}