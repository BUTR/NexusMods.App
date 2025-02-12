using DynamicData;
using DynamicData.Aggregation;
using DynamicData.Kernel;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.NexusModsLibrary;
using NexusMods.Abstractions.NexusWebApi;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.Query;
using NexusMods.Networking.ModUpdates;
using NexusMods.Networking.ModUpdates.Mixins;
namespace NexusMods.Networking.NexusWebApi;

/// <summary>
///     Provides services related to updating of mods.
///     Namely updating mod page info, and beaming update notifications to relevant receivers.
/// </summary>
public class ModUpdateService : IModUpdateService, IDisposable
{
    private readonly IConnection _connection;
    private readonly INexusApiClient _nexusApiClient;
    private readonly IGameDomainToGameIdMappingCache _gameIdMappingCache;
    private readonly ILogger<ModUpdateService> _logger;
    private readonly NexusGraphQLClient _gqlClient;
    
    // Use SourceCache to maintain latest values per key
    private readonly SourceCache<KeyValuePair<NexusModsFileMetadataId, ModUpdateOnPage>, EntityId> _newestModVersionCache = new (static kv => kv.Key);
    private readonly SourceCache<KeyValuePair<NexusModsModPageMetadataId, ModUpdatesOnModPage>, EntityId> _newestModOnAnyPageCache = new (static kv => kv.Key);
    private readonly IDisposable _updateObserver;

    /// <summary/>
    public ModUpdateService(
        IConnection connection,
        INexusApiClient nexusApiClient,
        IGameDomainToGameIdMappingCache gameIdMappingCache,
        ILogger<ModUpdateService> logger,
        NexusGraphQLClient gqlClient)
    {
        _connection = connection;
        _nexusApiClient = nexusApiClient;
        _gameIdMappingCache = gameIdMappingCache;
        _logger = logger;
        _gqlClient = gqlClient;
        // Note(sewer): This is a singleton, so we don't actually need to dispose, that said
        // I'm opting to for the sake of following good practices.
        _updateObserver = ObserveUpdates();
    }

    private IDisposable ObserveUpdates()
    {
        return NexusModsLibraryItem.ObserveAll(_connection)
            .Subscribe(changes =>
            {
                // Note(sewer): I'm not particularly happy about this, as this
                // fires once at startup. DynamicData however has no API to
                // determine if there are any listeners. If we could determine
                // that, we could no-op here, which would be useful if the user
                // doesn't have a listener like the library page open.
                //
                // We could PR that in, it's just exposing the `_changes` field
                // from the inner ObservableCache.
                //
                // For now I just wrote an optimized routine that only updates
                // the parts that concern us.

                // Note(sewer): Due to the nature of how MnemonicDB works
                // (snapshots et al.), we can access the details of the items
                // that were removed as the `ReadOnly` objects we get were part
                // of a snapshot/point in time where they were still available.
                //
                // This is convenient, because we extract all the mod pages that
                // were affected, and scope the update notifications to just
                // those, meaning fast operations in the presence of 1000s of mods.
                //
                // Any changes in individual mods may ripple through onto mod pages,
                // e.g. if you install a new mod with the newest version, other
                // existing file(s) [different versions] are now 'up to date',
                // even they themselves not part of the actual changeset in any
                // way.
                //
                // In the App, we only ripple down to the mod pages, right now,
                // as far as these details go, therefore we will update isolated
                // to the level of all mod pages affected by the changeset.
                // This should make for a 'fast' update.
                
                // We grab entityID(s) because we want to query the DB for the
                // latest info of the affected mod pages, not a possible snapshot.
                var affectedModPage = new List<EntityId>();
                
                // Accept all events; Add, Update, Remove, Refresh, Reset
                foreach (var change in changes)
                    affectedModPage.Add(change.Current.ModPageMetadataId);
                
                NotifyForUpdatesOfSpecificModPages(affectedModPage);
            });
    }

    /// <inheritdoc />
    public async Task<PerFeedCacheUpdaterResult<PageMetadataMixin>> CheckAndUpdateModPages(CancellationToken token, bool notify = true)
    {
        // Identify all mod pages needing a refresh
        var updateCheckResult = await RunUpdateCheck.CheckForModPagesWhichNeedUpdating(
            _connection.Db, 
            _nexusApiClient, 
            _gameIdMappingCache);
        
        // Fetch the data from the Nexus servers if at least a single item needs updating.
        if (updateCheckResult.AnyItemNeedsUpdate())
        {
            using var tx = _connection.BeginTransaction();
            await RunUpdateCheck.UpdateModFilesForOutdatedPages(
                _connection.Db,
                tx,
                _logger,
                _gqlClient,
                updateCheckResult,
                token);
            await tx.Commit();
        }
        
        if (notify)
            NotifyForUpdates();

        return updateCheckResult;
    }

