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

## CLI Project

The CLI layer follows a strict **Commands → Operations → Tools** pattern — Commands and Tools must NOT contain business logic; they delegate to `Operations/` static methods. For full details see **AGENTS.CLI.md** (architecture, entry point flow, MCP conventions, coverage table).

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
