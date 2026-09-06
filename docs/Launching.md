# Native launch definitions

A launch definition selects the components that supply Minecraft startup requirements. A launch component has a stable identity and declares its dependencies, arguments, libraries and other startup inputs. `profile.json` remains sufficient for ordinary instances; native launch definitions are optional.

## Storage and ownership

```text
launch/
├── import/
│   ├── definition.json
│   ├── components/*.json
│   └── files/
└── user/
    ├── definition.json
    ├── components/*.json
    └── files/
```

Importing or updating a modpack owns `launch/import/`. Local customizations belong in `launch/user/`, which modpack updates preserve. Pack export excludes user definitions and files. Instance snapshots include both directories.

`user/definition.json` takes precedence over `import/definition.json`. Without either file, Core selects the Profile's Minecraft version and mod loader. Placing a file in `components/` does not activate it: only selected components and their dependency closure participate.

For each identity, resolution chooses one complete definition from user, import or the platform provider in that order. A replacement does not inherit the other definition's dependencies or contributions. Local component files are syntax-checked even when unselected; local artifact references are resolved only for participating components.

## Selecting components

For example, `launch/user/definition.json` can retain the Profile bindings and select a custom component:

```json
{
  "formatVersion": 1,
  "components": [
    { "from": "Minecraft" },
    { "from": "Loader" },
    { "from": "Component", "id": "custom.startup" }
  ]
}
```

`Minecraft` and `Loader` obtain identity and version from the Profile; do not also supply `id` or `version`. A missing loader produces no selection. `Component` selects an explicit identity and optionally a version. Remote components require a resolved version; local definitions can supply their own version. Any explicitly requested version must match the chosen definition.

`enabled: false` disables a selection. A dependency on a disabled selection is an error. Explicit selection order is preserved. Missing dependencies are inserted before their first dependent without topologically reordering existing selections. Contradictory version requirements and component conflicts are errors.

## Declaring a component

`launch/user/components/custom.startup.json`:

```json
{
  "formatVersion": 1,
  "id": "custom.startup",
  "javaArguments": {
    "append": [
      { "value": "-Dcustom.enabled=true" }
    ]
  }
}
```

Component identities use ASCII letters, digits, dots, hyphens and underscores. Identities are matched case-sensitively. Definition and component format versions are currently `1`; unknown native JSON fields are rejected.

Components can declare:

- `requires` and `conflicts`, referring to other component identities and optional versions.
- `javaMajors`, the compatible Java major versions; participating constraints are intersected.
- `mainClass`, `assetIndex` and `startOnFirstThread`.
- `gameArguments` and `javaArguments`, with an optional `replace` list and an `append` list.
- `libraries`, whose artifact identity, source, checksum and usage are separate values.

The C# records in `src/TridentCore.Abstractions/Launching/` define the complete schema. Argument and library rules are evaluated against the resolved launch target. Artifact identities include versions and classifiers; versions containing `+` are valid.

Library usages distinguish classpath entries, client JARs, native extraction, download requirements and Java agents. Download requirements do not compete for classpath slots. Later versions replace earlier selections in place within classpath and native slots independently.

## Local files

Relative artifact and asset-index URLs resolve against the component file. A component under `components/` can refer to `../files/custom.jar`.

Imported references must remain inside `launch/import/` and may not traverse symbolic links beneath that root. User components may reference external local files. Active local references are checked for existence and any declared checksum, then hashed for cache invalidation. Moving an instance does not make external user files portable.

Local artifacts are consumed in place rather than copied into the shared library cache. A local asset index is projected into the instance's asset directory while asset objects use the shared cache.

## Deployment and caching

The launch pipeline resolves components, determines the actual Java version and architecture, then compiles an immutable result. Explicitly configured Java installations are validated rather than rewritten by deployment. Native selection follows the JVM architecture, including when it differs from the host architecture.

`data.lock.json` stores the compiled result, not another editable definition. Its fingerprint includes the resolved components, definition-tree content, compiler version and target. Cached arguments retain artifact identity references; launch assembly binds their physical paths. A cache hit reuses the whole result without applying declarations again.

Component resolution still runs and may consult the platform metadata services. Reusing a compiled result does not guarantee an entirely offline deployment. Missing or unusable lock data is rebuilt from durable inputs.

## Native extraction

Extracted native libraries remain instance-isolated in `build/natives`. Core extracts only artifacts explicitly declared for native extraction. A JAR name or classifier containing `natives` does not imply that usage: ordinary libraries remain on the classpath and may be extracted by the game itself.

The launcher and game can therefore write to the same native directory. Deployment overwrites the entries of selected native archives, honors extraction exclusions and leaves other files alone. It does not clear or relocate the directory. ZIP timestamps are not treated as evidence that two library versions have the same content.

## Format boundaries

MMC imports use the component order and activation state in `mmc-pack.json`. Selected local patches replace matching platform definitions in full; orphan patch files are not activated. Bundled local files are converted into native import references.

Trident packs carry `launch/import/` directly. MMC export translates supported native declarations into components and bundles referenced local libraries. Conversion fails when execution semantics cannot be preserved; this includes unsupported JVM replacement policies and native mappings that cannot round-trip without changing target selection.

Export formats that cannot carry launch definitions reject instances containing imported definitions rather than silently dropping them. User-only customizations remain excluded from pack export.

## Verification

```sh
dotnet test tests/TridentCore.Tests/TridentCore.Tests.csproj
dotnet build Trident.slnx
```

Tests use project-local sandboxes and offline metadata fixtures. They verify resolution, compilation, storage, native extraction and conversion boundaries without launching Minecraft. Successful fixture tests do not establish that a complete real-world modpack launches.
