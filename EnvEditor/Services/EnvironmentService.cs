using System.Runtime.Versioning;
using EnvEditor.Interop;
using EnvEditor.Models;
using Microsoft.Win32;

namespace EnvEditor.Services;

/// <summary>
/// 直接操作 HKCU\Environment 注册表。必须绕开 Environment.Get/SetEnvironmentVariable：
/// 1) 防止 %VAR% 被展开；2) 保留 REG_EXPAND_SZ 类型（SetEnvironmentVariable 只能写 REG_SZ）。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EnvironmentService
{
    private const string SubKey = "Environment"; // HKEY_CURRENT_USER\Environment
    private static readonly HashSet<string> HighRisk =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "PATH", "TEMP", "TMP", "PATHEXT", "USERPROFILE", "OneDrive", "USERNAME", "OS", "COMSPEC"
        };

    public bool IsHighRisk(string name) => HighRisk.Contains(name);

    public IReadOnlyList<UserVariable> ReadAll()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: false)
            ?? throw new InvalidOperationException("无法打开 HKCU\\Environment");

        var result = new List<UserVariable>();
        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue; // 跳过 (Default)
            var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var kind = key.GetValueKind(name);
            if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
                continue; // REG_MULTI_SZ 等不支持类型，交由上层标红
            result.Add(new UserVariable
            {
                Name = name,
                Value = raw as string ?? "",
                Kind = kind == RegistryValueKind.ExpandString ? VariableKind.Expand : VariableKind.String
            });
        }
        return result;
    }

    /// <summary>返回不支持同步的类型名（REG_MULTI_SZ 等），供 UI 标红。</summary>
    public IReadOnlyList<string> ReadUnsupportedNames()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: false)
            ?? throw new InvalidOperationException("无法打开 HKCU\\Environment");
        var list = new List<string>();
        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue;
            var kind = key.GetValueKind(name);
            if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
                list.Add(name);
        }
        return list;
    }

    public void Write(UserVariable v)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SubKey, writable: true)
            ?? throw new InvalidOperationException("无法写入 HKCU\\Environment");
        var regKind = v.Kind == VariableKind.Expand
            ? RegistryValueKind.ExpandString
            : RegistryValueKind.String;

        var current = key.GetValue(v.Name, "", RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (Equals(current, v.Value) && key.GetValueKind(v.Name) == regKind)
            return; // 无变化，避免无谓广播

        key.SetValue(v.Name, v.Value, regKind);
        NativeMethods.BroadcastEnvironmentChange();
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true);
        if (key is null) return;
        if (Array.Exists(key.GetValueNames(), n => n == name))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
            NativeMethods.BroadcastEnvironmentChange();
        }
    }

    public bool Exists(string name) =>
        ReadAll().Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public sealed class UserVariable
{
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public VariableKind Kind { get; init; }
}
