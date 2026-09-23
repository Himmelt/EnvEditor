# EnvEditor

基于 **.NET 10 + WPF** 的 Windows 用户环境变量同步工具。把你勾选的环境变量加密后存入 Git 仓库，
多台 PC 通过自定义 **同步 ID** 关联、拉取/上传，实现跨机器环境变量同步。

> 发布形态：**依赖框架的多文件 exe**（win-x64）。已取消单文件发布——`git2` 原生库直接随包落到 `publish/` 目录。

---

## 功能特性

- **加密存储**：PBKDF2-HMAC-SHA256（60 万次）+ AES-256-GCM，远端只存加密字符串；解密密钥由你在界面输入（可勾选"记住"用 Windows DPAPI 存本机，重启后自动回填）。
- **白名单同步**：勾选哪些变量参与同步，规避冲突。以**远端为准**——拉取时只新增/覆盖本机变量，绝不删除本机变量。
  ⚠️ 注意反过来：**取消勾选 = 下次上传会从远端删除该变量**（上传是整体覆盖，不是增量合并），界面状态列会标为"未勾选 → 下次上传将从远端移除"。本机变量不受影响。
- **本地管理**：独立的本机变量增 / 删 / 改功能，直接操作注册表，与云端同步解耦。
- **安全兜底**：执行"应用到本机"前强制全量备份当前用户变量，可一键回滚；`Path`/`TEMP` 等高危变量默认不勾选并标红。
- **凭据灵活**：默认界面输入 PAT（DPAPI 存本机）；可勾选"使用系统凭据管理器"，按仓库 URL 从 Windows 凭据管理器自动取 PAT。
- **零外部依赖**：用 LibGit2Sharp 内置 git 能力，目标机**无需安装 git.exe**。

---

## 项目结构

```
EnvEditor/          # WPF 主程序（net10.0-windows）
  ViewModels/       # MainViewModel：同步状态机 + 全部命令
  Services/         # Crypto / Git / Config / Backup / Credential / Environment
  Models/           # AppConfig、VarEntry、SyncPayload、VariableRow
  Interop/          # P/Invoke：广播环境变更、读 Windows 凭据管理器
  EnvEditor.slnx    # 解决方案（同时包含下面的测试工程）
EnvEditorTests/     # xUnit 测试，目录结构镜像主程序
  Services/         # CryptoService / GitService / ConfigStore / CredentialManager 测试
```

---

## 构建与发布

```bash
cd EnvEditor
dotnet publish -c Release -r win-x64
```

产物在 `EnvEditor/bin/Release/net10.0-windows/win-x64/publish/`：
`EnvEditor.exe` + `EnvEditor.dll` + `LibGit2Sharp.dll` + `git2-5853918.dll` 等。

> ⚠️ **运行要求**：因为发布形态是"依赖框架"，目标机器必须已安装 **.NET 10 Desktop Runtime (win-x64)**。
> 否则双击 `EnvEditor.exe` 会立即退出。若要免安装，需改为自包含发布
> （`dotnet publish -c Release -r win-x64 --self-contained true`，体积会明显增大）。

---

## 运行测试

```bash
dotnet test EnvEditor/EnvEditor.slnx
```

覆盖范围（38 个用例）：

| 目标 | 覆盖点 |
| --- | --- |
| `CryptoService` | 加解密往返、单行 Base64 存储格式、随机 salt/nonce、错密码/篡改密文判定为 `Ambiguous`、非 Base64 / magic 不符 / 长度不足判定为明确损坏 |
| `GitService.Sanitize` | 合法字符集与 64 长度上限、拦截路径穿越（`/`、`\.`）与空白/标点 |
| `CredentialManager.TargetForUrl` | 由 URL 推导 `git:{scheme}://{host}[:port]`、非 URL 回退 |
| `ConfigStore` | 明文不落盘（断言 `dpapi:`）、Load 回填明文、缺文件/损坏 JSON 返回默认、不勾选"记住"则不持久化密钥 |
| `MainWindow` | 启动冒烟：构造 + `Show()` 不抛异常（回归 TabSync 的 `Checked` 在 XAML 加载期提前触发导致 `MainFrame` 空引用），且默认落地页为 `SyncPage` |

