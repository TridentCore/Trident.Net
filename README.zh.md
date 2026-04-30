<div align="center">

<h1>Trident.Net</h1>

<p><strong>声明式 Minecraft 实例工具链：核心库、整合包流水线与命令行产品。</strong></p>

<p>
  <a href="https://dotnet.microsoft.com/"><img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white"></a>
  <a href="https://www.nuget.org/packages/TridentCore.Cli"><img alt="NuGet TridentCore.Cli" src="https://img.shields.io/nuget/v/TridentCore.Cli?style=for-the-badge&logo=nuget&logoColor=white&label=TridentCore.Cli"></a>
  <a href="docs/CLI.md"><img alt="CLI Docs" src="https://img.shields.io/badge/docs-CLI-2563EB?style=for-the-badge"></a>
  <img alt="Minecraft" src="https://img.shields.io/badge/Minecraft-instance_toolkit-62B47A?style=for-the-badge">
</p>

<p>
  <a href="README.md">English</a>
  ·
  <a href="#trident-作为库">作为库使用</a>
  ·
  <a href="#trident-作为-cli">作为 CLI 使用</a>
  ·
  <a href="#仓库结构">仓库结构</a>
  ·
  <a href="#ai-声明">AI 声明</a>
</p>

</div>

Trident.Net 是 Trident 的 .NET 实现：一套面向 Minecraft 实例、整合包、包仓库和账号的核心库，以及基于同一套核心能力构建的 `trident` 命令行工具。

Trident 的目标是把一个实例拆成可声明、可重建、可导入导出、可被工具链自动维护的结构。库负责定义模型和执行引擎；CLI 则把这些能力包装成可以直接用于本地游玩、整合包维护和 CI/CD 发布的产品化入口。

## 一套模型，两种入口

Trident 的核心对象是 `profile.json`。它描述游戏版本、加载器、包列表、规则和运行覆盖项；部署时 Core 会把 profile 解析成可启动的 `.minecraft` 目录结构，CLI 则提供创建、导入、构建、运行、导出和包管理命令。

```text
TridentCore.Abstractions  -> 文件模型、仓库接口、任务追踪、账号接口
TridentCore.Core          -> 实例管理、部署/启动引擎、导入导出、远程仓库、认证服务
TridentCore.Purl          -> Trident 使用的包 URL 解析与生成
TridentCore.Cli           -> 面向终端用户的 trident 命令
```

## Trident 作为库

这一部分面向要把 Trident 嵌入启动器、桌面应用、服务端工具或自动化系统的开发者。

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
│       ├── build/           # 最终投影出的 .minecraft
│       ├── import/          # 导入层，通常来自整合包或需要导出的文件
│       ├── live/            # 导入内容的可变运行层
│       └── persist/         # 用户持久层，例如 saves、screenshots、options.txt
└── .trident.cli/
    ├── accounts.json        # CLI 私有账号配置
    └── repositories.json    # CLI 私有仓库配置
```

### 核心概念

- Profile：实例的声明式入口，包含名称、Minecraft 版本、Loader、包 PURL、规则和运行覆盖项。
- Deploy：把 profile、远程元数据、缓存文件和本地层合成为 `build/`，并生成 `data.lock.json`。
- Layer：`import/` 放整合包或将来要导出的文件，`live/` 保存导入内容的运行时变更，`persist/` 保存用户数据。
- Projection：部署阶段会把虚拟文件结构增量投影到 `build/`，通常通过软链接建立关系并移除多余关系。
- Repository：通过统一接口访问 Modrinth、CurseForge 等包仓库，包标识使用 Trident PURL。
- Tracker：部署、安装、更新和启动过程以 tracker 暴露状态、阶段和进度，适合 UI 或 CLI 订阅。

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
    .AddSingleton<ProfileManager>()
    .AddSingleton<RepositoryAgent>()
    .AddSingleton<ImporterAgent>()
    .AddSingleton<ExporterAgent>()
    .AddSingleton<InstanceManager>();
```

在自己的应用中使用时，优先复用这些 manager，而不是直接操作文件：`ProfileManager` 管理 profile 生命周期，`InstanceManager` 负责部署和启动，`RepositoryAgent` 负责仓库查询，`ImporterAgent` 和 `ExporterAgent` 负责整合包转换。

## Trident 作为 CLI

这一部分面向整合包作者、服务器维护者和想用终端管理 Minecraft 实例的用户。

`trident` 是 Trident 的产品化命令行入口。它能从零创建实例，导入已有整合包，搜索并安装包，构建可启动目录，登录账号，运行游戏，并把同一个实例导出成多平台整合包。

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
| `src/TridentCore.Purl/` | Trident 包 URL 解析和生成。 |
| `src/TridentCore.Cli/` | `trident` 命令行产品。 |
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
| `TridentCore.Purl` | 人工编写 |
| `TridentCore.Cli` | 氛围编写(GPT-5.5) |

---

<div align="center">

<strong>Trident.Net</strong> keeps Minecraft instances declarative, rebuildable, and automation-friendly.

<br>

Library first. CLI packaged on NuGet. Modpack workflows included.

</div>
