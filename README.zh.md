<div align="center">

<h1>Trident.Net</h1>

<p><strong>声明式 Minecraft 实例工具链：内部 Core、整合包流水线与命令行产品。</strong></p>

<p>
  <a href="https://dotnet.microsoft.com/"><img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white"></a>
  <a href="https://www.nuget.org/packages/TridentCore.Cli"><img alt="NuGet TridentCore.Cli" src="https://img.shields.io/nuget/v/TridentCore.Cli?style=for-the-badge&logo=nuget&logoColor=white&label=TridentCore.Cli"></a>
  <a href="docs/CLI.md"><img alt="CLI Docs" src="https://img.shields.io/badge/docs-CLI-2563EB?style=for-the-badge"></a>
  <img alt="Minecraft" src="https://img.shields.io/badge/Minecraft-instance_toolkit-62B47A?style=for-the-badge">
</p>

<p>
  <a href="README.md">English</a>
  ·
  <a href="#内部-core-集成">内部 Core</a>
  ·
  <a href="#trident-作为-cli">作为 CLI 使用</a>
  ·
  <a href="#仓库结构">仓库结构</a>
  ·
  <a href="#ai-声明">AI 声明</a>
</p>

</div>

Trident.Net 是 Trident 的 .NET 实现：一组处理 Minecraft 实例、整合包、包仓库和账号的内部核心项目，以及基于这些能力构建的 `trident` 命令行工具。

Trident 的目标是把一个实例拆成可声明、可重建、可导入导出、可被工具链自动维护的结构。内部项目定义模型和执行引擎，由第一方宿主直接从源码使用：本仓库中的 CLI，以及通过 Trident.Net git submodule 使用它们的 Polymerium。CLI 是对外分发的产品入口。

## 一套模型，第一方宿主

Trident 的核心对象是 `profile.json`。它描述游戏版本、加载器、包列表、规则和运行覆盖项；部署时 Core 会把 profile 解析成可启动的 `.minecraft` 目录结构。CLI 在终端中提供这些能力，Polymerium 则以桌面应用呈现同一套模型。

```text
TridentCore.Abstractions  -> 文件模型、仓库接口、任务追踪、账号接口
TridentCore.Core          -> 实例管理、部署/启动引擎、导入导出、远程仓库、认证服务
TridentCore.Pref          -> Trident 使用的包 URL 解析与生成
TridentCore.Cli           -> 面向终端用户的 trident 命令
  ├── Operations/         -> 共享业务逻辑（Commands 和 Tools 共用）
  ├── Commands/           -> Spectre.Console.Cli 命令入口 + 富文本输出
  ├── Tools/              -> MCP Tool 入口 + JSON 序列化
```

## 内部 Core 集成

这一部分记录第一方宿主的集成方式。非 CLI 项目是随源码使用的内部实现，不是承诺兼容性的发布接口。设计改善需要调整内部 API 时应直接修改，并在同一项变更中更新所有第一方调用方；不保留兼容重载、别名、转发垫片或 obsolete 成员。

### 数据布局

Trident 只管理选定 home 目录下的数据。默认 home 会从当前目录向上查找 `.trident`，找不到时回落到用户目录下的 `~/.trident`；宿主程序也可以在首次使用前覆盖 `PathDef.Default` 或 `PathDef.HomeLocatorDefault`。

```text
.trident/
├── cache/
│   ├── assets/              # Minecraft assets indexes/objects
│   ├── libraries/           # Minecraft libraries
│   ├── packages/            # repository package files and metadata
│   └── runtimes/            # Java runtimes
├── instances/
│   └── {key}/
│       ├── profile.json     # 声明式实例元数据
│       ├── data.lock.json   # 部署锁定数据
│       ├── data.pack.json   # 打包数据
│       ├── patches/         # 外部原生 Patch 索引、规则与所属资产
│       ├── build/           # 最终投影出的 .minecraft；也承载导入内容的运行时变更
│       ├── import/          # 导入层，通常来自整合包或需要导出的文件
│       └── persist/         # 用户持久层，例如 saves、screenshots、options.txt
└── .trident.cli/
    ├── accounts.json        # CLI 私有账号配置
    └── repositories.json    # CLI 私有仓库配置
```

### 核心概念

