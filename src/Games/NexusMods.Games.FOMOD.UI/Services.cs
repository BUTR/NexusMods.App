using Microsoft.Extensions.DependencyInjection;
using NexusMods.App.UI;
using NexusMods.Common.GuidedInstaller;

namespace NexusMods.Games.FOMOD.UI;

public static class Services
{
    public static IServiceCollection AddGuidedInstallerUi(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddTransient<IGuidedInstaller, GuidedInstallerUi>()

            .AddViewModel<GuidedInstallerWindowViewModel, IGuidedInstallerWindowViewModel>()
            .AddViewModel<GuidedInstallerStepViewModel, IGuidedInstallerStepViewModel>()

            .AddView<FooterStepperView, IFooterStepperViewModel>()
            .AddView<GuidedInstallerStepView, IGuidedInstallerStepViewModel>()
            .AddView<GuidedInstallerGroupView, IGuidedInstallerGroupViewModel>()
            .AddView<GuidedInstallerOptionView, IGuidedInstallerOptionViewModel>();
    }
}
