using CommunityToolkit.Mvvm.ComponentModel;

namespace EnvEditor.Models;

public sealed partial class VariableRow : ObservableObject
{
    [ObservableProperty] private bool _isWhitelisted;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private VariableKind _kind;
    [ObservableProperty] private string _localValue = "";
    [ObservableProperty] private string _remoteValue = "";
    [ObservableProperty] private SyncState _state;
    [ObservableProperty] private bool _isHighRisk;
    [ObservableProperty] private string? _warning;

    public string KindText => Kind switch
    {
        VariableKind.Expand => "Expand (可展开 %VAR%)",
        VariableKind.Unsupported => "不支持类型",
        _ => "String"
    };
}