- Profile：实例的声明式入口，包含名称、Minecraft 版本、Loader、包 Pref、规则和运行覆盖项。
- Deploy：把 profile、远程元数据、缓存文件和本地层合成为 `build/`，并生成 `data.lock.json`。
- Layer：`import/` 放整合包或将来要导出的文件（以实体文件投影进 `build/`，让游戏直接读取），`persist/` 保存用户数据（以软链接投影进 `build/`）。导入内容的运行时变更直接落在 `build/`。
- Projection：部署阶段会把虚拟文件结构增量投影到 `build/`——import 为实体文件，包与持久层为软链接。
- Repository：通过统一接口访问 Modrinth、CurseForge 等包仓库，包标识使用 Trident Pref。
- Tracker：部署、安装、更新和启动过程以 tracker 暴露状态、阶段和进度，适合 UI 或 CLI 订阅。

原生部署 Patch 属于可选的实例外部数据，由 `patches/data.patch.json` 记录启用状态和顺序；在各部署作用位置先应用导入层，再应用用户层，不向 `profile.json` 添加引用。Trident Portable Instance 只携带导入层；用户层不参与整合包导入导出，由本地快照保存。格式、管理命令和导入导出行为见[原生部署 Patch 参考](docs/PATCHES.md)。

### 主要能力

- 创建、扫描、更新和删除受管理实例。
- 部署 Minecraft 原版、加载器、运行时、依赖包和构建产物。
- 启动实例，支持离线账号、Microsoft 账号、内存、窗口、Java home 和快速连接配置。
- 导入和导出 `trident`、`modrinth`、`curseforge` 格式整合包。
- 查询包、版本、依赖和实例内反向依赖。
- 通过 PrismLauncher 元数据解析 Forge、NeoForge、Fabric、Quilt loader。

### 集成方式

Core 以依赖注入为主要集成方式。`src/TridentCore.Cli/Startup.cs` 是当前最完整的宿主示例，展示了如何注册 HTTP client、缓存、导入器、导出器、远程服务和核心 manager。

```csharp
services.AddMemoryCache();
services.AddDistributedMemoryCache();

services
    .AddTransient<IProfileImporter, TridentImporter>()
    .AddTransient<IProfileImporter, CurseForgeImporter>()
    .AddTransient<IProfileImporter, ModrinthImporter>()
    .AddTransient<IProfileExporter, TridentExporter>()
    .AddTransient<IProfileExporter, CurseForgeExporter>()
    .AddTransient<IProfileExporter, ModrinthExporter>()
    .AddLifetimeRuntime()
    .AddPrismLauncher()
    .AddMojangLauncher()
    .AddMicrosoft()
    .AddXboxLive()
    .AddMinecraft()
    .AddMclogs()
    .AddTransient<PackageResolver>()
    .AddTransient<PackagePlanner>()
    .AddTransient<ProjectionArbitrator>()
    .AddTransient<SourceProjectionPlanner>()
    .AddTransient<PackageMaterializer>()
    .AddTransient<DeploymentPlanner>()
    .AddTransient<DeploymentDiffer>()
    .AddTransient<DeploymentIndexService>()
    .AddSingleton<ProfileManager>()
    .AddSingleton<RepositoryAgent>()
    .AddSingleton<ImporterAgent>()
    .AddSingleton<ExporterAgent>()
    .AddSingleton<InstanceManager>();
```

第一方宿主应优先复用这些 manager，而不是直接操作文件：`ProfileManager` 管理 profile 生命周期，`InstanceManager` 负责部署和启动，`RepositoryAgent` 负责仓库查询，`ImporterAgent` 和 `ExporterAgent` 负责整合包转换。

资源规划可独立于 `DeployEngine` 使用：`LockValidationHelper` 验证已解析需求，`SourceProjectionPlanner` 扫描受管来源，`ProjectionArbitrator` 以不读写文件系统的纯逻辑应用路径优先级，`DeploymentPlanner` 组装库与投影目标视图，`DeploymentDiffer` 再将目标视图与当前运行目录比较；`AssetPlanner` / `RuntimePlanner` 展开可读的本地索引。这些组件不联网、不写文件。部署消费方可用 `DeploymentIndexService` 补齐缺失索引，就绪检查消费方则直接停止。部署按锁内可选 major 准备 Mojang Java 运行时，不受用户启动偏好影响；用户 Java Home 仅在启动时选择。

## Trident 作为 CLI

这一部分面向整合包作者、服务器维护者和想用终端管理 Minecraft 实例的用户。

`trident` 是 Trident 的产品化命令行入口。它能从零创建实例，导入已有整合包，搜索并安装包，构建可启动目录，登录账号，运行游戏，并把同一个实例导出成多平台整合包。

