# Trident CLI

Trident CLI is the command-line entry point for Trident instance, loader, package, repository, and account management.

## Global Options

Global options are preprocessed before command dispatch and can be used before any command.

```sh
trident --home C:/path/to/.trident --json list
trident --no-interactive instance delete --instance cherry_picks --yes
trident --verbose package search --repository modrinth "Mouse Tweaks"
trident --debug repository status --label modrinth
```

| Option | Description |
| --- | --- |
| `--home <path>` | Use a specific Trident home instead of auto-detecting `.trident` or falling back to `~/.trident`. |
| `--json` | Force structured JSON output. |
| `--no-interactive` | Disable prompts/progress UI. Destructive commands require `--yes` where supported. |
| `--verbose` | Enable informational logging. |
| `--debug` | Enable debug logging and full exception output. |

When stdout is redirected, commands prefer structured JSON output automatically.

## Data Locations

Managed instances, caches, and profiles still live under the selected Trident home.

CLI-owned configuration is stored under the private brand directory:

```text
<trident-home>/.trident.cli/accounts.json
<trident-home>/.trident.cli/repositories.json
```

Secrets such as repository API keys and Microsoft refresh tokens are never printed, but the first implementation stores them in the local JSON files without OS keychain encryption.

## Context Resolution

Commands that need an instance resolve context in this order:

1. `--instance <key>`
2. `--profile <path>`
3. `profile.json` found in the current directory or a parent directory, if it is inside `<trident-home>/instances/<key>/profile.json`

Short option mapping used by implemented commands:

| Short | Long |
| --- | --- |
| `-I` | `--instance` |
| `-R` | `--repository` |
| `-v` | command-specific version option |

`-P` is reserved for future package-specific options and is not used for `--profile`.

## Shortcuts

| Shortcut | Equivalent Command |
| --- | --- |
| `trident create` | `trident instance create` |
| `trident import` | `trident instance import` |
| `trident build` | `trident instance build` |
| `trident list` | `trident instance list` |
| `trident inspect` | `trident instance inspect` |
| `trident search` | `trident package search` |
| `trident add` | `trident package add` |

## Instance Commands

```sh
trident instance list
trident instance inspect --instance cherry_picks
trident instance create --identity cherry_picks --name "Cherry Picks" --version 1.21.1
trident instance create --identity cherry_picks --name "Cherry Picks" --version 1.21.1 --loader net.neoforged:21.1.200
trident instance import --identity imported_pack --name "Imported Pack" path/to/modpack.zip
trident instance build --instance cherry_picks
trident instance export --instance cherry_picks --format trident --type offline --name "Cherry Picks" --author d3ara1n --output ./modpack.zip
trident instance unlock --instance cherry_picks
trident instance reset --instance cherry_picks --yes
trident instance delete --instance cherry_picks --yes
```

Supported export formats are `trident`, `modrinth`, and `curseforge`.

`reset` deletes build artifacts (`build/`, `live/`, `data.lock.json`) but keeps `profile.json`, `import/`, and `persist/`.

`delete` plants the Trident bomb marker and removes the profile from the active manager, matching the app behavior.

## Loader Commands

```sh
trident loader help
trident loader list
trident loader get --instance cherry_picks
trident loader set --instance cherry_picks net.neoforged:21.1.200
trident loader version list --version 1.21.1 --limit 20 net.neoforged
```

Supported loader identities come from PrismLauncher metadata mappings:

| Loader | Identity |
| --- | --- |
| Forge | `net.minecraftforge` |
| NeoForge | `net.neoforged` |
| Fabric | `net.fabricmc` |
| Quilt | `org.quiltmc` |

Flint is not exposed because the current Core Prism mapping does not support it.

## Package Commands

```sh
trident package list --instance cherry_picks
trident package search --repository modrinth --kind mod "Mouse Tweaks"
trident package search --instance cherry_picks "mouse"
trident package add --instance cherry_picks modrinth:aC3cM3Vq@9I21YYxf
trident package inspect modrinth:aC3cM3Vq
trident package inspect --instance cherry_picks modrinth:aC3cM3Vq
trident package dependency list --game-version 1.21.1 --loader net.neoforged modrinth:aC3cM3Vq
trident package dependent list --instance cherry_picks modrinth:aC3cM3Vq
trident package version list --game-version 1.21.1 --loader net.neoforged modrinth:aC3cM3Vq
trident package version set --instance cherry_picks modrinth:aC3cM3Vq@9I21YYxf
trident package enable --instance cherry_picks modrinth:aC3cM3Vq
trident package disable --instance cherry_picks modrinth:aC3cM3Vq
```

