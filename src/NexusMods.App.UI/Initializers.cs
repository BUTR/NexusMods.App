using System.Collections.ObjectModel;
using System.Reactive;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NexusMods.App.UI.LeftMenu;
using NexusMods.App.UI.LeftMenu.Home;
using NexusMods.App.UI.RightContent;
using ReactiveUI;
// ReSharper disable InconsistentNaming

namespace NexusMods.App.UI;

public static class Initializers
{
    public static readonly ICommand ICommand = ReactiveCommand.Create(() => { });
    public static readonly IImage IImage = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
    public static readonly ILeftMenuViewModel ILeftMenuViewModel = new HomeLeftMenuDesignViewModel();
    public static readonly IRightContentViewModel IRightContent = new PlaceholderDesignViewModel();

    public static readonly ReactiveCommand<Unit, Unit> ReactiveCommandUnitUnit =
        ReactiveCommand.Create(() => { });

    public static ReadOnlyObservableCollection<T> ReadOnlyObservableCollection<T>()
    {
        return new ReadOnlyObservableCollection<T>(new ObservableCollection<T>());
    }
}
