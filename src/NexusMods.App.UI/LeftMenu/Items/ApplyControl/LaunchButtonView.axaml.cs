using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.ReactiveUI;
using ReactiveUI;

namespace NexusMods.App.UI.LeftMenu.Items;

public partial class LaunchButtonView : ReactiveUserControl<ILaunchButtonViewModel>
{
    public LaunchButtonView()
    {
        InitializeComponent();
        
        this.WhenActivated(d =>
        {
            var isRunning = ViewModel!.IsRunningObservable;
            var isNotRunning = ViewModel!.IsRunningObservable.Select(running => !running);
            
            // Hide launch button when running
            // isNotRunning.BindToUi(this, view => view.LaunchButton.IsVisible)
            //     .DisposeWith(d);

            isNotRunning.BindToUi(this, view => view.LaunchButton.IsEnabled)
                .DisposeWith(d);
            
            // Show progress bar when running
            isRunning.BindToUi(this, view => view.LaunchSpinner.IsVisible)
                .DisposeWith(d);
            
            // Show progress bar when running
            isNotRunning.BindToUi(this, view => view.LaunchIcon.IsVisible)
                .DisposeWith(d);
            
            // bind the launch button
            this.OneWayBind(ViewModel, vm => vm.Command, v => v.LaunchButton.Command)
                .DisposeWith(d);

            // Set the 'play' / 'running' text.
            this.OneWayBind(ViewModel, vm => vm.Label, v => v.LaunchText.Text)
                .DisposeWith(d);

        });
    }
}

