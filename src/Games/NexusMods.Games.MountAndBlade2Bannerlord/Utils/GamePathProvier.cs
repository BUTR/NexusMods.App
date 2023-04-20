using Bannerlord.LauncherManager;
using NexusMods.Paths;
using NexusMods.Paths.Extensions;
using GameStore = NexusMods.DataModel.Games.GameStore;

namespace NexusMods.Games.MountAndBlade2Bannerlord.Utils;

public static class GamePathProvier
{
    private static string GetConfiguration(GameStore store) =>
        LauncherManagerHandler.GetConfigurationByPlatform(LauncherManagerHandler.FromStore(Converter.ToGameStoreTW(store)));
    
    public static GamePath PrimaryLauncherFile(GameStore store) =>
        new(GameFolderType.Game, Path.Combine("bin", GetConfiguration(store)).ToRelativePath().Join("TaleWorlds.MountAndBlade.Launcher.exe"));
    public static GamePath PrimaryXboxLauncherFile(GameStore store) =>
        new(GameFolderType.Game, Path.Combine("bin", GetConfiguration(store)).ToRelativePath().Join("Launcher.Native.exe"));
    public static GamePath PrimaryStandaloneFile(GameStore store) =>
        new(GameFolderType.Game, Path.Combine("bin", GetConfiguration(store)).ToRelativePath().Join(Constants.BannerlordExecutable));

    public static GamePath BLSEStandaloneFile(GameStore store) =>
        new(GameFolderType.Game, Path.Combine("bin", GetConfiguration(store)).ToRelativePath().Join("Bannerlord.BLSE.Standalone.exe"));
    public static GamePath BLSELauncherFile(GameStore store) =>
        new(GameFolderType.Game, Path.Combine("bin", GetConfiguration(store)).ToRelativePath().Join("Bannerlord.BLSE.Launcher.exe"));

    public static GamePath BLSELauncherExFile(GameStore store) =>
        new(GameFolderType.Game, Path.Combine("bin", GetConfiguration(store)).ToRelativePath().Join("Bannerlord.BLSE.LauncherEx.exe"));
}
