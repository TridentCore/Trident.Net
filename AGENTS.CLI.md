# Trident.Net CLI — Agent Guide

## Architecture

```
Commands/    → Spectre.Console.Cli ICommand implementations (terminal UI, rich output)
Operations/  → Static business logic layer shared by Commands and Tools
Tools/       → MCP [McpServerTool] implementations (JSON output, reuses Operations)
Services/    → CLI infrastructure (stores, context resolution, output abstraction)
Utilities/   → CLI-specific helpers
```

**Key rule:** Commands and Tools must NOT contain business logic directly. They delegate to `Operations/` static methods. Commands handle terminal presentation (Spectre tables, panels, colors). Tools handle MCP JSON serialization.

## Entry Point

1. `Program.Main` → `CliContext.Parse(args)` extracts global options (`--home`, `--json`, `--no-interactive`, `--verbose`, `--debug`, `--mcp`).
2. Home directory resolution: walk upward from CWD looking for `.trident/`, fall back to `~/.trident`.
3. `Startup.ConfigureServices()` registers all DI services.
4. If `--mcp`: `McpHost.RunAsync()` starts the MCP stdio server with tools from assembly.
5. Otherwise: `Startup.ConfigureCommands()` registers the Spectre command tree, then `app.RunAsync()`.

## MCP Conventions

- **Naming:** All tools use `trident_{category}_{action}` (e.g. `trident_instance_inspect`). The `Name` property on `[McpServerTool]` must always be set explicitly — never rely on the default method-name-only convention.
- **Return type:** `string`, JSON-serialized via `JsonSerializer.Serialize(result, McpJson.Options)`.
- **Parameters:** Use `[Description("...")]` for LLM-consumable documentation.
- **DI:** Constructor parameters on the `[McpServerToolType]` class.

## Adding a New Feature

1. Add a static method to the relevant `Operations/XxxOperation.cs`.
2. Add a `Commands/Xxx/XxxCloneCommand.cs` inheriting `Command<TSettings>`, calling the operation.
3. Register the command in `Startup.ConfigureCommands()`.
4. Add a `[McpServerTool(Name = "trident_xxx_clone")]` in `Tools/XxxTools.cs`, calling the same operation.

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
