using Avalonia.Media;
using NexusMods.App.UI.Windows;
using NexusMods.App.UI.WorkspaceSystem;
using ReactiveUI.Fody.Helpers;

namespace NexusMods.App.UI.Controls;

public class DummyViewModel : APageViewModel<IDummyViewModel>, IDummyViewModel
{
    [Reactive] public Color Color { get; set; } = Colors.Aqua;

    public DummyViewModel(IWindowManager windowManager) : base(windowManager)
    {
        TabTitle = "Dummy";
    }
}