PURL format:

```text
<label>:[<namespace>/]<project-id>[@<version-id>]
```

Examples:

```text
curseforge:238222@4633221
modrinth:aC3cM3Vq@9I21YYxf
```

`package dependent list` is instance-local only. It scans installed packages in the selected instance and resolves their dependencies; there is no online reverse-dependency API in Core.

## Pipeline Input

`package add` can read packages from stdin. Supported input shapes:

```json
"modrinth:aC3cM3Vq@9I21YYxf"
```

```json
[
  "modrinth:aC3cM3Vq@9I21YYxf",
  "curseforge:238222@4633221"
]
```

```json
{
  "packages": [
    { "purl": "modrinth:aC3cM3Vq@9I21YYxf" }
  ]
}
```

The reader also scans common structured result containers: `items`, `packages`, `package`, `dependencies`, `versions`, and `results`.

Example:

```sh
trident --json package search --repository modrinth --kind mod "Mouse Tweaks" \
  | trident --json --no-interactive package add --instance cherry_picks
```

## Pagination

Commands with pageable/list-like remote output support:

```sh
--sort asc|desc
--index <n>
--limit <n>
```

Implemented on:

```sh
trident loader version list
trident package search
trident package version list
```

## Account Commands

```sh
trident account help
trident account list
trident account add --type offline --username Steve
trident account add --type offline --username Steve --uuid 00000000-0000-0000-0000-000000000000
trident account add --type microsoft
trident account remove --uuid 00000000000000000000000000000000 --yes
```

Microsoft login uses device-code flow and prints the verification URI and user code. JSON mode writes the device-code prompt to stderr and the final account result to stdout.

Account list output includes username, UUID, type, enrollment time, last-used time, and default flag. It does not include access tokens or refresh tokens.

## Repository Commands

```sh
trident repository help
trident repository list
trident repository status
trident repository status --label modrinth
trident repository add --label modrinth-cn --driver modrinth --endpoint https://api.modrinth.com --user-agent "TridentCli"
trident repository add --label curseforge --endpoint https://api.curseforge.com --api-key "..."
trident repository remove --label modrinth-cn --yes
```

Supported repository drivers:

| Driver | Authorization Header |
| --- | --- |
| `curseforge` | `x-api-key` |
| `modrinth` | `Authorization` |

`--driver` defaults to `--label` when omitted, so documented built-in-style labels like `curseforge` and `modrinth` work without explicitly passing `--driver`.

`GitHub` is not exposed as supported because `RepositoryAgent` does not currently construct a GitHub repository implementation.

## Exit Codes

| Code | Meaning |
| --- | --- |
| `0` | Success |
| `1` | Unknown error |
| `2` | Usage or validation error |
| `3` | Resource not found |
| `4` | Remote/network error placeholder |
| `5` | Canceled |
| `6` | Partial success |

## Known Limitations

- `package dependent list` is instance-local only.
- `trident account add --type microsoft` requires manual device-code completion and is not covered by automated smoke tests.
- Account and repository secret storage is local JSON without encryption.
- Long-running build currently emits a final JSON result in structured mode; JSON event streaming is not implemented.
- `repository add --authorization-header <name:value>` from the original plan is not implemented; use the supported driver-specific `--api-key` handling.

## Manual Verification Checklist

```sh
dotnet build "Trident.slnx"
dotnet run --project "src/Trident.Cli/Trident.Cli.csproj" -- --help
dotnet run --project "src/Trident.Cli/Trident.Cli.csproj" -- --json --home <temp-home> create --identity smoke --name Smoke --version 1.21.1
dotnet run --project "src/Trident.Cli/Trident.Cli.csproj" -- --json --home <temp-home> list
dotnet run --project "src/Trident.Cli/Trident.Cli.csproj" -- --json --home <temp-home> inspect --instance smoke
dotnet run --project "src/Trident.Cli/Trident.Cli.csproj" -- --json --home <temp-home> loader list
dotnet run --project "src/Trident.Cli/Trident.Cli.csproj" -- --json --home <temp-home> account add --type offline --username Steve
dotnet run --project "src/Trident.Cli/Trident.Cli.csproj" -- --json --home <temp-home> repository list
dotnet run --project "src/Trident.Cli/Trident.Cli.csproj" -- --json --home <temp-home> --no-interactive instance delete --instance smoke --yes
```
