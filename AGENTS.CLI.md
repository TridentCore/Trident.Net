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

- **Naming:** All tools use `{category}_{action}` (e.g. `instance_inspect`). The `Name` property on `[McpServerTool]` must always be set explicitly — never rely on the default method-name-only convention.
- **Return type:** `string`, JSON-serialized via `JsonSerializer.Serialize(result, McpJson.Options)`.
- **Parameters:** Use `[Description("...")]` for LLM-consumable documentation.
- **DI:** Constructor parameters on the `[McpServerToolType]` class.

## Adding a New Feature

1. Add a static method to the relevant `Operations/XxxOperation.cs`.
2. Add a `Commands/Xxx/XxxCloneCommand.cs` inheriting `Command<TSettings>`, calling the operation.
3. Register the command in `Startup.ConfigureCommands()`.
4. Add a `[McpServerTool(Name = "xxx_clone")]` in `Tools/XxxTools.cs`, calling the same operation.

## Coverage Table

### Account

| Function | Operation | Command | Tool |
|----------|-----------|---------|------|
| List accounts | `AccountOperation.List` | `account list` | `account_list` |
| Add offline account | `AccountOperation.AddOffline` | `account add --type offline` | `account_add_offline` |
| Add Microsoft account | — | `account add --type microsoft` | — |
| Remove account | `AccountOperation.Remove` | `account remove` | `account_remove` |

> **Note:** `account add --type microsoft` involves an interactive device-code OAuth flow with CLI status output (`output.StatusAsync`), making it impractical to extract to a pure Operation. The Command retains this logic directly.

### Config

| Function | Operation | Command | Tool |
|----------|-----------|---------|------|
| Get value | `ConfigOperation.Get` | `config get` | `config_get` |
| Set value | `ConfigOperation.Set` | `config set` | `config_set` |
| Unset value | `ConfigOperation.Unset` | `config unset` | `config_unset` |
| List values | `ConfigOperation.List` | `config list` | `config_list` |

### Instance

| Function | Operation | Command | Tool |
|----------|-----------|---------|------|
| List instances | `InstanceOperation.List` | `instance list` | `instance_list` |
| Inspect instance | `InstanceOperation.Inspect` | `instance inspect` | `instance_inspect` |
| Create instance | `InstanceOperation.Create` | `instance create` | `instance_create` |
| Build instance | `InstanceOperation.BuildAsync` | `instance build` | — |
| Run instance | — | `instance run` | — |
| Delete instance | `InstanceOperation.Delete` | `instance delete` | `instance_delete` |
| Reset instance | `InstanceOperation.Reset` | `instance reset` | `instance_reset` |
| Unlock instance | `InstanceOperation.Unlock` | `instance unlock` | `instance_unlock` |
| Import instance | `InstanceOperation.ImportAsync` | `instance import` | `instance_import` |
| Export instance | `InstanceOperation.ExportAsync` | `instance export` | `instance_export` |

> **Note:** `instance run` is tightly coupled to CLI output (progress bars, scrap stream subscription, rich console formatting, process lifecycle management). Extracting to Operation is impractical; the Command retains all logic.

> **Note:** `instance build` and `instance run` are long-running, stateful operations that block until completion and produce continuous output streams. They are not suitable for MCP exposure — an LLM agent cannot meaningfully consume their progress, and accidental invocation could tie up resources or leave the environment in an incomplete state. These actions should only be triggered by the human user via the CLI.

### Loader

| Function | Operation | Command | Tool |
|----------|-----------|---------|------|
| List loaders | `LoaderOperation.List` | `loader list` | `loader_list` |
| List loader versions | `LoaderOperation.VersionList` | `loader version list` | `loader_version_list` |
| Get instance loader | `LoaderOperation.Get` | `loader get` | `loader_get` |
| Set instance loader | `LoaderOperation.Set` | `loader set` | `loader_set` |

### Package

| Function | Operation | Command | Tool |
|----------|-----------|---------|------|
| List packages | `PackageOperation.List` | `package list` | `package_list` |
| Search local packages | `PackageOperation.SearchLocal` | `package search` (with `--instance`) | `package_search` |
| Search remote packages | `PackageOperation.SearchRemote` | `package search` (with `-R`) | `package_search` |
| Add package | `PackageOperation.Add` | `package add` | `package_add` |
| Inspect package | `PackageOperation.Inspect` | `package inspect` | `package_inspect` |
| Enable package | `PackageOperation.SetEnabled` | `package enable` | `package_enable` |
| Disable package | `PackageOperation.SetEnabled` | `package disable` | `package_disable` |
| List dependencies | `PackageOperation.DependencyList` | `package dependency list` | `package_dependency_list` |
| List dependents | `PackageOperation.DependentList` | `package dependent list` | `package_dependent_list` |
| List versions | `PackageOperation.VersionList` | `package version list` | `package_version_list` |
| Set version | `PackageOperation.VersionSet` | `package version set` | `package_version_set` |

### Repository

| Function | Operation | Command | Tool |
|----------|-----------|---------|------|
| List repositories | `RepositoryOperation.List` | `repository list` | `repository_list` |
| Repository status | `RepositoryOperation.Status` | `repository status` | `repository_status` |
| Add repository | `RepositoryOperation.Add` | `repository add` | `repository_add` |
| Remove repository | `RepositoryOperation.Remove` | `repository remove` | `repository_remove` |