    /// <inheritdoc />
    public void NotifyForUpdates()
    {
        // Check for updates of all files in library.
        var filesInLibrary = NexusModsLibraryItem
            .All(_connection.Db)
            .Select(static libraryItem => libraryItem.FileMetadata)
            .DistinctBy(static fileMetadata => fileMetadata.Id)
            .ToDictionary(static x => x.Id, static x => x);

        NotifyForUpdatesOfSpecificFiles(filesInLibrary);
    }
    
    private void NotifyForUpdatesOfSpecificFiles(Dictionary<EntityId, NexusModsFileMetadata.ReadOnly> filesInLibrary)
    {
        var existingFileToNewerFiles = filesInLibrary
            .Select(kv =>
            {
                var newerFiles = RunUpdateCheck
                    .GetNewerFilesForExistingFile(kv.Value)
                    // `!filesInLibrary.ContainsKey(newFile.Id)`: This filters out the case where we already have the latest file version on disk. 
                    .Where(newFile => newFile.IsValid() && !filesInLibrary.ContainsKey(newFile.Id))
                    .ToArray();

                return new ModUpdateOnPage(kv.Value, newerFiles);
            })
            .Where(static kv => kv.NewerFiles.Length > 0)
            .ToDictionary(page => page.File.Id);

        UpdateNewestModVersionCache(existingFileToNewerFiles);

        var modPageToNewerFiles = existingFileToNewerFiles
            .GroupBy(
                kv => kv.Value.File.ModPageId, // 👈 mod page ID, NOT entity id.
                kv => kv.Value
            )
            .ToDictionary(
                group => group.Key,
                group => (ModUpdatesOnModPage)group.ToArray()
            );

        UpdateNewestModOnAnyPageCache(modPageToNewerFiles);
    }

    /// <inheritdoc />
    public IObservable<Optional<ModUpdateOnPage>> GetNewestFileVersionObservable(NexusModsFileMetadata.ReadOnly current)
    {
        return _newestModVersionCache.Connect()
            .Transform(kv => kv.Value)
            .QueryWhenChanged(query => query.Lookup(current.Id));
    }

    /// <inheritdoc />
    public IObservable<Optional<ModUpdatesOnModPage>> GetNewestModPageVersionObservable(NexusModsModPageMetadata.ReadOnly current)
    {
        return _newestModOnAnyPageCache.Connect()
            .Transform(kv => kv.Value)
            .QueryWhenChanged(query => query.Lookup(current.Id));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _newestModVersionCache.Dispose();
        _newestModOnAnyPageCache.Dispose();
        _updateObserver.Dispose();
    }
    
    private void UpdateNewestModVersionCache(Dictionary<EntityId, ModUpdateOnPage> existingFileToNewerFiles)
    {
        // First remove any invalid files, and then add any newer files.
        _newestModVersionCache.Edit(updater =>
        {
            // Remove any currently known files from _newestModVersionCache
            // that no longer require to be updated.
            foreach (var existingKey in updater.Keys)
            {
                if (!existingFileToNewerFiles.ContainsKey(existingKey))
                    updater.Remove(existingKey);
            }
            
            // Add any newer files.
            foreach (var kv in existingFileToNewerFiles)
                updater.AddOrUpdate(new KeyValuePair<NexusModsFileMetadataId, ModUpdateOnPage>(kv.Key, kv.Value));
        });
    }
    
    private void UpdateNewestModOnAnyPageCache(Dictionary<NexusModsModPageMetadataId, ModUpdatesOnModPage> modPageToNewerFiles)
    {
        // First remove any invalid mod pages, and then add any newer files.
        _newestModOnAnyPageCache.Edit(updater =>
        {
            // Remove any currently known mod pages from _newestModOnAnyPageCache
            // that no longer require to be updated.
            foreach (var existingKey in updater.Keys)
            {
                if (!modPageToNewerFiles.ContainsKey(existingKey))
                    updater.Remove(existingKey);
            }
            
            // Add any newer files.
            foreach (var kv in modPageToNewerFiles)
                updater.AddOrUpdate(kv);
        });
    }

