# Trident

下一代 Minecraft 实例管理器的文件组织结构。

## Architecture

Trident 是一种 Minecraft 的文件组织结构。
同时也是相应的一套工具链。

### File Structure

```sh
.trident/
├── cache/                   # Volatile directory
│   ├── assets/              # Game Assets
│   │   ├── indexes/         # Game Asset indexes
│   │   └── objects/         # Game Asset objects
│   ├── libraries/           # Game Libraries
│   ├── packages/            # Repository packages
│   └── runtimes/            # Java runtimes
├── instances/               # Instance directory
│   ├── {instance}/          # Instance root
│   │   ├── build/           # Build output treated as .minecraft
│   │   ├── import/          # Import layer
│   │   ├── persist/         # Persisted layer
│   │   ├── data.lock.json   # Version lock
└───┴───┴── profile.json     # Metadata profile
```

所有数据全部都存在 `.trident` 目录，不会触碰任何目录以外的文件。
这个目录通常位于 `$HOME` 下，但在一些实现中允许覆写，以下是一些常见的实现以及规则（优先级越靠前越高）：

- [**Polymerium**](https://github.com/d3ara1n/Polymerium)
  - 规则1：目录层级查找，从当前目录到根目录逐层向上查找 `.trident` 目录
  - 兜底：`~/.trident`
- **Trident Cli**
  - 规则1：通过命令行参数指定
  - 规则2：环境变量 `TRIDENT_HOME`
  - 规则3：目录层级查找，从当前目录到根目录逐层向上查找 `.trident` 目录
  - 兜底：`~/.trident`

### Deployment

只需要一个 `profile.json` 文件，Trident 就能还原出一个完整的实例。
Trident 会根据 `profile.json` 中的描述，从 `cache` 目录中查找或下载所需的文件，并将它们链接到 `build` 目录中。
Trident 会保证 `build` 目录中的文件总是和 `profile.json` 中的描述保持一致。

构建中涉及多个数据层，最终构建结果是这些层**增量**的覆加到 `build` 目录中：

1. 基础层：只需要一个游戏版本即可构建，构建出原版游戏的基础和启动参数（来自 `profile.json`）
2. 加载器层：加载器和原版游戏使用相同方法并影响相同构建*中间数据*，即库、启动参数（来自 `profile.json`）
3. *导入层*：导入的整合包会产生导入层，内部的文件会*投影*到输出目录（来自导入的整合包）
4. *持久层*：用于在多次构建中保持目录和文件，内部文件结构必须和构建目录的一致，文件会被加入软链接到对应的输出目录的对应位置（来自用户添加）

*中间数据*: 即**版本锁**(`data.lock.json`)中包含的数据。讲道理版本锁需要做到 portable，但此处由于这个游戏的启动参数必须包含完整路径而无法做到。
便携化会放在未来实现，届时 `data.lock.json` 就能从 `.gitignore` 中移除了。

*导入层*: 这一层分为两部分，`import` 的文件直接来自整合包，是只读的，会先通过存在性检测复制不存在的文件到 `live` 目录。
`live` 目录具有和 `persist` 一样的机制，会在构建时投影到输出目录，文件会在游戏游玩过程中被更新。
`import` 对于导入自整合包的实例该目录用于放置整合包的文件，对于自制且未来会导出到整合包的实例则用来放需要导出的文件（其实都是同一个类型的文件），打包器会将该目录的内容原样打入整合包。
一种典型用法为将需要未来导出到整合包的文件例如模组的配置文件从输出目录移动到 `import` 作为持久化文件并交给 Git 托管以多人开发整合包

*持久层*: 用于让游戏用户文件能在实例更新和重置中保持，例如 `screenshots`、`saves`、`options.txt` 这些*文件和目录*添加进持久化列表来避免丢失。

*投影*：实现部署的核心机制，将虚拟的目录结构投影到输出目录。使用软链接创建文件关系，**增量**添加增加的文件关系，并移除多余的文件关系。

*文件和目录*: 如果要投影整个目录，需要在目录中添加一个空的 `.keep` 目录，例如 `screenshots/.keep`。
这个文件还能起到 `.gitkeep` 相同的作用，但并不建议 Git 仓库里添加 `saves/.keep` 这种游戏会往里面生成大量不适合添加进 Git 的文件的目录。

### Modpack Creation in real world: Github Actions

> [!WARNING]
> Trident Cli 还在制作中，暂时不可用且展示的 API 在未来会发生变动。

在需要 CD 构建整合包的情况下可以使用 Trident Cli 在 Github Actions 中实现。

以下是一个简单的示例，用于在整合包仓库(元数据文件使用 `src/profile.json`)构建 Polypack 格式的整合包并发布到 Release：

```yaml
name: Build and Publish Modpack

on:
  push:
    tag:
      - v*

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 9.0

      - name: Install Trident Cli
        run: dotnet tool install -g Trident.Cli

      - name: Run Trident CLI
        run: trident publish --output Releases --format polypack --type online src/profile.json

      - name: Create Release
        uses: softprops/action-gh-release@v2
        with:
          name: ${{ github.ref_name }}
          body: Modpack Release
          tag_name: ${{ github.ref_name }}
          draft: false
          prerelease: false
          files: Releases/*
```

## Repository

本仓库包含了对 Trident 的各种模块的 .Net 实现。

### Project Structure

- `src/Trident.Core/` 为核心库，包含所有业务逻辑，具有抽象接口的各个平台实现。
- `src/Trident.Abstractions/` 为抽象库，包含所有抽象定义。
- `src/Trident.Purl/` 为 Purl 实现，包含 Purl 的解析和生成逻辑。
