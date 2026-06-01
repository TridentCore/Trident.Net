# Trident.Net — Agent Guide

## Project Overview

Trident.Net is a .NET 10 toolchain for managing **Minecraft instances, modpacks, package repositories, and accounts**. Instances are declarative (driven by `profile.json`), rebuildable, importable, exportable, and friendly to automation.

The repository provides both a **library** surface (for embedding in launchers or tools) and a **CLI product** (`trident`) installable as a .NET global tool. The CLI also ships an **MCP server mode** (`--mcp`) for AI agent integration.

## Repository Layout

```
src/TridentCore.Abstractions/   Pure models, interfaces, enums, extension methods
src/TridentCore.Purl/           Package URL (PURL) parsing and formatting library
src/TridentCore.Core/           Business logic, engines, API clients, services
src/TridentCore.Cli/            The `trident` CLI product (Commands + MCP Tools)
```

Layering: `Abstractions ← Purl ← Core ← Cli`. Dependencies flow downward only.

## Core Concepts

- **Profile** (`profile.json`) — Declarative description of a Minecraft instance: game version, loader, packages, rules, and runtime overrides. Managed by `ProfileManager`.
- **PURL** (Package URL) — Identifier format `repository:projectId@versionId`, e.g. `modrinth:aC3cM3Vq@9I21YYxf`. Parsed by `TridentCore.Purl`.
- **Instance** — A concrete deployment of a profile on disk, built by the staged `DeployEngine` pipeline.
- **Repository** — Package source abstraction (Modrinth, CurseForge, user-configured). Queried through `RepositoryAgent`.
- **Loader** — Mod loader (Forge, NeoForge, Fabric, Quilt). Metadata via PrismLauncher API.
- **Lock Data** (`data.lock.json`) — Snapshotted deployment state for rebuild and diff.

## CLI Project (`src/TridentCore.Cli/`)

### Purpose

The CLI is the user-facing product layer. It provides two entry points:

1. **Interactive CLI** — Spectre.Console.Cli based terminal tool with rich formatting.
2. **MCP Server** — `--mcp` flag starts a stdio-based ModelContextProtocol server for AI tool use.

### Architecture Pattern

```
Commands/    → Spectre.Console.Cli ICommand implementations (terminal UI, rich output)
Operations/  → Static business logic layer shared by Commands and Tools
Tools/       → MCP [McpServerTool] implementations (JSON output, reuses Operations)
Services/    → CLI infrastructure (stores, context resolution, output abstraction)
Utilities/   → CLI-specific helpers
```

**Key rule:** Commands and Tools must NOT contain business logic directly. They delegate to `Operations/` static methods. Commands handle terminal presentation (Spectre tables, panels, colors). Tools handle MCP JSON serialization.

### Entry Point Flow

1. `Program.Main` → `CliContext.Parse(args)` extracts global options (`--home`, `--json`, `--no-interactive`, `--verbose`, `--debug`, `--mcp`).
2. Home directory resolution: walk upward from CWD looking for `.trident/`, fall back to `~/.trident`.
3. `Startup.ConfigureServices()` registers all DI services.
4. If `--mcp`: `McpHost.RunAsync()` starts the MCP stdio server with tools from assembly.
5. Otherwise: `Startup.ConfigureCommands()` registers the Spectre command tree, then `app.RunAsync()`.

### Adding a New Feature

To add a feature (e.g. `instance clone`):

1. Add a static method to the relevant `Operations/XxxOperation.cs` class.
2. Add a `Commands/Xxx/XxxCloneCommand.cs` inheriting `Command<TSettings>`, calling the operation.
3. Register the command in `Startup.ConfigureCommands()`.
4. Add a `[McpServerTool(Name = "trident_xxx_clone")]` method in `Tools/XxxTools.cs`, calling the same operation.
5. Update the coverage table in this file.

### MCP Tool Naming Convention

All MCP tools use the naming pattern `trident_{category}_{action}`:

- `trident_account_list`
- `trident_instance_inspect`
- `trident_package_search`

The `Name` property on `[McpServerTool]` must always be set explicitly. Never rely on the default method-name-only convention, as it causes registration conflicts.

### MCP Tool Implementation

- Tool methods return `string` (JSON-serialized via `JsonSerializer.Serialize(result, McpJson.Options)`).
- Parameters use `[Description("...")]` for LLM-consumable documentation.
- Dependency injection works via constructor parameters on the `[McpServerToolType]` class.

## Development Conventions