> - `ConfigStore` 通过构造函数注入临时目录（`new ConfigStore(dir)`），测试不读写你的真实配置。
> - 仅 `MainWindowTests` 会短暂 `Show()` 主窗口（跑在独立 STA 线程上），因而会**只读地**加载真实 `%APPDATA%\EnvEditor\config.json` 与本机变量列表——测试期间屏幕上会闪过一个窗口，属预期现象。

---

## 第一次使用

1. **填设置区**：同步 ID（自定义字符串，同 ID 的机器共享一份配置）、仓库 URL（HTTPS）、分支（如 `main`）、用户名、邮箱、PAT。
   - 若系统凭据管理器里已存有该仓库的 PAT，可勾选"使用系统凭据管理器"，不必在此填 PAT。
   - 点击「保存配置」持久化（敏感字段经 DPAPI 加密，明文不落盘）。
2. **点「拉取」**：从远端取回加密文件并解密，界面列出每个变量的本地值 / 远端值 / 状态。
   - 密钥遗忘后可勾选「记住密钥」，**但必须再点一次「保存配置」**才会写入本机（下次启动自动回填）。
3. **勾选要同步的变量**：白名单即"下次上传哪些"。高危变量有红色警示；取消勾选且远端已有的变量会显示为"未勾选 → 下次上传将从远端移除"，勾选框改动会实时反映到状态列。
4. **「应用到本机」**：把远端值写入本机（新增或覆盖），执行前自动备份。
5. **「上传」**：把本机勾选的变量加密后推送到远端；若本次有未勾选的远端变量被移除，状态栏会写明移除了几个。
6. 换另一台机器，用**同一个同步 ID** 重复 1–4，即可同步。

---

## 本地变量管理（独立功能）

界面提供「+新增本机变量 / 编辑 / 删除本机变量」按钮，直接改 `HKCU\Environment` 并广播刷新。
这是与云端同步**解耦**的能力：本地删除直接改注册表、不可经云端恢复；云端同步只新增/修改本机、不会删除本机变量。

---

## 安全说明

- **远端加密**：远端 `profiles/{syncId}.enc` 是密文，没有你的密钥解不开。
- **密钥遗忘 = 数据永久不可解密**：本工具不保存明文密钥的可恢复副本。
- **凭据等价**：本程序 DPAPI 存储与 Windows 凭据管理器底层都是 DPAPI，安全等级一致；"系统凭据"选项的价值是便利与单一来源，不是安全升级。
- **环境变量改动只对新建进程生效**：已运行的程序需重启才能看到新值。

---

## 已知限制

1. 仅支持 HTTPS + PAT，不支持 SSH（LibGit2Sharp 原生库限制）。
2. 仅用户变量 `HKCU\Environment`，不管系统变量（无需管理员权限）。
3. 多机同时上传同一同步 ID 为"后写者胜"，请避免并发修改同一变量集合。
4. 建议为本工具使用**独立的 Git 仓库**，避免他人 force-push 误伤。
5. 原生 `git2` 库通过 NuGet 随包发布，多文件模式下直接在 `publish/` 目录加载，无需解包。
6. 上传是**整体覆盖**而非增量合并：payload 只包含本次勾选的变量，未勾选的远端变量会被删除。反向的"拉取 / 应用到本机"只新增或覆盖本机变量，永不删除本机变量。

---

## 数据与配置位置

- 配置：`%APPDATA%\EnvEditor\config.json`（PAT / 密码经 DPAPI）
- Git 工作副本（纯缓存，可删）：`%LOCALAPPDATA%\EnvEditor\repo`
- 备份：`%LOCALAPPDATA%\EnvEditor\backups\env-{yyyyMMdd-HHmmss}.json`（保留最近 20 份）
