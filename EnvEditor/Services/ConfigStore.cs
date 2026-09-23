using System.Security.Cryptography;
using System.Text.Json;
using System.IO;
using EnvEditor.Models;

namespace EnvEditor.Services;

/// <summary>
/// 配置持久化到 %APPDATA%\EnvEditor\config.json。
/// 敏感字段（PAT、环境变量密码）经 DPAPI (CurrentUser) 加密后保存，明文不落盘。
/// </summary>
public sealed class ConfigStore
{
    private readonly string _dir;
    private readonly string _filePath;

    /// <summary>目录留空则用 %APPDATA%\EnvEditor；测试可注入临时目录。</summary>
    public ConfigStore(string? directory = null)
    {
        _dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EnvEditor");
        _filePath = Path.Combine(_dir, "config.json");
    }

    public AppConfig Load()
    {
        if (!File.Exists(_filePath)) return new AppConfig();
        try
        {
            var json = File.ReadAllText(_filePath);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            // 反序列化敏感字段为明文（DPAPI 解密失败返回 null）
            cfg.PatProtected = TryUnprotect(cfg.PatProtected);
            cfg.EnvPasswordProtected = TryUnprotect(cfg.EnvPasswordProtected);
            return cfg;
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig cfg)
    {
        Directory.CreateDirectory(_dir);
        var toSave = new AppConfig
        {
            RepoUrl = cfg.RepoUrl,
            Branch = cfg.Branch,
            UserName = cfg.UserName,
            Email = cfg.Email,
            SyncId = cfg.SyncId,
            UseSystemCredential = cfg.UseSystemCredential,
            CredentialTarget = cfg.CredentialTarget,
            RememberEnvPassword = cfg.RememberEnvPassword,
            PatProtected = TryProtect(cfg.PatProtected),
            EnvPasswordProtected = cfg.RememberEnvPassword ? TryProtect(cfg.EnvPasswordProtected) : null
        };
        var json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    // ── DPAPI 辅助 ──
    private static string? TryProtect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var blob = ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return "dpapi:" + Convert.ToBase64String(blob);
    }

    private static string? TryUnprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith("dpapi:"))
            return null;
        try
        {
            var blob = Convert.FromBase64String(stored["dpapi:".Length..]);
            return System.Text.Encoding.UTF8.GetString(
                ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return null; // 换机器/改 Windows 密码/损坏 → 无法解
        }
    }
}
