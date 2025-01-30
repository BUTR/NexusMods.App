using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.NexusModsLibrary;
using NexusMods.Abstractions.NexusWebApi;
using NexusMods.Abstractions.NexusWebApi.Types;
using NexusMods.Abstractions.NexusWebApi.Types.V2.Uid;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Networking.ModUpdates;
using NexusMods.Networking.ModUpdates.Mixins;
using NexusMods.Networking.ModUpdates.Traits;
using NexusMods.Networking.NexusWebApi.Extensions;
using StrawberryShake;
namespace NexusMods.Networking.NexusWebApi;

/// <summary>
/// Utility class that encapsulates the logic for running the actual update check.
/// </summary>
public static class RunUpdateCheck
{
    /// <summary>
    /// Identifies all mod pages whose information needs refreshed.
    /// </summary>
    public static async Task<PerFeedCacheUpdaterResult<PageMetadataMixin>> CheckForModPagesWhichNeedUpdating(IDb db, INexusApiClient apiClient, IGameDomainToGameIdMappingCache mappingCache)
    {
        // Extract all GameDomain(s)
        var modPages = PageMetadataMixin.EnumerateDatabaseEntries(db).ToArray();
        var gameIds = modPages.Select(x => (x.GetModPageId().GameId)).Distinct().ToArray();
        
        // Note: The v1Timespan accounts for 1 month minus 5 minutes
        //  - We use 28 days because February is the shortest month at 28.
        //  - Serverside caches for 5 minutes, so we subtract that. 
        var v1Timespan = TimeSpan.FromDays(28).Subtract(TimeSpan.FromMinutes(5));
        var updater = new MultiFeedCacheUpdater<PageMetadataMixin>(modPages, v1Timespan);

        foreach (var gameId in gameIds)
        {
            // Note (sewer): We need to update to V2 stat.
            var gameDomain = (await mappingCache.TryGetDomainAsync(gameId, CancellationToken.None)).Value.Value;
            var modUpdates = await apiClient.ModUpdatesAsync(gameDomain, PastTime.Month);
            var updateResults = ModFeedItemUpdateMixin.FromUpdateResults(modUpdates.Data, gameId);
            updater.Update(updateResults);
        }
        
        return updater.BuildFlattened(v1Timespan);
    }

    /// <summary>
    /// Updates the metadata for mod pages returned from the <see cref="CheckForModPagesWhichNeedUpdating"/> API call.
    /// </summary>
    public static async Task UpdateModFilesForOutdatedPages(IDb db, ITransaction tx, ILogger logger, INexusGraphQLClient gqlClient, PerFeedCacheUpdaterResult<PageMetadataMixin> result, CancellationToken cancellationToken)
    {
        // Note(sewer): Undetermined items may be removed items from the site; or
        // caused by programmer error, so we should log these whenever possible,
        // but they should not cause a critical error; in case it's simply the result
        // of mod removal such as DMCA takedown.
        using var semaphore = new SemaphoreSlim(16);
        var tasks = new List<Task>();

        // Helper function to process a single mixin with error handling
        async Task ProcessMixin(PageMetadataMixin mixin, bool isUndetermined)
        {
            try
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    await UpdateModPage(db, tx, gqlClient, cancellationToken, mixin);
                }
                finally
                {
                    semaphore.Release();
                }
            }
            catch (Exception e)
            {
                if (isUndetermined)
                {
                    var id = mixin.GetModPageId();
                    logger.LogError(e, "Failed to update metadata for Mod (GameID: {Page}, ModId: {ModId})", id.GameId, id.ModId);
                }
                else
                {
                    // Rethrow for non-undetermined items as these should be truly exceptional
                    throw;
                }
            }
        }

        // Process undetermined items
        foreach (var mixin in result.UndeterminedItems)
        {
            tasks.Add(ProcessMixin(mixin, true));
        }

        // Process out of date items
        foreach (var mixin in result.OutOfDateItems)
        {
            tasks.Add(ProcessMixin(mixin, false));
        }

        // Wait for all tasks to complete
        await Task.WhenAll(tasks);
    }

    private static async Task UpdateModPage(IDb db, ITransaction tx, INexusGraphQLClient gqlClient, CancellationToken cancellationToken, PageMetadataMixin mixin)
    {
        var uid = mixin.GetModPageId();
        var modIdString = uid.ModId.Value.ToString();
        var gameIdString = uid.GameId.Value.ToString();
        
        // Update Mod
        var modInfo = await gqlClient.ModInfo.ExecuteAsync((int)uid.GameId.Value, (int)uid.ModId.Value, cancellationToken);
        foreach (var node in modInfo.Data!.LegacyMods.Nodes)
            node.Resolve(db, tx);
        
        // Update Mod Files
        var filesByUid = await gqlClient.ModFiles.ExecuteAsync(modIdString, gameIdString, cancellationToken);
        filesByUid.EnsureNoErrors();

        var pageEntityId = mixin.GetModPageEntityId();
        foreach (var node in filesByUid.Data!.ModFiles)
            node.Resolve(db, tx, pageEntityId);
    }

    /// <summary>
    /// Returns all files which have a 'newer' date than the current one.
    /// </summary>
    /// <remarks>
    ///     The returned items are returned in descending order, from newest to oldest.
    /// </remarks>
    public static IEnumerable<NexusModsFileMetadata.ReadOnly> GetNewerFilesForExistingFile(IDb db, UidForFile uid)
    {
        var metadata = NexusModsFileMetadata.FindByUid(db, uid).First();
        return GetNewerFilesForExistingFile(metadata);
    } 
    
    /// <summary>
    /// Returns all files which have a 'newer' date than the current one.
    /// </summary>
    /// <remarks>
    ///     The returned items are returned in descending order, from newest to oldest.
    /// </remarks>
    public static IEnumerable<NexusModsFileMetadata.ReadOnly> GetNewerFilesForExistingFile(NexusModsFileMetadata.ReadOnly file) 
        => GetNewerFilesForExistingFile(new ModFileMetadataMixin(file)).Select(x => ((ModFileMetadataMixin)x).Metadata);

    /// <summary>
    /// Returns all files which have a 'newer' date than the current one,
    /// using fuzzy name matching to handle variations in file naming.
    /// </summary>
    /// <remarks>
    ///     The returned items are returned in descending order, from newest to oldest.
    /// </remarks>
    public static IEnumerable<IAmAModFile> GetNewerFilesForExistingFile(IAmAModFile file)
    {
        // Get the normalized name for the current file
        var normalizedName = FuzzySearch.NormalizeFileName(file.Name, file.Version);
        
        // Get all other files from the same mod page
        return file.OtherFilesInSameModPage
            .Where(otherFile => 
                // Must be uploaded later
                // Note(sewer):
                //
                //     In future we might check version too, but this may
                //     be a bit unreliable in cases where versions have
                //     a suffix and non-suffix variants. So these items would
                //     need to be put into a different group/pool to match from.
                //
                //     Because, when mapped to SemVer, the suffix would be
                //     interpreted as a pre-release, and an item without a suffix
                //     may be assumed as a more recent version.
                otherFile.UploadedAt > file.UploadedAt &&
                // Must have matching normalized name
                FuzzySearch.NormalizeFileName(otherFile.Name, otherFile.Version)
                    .Equals(normalizedName, StringComparison.OrdinalIgnoreCase)
            )
            .OrderByDescending(x => x.UploadedAt);
    }
}
