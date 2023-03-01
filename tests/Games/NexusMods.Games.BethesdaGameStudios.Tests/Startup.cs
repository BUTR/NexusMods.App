﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Common;
using NexusMods.DataModel;
using NexusMods.DataModel.RateLimiting;
using NexusMods.FileExtractor;
using NexusMods.FileExtractor.Extractors;
using NexusMods.Paths;
using NexusMods.Paths.Utilities;
using NexusMods.StandardGameLocators;
using NexusMods.StandardGameLocators.TestHelpers;
using Xunit.DependencyInjection;
using Xunit.DependencyInjection.Logging;

namespace NexusMods.Games.BethesdaGameStudios.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection container)
    {
        container.AddUniversalGameLocator<SkyrimSpecialEdition>(new Version("1.6.659.0"));
        container.AddBethesdaGameStudios();
        
        container.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        container.AddDataModel(KnownFolders.EntryFolder.CombineUnchecked("DataModel").CombineUnchecked(Guid.NewGuid().ToString()));
        container.AddAllSingleton<IResource, IResource<FileContentsCache, Size>>(s =>
            new Resource<FileContentsCache, Size>("File Analysis"));
        container.AddAllSingleton<IResource, IResource<IExtractor, Size>>(s =>
            new Resource<IExtractor, Size>("File Extraction"));
        container.AddFileExtractors();

        container.AddSingleton<TemporaryFileManager>(s => 
            new TemporaryFileManager(KnownFolders.EntryFolder.CombineUnchecked("tempFiles")));
    }
    
    public void Configure(ILoggerFactory loggerFactory, ITestOutputHelperAccessor accessor) =>
        loggerFactory.AddProvider(new XunitTestOutputLoggerProvider(accessor, delegate { return true;}));
}

