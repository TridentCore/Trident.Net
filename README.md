<div align="center">

<h1>Trident.Net</h1>

<p><strong>Declarative Minecraft instance tooling: core libraries, modpack pipelines, and a command-line product.</strong></p>

<p>
  <a href="https://dotnet.microsoft.com/"><img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white"></a>
  <a href="https://www.nuget.org/packages/TridentCore.Cli"><img alt="NuGet TridentCore.Cli" src="https://img.shields.io/nuget/v/TridentCore.Cli?style=for-the-badge&logo=nuget&logoColor=white&label=TridentCore.Cli"></a>
  <a href="docs/CLI.md"><img alt="CLI Docs" src="https://img.shields.io/badge/docs-CLI-2563EB?style=for-the-badge"></a>
  <img alt="Minecraft" src="https://img.shields.io/badge/Minecraft-instance_toolkit-62B47A?style=for-the-badge">
</p>

<p>
  <a href="README.zh.md">简体中文</a>
  ·
  <a href="#trident-as-a-library">Library</a>
  ·
  <a href="#trident-as-a-cli">CLI</a>
  ·
  <a href="#repository-layout">Repository Layout</a>
  ·
  <a href="#ai-disclosure">AI Disclosure</a>
</p>

</div>

Trident.Net is the .NET implementation of Trident: a set of core libraries for Minecraft instances, modpacks, package repositories, and accounts, plus the `trident` command-line tool built on the same core capabilities.

Trident keeps an instance declarative, rebuildable, importable, exportable, and friendly to automation. The libraries define the model and execution engine; the CLI exposes those capabilities as a product-ready entry point for local play, modpack maintenance, and CI/CD publishing.

## One Model, Two Entrypoints

The core object in Trident is `profile.json`. It describes the game version, loader, packages, rules, and runtime overrides. During deployment, Core resolves the profile into a launchable `.minecraft` directory. The CLI provides commands for creating, importing, building, running, exporting, and managing packages.

```text
TridentCore.Abstractions  -> file models, repository interfaces, trackers, account interfaces
TridentCore.Core          -> instance management, deploy/run engine, import/export, remote repositories, auth services
TridentCore.Purl          -> Trident package URL parsing and formatting
TridentCore.Cli           -> end-user trident command
```

## Trident As A Library

This section is for developers embedding Trident into launchers, desktop apps, server tools, or automation systems.

### Data Layout

Trident only manages data under the selected home directory. By default, home is resolved by walking up from the current directory and looking for `.trident`; if none is found, Trident falls back to `~/.trident`. Host applications can override `PathDef.Default` or `PathDef.HomeLocatorDefault` before first use.

```text
.trident/
├── cache/
│   ├── assets/              # Minecraft assets indexes/objects
│   ├── libraries/           # Minecraft libraries
│   ├── packages/            # repository package files and metadata
│   └── runtimes/            # Java runtimes
├── instances/
│   └── {key}/
│       ├── profile.json     # declarative instance metadata
│       ├── data.lock.json   # deployment lock data
│       ├── data.pack.json   # pack data
│       ├── build/           # projected .minecraft output
│       ├── import/          # imported layer, usually from modpacks or exportable files
│       ├── live/            # mutable runtime layer for imported content
│       └── persist/         # user-persistent data such as saves, screenshots, options.txt
└── .trident.cli/
    ├── accounts.json        # CLI-private account configuration
    └── repositories.json    # CLI-private repository configuration
```

### Core Concepts

- Profile: the declarative instance entrypoint, including name, Minecraft version, loader, package PURLs, rules, and runtime overrides.
- Deploy: combines the profile, remote metadata, cached files, and local layers into `build/`, then writes `data.lock.json`.
- Layer: `import/` stores modpack or exportable files, `live/` stores runtime mutations to imported content, and `persist/` stores user data.
- Projection: deployment incrementally projects the virtual file structure into `build/`, usually by creating symlink relationships and removing stale relationships.
- Repository: a unified interface for package sources such as Modrinth and CurseForge; package identities use Trident PURLs.
- Tracker: deploy, install, update, and run operations expose state, stage, and progress through trackers for UI and CLI subscribers.

### Capabilities

- Create, scan, update, and delete managed instances.
- Deploy vanilla Minecraft, loaders, runtimes, dependencies, and build artifacts.
- Run instances with offline accounts, Microsoft accounts, memory/window options, Java home, and quick-connect settings.
- Import and export `trident`, `modrinth`, and `curseforge` modpack formats.
- Query packages, versions, dependencies, and reverse dependencies inside an instance.
- Resolve Forge, NeoForge, Fabric, and Quilt loaders through PrismLauncher metadata.

### Integration

Core is primarily integrated through dependency injection. `src/TridentCore.Cli/Startup.cs` is the most complete host example and shows how to register HTTP clients, caches, importers, exporters, remote services, and core managers.

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

In your own application, prefer these managers over direct file manipulation: `ProfileManager` manages the profile lifecycle, `InstanceManager` deploys and runs instances, `RepositoryAgent` queries repositories, and `ImporterAgent` plus `ExporterAgent` convert modpacks.

## Trident As A CLI

This section is for modpack authors, server maintainers, and users who want to manage Minecraft instances from the terminal.

`trident` is the productized command-line entrypoint for Trident. It can create an instance from scratch, import existing modpacks, search and install packages, build launchable directories, log in accounts, run the game, and export the same instance into multiple modpack formats.

### Use Cases

- Maintain a locally rebuildable Minecraft instance.
- Keep core modpack metadata in Git, then build and export releases from commands.
- Generate Trident, Modrinth, or CurseForge artifacts in CI.
- Chain scripts with JSON output, for example by piping package search results into install commands.
- Manage multiple instances, accounts, and repository configurations under one `.trident` home.

