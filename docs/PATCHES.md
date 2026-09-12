# Native deployment patches

A native patch changes data at a boundary of Trident's existing deployment pipeline. It can replace, append or remove libraries and argument groups, and replace launch settings. It does not modify JAR contents or introduce a component dependency model.

`profile.json` remains self-contained and contains no patch references. Patches are optional external instance data, like Pack Source (`import/`) and Local Data (`persist/`). Sharing a profile alone does not share these external customizations.

## Storage

```text
instances/<key>/
├── profile.json
├── patches/
│   ├── data.patch.json
│   ├── import/
│   │   └── pack-runtime/
│   │       ├── patch.json
│   │       └── assets/bootstrap.jar
│   └── users/
│       └── my-adjustments/patch.json
├── import/
├── persist/
└── build/
```

The index is the only activation and ordering source. Files are not enabled by directory discovery. Missing external patch data does not add a requirement to the profile.

```json
{
  "format": 1,
  "import": [
    { "path": "pack-runtime/patch.json", "enabled": true }
  ],
  "users": [
    { "path": "my-adjustments/patch.json", "enabled": false }
  ]
}
```

Paths are relative to their layer and use `/`. The `import` layer is the managed modpack layer and the only one an archive carries; the `users` layer belongs to the machine and never leaves it. Within a deployment boundary, imported patches run in index order, followed by user patches in index order. Operations inside a document run in array order. Pipeline boundaries themselves always retain their execution order. Disabling an entry preserves its position.

Each indexed entry points to a `patch.json` in its own directory. Indexed ownership directories cannot overlap. Each patch owns its relative local asset references. Absolute paths, parent traversal and symbolic links inside those references are rejected. Patch files and assets are not automatically projected into the Run Directory (`build/`).

## Document and operations

```json
{
  "format": 2,
  "name": "My launch adjustments",
  "operations": [
    {
      "target": "launch.compatibleJavaMajors",
      "action": "replace",
      "value": [21, 25]
    },
    {
      "target": "launch.compatibleJavaMajors",
      "action": "intersect",
      "value": [17, 21]
    },
    {
      "target": "launch.jvmArguments",
      "action": "append",
      "value": [["-Dfile.encoding=UTF-8"]]
    },
    {
      "target": "launch.libraries",
      "action": "remove",
      "match": { "identity": "example:old-library:**" }
    },
    {
      "target": "launch.libraries",
      "action": "append",
      "value": [
        {
          "identity": "example:bootstrap:1.0",
          "local": "assets/bootstrap.jar",
          "classpath": true
        }
      ]
    },
    {
      "target": "launch.agents",
      "action": "append",
      "value": [
        {
          "library": {
            "identity": "example:agent:1.0",
            "url": "https://example.org/agent.jar"
          },
          "arguments": "example=value"
        }
      ]
    }
  ]
}
```

### Targets

The scopes are `vanilla`, `loader` and `launch`. `loader` receives the vanilla output; `launch` receives the loader output. Whole-result replacement supplies the accumulated result at that boundary, not a component definition.

| Target suffix | Value | Operations |
| --- | --- | --- |
| `libraries` | Array of library descriptions | replace, append, remove |
| `agents` | Array of agent descriptions | replace, append, remove |
| `gameArguments` | Array of token arrays | replace, append, remove |
| `jvmArguments` | Array of token arrays | replace, append, remove |
| `mainClass` | Nonempty string | replace |
| `compatibleJavaMajors` | Nonempty array of positive integers | replace, intersect |
| `assetIndex` | `{id, url, hash?}` | replace |
| `mainJar` | Library description | replace, remove |

For example, `vanilla.libraries` changes the vanilla library result and `launch.mainClass` changes the final entry point. `loader.enabled` accepts a boolean replacement and controls whether the standard loader producer runs. Setting it to false still allows loader-scope patch operations on the incoming vanilla result.

Replacing the whole `vanilla` or `loader` result skips that default producer. Replacing `vanilla.libraries` skips the default library dependency expansion. A whole-result value has this shape:

```json
{
  "mainClass": "example.bootstrap.Main",
  "compatibleJavaMajors": [25],
  "defaultJvmArguments": true,
  "gameArguments": [["--username", "${auth_player_name}"]],
  "jvmArguments": [],
  "libraries": [],
  "mainJar": {
    "identity": "example:game:1.0",
    "url": "https://example.org/game.jar"
  },
  "assetIndex": {
    "id": "example",
    "url": "https://example.org/assets.json"
  }
}
```

`defaultJvmArguments` supplies Trident's classpath, memory, native paths and platform arguments before the additional groups. It does not infer a macOS threading model: include `-XstartOnFirstThread` in an argument operation restricted to macOS only when the replacement runtime requires it. The standard vanilla producer supplies its own first-thread setting. Set `defaultJvmArguments` to false only when supplying the complete JVM invocation yourself. The final result must have an entry point, an asset index and a compatible Java major set. The set may be empty once intersections have run: that is reported when the runtime is resolved, not as a patch error.

