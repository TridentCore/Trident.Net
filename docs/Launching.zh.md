# 原生启动定义

启动定义用于选择提供 Minecraft 启动要求的组件。启动组件具有稳定身份，声明依赖、参数、库及其他启动输入。普通实例仅用 `profile.json` 即可，原生启动定义是可选能力。

## 存储与所有权

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

整合包导入或更新管理 `launch/import/`。本地自定义放在 `launch/user/`，整合包更新保留该目录。整合包导出不包含用户定义及文件，实例快照包含两者。

`user/definition.json` 优先于 `import/definition.json`。两者都不存在时，Core 默认选择 Profile 的 Minecraft 版本和模组加载器。文件放进 `components/` 不等于启用：只有选中的组件及其依赖闭包参与启动。

每个身份按 user、import、平台提供者的优先级选择一个完整定义。替换不会继承另一份定义的依赖或内容。未选中的本地组件文件仍进行语法检查，但只有参与启动的组件才解析本地资产引用。

## 选择组件

例如，`launch/user/definition.json` 可以保留 Profile 绑定并选中自定义组件：

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

`Minecraft` 和 `Loader` 从 Profile 获取身份与版本，不应另填 `id` 或 `version`。没有模组加载器时不产生对应选择。`Component` 选择显式身份，可附带版本。远程组件需要确定版本，本地定义可以提供自己的版本；显式要求的版本必须匹配选中的定义。

`enabled: false` 禁用选择，依赖被禁用的组件会报错。显式选择的顺序保持不变，缺少的依赖插入到首个依赖者之前，不对现有选择重新拓扑排序。矛盾的版本要求和组件互斥都会报错。

## 声明组件

`launch/user/components/custom.startup.json`：

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

组件身份使用 ASCII 字母、数字、点号、连字符与下划线，匹配区分大小写。定义和组件格式版本当前为 `1`，不认识的原生 JSON 字段会被拒绝。

组件可以声明：

- `requires` 与 `conflicts`，引用其他组件的身份及可选版本。
- `javaMajors`，兼容的 Java 主版本；参与启动的约束求交集。
- `mainClass`、`assetIndex` 与 `startOnFirstThread`。
- `gameArguments` 与 `javaArguments`，包含可选的 `replace` 列表和 `append` 列表。
- `libraries`，将资产身份、来源、校验散列与用途分别声明。

完整结构以 `src/TridentCore.Abstractions/Launching/` 的 C# record 为准。参数与库的规则按实际启动目标求值。资产身份包含版本与 classifier，允许版本号中出现 `+`。

库用途区分 classpath 项、客户端 JAR、native 解压项、下载必需项与 Java agent。下载必需项不竞争 classpath 槽位。classpath 与 native 分别仲裁，后声明的版本就地替换前面的选择。

## 本地文件

相对的资产与资产索引 URL 按组件文件所在目录解析。`components/` 中的组件可以引用 `../files/custom.jar`。

导入引用必须留在 `launch/import/` 内，不能经过该根目录之下的符号链接。用户组件允许引用外部本地文件。活动引用会检查存在性及声明的校验散列，并计算内容散列用于缓存失效。移动实例不代表外部用户文件也具备可移植性。

本地资产在原地使用，不复制到共享库缓存。本地资产索引投影到实例的资产目录，资产对象使用共享缓存。

## 部署与缓存

启动管线先解析组件，再确定实际 Java 版本与架构，最后编译不可变结果。全局 Java 设置按组件要求的主版本提供候选，找不到时可以下载捆绑运行时。实例级 Java 覆盖是强制选择：Core 只探测该路径，跳过全局候选选择和运行时下载，并按它实际探测出的主版本与架构编译，即使元数据没有列出该主版本。native 选择跟随 JVM 架构，包括 JVM 与宿主架构不同的情况。

`data.lock.json` 保存编译结果，不是另一份可编辑定义。指纹包括已解析组件、定义树内容、编译器版本和目标。缓存参数保留资产身份引用，启动装配时才绑定物理路径。命中缓存时整体复用结果，不会再次应用声明。

组件解析仍会执行，也可能访问平台元数据服务。复用编译结果不保证整个部署过程离线。锁定数据缺失或不可用时，从持久输入重新计算。

## native 解压

解压后的原生库保持实例隔离，位于 `build/natives`。Core 只解压显式声明为 native 的资产。JAR 名称或 classifier 含有 `natives` 不代表该用途：普通库保留在 classpath，可能由游戏自行解压。

因此启动器与游戏都可能写入同一个 native 目录。部署覆盖选中原生库归档的条目，遵守解压排除项，保留其他文件，不整体清空或搬迁目录。ZIP 时间戳不能证明两个库版本内容相同。

## 扩展元数据 trait

外部元数据的 trait 是开放的行为标记集合，不是启动器必须逐项实现的能力清单。`MetadataComponentHelper.Convert` 按名称匹配：识别到的标记执行已实现的行为，未识别的标记忽略，不拒绝启动，也不向用户逐项告警。它们不是直接追加到游戏命令行的参数。

支持新标记时，扩展该转换器中的 trait 处理段，将行为映射到原生组件模型。例如 `FirstThreadOnMacOS` 设置 `StartOnFirstThread`，而 `XR:Initial` 这类标记无需操作。未匹配的标记应让原生字段保持未设置，不能覆盖其他组件的声明。如果支持需要新增原生字段，应贯通编译与支持的导出格式，而不是在各启动调用点判断 trait 名称。

宽松策略仅适用于外部 trait。必需资产、路径边界和具体执行数据继续各自校验，原生 JSON 结构校验仍保持严格。

## 格式边界

MMC 导入遵循 `mmc-pack.json` 的组件顺序与启用状态。选中的本地 patch 完整替换同身份的平台定义，孤立的 patch 文件不自动启用。随包携带的本地文件转换为原生导入引用。

Trident 整合包直接携带 `launch/import/`。MMC 导出将可表达的原生声明转换为组件，并携带引用的本地库。执行语义不能保留时转换失败，包括不支持的 JVM 替换策略，以及往返转换会改变目标选择的 native 映射。

不能携带启动定义的导出格式会拒绝包含导入定义的实例，而不是静默丢弃它们。只属于用户的自定义仍不参与整合包导出。

## 验证

```sh
dotnet test tests/TridentCore.Tests/TridentCore.Tests.csproj
dotnet build Trident.slnx
```

测试使用项目内沙箱和离线元数据夹具，验证解析、编译、存储、native 解压及转换边界，不启动 Minecraft。夹具测试成功不等于完整的真实整合包已经通过启动验收。
