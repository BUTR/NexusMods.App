using System.Reactive.Disposables;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using Avalonia.VisualTree;
using DynamicData.Binding;
using NexusMods.Icons;
using ReactiveUI;

namespace NexusMods.App.UI.Controls.TopBar;

public partial class TopBarView : ReactiveUserControl<ITopBarViewModel>
{
    public TopBarView()
    {
        InitializeComponent();

        this.WhenActivated(d =>
            {
                this.OneWayBind(ViewModel, vm => vm.ActiveWorkspaceTitle, view => view.ActiveWorkspaceTitleTextBlock.Text)
                    .DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.ActiveWorkspaceSubtitle, view => view.ActiveWorkspaceSubtitleTextBlock.Text)
                    .DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.SelectedTab!.GoBackInHistoryCommand, view => view.GoBackInHistory)
                    .DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.SelectedTab!.GoForwardInHistoryCommand, view => view.GoForwardInHistory)
                    .DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.AddPanelDropDownViewModel, view => view.AddPanelViewModelViewHost.ViewModel)
                    .DisposeWith(d);


                this.BindCommand(ViewModel, vm => vm.OpenSettingsCommand, view => view.OpenSettingsButton)
                    .DisposeWith(d);


                this.BindCommand(ViewModel, vm => vm.ViewChangelogCommand, view => view.ViewChangelogMenuItem)
                    .DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.ViewAppLogsCommand, view => view.ViewAppLogsMenuItem)
                    .DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.GiveFeedbackCommand, view => view.GiveFeedbackMenuItem)
                    .DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.GiveFeedbackCommand, view => view.GiveFeedbackButton)
                    .DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.LoginCommand, view => view.LoginButton)
                    .DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.Avatar, view => view.AvatarUnifiedIcon.Value,
                        avatar => new IconValue(new AvaloniaImage(avatar))
                    )
                    .DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.OpenNexusModsProfileCommand, view => view.OpenNexusModsProfileMenuItem)
                    .DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.OpenNexusModsPremiumCommand, view => view.FreeButton)
                    .DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.OpenNexusModsAccountSettingsCommand, view => view.OpenNexusModsAccountSettingsMenuItem)
                    .DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.LogoutCommand, view => view.SignOutMenuItem)
                    .DisposeWith(d);

                this.WhenAnyValue(
                        x => x.ViewModel!.IsLoggedIn,
                        x => x.ViewModel!.IsPremium,
                        x => x.ViewModel!.IsSupporter
                    )
                    .Subscribe(userinfo =>
                        {
                            var (isLoggedIn, isPremium, isSupporter) = userinfo;
                            PremiumTextBlock.IsVisible = isLoggedIn && isPremium;
                            FreeButton.IsVisible = isLoggedIn && !isPremium && !isSupporter;
                            SupporterButton.IsVisible = isLoggedIn && !isPremium && isSupporter;
                        }
                    )
                    .DisposeWith(d);

                this.WhenValueChanged(
                        x => x.ViewModel!.Username
                    )
                    .Subscribe(username => { ToolTip.SetTip(AvatarMenuItemButton, $"Logged in to Nexus Mods as {username}"); })
                    .DisposeWith(d);

                this.WhenValueChanged(
                        x => x.ViewModel!.IsLoggedIn
                    )
                    .Subscribe(b =>
                        {
                            AvatarMenuItemButton.IsVisible = b;
                            LoginButton.IsVisible = !b;
                        }
                    )
                    .DisposeWith(d);
            }
        );
    }
}
