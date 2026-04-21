# Trident Cli

## 命令

```sh

# 为了能在非交互式终端使用，--no-interactive 参数可以将输出切换为 ILogger 的输出而不是指定 AnsiConsole 输出

# 当处于管道中时，会使用 Json 格式输入输出，比如 trident search "Mouse Tweaks" | Select-First | trident package add --instance cherry_picks
## 第一个命令得到一个 Purl[] 的 Json，通过第二个命令变成单条 Purl，第三个命令接受字符串输入并执行操作

# search/list 等列表型结果允许使用 --sort asc/desc 或 --index 10 --limit 20 来分页

# 长短名映射
# -I --instance
# -P --package
# -R --repository

# 上下文
## 除了 Trident Home 这个隐藏的上下文外，还有 { Instance: string? }
## Instance 有两种指定方式，首先检查是否使用 --instance 指定
## 如果没有就检查当前目录或上级目录存在 profile.json（且位于 Trident Home）
## 如果都没有，那就是没有

# 快捷命令
## 等价于 trident instance create
trident create
## 等价于 trident instance import
trident import
## 等价于 trident instance install
trident install
## 等价于 trident instance list
trident list
## 等价于 trident instance inspect
trident inspect
## 等价于 trident package search
trident search
## 等价于 trident package add
trident add

# 实例管理
## 枚举管理的实例列表
trident instance list
## 查看特定实例的基本信息
trident instance inspect --instance cherry_picks
## 创建一个新的实例
trident instance create --identity cherry_picks --name "Cherry Picks" --version 1.21.1
## 构建实例
trident instance build --instance cherry_picks
## 导入实例 --name 可选
trident instance import --name Modpack path/to/modpack.zip
## 解锁
trident instance unlock --instance cherry_picks
## 导出且将资源解析后打包进包体（离线模式）
trident instance export --instance cherry_picks --format tripack --type offline --name "Cherry Picks" --author d3ara1n --output ./modpack.zip
## 删除
trident instance delete --instance cherry_picks
## 重置
trident instance reset --instance cherry_picks

# 加载器管理
## 查看支持的加载器和相关信息
trident loader help
## 获取可用的加载器列表
trident loader list
## 设置加载器
trident loader set --instance cherry_picks net.neoforged:42.1.200
## 查看实例的加载器
trident loader get --instance cherry_picks
## 查询并列出关联版本，游戏版本为必须字段
trident loader version list --version 1.21.1 net.neoforged

# 包管理
## 枚举实例安装的包
trident package list --instance cherry_picks
## 添加一个包
trident package add --instance cherry_picks curseforge:114514@1919810
## 搜索实例的包，相当于在列表中过滤指定选项
trident package search --instance cherry_picks --repository curseforge --kind mod "Mouse"
## 搜索在线的包
trident package search --kind mod "Mouse"
## 查看实例的包信息，包括部署规则应用情况
trident package inspect --instance cherry_picks curseforge:114514@1919810
## 查看一个在线包
trident package inspect curseforge:114514@1919810
## 查看一个安装了的包的依赖
trident package dependency list --instance cherry_picks curseforge:114514@1919810
## 查看一个安装了的包的附属
trident package dependent list --instance cherry_picks curseforge:114514@1919810
## 查看一个在线包的依赖
trident package dependency list curseforge:114514@1919810
## 查看一个在线包的附属
trident package dependent list curseforge:114514@1919810
## 查看一个包的版本（无关是否在线还是已安装）
trident package version list curseforge:114514
## 设置一个包的版本
trident package version set --instance cherry_picks curseforge:114514@1919810
## 启用/禁用某个包
trident package enable/disable --instance cherry_picks curseforge:114514@1919810

# 账号管理
## 查看支持的账号类型和相关信息
trident account help
## 列出托管的账号
trident account list
## 移除一个账号
trident account remove --uuid 00000000-0000-0000-0000000000000000
## 使用 Device Code Flow 添加微软账号并在打印 User Code 之后进入等待直到完成或超时
trident account add --type microsoft
## 添加离线账号
trident account add --type offline --username Steve --uuid 00000000-0000-0000-0000000000000000

# 仓库管理
## 查看支持的仓库提供程序标签
trident repository help
## 查看托管的仓库列表
trident repository list
## 添加一个仓库
trident repository add --label curseforge --endpoint https://api.curseforge.com --api-key "1145141919810"
## 移除一个仓库
trident repository remove --label curseforge

```
