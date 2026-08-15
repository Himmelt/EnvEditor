using System.Text.Json.Serialization;

namespace EnvEditor.Models;

public enum VariableKind
{
    String,   // REG_SZ
    Expand,   // REG_EXPAND_SZ
    Unsupported // 其它类型（REG_MULTI_SZ 等），不纳入同步
}

public enum SyncState
{
    NotTracked,    // 未勾选，不参与同步
    InSync,        // 已勾选，本地 == 远端
    LocalOnly,     // 已勾选，远端尚无 → 下次上传新增
    RemoteOnly,    // 远端有、本机无 → 应用后新建
    Different,     // 两边都有但值不同 → 应用=远端覆盖本地
    PendingRemove, // 曾在远端、现取消勾选 → 下次上传移除
    Unsupported    // 不支持的类型，跳过
}

public sealed class AppConfig
{
    public string RepoUrl { get; set; } = "";
    public string Branch { get; set; } = "main";
    public string UserName { get; set; } = "";
    public string Email { get; set; } = "";
    public string SyncId { get; set; } = "";

    public bool UseSystemCredential { get; set; }
    public string CredentialTarget { get; set; } = ""; // 留空则按仓库 host 推导

    // 经 DPAPI 保护的敏感字段；明文为 null。读不到（换机器/改密码）时为 null。
    public string? PatProtected { get; set; }
    public string? EnvPasswordProtected { get; set; }
    public bool RememberEnvPassword { get; set; }
}

public sealed class VarEntry
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public VariableKind Kind { get; set; }
}

public sealed class SyncPayload
{
    public string SyncId { get; set; } = "";
    public string MachineName { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public string Fmt { get; set; } = "env1";
    public List<VarEntry> Variables { get; set; } = new();
}

public sealed class BackupPayload
{
    public DateTimeOffset CreatedAt { get; set; }
    public List<VarEntry> Variables { get; set; } = new();
}

public sealed class DecryptionFailedException : Exception
{
    // true = 密码错或文件损坏，二者无法区分；false = 明确格式不符/损坏
    public bool Ambiguous { get; }
    public DecryptionFailedException(string message, bool ambiguous) : base(message)
        => Ambiguous = ambiguous;
}
