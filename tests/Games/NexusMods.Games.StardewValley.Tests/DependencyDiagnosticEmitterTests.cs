using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Diagnostics;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Abstractions.NexusWebApi.Types.V2;
using NexusMods.Games.StardewValley.Emitters;
using NexusMods.Games.TestFramework;
using NexusMods.MnemonicDB.Abstractions.ElementComparers;
using NexusMods.StandardGameLocators.TestHelpers;
using Xunit.Abstractions;

namespace NexusMods.Games.StardewValley.Tests;

[Trait("RequiresNetworking", "True")]
public class DependencyDiagnosticEmitterTests : ALoadoutDiagnosticEmitterTest<DependencyDiagnosticEmitterTests, StardewValley, DependencyDiagnosticEmitter>
{
    public DependencyDiagnosticEmitterTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper) { }

    protected override IServiceCollection AddServices(IServiceCollection services)
    {
        return base.AddServices(services)
            .AddStardewValley()
            .AddUniversalGameLocator<StardewValley>(new Version("1.6.14"));
    }

    [Fact]
    public async Task Test_MissingRequiredDependency()
    {
        var loadout = await CreateLoadout();

        // SMAPI 4.1.10 (https://www.nexusmods.com/stardewvalley/mods/2400?tab=files)
        await InstallModFromNexusMods(loadout, ModId.From(2400), FileId.From(119630));

        // Farm Type Manager 1.24.0 (https://www.nexusmods.com/stardewvalley/mods/3231?tab=files)
        await InstallModFromNexusMods(loadout, ModId.From(3231), FileId.From(117244));

        var diagnostic = await GetSingleDiagnostic(loadout);
        var missingRequiredDependencyMessageData = diagnostic.Should().BeOfType<Diagnostic<Diagnostics.MissingRequiredDependencyMessageData>>(because: "Content Patcher is required for Farm Type Manager").Which.MessageData;
        missingRequiredDependencyMessageData.MissingDependencyModId.Should().Be("Pathoschild.ContentPatcher", because: "Farm Type Manager requires Content Patcher");

        // Content Patcher 2.5.3 (https://www.nexusmods.com/stardewvalley/mods/1915?tab=files)
        await InstallModFromNexusMods(loadout, ModId.From(1915), FileId.From(124659));

        await ShouldHaveNoDiagnostics(loadout, because: "The required dependency Content Patcher has been installed");
    }

    [Fact]
    public async Task Test_DisabledRequiredDependencyMessageData()
    {
        var loadout = await CreateLoadout();

        // SMAPI 4.1.10 (https://www.nexusmods.com/stardewvalley/mods/2400?tab=files)
        await InstallModFromNexusMods(loadout, ModId.From(2400), FileId.From(119630));

        // Farm Type Manager 1.24.0 (https://www.nexusmods.com/stardewvalley/mods/3231?tab=files)
        var farmTypeManager = await InstallModFromNexusMods(loadout, ModId.From(3231), FileId.From(117244));

        // Content Patcher 2.5.3 (https://www.nexusmods.com/stardewvalley/mods/1915?tab=files)
        var contentPatcher = await InstallModFromNexusMods(loadout, ModId.From(1915), FileId.From(124659));

        await ShouldHaveNoDiagnostics(loadout, because: "All required dependencies are installed and enabled");

        {
            using var tx = Connection.BeginTransaction();
            tx.Add(contentPatcher.Id, LoadoutItem.Disabled, Null.Instance);
            await tx.Commit();
        }

        var diagnostic = await GetSingleDiagnostic(loadout);
        var disabledRequiredDependencyMessageData = diagnostic.Should().BeOfType<Diagnostic<Diagnostics.DisabledRequiredDependencyMessageData>>(because: "Content Patcher is disabled and required by Farm Type Manager").Which.MessageData;

        var dependency = disabledRequiredDependencyMessageData.Dependency.ResolveData(ServiceProvider, Connection);
        dependency.AsLoadoutItem().ParentId.Should().Be(contentPatcher.LoadoutItemGroupId, because: "Content Patcher is the dependency");

        var dependent = disabledRequiredDependencyMessageData.SMAPIMod.ResolveData(ServiceProvider, Connection);
        dependent.AsLoadoutItem().ParentId.Should().Be(farmTypeManager.LoadoutItemGroupId, because: "Farm Type Manager is the dependent");
    }

    [Fact]
    public async Task Test_RequiredDependencyIsOutdated()
    {
        var loadout = await CreateLoadout();

        // SMAPI 4.1.10 (https://www.nexusmods.com/stardewvalley/mods/2400?tab=files)
        await InstallModFromNexusMods(loadout, ModId.From(2400), FileId.From(119630));

        // Farm Type Manager 1.24.0 (https://www.nexusmods.com/stardewvalley/mods/3231?tab=files)
        var farmTypeManager = await InstallModFromNexusMods(loadout, ModId.From(3231), FileId.From(117244));

        // Content Patcher 1.30.4 (https://www.nexusmods.com/stardewvalley/mods/1915?tab=files)
        var contentPatcher = await InstallModFromNexusMods(loadout, ModId.From(1915), FileId.From(78987));

        var diagnostic = await GetSingleDiagnostic(loadout);
        var requiredDependencyIsOutdatedMessageData = diagnostic.Should().BeOfType<Diagnostic<Diagnostics.RequiredDependencyIsOutdatedMessageData>>(because: "Content Patcher is outdated and required by Farm Type Manager").Which.MessageData;

        requiredDependencyIsOutdatedMessageData.CurrentVersion.Should().Be("1.30.4", because: "Content Patcher 1.30.4 was installed");
        requiredDependencyIsOutdatedMessageData.MinimumVersion.Should().Be("2.0.0", because: "Farm Type Manager 1.24.0 requires at least version 2.0.0 of Content Patcher");

        var dependency = requiredDependencyIsOutdatedMessageData.Dependency.ResolveData(ServiceProvider, Connection);
        dependency.AsLoadoutItem().ParentId.Should().Be(contentPatcher.LoadoutItemGroupId, because: "Content Patcher is the dependency");

        var dependent = requiredDependencyIsOutdatedMessageData.Dependent.ResolveData(ServiceProvider, Connection);
        dependent.AsLoadoutItem().ParentId.Should().Be(farmTypeManager.LoadoutItemGroupId, because: "Farm Type Manager is the dependent");
    }
}
