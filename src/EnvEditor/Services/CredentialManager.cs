using System.Runtime.Versioning;
using EnvEditor.Interop;

namespace EnvEditor.Services;

/// <summary>
/// 读取 Windows 凭据管理器中的 git PAT。
/// LibGit2Sharp 不读系统凭据，故这里主动按仓库 URL 推导目标名去取。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CredentialManager
{
    /// <summary>
    /// 根据仓库 URL 推导 Windows 凭据管理器里的目标名，例如 https://github.com/foo/bar
    /// → git:https://github.com
    /// </summary>
    public static string TargetForUrl(string repoUrl)
    {
        if (Uri.TryCreate(repoUrl, UriKind.Absolute, out var uri) && uri.Host.Length > 0)
            return "git:" + uri.GetLeftPart(UriPartial.Authority); // 例如 git:https://github.com
        return "git:" + repoUrl.TrimEnd('/');
    }

    /// <summary>读取指定目标名的凭据密码；取不到返回 null。</summary>
    public string? Read(string target) => NativeMethods.ReadCredential(target);
}
