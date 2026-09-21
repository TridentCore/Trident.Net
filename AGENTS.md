# Trident.Net — Agent Guide

## Project Overview

Trident.Net is a .NET 10 toolchain for managing **Minecraft instances, modpacks, package repositories, and accounts**. Instances are declarative (driven by `profile.json`), rebuildable, importable, exportable, and friendly to automation.

The non-CLI projects are internal implementation consumed directly from source by first-party hosts: the CLI in this repository and Polymerium through its Trident.Net git submodule. Only the `trident` CLI is a distributed product; it also ships an MCP server mode (`--mcp`) for AI agent integration.

## Repository Layout

```
src/TridentCore.Abstractions/   Pure models, interfaces, enums, extension methods
src/TridentCore.Pref/           Package Reference (Pref) parsing and formatting library
src/TridentCore.Core/           Business logic, engines, API clients, services
src/TridentCore.Cli/            The `trident` CLI product (Commands + MCP Tools)
```

Layering: `Abstractions ← Pref ← Core ← Cli`. Dependencies flow downward only.

## Core Concepts

- **Profile** (`profile.json`) — Declarative description of a Minecraft instance: game version, loader, packages, rules, and runtime overrides. Managed by `ProfileManager`.
- **Pref** (Package Reference) — Identifier format `pref://repository/projectId` or `pref://repository/projectId@versionId`, e.g. `pref://modrinth/aC3cM3Vq@9I21YYxf`. Parsed by `TridentCore.Pref`.
- **Instance** — A concrete deployment of a profile on disk, built by the staged `DeployEngine` pipeline.
- **Repository** — Package source abstraction (Modrinth, CurseForge, user-configured). Queried through `RepositoryAgent`.
- **Loader** — Mod loader (Forge, NeoForge, Fabric, Quilt). Metadata via PrismLauncher API.
- **Lock Data** (`data.lock.json`) — Snapshotted deployment state for rebuild and diff.
- **Native patches** (`patches/`) — External deployment declarations with no references in `profile.json`; `data.patch.json` orders import and users layers. Only the import layer participates in modpack import/export. The users layer is local user data, preserved on updates and included in local snapshots.

## CLI Project

The CLI layer follows a strict **Commands → Operations → Tools** pattern — Commands and Tools must NOT contain business logic; they delegate to `Operations/` static methods. For full details see **AGENTS.CLI.md** (architecture, entry point flow, MCP conventions, coverage table).

## Development Conventions

- **Framework:** .NET 10, C# 13
- **Internal API policy:** Non-CLI projects have no source or binary compatibility contract. Prefer direct breaking changes that improve the design and update every first-party caller in the same change. Never add compatibility overloads, aliases, forwarding shims, or `[Obsolete]` members.
- **File-scoped namespaces**, implicit usings
- **Primary constructors** preferred
- **`var`** preferred everywhere
- **Expression-bodied members** preferred
- **Private fields:** `_camelCase`
- **Coding style:** See `.editorconfig` for full rules
- **Solution file:** `Trident.slnx`
- **Build:** `dotnet build Trident.slnx`
- **Verification:** Do not add test cases, mock-based suites, or feature-specific test/check projects except for the Pref/PURL parsing and formatting library. Validate deployment and launch support with actual distribution packs and real instance runs; compilation and passing assertions are not end-to-end acceptance.
- **Stateless helpers:** Use static `XxxHelper` classes under `Utilities`; reserve `XxxService` for stateful application capabilities.
