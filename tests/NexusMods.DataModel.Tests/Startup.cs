using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Common;
using NexusMods.DataModel.Abstractions;
using NexusMods.DataModel.JsonConverters.ExpressionGenerator;
using NexusMods.DataModel.RateLimiting;
using NexusMods.FileExtractor;
using NexusMods.FileExtractor.Extractors;
using NexusMods.Paths;
using NexusMods.Paths.Utilities;
using NexusMods.StandardGameLocators;
using NexusMods.StandardGameLocators.TestHelpers;
using Xunit.DependencyInjection;
using Xunit.DependencyInjection.Logging;

namespace NexusMods.DataModel.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection container)
    {
        var prefix = FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory)
            .CombineUnchecked(typeof(Startup).FullName ?? "NexusMods.DataModel.Tests")
            .CombineUnchecked(Guid.NewGuid().ToString());

        container
            .AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace))
            .AddFileSystem()
            .AddSingleton<TemporaryFileManager>()
            .AddDataModel(new DataModelSettings(prefix))
            .AddStandardGameLocators(false)
            .AddFileExtractors()
            .AddStubbedGameLocators()
            .AddAllSingleton<IResource, IResource<ArchiveAnalyzer, Size>>(_ => new Resource<ArchiveAnalyzer, Size>("File Analysis"))
            .AddAllSingleton<IResource, IResource<IExtractor, Size>>(_ => new Resource<IExtractor, Size>("File Extraction"))
            //.AddSingleton<IFileAnalyzer, ArchiveContentsCacheTests.MutatingFileAnalyzer>()
            .AddSingleton<ITypeFinder>(_ => new AssemblyTypeFinder(typeof(Startup).Assembly))
            .Validate();
    }

    public void Configure(ILoggerFactory loggerFactory, ITestOutputHelperAccessor accessor) =>
        loggerFactory.AddProvider(new XunitTestOutputLoggerProvider(accessor, delegate { return true; }));
}