[![asciicast](https://asciinema.org/a/1261198.svg)](https://asciinema.org/a/1261198)

### 适合做什么

- 本地维护一个可重复构建的 Minecraft 实例。
- 把整合包的核心元数据放进 Git，通过命令构建和导出发布包。
- 在 CI 中自动生成 Trident、Modrinth 或 CurseForge 格式产物。
- 用 JSON 输出串联脚本，例如搜索包后通过管道安装。
- 在同一个 `.trident` home 下管理多个实例、账号和仓库配置。

### 安装方式

从 NuGet 安装 CLI global tool：

```sh
dotnet tool install --global TridentCore.Cli
```

安装后可以直接使用 `trident` 命令：

```sh
trident --help
```

更新或卸载：

```sh
dotnet tool update --global TridentCore.Cli
dotnet tool uninstall --global TridentCore.Cli
```

下面的示例默认 `trident` 已经在 PATH 中。如果刚安装后当前 shell 找不到命令，请确认 .NET global tool 目录已经加入 PATH。

### 快速开始

```sh
trident create --identity cherry_picks --name "Cherry Picks" --version 1.21.1 --loader net.neoforged:21.1.200
trident add --instance cherry_picks modrinth:aC3cM3Vq@9I21YYxf
trident build --instance cherry_picks
trident run --instance cherry_picks --username Steve
trident instance export --instance cherry_picks --format modrinth --type online --author d3ara1n --output ./releases/cherry-picks.mrpack
```

### 全局选项

全局选项会在命令派发前预处理，可以放在命令参数中的任意位置。

| Option | 用途 |
| --- | --- |
| `--home <path>` / `--home=<path>` | 指定 Trident home，覆盖自动查找的 `.trident`。 |
| `--json` | 强制结构化 JSON 输出。 |
| `--no-interactive` | 禁用提示、spinner 和进度 UI；破坏性命令需要配合 `--yes`。 |
| `--verbose` | 输出信息级日志。 |
| `--debug` | 输出调试日志和完整异常；同时启用 verbose。 |
| `--mcp` | 以 MCP（Model Context Protocol）服务器模式启动，通过 stdio 通信。隐含 `--json` 和 `--no-interactive`。 |

当 stdout 被重定向时，CLI 会自动倾向输出 JSON，方便管道和脚本消费。

### 命令总览

| 场景 | 命令 |
| --- | --- |
| 实例 | `trident instance create/list/inspect/build/import/export/unlock/reset/delete/run` |
| 快捷方式 | `trident create/import/build/run/list/inspect` |
| Loader | `trident loader list/get/set`、`trident loader version list` |
| 包 | `trident package list/search/add/inspect/enable/disable` |
| 包关系 | `trident package dependency list`、`trident package dependent list` |
| 包版本 | `trident package version list/set` |
| 包快捷方式 | `trident search`、`trident add` |
| 账号 | `trident account list/add/remove` |
| 仓库 | `trident repository list/status/add/remove` |

需要实例上下文的命令按顺序解析：`--instance <key>`、`--profile <path>`、当前目录或父目录中的受管理 `profile.json`。常用短选项包括 `-I|--instance`、`-R|--repository`、`-v|--version`、`-n|--name`、`-i|--id`、`-l|--loader`、`-y|--yes`、`-A|--account`、`-u|--username`。

### 工作流示例

创建并构建实例：

```sh
trident create --identity vanilla --name "Vanilla 1.21.1" --version 1.21.1
trident build --instance vanilla --full-check
```

导入、运行和重置整合包：

```sh
trident import --identity imported_pack --name "Imported Pack" ./modpack.zip
trident run --instance imported_pack --username Steve --max-memory 6144
trident instance reset --instance imported_pack --yes
```

搜索、安装和切换包版本：

```sh
trident package search --repository modrinth --kind mod --version 1.21.1 --loader net.neoforged "Mouse Tweaks"
trident package add --instance cherry_picks modrinth:aC3cM3Vq@9I21YYxf
trident package version list --version 1.21.1 --loader net.neoforged modrinth:aC3cM3Vq
trident package version set --instance cherry_picks modrinth:aC3cM3Vq@9I21YYxf
```

用管道把搜索结果交给安装命令：

```sh
trident --json package search --repository modrinth --kind mod "Mouse Tweaks" \
  | trident --json --no-interactive package add --instance cherry_picks
```

账号和仓库：

```sh
trident account add --type offline --username Steve
trident account add --type microsoft
trident repository add --label modrinth-cn --driver modrinth --endpoint https://api.modrinth.com --user-agent "TridentCli"
trident repository status --label modrinth-cn
```

### MCP 服务器

Trident 可以作为 MCP（Model Context Protocol）服务器运行，把各项能力作为工具暴露给 AI agent 和 MCP 客户端：

```sh
trident --mcp
```

服务器通过 stdio 通信。在 MCP 客户端（如 Claude Desktop、opencode）中配置：

```json
{
  "mcpServers": {
    "trident": {
      "command": "trident",
      "args": ["--mcp"]
    }
  }
}
```

可用工具：

| 工具 | 说明 |
| --- | --- |
| `List` (InstanceTools) | 列出所有实例。 |
| `Inspect` (InstanceTools) | 查看实例详情和包预览。 |
| `List` (PackageTools) | 列出实例中的已安装包。 |
| `Search` (PackageTools) | 在远程仓库或实例内搜索包。 |
| `Add` (PackageTools) | 通过 Pref 添加包到实例。 |
| `Inspect` (PackageTools) | 通过 Pref 查看包详情。 |
| `SetEnabled` (PackageTools) | 启用或禁用已安装的包。 |
| `Get` / `Set` / `Unset` / `List` (ConfigTools) | 管理配置值。 |
| `List` / `Status` (RepositoryTools) | 列出仓库和检查状态。 |
| `List` (AccountTools) | 列出已注册账号。 |
| `List` / `VersionList` (LoaderTools) | 列出支持的加载器和查询版本。 |

所有工具返回 JSON。MCP 模式下同样支持 `--home` 选项。

#### 能力边界

MCP 接口刻意排除了那些不可逆、资源占用重、涉及外部信任协商，或在整个软件生命周期内只触发零到一次的操作：

- **启动游戏** —— `trident run` 会拉起一个占用 CPU/GPU/内存的重型进程，并写入存档；“游戏已经在运行”不是 agent 能回滚的状态。
- **OAuth 账号登录** —— Microsoft device-code 登录是一次性的、与身份提供方的信任协商，用户在 GUI 中点一次即可。让 agent 驱动它毫无收益，却要触碰凭据生命周期。

接口上保留的是其余部分：可逆、幂等、数据层的操作，例如包 Pref、版本选择、依赖分析、导入导出。这是一份设计契约，不是待办清单——未经先行讨论，不要添加启动或凭据生命周期类工具。

### CI/CD 发布整合包

Trident CLI 可以在 GitHub Actions 中把同一个实例导出为多个发行格式。下面示例假定仓库内有可被 CLI 管理的 `.trident` home，或者通过 `--home` 指定构建用目录。

```yaml
name: Build and Publish Modpack

on:
  push:
    tags:
      - v*

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Install CLI
        run: dotnet tool install --global TridentCore.Cli

      - name: Export packs
        run: |
          trident instance export --format trident --type online --author d3ara1n --output Releases/trident.zip
          trident instance export --format curseforge --type online --author d3ara1n --output Releases/curseforge.zip
          trident instance export --format modrinth --type online --author d3ara1n --output Releases/modrinth.mrpack

      - name: Create Release
        uses: softprops/action-gh-release@v2
        with:
          name: ${{ github.ref_name }}
          tag_name: ${{ github.ref_name }}
          files: Releases/*
```

### 输出与限制

- 人类可读输出使用 Spectre Console 的表格、面板、状态和进度反馈。
- `--json` 或 stdout 重定向时会输出结构化 JSON；Microsoft device-code 登录提示仍会写入 stderr。
- CLI 账号和仓库密钥会保存在 `<trident-home>/.trident.cli/*.json`，当前实现不会使用系统 keychain 加密。
- `package dependent list` 是实例本地反向依赖扫描，不是远程仓库的全局反向依赖查询。
- 更多 CLI 细节和验证清单见 [`docs/CLI.md`](docs/CLI.md)。

## 仓库结构

| 路径 | 说明 |
| --- | --- |
| `src/TridentCore.Abstractions/` | 抽象模型、接口和共享工具。 |
| `src/TridentCore.Core/` | 核心业务逻辑、部署/启动、导入导出、远程服务。 |
| `src/TridentCore.Pref/` | Trident 包引用解析和生成。 |
| `src/TridentCore.Cli/` | `trident` 命令行产品（CLI + MCP 服务器）。 |
| `docs/CLI.md` | CLI 详细参考和验证清单。 |

## 开发

```sh
dotnet restore Trident.slnx
dotnet build Trident.slnx
dotnet pack src/TridentCore.Cli/TridentCore.Cli.csproj --configuration Release
```

## AI 声明

| 项目 | AI 含量声明 |
| --- | --- |
| `TridentCore.Abstractions` | 人工编写 |
| `TridentCore.Core` | 人工编写 |
| `TridentCore.Pref` | 人工编写 |
| `TridentCore.Cli` | 氛围编写(GPT-5.5) |

---

<div align="center">

<strong>Trident.Net</strong> keeps Minecraft instances declarative, rebuildable, and automation-friendly.

<br>

内部 Core。CLI 通过 NuGet 分发。内置整合包工作流。

</div>
