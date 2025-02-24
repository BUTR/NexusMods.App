using Avalonia.Platform.Storage;
using Humanizer;
using Humanizer.Bytes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Library;
using NexusMods.Abstractions.NexusModsLibrary.Models;
using NexusMods.App.UI.Extensions;
using NexusMods.App.UI.Overlays;
using NexusMods.CrossPlatform.Process;
using NexusMods.Extensions.BCL;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.TxFunctions;
using R3;
using ReactiveUI.Fody.Helpers;

namespace NexusMods.App.UI.Pages.CollectionDownload;

public interface IManualDownloadRequiredOverlayViewModel : IOverlayViewModel
{
    string DownloadName { get; }
    string ExpectedHash { get; }
    string ExpectedSize { get; }

    bool HasInstructions { get; }
    string Instructions { get; }

    bool IsCheckingFile { get; }
    bool IsIncorrectFile { get; }
    string ReceivedHash { get; }

    ReactiveCommand CommandCancel { get; }
    ReactiveCommand CommandOpenBrowser { get; }
    ReactiveCommand CommandAddFile { get; }
    ReactiveCommand CommandTryAgain { get; }
}

public class ManualDownloadRequiredOverlayViewModel : AOverlayViewModel<IManualDownloadRequiredOverlayViewModel>, IManualDownloadRequiredOverlayViewModel
{
    public ManualDownloadRequiredOverlayViewModel(IServiceProvider serviceProvider, CollectionDownloadExternal.ReadOnly downloadEntity)
    {
        var osInterop = serviceProvider.GetRequiredService<IOSInterop>();
        var avaloniaInterop = serviceProvider.GetRequiredService<IAvaloniaInterop>();
        var libraryService = serviceProvider.GetRequiredService<ILibraryService>();
        var connection = serviceProvider.GetRequiredService<IConnection>();
        var logger = serviceProvider.GetRequiredService<ILogger<ManualDownloadRequiredOverlayViewModel>>();

        DownloadName = downloadEntity.AsCollectionDownload().Name;
        ExpectedHash = downloadEntity.Md5.ToString();
        ExpectedSize = ByteSize.FromBytes(downloadEntity.Size.Value).Humanize();

        HasInstructions = downloadEntity.AsCollectionDownload().Instructions.HasValue;
        Instructions = downloadEntity.AsCollectionDownload().Instructions.ValueOr(string.Empty);

        CommandCancel = new ReactiveCommand(_ =>
        {
            base.Close();
        });

        CommandOpenBrowser = new ReactiveCommand(
            executeAsync: async (_, cancellationToken) =>
            {
                await osInterop.OpenUrl(downloadEntity.Uri, cancellationToken: cancellationToken);
            },
            awaitOperation: AwaitOperation.Parallel,
            configureAwait: false
        );

        CommandAddFile = new ReactiveCommand(
            executeAsync: async (_, _) =>
            {
                var paths = await avaloniaInterop.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"Browse file for download \"{downloadEntity.AsCollectionDownload().Name}\"",
                    AllowMultiple = false,
                });

                if (!paths.TryGetFirst(out var file)) return;

                IsCheckingFile = true;

                var localFile = await libraryService.AddLocalFile(file);
                var receivedHash = localFile.AsLibraryFile().Md5.Value;
                ReceivedHash = receivedHash.ToString();

                if (receivedHash == downloadEntity.Md5)
                {
                    logger.LogInformation("Received file with matching hash for download `{DownloadName}` (index={Index})", downloadEntity.AsCollectionDownload().Name, downloadEntity.AsCollectionDownload().ArrayIndex);
                    base.Close();
                    return;
                }

                logger.LogWarning("Received file with hash `{ActualHash}` that doesn't match expected hash of `{ExpectedHash}` for download `{DownloadName}` (index={Index})", receivedHash, downloadEntity.Md5, downloadEntity.AsCollectionDownload().Name, downloadEntity.AsCollectionDownload().ArrayIndex);

                IsCheckingFile = false;
                IsIncorrectFile = true;

                {
                    using var tx = connection.BeginTransaction();
                    tx.Delete(localFile, recursive: true);
                    await tx.Commit();
                }
            },
            awaitOperation: AwaitOperation.Drop,
            configureAwait: false
        );

        CommandTryAgain = new ReactiveCommand(_ =>
        {
            // reset
            IsCheckingFile = false;
            IsIncorrectFile = false;
            ReceivedHash = string.Empty;
        });
    }

    public string DownloadName { get; }
    public string ExpectedHash { get; }
    public string ExpectedSize { get; }

    public bool HasInstructions { get; }
    public string Instructions { get; }

    [Reactive] public bool IsCheckingFile { get; private set; }
    [Reactive] public bool IsIncorrectFile { get; private set; }
    [Reactive] public string ReceivedHash { get; private set; } = string.Empty;

    public ReactiveCommand CommandCancel { get; }
    public ReactiveCommand CommandOpenBrowser { get; }
    public ReactiveCommand CommandAddFile { get; }
    public ReactiveCommand CommandTryAgain { get; }
}