### Collections and selectors

Without `match`, replace replaces an entire collection and remove clears it. With `match`, replace keeps the first matched position and remove deletes all matches. An empty library or agent selector matches every entry in that collection. An unmatched replacement is an error by default; unmatched removal is a no-op. Declarations share one arbitration mechanism across vanilla, loaders and patches, with separate slots for classpath, native extraction, agents and files needed only for download. Classpath, native and agent slots use group, name, classifier and extension, with the later declaration winning regardless of version. Download-only slots also include the version because installers address these files by exact coordinates. They neither replace runtime libraries nor gain classpath inclusion from them. Within a native slot, classpath inclusion is retained if a declaration requests both uses. Append moves the winning declaration to its insertion position. The main JAR occupies the final position and takes precedence over an ordinary library in the same slot.

Library selectors support `identity`, `native` and `classpath`. Identity patterns use Maven notation: `group:name:version[:classifier][@extension]`. `*` matches inside one coordinate segment, while `**` also matches separators. A two-segment selector such as `example:library` implies a version wildcard without selecting classified variants. Use `example:library:**` to include those variants.

Library and agent append may specify either `before` or `after` using an identity selector. The first matching entry is the anchor; a missing anchor is an error. Selected replacement may specify `ifMissing: "append"` or `ifMissing: "ignore"`. `onlyIfNewer: true` compares one replacement with one selected entry using natural numeric version ordering.

Argument values are arrays of groups, such as `[["--tweakClass", "a.Tweaker"], ["--tweakClass", "b.Tweaker"]]`. Groups retain their boundaries in the lock and are flattened only when starting Java. `match.arguments` selects groups by a token prefix. Repeated options, empty values and values beginning with `-` are retained.

File references in arguments resolve when assembling the launch command, after every patch has applied. `${main_jar}` refers to the final main JAR; `${forge_installer}` refers to the unique non-classpath, non-native Forge or NeoForge installer library. A missing or ambiguous reference is an error. Standard and imported ForgeWrapper declarations use these references, so replacing a library or the main JAR changes the path used at launch. Removing an argument group leaves it removed; launch assembly only resolves surviving references.

### Library descriptions

A library supplies exactly one of `url` or `local`. URLs use HTTP or HTTPS. Local paths are relative to the patch document's directory. Optional fields are:

- `hash`: `{ "algorithm": "sha256", "value": "..." }`; sha1, sha256, sha512 and md5 are supported.
- `classpath`: whether the file enters the classpath, default true.
- `native`: whether to extract it into the managed natives directory, default false.
- `exclude`: archive-entry prefixes to omit during native extraction.
- `rules`: platform conditions.

The main JAR is placed after ordinary classpath libraries. Native extraction and classpath inclusion are independent. Native extraction fingerprints the actual archive contents together with their order and exclusion rules. An unchanged input with intact extracted files reuses the directory; changed inputs or missing or modified outputs trigger a fresh extraction into a temporary directory, which replaces the old directory only after extraction succeeds. Replacements and downgrades therefore do not depend on ZIP timestamps. Local assets remain instance-owned; remote patch libraries use source-specific cache paths to avoid same-coordinate source collisions.

### Agent descriptions

An agent supplies a `library` field with the same shape as a library description, plus an optional `arguments` string. An agent reference never adds its JAR to the classpath and cannot request native extraction. Agent identity uses the same group, name, classifier and extension as library arbitration: the later declaration replaces both the JAR and its arguments, regardless of version. Surviving agents retain their relative order. Agent selectors support `identity` only.

Each agent is emitted as `-javaagent:<path>`, with `=<arguments>` appended when arguments are provided, including an explicitly empty string. Library and agent references share one file deployment path, so a JAR referenced in both roles is downloaded only once. Their execution roles remain separate; a library can be on the classpath while the same file also serves as an agent.

Authlib-injector is an account dependency: the launch region carries it as a non-classpath library and the lock freezes the resolved version, so re-deployment does not re-resolve it. The external-auth account configurer adds its agent and server URL only when that account is selected. It does not modify the instance's agent declarations. Account-supplied agents pass through the same identity arbitration before JVM arguments are emitted.

Operations and libraries can carry ordered platform rules:

```json
"rules": [
  { "action": "allow", "os": "osx", "arch": "^arm64$" }
]
```

With no rules, an item applies everywhere. With rules, it begins disallowed and each matching rule sets allow/disallow: rules are evaluated top to bottom and the last matching rule decides. A bare `allow` matches every platform, so `[allow, disallow windows]` expresses everywhere except Windows, and `[allow osx]` expresses macOS only. OS values include windows, linux, osx and architecture-qualified names such as osx-arm64. Architecture and OS-version conditions are regular expressions evaluated against the host architecture and OS version.