    private void NotifyForUpdatesOfSpecificModPages(List<EntityId> modPageIds)
    {
        // Note(sewer): Change to generic inheriting IEnumerable if ever making this public.
        var filesInLibrary = new Dictionary<EntityId, NexusModsFileMetadata.ReadOnly>();
        var db = _connection.Db;
        
        // Anyway, just grab all mods on a given mod page that we have in the library.
        foreach (var pageId in modPageIds)
        {
            var page = NexusModsModPageMetadata.Load(db, pageId);
            
            // Note(sewer): This can be slow-ish if a mod page has many files.
            // Like SMAPI. But those are very few and far between in practice.
            // When we have the Query System, it may be worth changing this to
            // a more optimal query, because what we want may be expressed
            // as a nicer query.
            foreach (var file in page.Files)
            {
                // Not all files we store will have an associated library file,
                // we actually fetch info for entire mod pages when downloading
                // an item from a mod page.

                // Only add a file as one that we have if an associated library file,
                // exists.
                if (file.LibraryFiles.Count > 0) // Note(sewer): Avoiding IEnumerable to not allocate on Gen0
                    filesInLibrary.Add(file.Id, file);
            }
        }

        // And now beam the update stuff, brrr!!
        NotifyForUpdatesOfSpecificFiles(filesInLibrary);
    }
}

/// <summary>
/// Marks all the files on a mod page that are newer than the current ones.
/// </summary>
/// <param name="FileMappings">
/// An array, where each entry is a mapping of a single file (currently in the library) to its newer versions
/// available on `nexusmods.com`.
/// </param>
public readonly record struct ModUpdatesOnModPage(ModUpdateOnPage[] FileMappings)
{
    /// <summary>
    /// This returns the number of mod files to update on this page.
    /// Given that each array entry represents a single mod file, this is just the count of the internal array.
    /// </summary>
    public int NumberOfModFilesToUpdate => FileMappings.Length;
    
    /// <summary>
    /// Returns the newest file across mods on this mod page.
    /// </summary>
    public NexusModsFileMetadata.ReadOnly NewestFile()
    {
        // Note(sewer): This matches the behaviour established in the design for
        // the mod update feature. The row should show the details of the newest mod.
        // In our case, we simply need to select the most recent file across all mods
        // within this page. In practice, there's usually only one mod, but there can
        // sometimes be more in some rare cases.
        
        // Compare the newest file in all `FileMappings` and return most recent one
        // (without LINQ, avoid alloc, since every mod row will touch this code in UI).
        var newestFile = FileMappings[0].NewerFiles[0];
        for (var x = 1; x < FileMappings.Length; x++)
        {
            var newerFile = FileMappings[x].NewerFiles[0];
            if (newerFile.UploadedAt > newestFile.UploadedAt)
                newestFile = newerFile;
        }
        
        return newestFile;
    }
    
    /// <summary>
    /// Returns the newest file from every mod on this page
    /// </summary>
    public IEnumerable<NexusModsFileMetadata.ReadOnly> NewestFileForEachPage() => FileMappings.Select(x => x.NewestFile);

    /// <summary/>
    public static explicit operator ModUpdatesOnModPage(ModUpdateOnPage[] inner) => new(inner);
    
    /// <summary/>
    public static explicit operator ModUpdatesOnModPage(ModUpdateOnPage inner) => new([inner]); // promote single item to array.
}

/// <summary>
/// Represents a mapping of a single file (currently in the library) to its newer versions
/// available on `nexusmods.com`.
/// </summary>
/// <param name="File">Unique identifier for the file that's currently in the library.</param>
/// <param name="NewerFiles">Newer files for this specific library file.</param>
public readonly record struct ModUpdateOnPage(NexusModsFileMetadata.ReadOnly File, NexusModsFileMetadata.ReadOnly[] NewerFiles)
{
    /// <summary>
    /// The newest update file for this individual mod on a mod page.
    /// </summary>
    public NexusModsFileMetadata.ReadOnly NewestFile => NewerFiles[0];
    
    /// <summary/>
    public static implicit operator ModUpdateOnPage(KeyValuePair<NexusModsFileMetadata.ReadOnly, NexusModsFileMetadata.ReadOnly[]> kv) => new(kv.Key, kv.Value);
    /// <summary/>
    public static implicit operator KeyValuePair<NexusModsFileMetadata.ReadOnly, NexusModsFileMetadata.ReadOnly[]>(ModUpdateOnPage modUpdateOnPage) => new(modUpdateOnPage.File, modUpdateOnPage.NewerFiles);
}
