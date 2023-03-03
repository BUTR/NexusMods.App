﻿using System.Diagnostics;
using System.Reactive.Linq;
using Avalonia.Controls.Mixins;
using Avalonia.Input;
using Avalonia.ReactiveUI;
using Avalonia.VisualTree;
using Noggog;
using ReactiveUI;
using Image = Avalonia.Controls.Image;

namespace NexusMods.App.UI.Controls.TopBar;

public partial class TopBarView : ReactiveUserControl<ITopBarViewModel>
{
    public TopBarView()
    {
        InitializeComponent();
        
        this.WhenActivated(d =>
        {
            this.BindCommand(ViewModel, vm => vm.LoginCommand, v => v.LoginButton)
                .DisposeWith(d);
            
            this.BindCommand(ViewModel, vm => vm.LogoutCommand, view => view.UserButton)
                .DisposeWith(d);
            
            this.WhenAnyValue(v => v.ViewModel.IsLoggedIn)
                .Select(v => !v)
                .BindToUI(this, v => v.LoginButton.IsVisible)
                .DisposeWith(d);
            
            this.WhenAnyValue(v => v.ViewModel.IsLoggedIn)
                .Select(v => v)
                .BindToUI(this, v => v.UserPanel.IsVisible)
                .DisposeWith(d);
            
            this.WhenAnyValue(view => view.ViewModel.IsLoggedIn)
                .CombineLatest(this.WhenAnyValue(view => view.ViewModel.IsPremium))
                .Select(t => t.First && t.Second)
                .BindToUI(this, view => view.Premium.IsVisible)
                .DisposeWith(d);

            var avatar = UserButton.FindDescendantOfType<Image>();
            ViewModel.WhenAnyValue(vm => vm.Avatar)
                .Select(e => e)
                .BindToUI(avatar, v => v.Source)
                .DisposeWith(d);
            
            this.BindCommand(ViewModel, vm => vm.CloseCommand, v => v.WindowCloseButton)
                .DisposeWith(d);
            this.BindCommand(ViewModel, vm => vm.MinimizeCommand, v => v.WindowMinimizeButton)
                .DisposeWith(d);
            this.BindCommand(ViewModel, vm => vm.MaximizeCommand, v => v.WindowMaximizeButton)
                .DisposeWith(d);
            
        });
    }
}