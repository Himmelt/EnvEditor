using System.IO;
using System.Text.Json;
using EnvEditor.Models;
using EnvEditor.Services;

namespace EnvEditorTests.Services;

public class ConfigStoreTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "EnvEditorTests-" + Guid.NewGuid().ToString("N"));

    private string ConfigFile => Path.Combine(_tempDir, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var cfg = new ConfigStore(_tempDir).Load();

        Assert.Equal("", cfg.SyncId);
        Assert.Null(cfg.PatProtected);
        Assert.Null(cfg.EnvPasswordProtected);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsInsteadOfThrowing()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigFile, "{ this is not json");

        var cfg = new ConfigStore(_tempDir).Load();

        Assert.Equal("", cfg.RepoUrl);
    }

    [Fact]
    public void SaveLoad_RoundTripsNonSecretFields()
    {
        var store = new ConfigStore(_tempDir);
        store.Save(new AppConfig
        {
            RepoUrl = "https://github.com/foo/bar.git",
            Branch = "trunk",
            UserName = "alice",
            Email = "alice@example.com",
            SyncId = "team-a",
            UseSystemCredential = true,
            CredentialTarget = "git:https://github.com"
        });

        var cfg = store.Load();

        Assert.Equal("https://github.com/foo/bar.git", cfg.RepoUrl);
        Assert.Equal("trunk", cfg.Branch);
        Assert.Equal("alice", cfg.UserName);
        Assert.Equal("alice@example.com", cfg.Email);
        Assert.Equal("team-a", cfg.SyncId);
        Assert.True(cfg.UseSystemCredential);
        Assert.Equal("git:https://github.com", cfg.CredentialTarget);
    }

    [Fact]
    public void Save_WritesSecretsDpapiProtected_NeverPlaintext()
    {
        var store = new ConfigStore(_tempDir);
        store.Save(new AppConfig
        {
            PatProtected = "ghp_plaintext_pat",
            RememberEnvPassword = true,
            EnvPasswordProtected = "plaintext-env-key"
        });

        var raw = File.ReadAllText(ConfigFile);

        Assert.DoesNotContain("ghp_plaintext_pat", raw);
        Assert.DoesNotContain("plaintext-env-key", raw);
        Assert.Contains("dpapi:", raw);
    }

    [Fact]
    public void SaveLoad_RestoresSecretsAsPlaintextInMemory()
    {
        // "记住密钥"跨重启可用的前提：Load 能把密文解回明文供界面回填
        var store = new ConfigStore(_tempDir);
        store.Save(new AppConfig
        {
            PatProtected = "ghp_pat",
            RememberEnvPassword = true,
            EnvPasswordProtected = "env-key"
        });

        var cfg = store.Load();

        Assert.Equal("ghp_pat", cfg.PatProtected);
        Assert.Equal("env-key", cfg.EnvPasswordProtected);
        Assert.True(cfg.RememberEnvPassword);
    }

    [Fact]
    public void Save_WithoutRemember_DoesNotPersistEnvPassword()
    {
        var store = new ConfigStore(_tempDir);
        store.Save(new AppConfig
        {
            RememberEnvPassword = false,
            EnvPasswordProtected = "env-key"
        });

        var raw = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigFile));

        Assert.Null(raw!.EnvPasswordProtected);
        Assert.Null(store.Load().EnvPasswordProtected);
    }

    [Fact]
    public void Save_NullSecrets_StaysNull()
    {
        var store = new ConfigStore(_tempDir);
        store.Save(new AppConfig { PatProtected = null, EnvPasswordProtected = null });

        var cfg = store.Load();

        Assert.Null(cfg.PatProtected);
        Assert.Null(cfg.EnvPasswordProtected);
    }
}