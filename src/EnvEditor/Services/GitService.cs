using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using System.IO;
using System.Linq;
using System.Threading;

namespace EnvEditor.Services;

/// <summary>
/// LibGit2Sharp 封装。统一用 Init+remote+fetch 流程（不 Clone，规避空仓库 master/main 错配）。
/// 本地工作副本是纯缓存：拉取=硬重置到远端 tip；推送=fetch→reset→写→commit→push（恒 fast-forward）。
/// </summary>
public sealed class GitService
{
    private readonly string _localPath;
    private readonly string _remoteUrl;
    private readonly string _branchName;
    private readonly string _userName;
    private readonly string _email;
    private readonly Func<string> _patResolver;

    public GitService(string localPath, string remoteUrl, string branchName,
                      string userName, string email, Func<string> patResolver)
    {
        _localPath = localPath;
        _remoteUrl = remoteUrl;
        _branchName = string.IsNullOrWhiteSpace(branchName) ? "main" : branchName;
        _userName = userName;
        _email = email;
        _patResolver = patResolver;
    }

    private CredentialsHandler Creds => (_, _, _) => new UsernamePasswordCredentials
    {
        Username = string.IsNullOrWhiteSpace(_userName) ? "git" : _userName,
        Password = _patResolver() ?? ""
    };

    public void EnsureRepo()
    {
        if (!Repository.IsValid(_localPath))
        {
            Repository.Init(_localPath);
            using var repo = new Repository(_localPath);
            if (repo.Network.Remotes["origin"] is null)
                repo.Network.Remotes.Add("origin", _remoteUrl);
        }
        using var r = new Repository(_localPath);
        Fetch(r);
    }

    // 拉取：fetch + 硬重置到 origin/{branch}
    public void SyncToRemote()
    {
        using var repo = new Repository(_localPath);
        Fetch(repo);
        var rb = ResolveRemoteBranch(repo);
        if (rb is not null) repo.Reset(ResetMode.Hard, rb.Tip);
    }

    public string? ReadRemoteFile(string syncId)
    {
        SyncToRemote();
        var full = Path.Combine(_localPath, "profiles", Sanitize(syncId) + ".enc");
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    // 发布：写文件 + commit + push（含重试）。可在重试间隙被 ct 取消
    public void Publish(string syncId, string encryptedContent, CancellationToken ct = default)
    {
        var rel = "profiles/" + Sanitize(syncId) + ".enc";
        StageCommitPush(rel, encryptedContent, "sync " + syncId + " @ " + DateTimeOffset.Now.ToString("u"), ct);
    }

    // ───────── 内部 ─────────

    private void Fetch(Repository repo)
    {
        var remote = repo.Network.Remotes["origin"]
            ?? throw new InvalidOperationException("未配置 origin remote");
        var specs = remote.FetchRefSpecs.Select(r => r.Specification).ToList();
        Commands.Fetch(repo, "origin", specs,
            new FetchOptions { CredentialsProvider = Creds }, null);
    }

    private Branch? ResolveRemoteBranch(Repository repo)
    {
        var byCfg = repo.Branches["origin/" + _branchName];
        if (byCfg is not null) return byCfg;

        var remoteHead = repo.Refs["refs/remotes/origin/HEAD"];
        if (remoteHead is not null)
        {
            var target = remoteHead.ResolveToDirectReference()?.TargetIdentifier;
            var name = target?.Replace("refs/remotes/origin/", "");
            if (name is not null) return repo.Branches["origin/" + name];
        }
        return null; // 空仓库：尚无分支
    }

    private void StageCommitPush(string rel, string content, string message, CancellationToken ct, int retries = 5)
    {
        using var repo = new Repository(_localPath);
        for (var i = 0; i < retries; i++)
        {
            ct.ThrowIfCancellationRequested();
            Fetch(repo);
            var rb = ResolveRemoteBranch(repo);
            if (rb is not null) repo.Reset(ResetMode.Hard, rb.Tip);

            var full = Path.Combine(_localPath, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);

            Commands.Stage(repo, rel);
            bool changed;
            if (repo.Head.Tip is null)
                changed = true; // 空仓库首次提交
            else
                changed = repo.Diff.Compare<TreeChanges>(repo.Head.Tip.Tree, DiffTargets.Index).Any();
            if (!changed) return; // 暂存内容相对 HEAD 无变化，不提交

            var sig = new Signature(_userName, _email, DateTimeOffset.Now);
            repo.Commit(message, sig, sig);

            try
            {
                var remote = repo.Network.Remotes["origin"];
                repo.Network.Push(remote,
                    "refs/heads/" + _branchName + ":refs/heads/" + _branchName,
                    new PushOptions { CredentialsProvider = Creds });
                return;
            }
            catch (NonFastForwardException) { /* 重试：重新对齐基线 */ }
        }
        throw new InvalidOperationException("推送失败：多次遇到并发修改，请稍后重试。");
    }

    public static string Sanitize(string syncId)
    {
        if (string.IsNullOrWhiteSpace(syncId) || syncId.Length > 64)
            throw new ArgumentException("同步 ID 必须非空且长度不超过 64");
        foreach (var c in syncId)
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '_'))
                throw new ArgumentException("同步 ID 只能包含字母、数字、- 和 _");
        return syncId;
    }
}
