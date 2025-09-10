# Trident Cli

## 命令

```sh

# 为了能在非交互式终端使用，Cli 不提供交互输入功能并保证指令的确定性

# search/list 等列表型结果允许使用 --sort asc/desc 或 --index 10 --limit 20 来分页

# 长短名映射
# -I --instance
# -P --package
# -R --repository

# 实例上下文 Home/Profile 可以通过一系列方式指定并具有以下优先级
# - --profile {path_to_profile} 指定 profile.json 文件并将该目录作为 Instance Home
# - --instance {key_to_home} 在 Trident Home 中搜索 instances/{key_to_home}
# - 如果两个都没有指定，会在搜索 Trident Home 的时候顺便搜索 profile.json 文件及其所在目录作为关联实例或 Instance Home

# 创建实例
## 从特定版本创建空实例
trident create --id cherry_picks --name "Cherry Picks" --version 1.21.1
## 从文件导入，其中重命名和标识是可选的
trident import --id cherry_picks --name "Cherry Picks" ./modpack.zip
## 从 Purl 在线安装，其中重命名和标识是可选的
trident install --id cherry_picks --name "Cherry Picks" github:d3ara1n/cherry_picks@latest

# 元数据管理
## 添加 NeoForge 加载器为唯一加载器
trident loader set --instance cherry_picks net.neoforged:42.1.200
## 添加一个包
trident package add --instance cherry_picks curseforge:114@514
trident package remove --instance cherry_picks curseforge:114@514
## 搜索并列出关键字特定的包
trident search --repository curseforge --version 1.21.1 --loader net.neoforged --kind mod "Mouse Tweaks"

# 查看元数据
## 查看支持的加载器和相关信息
trident loader help
## 查看实例的加载器
trident loader get --instance cherry_picks
## 查询并列出关联版本，游戏版本为必须字段
trident loader query --version 1.21.1 net.neoforged
## 查询并列出实例中能被查询语句过滤的包
trident package list --instance cherry_picks --query "@USER JEI" --kind mod
## 启用/禁用某个包
trident package enable/disable --instance cherry_picks curseforge:114@514
## 查看包的关联信息，包含部分在线信息，以及标签、依赖、附属等
trident package inspect --instance cherry_picks curseforge:114@514
## 查看报的在线信息
trident package query curseforge:114@514

# 实例管理
## 解锁
trident unlock --instance cherry_picks
## 导出且将资源解析后打包进包体（离线模式）
trident export --instance cherry_picks --format tripack --type offline --name "Cherry Picks" --author d3ara1n --output ./modpack.zip
## 部署
trident build --instance cherry_picks
## 删除
trident delete --instance cherry_picks
## 重置
trident reset --instance cherry_picks

# 账号管理
## 查看支持的账号类型和相关信息
trident account help
## 列出托管的账号
trident account list
## 移除一个账号
trident account remove --guid 00000000-0000-0000-0000000000000000
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
