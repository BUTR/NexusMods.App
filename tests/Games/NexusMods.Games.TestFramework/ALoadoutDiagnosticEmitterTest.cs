using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Diagnostics;
using NexusMods.Abstractions.Diagnostics.Emitters;
using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Loadouts;
using Xunit.Abstractions;

namespace NexusMods.Games.TestFramework;

public class ALoadoutDiagnosticEmitterTest<TTest, TGame, TEmitter> : AIsolatedGameTest<TTest, TGame>
    where TGame : AGame
    where TEmitter : ILoadoutDiagnosticEmitter
{
    protected readonly TEmitter Emitter;

    protected ALoadoutDiagnosticEmitterTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        Emitter = ServiceProvider.FindImplementationInContainer<TEmitter, ILoadoutDiagnosticEmitter>();
    }

    protected async ValueTask<Diagnostic[]> GetAllDiagnostics(LoadoutId loadoutId)
    {
        var loadout = Loadout.Load(Connection.Db, loadoutId);
        return await Emitter.Diagnose(loadout, CancellationToken.None).ToArrayAsync();
    }

    protected async ValueTask<Diagnostic> GetSingleDiagnostic(LoadoutId loadoutId)
    {
        var loadout = Loadout.Load(Connection.Db, loadoutId);
        var diagnostics = await GetAllDiagnostics(loadout);
        diagnostics.Should().ContainSingle();
        return diagnostics.First();
    }

    protected async ValueTask ShouldHaveNoDiagnostics(LoadoutId loadoutId, string because = "")
    {
        var loadout = Loadout.Load(Connection.Db, loadoutId);
        var diagnostics = await Emitter.Diagnose(loadout, CancellationToken.None).ToArrayAsync();
        diagnostics.Should().BeEmpty(because: because);
    }
}