- **Framework:** .NET 10, C# 13
- **File-scoped namespaces**, implicit usings
- **Primary constructors** preferred
- **`var`** preferred everywhere
- **Expression-bodied members** preferred
- **Private fields:** `_camelCase`
- **Coding style:** See `.editorconfig` for full rules
- **Solution file:** `Trident.slnx`
- **Build:** `dotnet build Trident.slnx`

## Coverage Table

### Account

| Function | Operation | Command | Tool |
|----------|-----------|---------|------|
| List accounts | `AccountOperation.List` | `account list` | `trident_account_list` |
| Add offline account | `AccountOperation.AddOffline` | `account add --type offline` | `trident_account_add_offline` |
| Add Microsoft account | — | `account add --type microsoft` | — |
| Remove account | `AccountOperation.Remove` | `account remove` | `trident_account_remove` |

> **Note:** `account add --type microsoft` involves an interactive device-code OAuth flow with CLI status output (`output.StatusAsync`), making it impractical to extract to a pure Operation. The Command retains this logic directly.

### Config

| Function | Operation | Command | Tool |
|----------|-----------|---------|------|
| Get value | `ConfigOperation.Get` | `config get` | `trident_config_get` |
| Set value | `ConfigOperation.Set` | `config set` | `trident_config_set` |
| Unset value | `ConfigOperation.Unset` | `config unset` | `trident_config_unset` |
| List values | `ConfigOperation.List` | `config list` | `trident_config_list` |

### Instance

| Function | Operation | Command | Tool |
|----------|-----------|---------|------|
| List instances | `InstanceOperation.List` | `instance list` | `trident_instance_list` |
| Inspect instance | `InstanceOperation.Inspect` | `instance inspect` | `trident_instance_inspect` |
| Create instance | `InstanceOperation.Create` | `instance create` | `trident_instance_create` |
| Build instance | `InstanceOperation.BuildAsync` | `instance build` | `trident_instance_build` |
| Run instance | — | `instance run` | — |
| Delete instance | `InstanceOperation.Delete` | `instance delete` | `trident_instance_delete` |
| Reset instance | `InstanceOperation.Reset` | `instance reset` | `trident_instance_reset` |
| Unlock instance | `InstanceOperation.Unlock` | `instance unlock` | `trident_instance_unlock` |
| Import instance | `InstanceOperation.ImportAsync` | `instance import` | `trident_instance_import` |
| Export instance | `InstanceOperation.ExportAsync` | `instance export` | `trident_instance_export` |

> **Note:** `instance run` is tightly coupled to CLI output (progress bars, scrap stream subscription, rich console formatting, process lifecycle management). Extracting to Operation is impractical; the Command retains all logic.

### Loader

| Function | Operation | Command | Tool |
|----------|-----------|---------|------|
| List loaders | `LoaderOperation.List` | `loader list` | `trident_loader_list` |
| List loader versions | `LoaderOperation.VersionList` | `loader version list` | `trident_loader_version_list` |
| Get instance loader | `LoaderOperation.Get` | `loader get` | `trident_loader_get` |
| Set instance loader | `LoaderOperation.Set` | `loader set` | `trident_loader_set` |

### Package

| Function | Operation | Command | Tool |
|----------|-----------|---------|------|
| List packages | `PackageOperation.List` | `package list` | `trident_package_list` |
| Search packages | `PackageOperation.SearchLocal` / `SearchRemote` | `package search` | `trident_package_search` |
| Add package | `PackageOperation.Add` | `package add` | `trident_package_add` |
| Inspect package | `PackageOperation.Inspect` | `package inspect` | `trident_package_inspect` |
| Enable/disable package | `PackageOperation.SetEnabled` | `package enable` / `disable` | `trident_package_set_enabled` |
| List dependencies | `PackageOperation.DependencyList` | `package dependency list` | `trident_package_dependency_list` |
| List dependents | `PackageOperation.DependentList` | `package dependent list` | `trident_package_dependent_list` |
| List versions | `PackageOperation.VersionList` | `package version list` | `trident_package_version_list` |
| Set version | `PackageOperation.VersionSet` | `package version set` | `trident_package_version_set` |

### Repository

| Function | Operation | Command | Tool |
|----------|-----------|---------|------|
| List repositories | `RepositoryOperation.List` | `repository list` | `trident_repository_list` |
| Repository status | `RepositoryOperation.Status` | `repository status` | `trident_repository_status` |
| Add repository | `RepositoryOperation.Add` | `repository add` | `trident_repository_add` |
| Remove repository | `RepositoryOperation.Remove` | `repository remove` | `trident_repository_remove` |
