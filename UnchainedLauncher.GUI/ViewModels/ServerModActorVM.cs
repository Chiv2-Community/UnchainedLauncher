using CommunityToolkit.Mvvm.ComponentModel;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels;

public partial class ServerModActorVM(BlueprintDto blueprint, string source) : ObservableObject {
    public BlueprintDto Blueprint { get; } = blueprint;

    [ObservableProperty]
    private string _source = source;
}
