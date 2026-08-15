using System.Text.Json;
using System.IO;
using EnvEditor.Models;
using EnvEditor.Services;

namespace EnvEditor.Services;

/// <summary>
/// 应用远端变量前强制全量备份当前用户变量（含 kind），支持一键回滚。
/// 备份存于 %LOCALAPPDATA%\EnvEditor\backups，保留最近 20 份。
/// </summary>
public sealed class BackupService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnvEditor", "backups");
    private const int MaxKeep = 20;

    public string Backup(IReadOnlyList<UserVariable> variables)
    {
        Directory.CreateDirectory(Dir);
        var payload = new BackupPayload
        {
            CreatedAt = DateTimeOffset.Now,
            Variables = variables.Select(v => new VarEntry
            {
                Name = v.Name,
                Value = v.Value,
                Kind = v.Kind
            }).ToList()
        };
        var path = Path.Combine(Dir, "env-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + ".json");
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        Prune();
        return path;
    }

    public IReadOnlyList<string> ListBackups()
    {
        if (!Directory.Exists(Dir)) return Array.Empty<string>();
        return Directory.GetFiles(Dir, "env-*.json")
            .OrderByDescending(f => f)
            .ToList();
    }

    public BackupPayload? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<BackupPayload>(File.ReadAllText(path)); }
        catch { return null; }
    }

    /// <summary>用指定备份回滚：按 kind 写回注册表并广播。</summary>
    public void Restore(string path, EnvironmentService env)
    {
        var payload = Read(path) ?? throw new InvalidOperationException("备份文件无法读取");
        foreach (var e in payload.Variables)
        {
            env.Write(new UserVariable { Name = e.Name, Value = e.Value, Kind = e.Kind });
        }
    }

    private void Prune()
    {
        var files = Directory.GetFiles(Dir, "env-*.json")
            .OrderByDescending(f => f)
            .Skip(MaxKeep)
            .ToList();
        foreach (var f in files) File.Delete(f);
    }
}
