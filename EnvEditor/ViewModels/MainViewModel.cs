using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvEditor.Models;
using EnvEditor.Services;

namespace EnvEditor.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly EnvironmentService _env = new();
    private readonly CryptoService _crypto = new();
    private readonly ConfigStore _configStore = new();
    private readonly BackupService _backup = new();
    private readonly CredentialManager _credMgr = new();

    private AppConfig _config = new();
    private SyncPayload? _remotePayload;
    private CancellationTokenSource? _cts;

    // 最近一次 Recompute 的快照，供勾选框切换时就地重算状态
    private Dictionary<string, UserVariable> _localMap = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, VarEntry> _remoteMap = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _unsupported = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressRowEvents;

    private static string RepoLocalPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnvEditor", "repo");

    // ── 配置字段（绑定设置区）──
    [ObservableProperty] private string _repoUrl = "";
    [ObservableProperty] private string _branch = "main";
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _syncId = "";
    [ObservableProperty] private bool _useSystemCredential;
    [ObservableProperty] private string _credentialTarget = "";
    [ObservableProperty] private string _patInput = "";
    [ObservableProperty] private bool _rememberEnvPassword;
    [ObservableProperty] private string _passwordInput = "";

    // ── 本地变量增删改 ──
    [ObservableProperty] private string _newVarName = "";
    [ObservableProperty] private string _newVarValue = "";
    [ObservableProperty] private VariableKind _newVarKind = VariableKind.String;

    // ── 搜索/筛选 ──
    [ObservableProperty] private string _searchText = "";

    // ── UI 状态 ──
    // Rows 为全量数据源（Recompute 赋值时触发 ApplyFilter）；FilteredRows 供 DataGrid 绑定
    private ObservableCollection<VariableRow> _rows = new();
    public ObservableCollection<VariableRow> Rows
    {
        get => _rows;
        set
        {
            if (SetProperty(ref _rows, value))
                ApplyFilter();
        }
    }
    public ObservableCollection<VariableRow> FilteredRows { get; } = new();
    [ObservableProperty] private VariableRow? _selectedRow;
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private bool _isBusy;
    // 操作日志（最新在上，保留最近 100 条）
    public ObservableCollection<string> LogHistory { get; } = new();

    public IReadOnlyList<VariableKind> KindOptions { get; } = new[] { VariableKind.String, VariableKind.Expand };

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    // 按名称/本地值/远端值过滤到 FilteredRows
    private void ApplyFilter()
    {
        FilteredRows.Clear();
        var q = (SearchText ?? "").Trim();
        foreach (var r in Rows)
        {
            if (q.Length == 0 ||
                r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.LocalValue.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.RemoteValue.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                FilteredRows.Add(r);
            }
        }
    }

    // 把当前状态写入状态栏并记入日志
    private void SetStatus(string msg)
    {
        StatusMessage = msg;
        LogHistory.Insert(0, $"{DateTime.Now:HH:mm:ss}  {msg}");
        while (LogHistory.Count > 100) LogHistory.RemoveAt(LogHistory.Count - 1);
    }

    // ── 生命周期 ──
    public void LoadConfig()
    {
        _config = _configStore.Load();
        RepoUrl = _config.RepoUrl;
        Branch = string.IsNullOrWhiteSpace(_config.Branch) ? "main" : _config.Branch;
        UserName = _config.UserName;
        Email = _config.Email;
        SyncId = _config.SyncId;
        UseSystemCredential = _config.UseSystemCredential;
        CredentialTarget = _config.CredentialTarget;
        PatInput = _config.PatProtected ?? "";
        RememberEnvPassword = _config.RememberEnvPassword;
        // 回填上次"记住"的解密密钥；DPAPI 解不出（换机器/改密码）时为 null，需用户重新输入
        PasswordInput = RememberEnvPassword ? _config.EnvPasswordProtected ?? "" : "";
        Recompute();
        SetStatus("配置已加载");
    }

    [RelayCommand]
    private void SaveConfig()
    {
        // 输入框为空时保留已存的密文，避免密码框为空就把"记住的"密钥/PAT 静默清空
        var pat = string.IsNullOrEmpty(PatInput) ? _config.PatProtected : PatInput;
        var pwd = string.IsNullOrEmpty(PasswordInput) ? _config.EnvPasswordProtected : PasswordInput;
        _config = new AppConfig
        {
            RepoUrl = RepoUrl,
            Branch = string.IsNullOrWhiteSpace(Branch) ? "main" : Branch,
            UserName = UserName,
            Email = Email,
            SyncId = SyncId,
            UseSystemCredential = UseSystemCredential,
            CredentialTarget = CredentialTarget,
            RememberEnvPassword = RememberEnvPassword,
            PatProtected = pat,
            EnvPasswordProtected = RememberEnvPassword ? pwd : null
        };
        _configStore.Save(_config);
        SetStatus(RememberEnvPassword && !string.IsNullOrEmpty(pwd)
            ? "配置已保存（解密密钥已记住，下次启动自动回填）"
            : "配置已保存");
    }

    private string ResolvePat()
    {
        if (_config.UseSystemCredential || UseSystemCredential)
        {
            var target = string.IsNullOrWhiteSpace(CredentialTarget)
                ? CredentialManager.TargetForUrl(RepoUrl)
                : CredentialTarget;
            var sys = _credMgr.Read(target);
            if (!string.IsNullOrEmpty(sys)) return sys;
        }
        return PatInput;
    }

    private GitService BuildGit() =>
        new(RepoLocalPath, RepoUrl, Branch, UserName, Email, ResolvePat);

    private bool Precheck()
    {
        if (string.IsNullOrWhiteSpace(SyncId)) { SetStatus("请填写同步 ID"); return false; }
        if (string.IsNullOrWhiteSpace(RepoUrl)) { SetStatus("请填写仓库地址"); return false; }
        try { GitService.Sanitize(SyncId); }
        catch (ArgumentException ex) { SetStatus(ex.Message); return false; }
        if (string.IsNullOrWhiteSpace(PasswordInput)) { SetStatus("请输入解密密钥（用于加密/解密远端数据）"); return false; }
        return true;
    }

    // 启动一个可取消的后台操作：统一管理 _cts 与 IsBusy，统一异常处理
    private async Task RunAsync(Func<CancellationToken, Task> work, string busyMsg)
    {
        if (IsBusy) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            IsBusy = true;
            SetStatus(busyMsg);
            await work(token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("操作已取消");
        }
        catch (DecryptionFailedException ex)
        {
            SetStatus(ex.Ambiguous
                ? "解密失败：密钥错误或数据已损坏（二者无法区分）。请确认解密密钥；若密钥已遗忘，远端数据无法恢复。"
                : "解密失败：" + ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("操作失败：" + ex.Message);
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }

    // ── 拉取 ──
    [RelayCommand]
    private Task PullAsync() => RunAsync(async token =>
    {
        if (!Precheck()) return;
        var git = BuildGit();
        await Task.Run(() => git.EnsureRepo(), token);
        var enc = await Task.Run(() => git.ReadRemoteFile(SyncId), token);
        if (enc is null)
        {
            _remotePayload = null;
            Recompute();
            SetStatus("远端暂无该同步 ID 的数据（可勾选后上传）");
            return;
        }
        var json = await Task.Run(() => _crypto.Decrypt(enc, PasswordInput), token);
        _remotePayload = JsonSerializer.Deserialize<SyncPayload>(json);
        Recompute();
        SetStatus($"拉取完成：远端 {_remotePayload?.Variables.Count ?? 0} 个变量");
    }, "正在拉取远端…");

    // ── 应用到本机 ──
    [RelayCommand]
    private Task ApplyAsync() => RunAsync(async token =>
    {
        if (_remotePayload is null) { SetStatus("请先拉取"); return; }
        var payload = _remotePayload;
        var result = await Task.Run(() =>
        {
            var local = _env.ReadAll();
            var backupPath = _backup.Backup(local);
            var n = 0;
            foreach (var row in Rows.Where(r => r.IsWhitelisted && r.State != SyncState.Unsupported))
            {
                token.ThrowIfCancellationRequested();
                var entry = payload.Variables
                    .FirstOrDefault(e => e.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
                if (entry is null) continue;
                _env.Write(new UserVariable { Name = entry.Name, Value = entry.Value, Kind = entry.Kind });
                n++;
            }
            return (n, backupPath);
        }, token);
        Recompute();
        SetStatus($"已应用 {result.n} 个变量到本机（备份：{Path.GetFileName(result.backupPath)}，仅对新建进程生效）");
    }, "正在应用到本机…");

    // ── 上传 ──
    [RelayCommand]
    private Task PushAsync() => RunAsync(async token =>
    {
        if (!Precheck()) return;
        var (entries, count) = await Task.Run(() =>
        {
            var local = _env.ReadAll();
            var white = Rows
                .Where(r => r.IsWhitelisted && r.State != SyncState.Unsupported)
                .Select(r => r.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var list = local
                .Where(x => white.Contains(x.Name))
                .Select(x => new VarEntry { Name = x.Name, Value = x.Value, Kind = x.Kind })
                .ToList();
            return (list, list.Count);
        }, token);
        var payload = new SyncPayload
        {
            SyncId = SyncId,
            MachineName = Environment.MachineName,
            UpdatedAt = DateTimeOffset.Now,
            Variables = entries
        };
        var json = JsonSerializer.Serialize(payload);
        var enc = await Task.Run(() => _crypto.Encrypt(json, PasswordInput), token);
        var git = BuildGit();
        await Task.Run(() =>
        {
            git.EnsureRepo();
            git.Publish(SyncId, enc, token);
        }, token);
        _remotePayload = payload;
        Recompute();
        // 未勾选但远端存在的变量会被本次整体覆盖删除，明确告知
        var removed = Rows.Count(r => r.State == SyncState.PendingRemove);
        SetStatus($"上传完成：{count} 个变量已同步到远端"
                  + (removed > 0 ? $"，另有 {removed} 个未勾选变量已从远端移除" : ""));
    }, "正在上传…");

    // ── 测试连接 ──
    [RelayCommand]
    private Task TestConnectionAsync() => RunAsync(async token =>
    {
        if (string.IsNullOrWhiteSpace(RepoUrl)) { SetStatus("请填写仓库地址"); return; }
        var git = BuildGit();
        await Task.Run(() => git.EnsureRepo(), token);
        SetStatus("连接成功：已 fetch 远端");
    }, "正在测试连接…");

    // ── 取消当前操作 ──
    [RelayCommand]
    private void Cancel()
    {
        if (_cts is null) return;
        _cts.Cancel();
        SetStatus("正在取消…");
    }

    // ── 刷新本机变量列表 ──
    [RelayCommand]
    private void Refresh()
    {
        Recompute();
        SetStatus("已刷新本机变量列表");
    }

    // ── 备份 / 回滚 ──
    [RelayCommand]
    private void Backup()
    {
        var local = _env.ReadAll();
        var path = _backup.Backup(local);
        SetStatus($"已全量备份至 {Path.GetFileName(path)}");
    }

    [RelayCommand]
    private void Rollback()
    {
        var backups = _backup.ListBackups();
        if (backups.Count == 0) { SetStatus("没有可用备份"); return; }
        var newest = backups[0];
        var r = MessageBox.Show($"将用备份 {Path.GetFileName(newest)} 覆盖当前本机变量，继续？",
            "回滚确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            _backup.Restore(newest, _env);
            Recompute();
            SetStatus($"已回滚到 {Path.GetFileName(newest)}");
        }
        catch (Exception ex)
        {
            SetStatus("回滚失败：" + ex.Message);
        }
    }

    // ── 本地变量管理（直接改 HKCU，与云端解耦）──
    [RelayCommand]
    private void AddLocal()
    {
        if (string.IsNullOrWhiteSpace(NewVarName)) { SetStatus("变量名不能为空"); return; }
        _env.Write(new UserVariable { Name = NewVarName, Value = NewVarValue ?? "", Kind = NewVarKind });
        SetStatus($"已写入本机变量 {NewVarName}");
        NewVarName = "";
        NewVarValue = "";
        Recompute();
    }

    [RelayCommand]
    private void EditSelected()
    {
        if (SelectedRow is null) { SetStatus("请先选中一行"); return; }
        NewVarName = SelectedRow.Name;
        NewVarValue = SelectedRow.LocalValue;
        NewVarKind = SelectedRow.Kind == VariableKind.Expand ? VariableKind.Expand : VariableKind.String;
        SetStatus($"已载入「{SelectedRow.Name}」到上方编辑框，修改后点「添加/保存」");
    }

    [RelayCommand]
    private void DeleteLocal()
    {
        if (SelectedRow is null) { SetStatus("请先选中一行"); return; }
        var name = SelectedRow.Name;
        if (_env.IsHighRisk(name))
        {
            var r = MessageBox.Show($"「{name}」为高危变量，确定从本机删除？",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }
        _env.Delete(name);
        SetStatus($"已删除本机变量 {name}");
        Recompute();
    }

    // ── 状态计算：本地 / 远端 / 白名单 → SyncState ──
    private void Recompute()
    {
        var local = _env.ReadAll();
        _localMap = local.ToDictionary(v => v.Name, v => v, StringComparer.OrdinalIgnoreCase);
        var remote = _remotePayload?.Variables ?? new List<VarEntry>();
        _remoteMap = remote.ToDictionary(v => v.Name, v => v, StringComparer.OrdinalIgnoreCase);
        _unsupported = _env.ReadUnsupportedNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prevSel = Rows.ToDictionary(r => r.Name, r => r.IsWhitelisted, StringComparer.OrdinalIgnoreCase);
        var prevSelectedName = SelectedRow?.Name;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in _localMap.Keys) names.Add(n);
        foreach (var n in _remoteMap.Keys) names.Add(n);
        foreach (var n in _unsupported) names.Add(n);

        var next = new ObservableCollection<VariableRow>();
        _suppressRowEvents = true; // 重建期间 IsWhitelisted 赋值会触发事件，这里统一抑制
        try
        {
            foreach (var name in names)
            {
                _localMap.TryGetValue(name, out var lv);
                _remoteMap.TryGetValue(name, out var rv);
                var row = new VariableRow
                {
                    Name = name,
                    LocalValue = lv?.Value ?? "",
                    Kind = lv?.Kind ?? rv?.Kind ?? VariableKind.String,
                    RemoteValue = rv?.Value ?? "",
                    IsHighRisk = _env.IsHighRisk(name)
                };

                // 白名单默认：远端有且非高危则默认勾选（pull 后可直接应用）；否则沿用上次选择
                var selected = rv is not null && !row.IsHighRisk;
                if (prevSel.TryGetValue(name, out var wasSel)) selected = wasSel;
                row.IsWhitelisted = selected;

                ApplyState(row, lv, rv);
                row.PropertyChanged += OnRowPropertyChanged;
                next.Add(row);
            }
        }
        finally
        {
            _suppressRowEvents = false;
        }

        Rows = next;

        // Rows 重建 + ApplyFilter 会清空选中项，按名称恢复
        if (prevSelectedName is not null)
            SelectedRow = FilteredRows.FirstOrDefault(
                r => r.Name.Equals(prevSelectedName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>就地重算单行状态。勾选框切换时由 <see cref="OnRowPropertyChanged"/> 调用。</summary>
    private void ApplyState(VariableRow row, UserVariable? lv, VarEntry? rv)
    {
        if (_unsupported.Contains(row.Name))
        {
            row.State = SyncState.Unsupported;
            row.Warning = "类型不支持同步（REG_MULTI_SZ 等）";
            row.IsWhitelisted = false;
            return;
        }

        if (!row.IsWhitelisted)
        {
            // 上传是整体覆盖 payload，故"未勾选 + 远端存在"等于下次上传会删掉远端那份
            if (rv is not null)
            {
                row.State = SyncState.PendingRemove;
                row.Warning = "未勾选 → 下次上传将从远端移除";
            }
            else
            {
                row.State = SyncState.NotTracked;
                row.Warning = "";
            }
        }
        else if (lv is null && rv is not null)
        {
            row.State = SyncState.RemoteOnly;
            row.Warning = "远端有、本机无（应用后新增）";
        }
        else if (lv is not null && rv is null)
        {
            row.State = SyncState.LocalOnly;
            row.Warning = "本机有、远端无（上传新增）";
        }
        else if (lv is not null && rv is not null)
        {
            if (lv.Value == rv.Value && lv.Kind == rv.Kind)
            {
                row.State = SyncState.InSync;
                row.Warning = "";
            }
            else
            {
                row.State = SyncState.Different;
                row.Warning = "值不同（应用 = 远端覆盖本地）";
            }
        }
        else
        {
            row.State = SyncState.NotTracked;
            row.Warning = "";
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressRowEvents || e.PropertyName != nameof(VariableRow.IsWhitelisted)) return;
        if (sender is not VariableRow row) return;
        _localMap.TryGetValue(row.Name, out var lv);
        _remoteMap.TryGetValue(row.Name, out var rv);
        ApplyState(row, lv, rv);
    }
}