`compatibleJavaMajors` is a set, not a choice: it lists every Java major the result accepts. `replace` declares the set outright and discards whatever earlier declarations supplied; `intersect` narrows it to the majors both sides accept. A whole-result replacement carries the set as well. The applied result may end up empty, in which case the runtime resolution reports that no Java version fits rather than rejecting the patch. Index format stays 1; documents are format 2, and format 1 documents remain readable so modpacks built against the earlier single-major form still deploy.

## Java and cache behavior

Java selection has two sides. The **requirement** is the patched `compatibleJavaMajors` set. The **providers** are the Java homes the user configured — a per-major home in the launcher settings, or a path pinned for the whole instance (the instance override, the CLI option), which applies to any major — plus Trident's bundled runtimes. Resolution takes the newest demanded major a configured home covers, and only when no configured home covers any of them does it take the newest demanded major Trident can bundle, downloading it and resolving it to the Mojang runtime family for that major. A major the user did not configure is not a substitute for one they did, and a configured path is used as-is: never validated, never silently replaced by another runtime. When neither side covers any demanded major, deployment fails naming the demanded majors; when the requirement is empty, it fails reporting that no compatible Java remains. Both are reported to the user with a prompt to configure Java.

Patch operations therefore change which majors the instance accepts, never the Java path.

The lock has independent vanilla, loader and launch regions. Each fingerprints the deployment data format together with its relevant operations, assets and upstream output. The loader region also includes the profile's Minecraft version because its standard provider can resolve version-specific intermediary mappings independently of the vanilla output. Package version resolution still depends on the profile's Minecraft/loader compatibility fields, not on arbitrary patch edits. The runtime region depends on the resolved major. Unchanged downstream inputs retain their caches.

An operation is always applied to that region's input, never reapplied to its already-patched cached output. Local asset content changes participate in invalidation. Source assets removed by the effective rules need not be materialized.

## Management

The static Core `PatchStorageHelper` reads and validates documents, saves indices/documents, adds a user patch with its referenced assets, toggles or reorders entries, and removes a patch with its owned files. It does not edit the profile.

```sh
trident patch list --instance example
trident patch add --instance example --path ./my-patch/patch.json --name my-adjustments
trident patch disable --instance example --path my-adjustments/patch.json
trident patch enable --instance example --layer import --path pack-runtime/patch.json
trident patch move --instance example --path my-adjustments/patch.json --position 0
trident patch remove --instance example --path my-adjustments/patch.json --yes
```

Positions are zero-based within a layer. The default layer is users. Index edits validate paths and ownership without reading unrelated documents. Enabling validates the selected document; disabling, reordering and removing remain available even when a document is malformed or missing. Removal deletes owned files and requires CLI confirmation. Edit rule documents directly or through `PatchStorageHelper.SaveDocumentAsync`; the next deployment detects the change. MCP exposes `patch_list`, `patch_set_enabled` and `patch_move`.

## Import, update and export

External archives and directly migrated instance directories with `mmc-pack.json` use the same conversion and file extraction code without contacting a repository: the profile takes the Minecraft version and loader identity from the components array, and every component the archive also carries a local definition for (`patches/<uid>.json`, present when a component was installed from a file) is converted into a native document in components order. Components without local definitions are left to the standard deployment providers. When a local Fabric or Quilt definition replaces its provider, an enabled intermediary component with an explicit version is translated into its canonical Maven library reference. Patch conversion requires no component metadata requests in either entry point. Disabled components are skipped: they contribute no launch data. Platform rules, local libraries, JVM arguments and first-thread behavior are preserved; nonempty source rule sets start disallowed and retain their ordered allow/disallow decisions during conversion, without inserting an unconditional allow. `-XstartOnFirstThread` is added on macOS only when a component declares the `FirstThreadOnMacOS` trait, and other traits are ignored. Java compatibility lists are carried as an intersecting declaration, so a later component can only narrow what earlier ones accept. A local definition for the Minecraft or the loader component replaces that region's default producer, so a locally pinned version is not overlaid with the standard one. Loaders outside the metadata the producers know are out of scope: an archive that carries one cannot be launched from it alone.

JAR modifications are unsupported. Native patches do not provide arbitrary script execution or JAR rewriting operations. Agent declarations are preserved as first-class launch data rather than folded into the classpath.

Modpack updates stage the new import layer and index together, replace the import layer wholesale and roll back the patch change if the update fails. The users layer takes no part in an update: its entries and files stay exactly as they were.

Trident Portable Instance carries the import layer's documents, disabled entries and assets, together with an index that lists import entries only. The users layer is local instance data: no export format carries it, no import reads it, and user-layer content found in an archive is ignored. Other export formats omit imported native patches and report this loss in the export UI or CLI result. No reverse conversion to external patch formats is promised.

Local instance snapshots include both layers, the full index and assets. Reset clears the Run Directory and deployment lock; it retains the external patch sources.

## Verification

Verify deployment support using actual distribution archives: import them, deploy the resulting instances and launch the game. Inspecting generated rules and launch arguments helps diagnose failures, but compilation and synthetic assertions do not establish that a modpack runs correctly.