### Installation

Install the CLI as a NuGet global tool:

```sh
dotnet tool install --global TridentCore.Cli
```

After installation, invoke the tool with the `trident` command:

```sh
trident --help
```

Update or uninstall it with:

```sh
dotnet tool update --global TridentCore.Cli
dotnet tool uninstall --global TridentCore.Cli
```

The examples below assume `trident` is already on PATH. If a newly installed tool is not found in the current shell, verify that the .NET global tools directory has been added to PATH.

### Quick Start

```sh
trident create --identity cherry_picks --name "Cherry Picks" --version 1.21.1 --loader net.neoforged:21.1.200
trident add --instance cherry_picks modrinth:aC3cM3Vq@9I21YYxf
trident build --instance cherry_picks
trident run --instance cherry_picks --username Steve
trident instance export --instance cherry_picks --format modrinth --type online --author d3ara1n --output ./releases/cherry-picks.mrpack
```

### Global Options

Global options are preprocessed before command dispatch and can appear anywhere in the command line.

| Option | Purpose |
| --- | --- |
| `--home <path>` / `--home=<path>` | Sets the Trident home directory and overrides automatic `.trident` discovery. |
| `--json` | Forces structured JSON output. |
| `--no-interactive` | Disables prompts, spinners, and progress UI; destructive commands also require `--yes`. |
| `--verbose` | Enables information-level logs. |
| `--debug` | Enables debug logs and full exceptions; also enables verbose output. |

When stdout is redirected, the CLI automatically prefers JSON output for pipeline and scripting scenarios.

### Command Overview

| Scenario | Commands |
| --- | --- |
| Instances | `trident instance create/list/inspect/build/import/export/unlock/reset/delete/run` |
| Shortcuts | `trident create/import/build/run/list/inspect` |
| Loaders | `trident loader list/get/set`, `trident loader version list` |
| Packages | `trident package list/search/add/inspect/enable/disable` |
| Package relations | `trident package dependency list`, `trident package dependent list` |
| Package versions | `trident package version list/set` |
| Package shortcuts | `trident search`, `trident add` |
| Accounts | `trident account list/add/remove` |
| Repositories | `trident repository list/status/add/remove` |

Commands that need an instance context resolve it in this order: `--instance <key>`, `--profile <path>`, then a managed `profile.json` in the current directory or one of its parents. Common short options include `-I|--instance`, `-R|--repository`, `-v|--version`, `-n|--name`, `-i|--id`, `-l|--loader`, `-y|--yes`, `-A|--account`, and `-u|--username`.

### Workflow Examples

Create and build an instance:

```sh
trident create --identity vanilla --name "Vanilla 1.21.1" --version 1.21.1
trident build --instance vanilla --full-check
```

Import, run, and reset a modpack:

```sh
trident import --identity imported_pack --name "Imported Pack" ./modpack.zip
trident run --instance imported_pack --username Steve --max-memory 6144
trident instance reset --instance imported_pack --yes
```

Search, install, and switch package versions:

```sh
trident package search --repository modrinth --kind mod --version 1.21.1 --loader net.neoforged "Mouse Tweaks"
trident package add --instance cherry_picks modrinth:aC3cM3Vq@9I21YYxf
trident package version list --version 1.21.1 --loader net.neoforged modrinth:aC3cM3Vq
trident package version set --instance cherry_picks modrinth:aC3cM3Vq@9I21YYxf
```

Pipe search results into an install command:

```sh
trident --json package search --repository modrinth --kind mod "Mouse Tweaks" \
  | trident --json --no-interactive package add --instance cherry_picks
```

Accounts and repositories:

```sh
trident account add --type offline --username Steve
trident account add --type microsoft
trident repository add --label modrinth-cn --driver modrinth --endpoint https://api.modrinth.com --user-agent "TridentCli"
trident repository status --label modrinth-cn
```

### CI/CD Modpack Publishing

Trident CLI can export the same instance into multiple release formats in GitHub Actions. The example below assumes the repository contains a `.trident` home managed by the CLI, or that the workflow supplies one through `--home`.

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

### Output And Limitations

- Human-readable output uses Spectre Console tables, panels, status messages, and progress feedback.
- `--json` or stdout redirection emits structured JSON; Microsoft device-code login prompts still write to stderr.
- CLI account and repository secrets are stored in `<trident-home>/.trident.cli/*.json`; the current implementation does not encrypt them with the system keychain.
- `package dependent list` scans local reverse dependencies inside an instance; it is not a global reverse-dependency query against remote repositories.
- See [`docs/CLI.md`](docs/CLI.md) for more CLI details and validation notes.

## Repository Layout

| Path | Description |
| --- | --- |
| `src/TridentCore.Abstractions/` | Shared models, interfaces, and utilities. |
| `src/TridentCore.Core/` | Core business logic, deployment/run engine, import/export, and remote services. |
| `src/TridentCore.Purl/` | Trident package URL parsing and formatting. |
| `src/TridentCore.Cli/` | The `trident` command-line product. |
| `docs/CLI.md` | Detailed CLI reference and validation notes. |

## Development

```sh
dotnet restore Trident.slnx
dotnet build Trident.slnx
dotnet pack src/TridentCore.Cli/TridentCore.Cli.csproj --configuration Release
```

## AI Disclosure

| Project | AI disclosure |
| --- | --- |
| `TridentCore.Abstractions` | Human-written |
| `TridentCore.Core` | Human-written |
| `TridentCore.Purl` | Human-written |
| `TridentCore.Cli` | Vibe-coded (GPT-5.5) |

---

<div align="center">

<strong>Trident.Net</strong> keeps Minecraft instances declarative, rebuildable, and automation-friendly.

<br>

Library first. CLI packaged on NuGet. Modpack workflows included.

</div>
