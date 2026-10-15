# YooAsset 与 HybridCLR 热更新改造实施方案

更新日期：2026-09-07

状态：方案已记录，业务改造尚未实施。本文作为后续修改项目的依据，不代表下面的功能已经实现。

## 1. 已确认的方向与实施边界

- 采用「完整基础可玩资源首包 + HostPlayMode 增量更新」。
- Unity Scenes In Build 只启用 BootScene。
- RaidScene、ShelterScene 通过 YooAsset 加载，其基础版本 Bundle 随首包提供。
- 基础版本 HotUpdate.dll.bytes 作为 YooAsset 资源随首包提供，运行时由 HybridCLR 加载；不能将热更新程序集同时作为普通 AOT 业务程序集打入 Player。
- 先保留现有功能，再按模块逐步改造，避免一次性移动全部 Scripts。
- 用户要求先分析、记录方案，实际代码修改等待后续明确指令。本次只创建本文。
- 上一次尝试的 HotUpdateContracts、YooAssetHotUpdateContext 及对应入口接口改造已经撤回，不得假设项目已有这些接口。
- 后续执行前重新检查 Git 差异、实际文件、Unity 编译状态和包版本；不能直接以本文覆盖用户新代码。

## 2. 当前项目基线

以下是本轮检查与此前代码审阅得到的基线；旧路线图里仍出现 StartScene，当前业务场景名称以实际工程中的 RaidScene 为准。

| 项目 | 当前情况 |
|---|---|
| Unity | 此前使用 2022.3.62f1，实施前重新核对 ProjectVersion.txt |
| YooAsset | 此前使用 3.0.5，具体 API 以安装包源码为准 |
| 启动场景 | BootScene，EditorBuildSettings 中唯一启用的场景 |
| 业务场景 | RaidScene、ShelterScene，未启用为 Player 内置场景 |
| 更新器 | Assets/Scripts/MyYooAsset/MyYooAsset.cs |
| DLL 加载器 | Assets/Scripts/HybridClr_test/LoadDll.cs |
| 热更入口 | Assets/HotUpdate/GameEntry.cs，当前为 static Run(ResourcePackage) |
| 热更程序集 | Assets/HotUpdate/HotUpdate.asmdef |
| DLL 资源目录 | Assets/AB/HotDll |
| 内置资源目录 | Assets/StreamingAssets/yoo/DefaultPackage |
| 场景流程 | Assets/Scripts/Core/SceneFlowController.cs |
| 收集配置 | 此前位于 Assets/Samples/YooAsset/3.0.5/Space Shooter/GameSetting/BundleCollectorSetting.asset，迁移前重新确认有效配置 |

已有功能包括玩家战斗、僵尸行为与尸体搜索、网格背包、物资容器、生存属性、消耗品、基地仓库、工作台、夜袭、楼层显隐、小地图、出生与撤离。存在这些代码和配置不等于本轮已经完成运行回归。

目前大部分业务仍在 Assembly-CSharp；GameEntry 和 Hello 在 HotUpdate。更新业务场景 Bundle 不会自动更新其引用的 AOT 脚本实现。

目前配置/图标加载中仍有 Resources 路径：

- GridInventoryService：Data/item_definitions。
- LootTableDatabase：Data/loot_tables。
- GridInventoryUnityUI：definition.iconResourcePath 对应的 Sprite。

注意：两个配置加载器还可能优先使用 Inspector 绑定的 TextAsset。迁移时必须同时检查绑定与回退路径，否则修改加载代码后仍可能使用旧配置。

## 3. 首包内容与运行流程

| 内容 | 首包形式 | 后续更新方式 |
|---|---|---|
| BootScene、更新 UI、启动所需字体图片 | Player 内置引用 | 修改后重打 Player |
| MyYooAsset、LoadDll、资源解密、平台能力 | AOT 代码 | 修改后重打 Player |
| HotUpdate.dll.bytes | YooAsset 内置 Bundle | 发布新的 DLL 资源 Bundle |
| RaidScene、ShelterScene | YooAsset 内置 Bundle | 发布新的场景及依赖 Bundle |
| 基础玩家、僵尸、UI、音效、图标、配置 | YooAsset 内置 Bundle/依赖 | 发布变化的资源 |
| 补充元数据（需要时） | 与 Player 基线匹配的资源 | 按该 Player 的兼容规则管理 |
| 后续大型地图、非必要活动内容 | 可不内置 | 按模块下载，另行实现按需流程 |

目标顺序：

```text
BootScene
→ 初始化资源包和内置/沙盒文件系统
→ 请求兼容的远程版本并加载 Manifest
→ 下载缺失或变化的基础资源
→ 加载所需 AOT 补充元数据
→ 加载 HotUpdate.dll（有依赖 DLL 时按依赖顺序加载）
→ GameEntry 初始化配置和业务服务
→ YooAsset 加载 RaidScene
→ 正常进行出击、撤离、基地循环
```

首包含基础资源不等于支持断网启动。当前远程请求失败会停止流程；离线回退必须独立设计并验收。

## 4. 分层原则及必要修正

### 4.1 AOT 启动与基础设施

负责资源更新、错误重试、DLL 加载、平台能力和必要的资源生命周期管理。BootScene 不能依赖尚未加载的热更组件。

MyYooAsset 逐步收敛为启动协调器。通用资源加载可以抽成 AssetService/SceneService；「如何加载场景」是基础能力，「撤离后去哪、何时清空本局状态」是业务决策。不能仅因为函数名是 LoadScene，就认定它必须属于业务层。

### 4.2 共享接口（按实际跨层需求引入）

必要时建立独立 AOT asmdef，放稳定的接口与跨层数据，例如入口接口、资源服务接口。AOT 与 HotUpdate 都引用它，禁止循环引用。

不要提前为每个类制造接口，也不要把共享层变成第二个业务程序集。当前 static GameEntry + 一次反射入口可以继续工作，接口化应有具体调用需求后再落地。

Unity 自定义 asmdef 不能直接引用预定义 Assembly-CSharp。因此热更业务需要调用现有默认程序集中的类型时，要抽取接口、移动适当的基础类型到独立程序集，或通过注入实现连接。

### 4.3 热更新业务

优先迁移变化频繁且容易隔离的规则：掉落、生存、消耗品、制作、夜袭、战斗公式、后续任务和活动。新功能优先在 HotUpdate 开发。

已有 MonoBehaviour 可以逐步保留为适配组件，将规则交给热更实现；这只是迁移策略，不要求所有 MonoBehaviour 永久留在 AOT。新的热更 MonoBehaviour 也可以用于远程场景和 Prefab，前提是程序集先加载且构建、类型引用正确。

移动已有脚本跨程序集时必须检查序列化、UnityEvent、类型名字符串、编辑器脚本、场景和 Prefab。保留 .meta/GUID；不能声称跨程序集必然丢脚本，也不能假设仅保留 GUID 就保证所有序列化都兼容。

## 5. 实施阶段与验收

### 阶段 0：固定可回归基线

1. 检查工作区及 Unity Console，记录既有问题。
2. 保存当前 Player、对应 DLL、YooAsset 构建结果、构建配置和版本信息。
3. 回归 Boot → Raid → 撤离 → Shelter → 再出击。
4. 记录背包、搜索容器、生存数值跨场景是否正确保留。

原因：后续每个阶段都能区分迁移引入的问题与旧问题。

验收：有可运行的基线安装包，记录启动日志与已知问题。

### 阶段 1：整理正式资源配置

1. 确认可用的 YooAsset Collector 配置及编辑器对它的引用/查找方式。
2. 在 Unity 内保留 GUID 地迁移到 Assets/GameSettings/YooAsset 等项目目录。
3. 确认 RaidScene、ShelterScene、HotDll 及基础资源仍被正确收集。
4. 检查重复地址、同名文件、场景依赖和 Bundle 划分；两个场景不一定需要打进同一个 Bundle，以报告和更新粒度决定。
5. 检查对 Samples 的代码、程序集、Prefab、材质、Shader 引用，再决定是否删除示例；删除示例不是本阶段的默认动作。

验收：配置可打开，构建报告包含所需资源；BootScene 不作为远程业务场景收集；不依赖被删除的示例资产。

### 阶段 2：配置与图标接入 YooAsset

1. 为配置定义稳定地址，迁移 item_definitions.json、loot_tables.json 到正式资源目录。
2. 审查 Inspector TextAsset 引用、Resources 回退和相关编辑器同步工具。
3. 在业务服务初始化之前异步加载、解析并校验配置；显式传入服务，避免 Awake 抢先读取旧配置。
4. 保留物品 ID、掉落表 ID 等稳定键；校验重复 ID、无效引用和必要字段。
5. 为图标建立地址解析与缓存机制，迁移 iconResourcePath 时制定兼容转换。
6. 管理资源句柄：图标显示期间保留，最后一个使用者释放后再释放句柄；缺失时使用明确占位图。
7. 配置首次加载失败显示错误；不要静默使用另一份旧配置造成版本混用。

主要文件：GridInventoryService、LootTableDatabase、GridInventoryUnityUI，以及操作这些配置的 Editor 工具。

验收：旧 Player 下载新资源后，物品属性/掉落配置/图标发生预期变化；搜索、使用、拖拽、堆叠功能不退化。

### 阶段 3：完善启动与错误状态

1. 拆分初始化、版本、Manifest、下载、元数据、DLL、入口等步骤，提供阶段、进度、错误信息。
2. 让 playMode 字段真正对应所选模式；当前实现需核实是否仍固定使用 HostPlayModeOptions。
3. 提供加载 UI 和重试，防止重复初始化、重复加载 DLL、重复创建常驻服务。
4. 清理无用缓存失败通常应记录警告并继续，不应自动阻止已有有效资源启动；致命错误与维护错误分开处理。
5. 检查所有协程运行期异常。调用返回 IEnumerator 的方法时 try/catch 不能自动捕获其后续 MoveNext 中的异常。
6. 明确资源句柄持有/释放以及场景切换中的失败恢复；避免失败后 loading 标记永久为 true。
7. 常驻对象只保留必要服务，检查重复 EventSystem、Camera 和 UI。

验收：超时、下载失败、DLL 地址错误、场景加载失败都有可见结果；成功路径只有一次业务入口。

### 阶段 4：建立必要的跨层边界

1. 依据首个待迁移模块列出所有类型依赖。
2. 必要时引入共享 AOT 程序集，提供最小接口及数据结构。
3. 启动层加载热更入口后注入服务，热更入口注册规则实现。
4. 保留现有 MonoBehaviour 和场景绑定，先替换内部规则调用。
5. GameEntry 负责初始化顺序，避免业务组件在规则注册前运行。

验收：无 asmdef 循环引用、无 Missing Script；IL2CPP Player 中入口与接口调用正常。本阶段修改 AOT 边界后必须重做基础安装包。

### 阶段 5：逐个迁移业务

| 顺序 | 模块 | 拆分内容 | 回归重点 |
|---|---|---|---|
| 1 | 掉落 | 权重抽取、奖励规则 | 首次生成、再次打开不重抽、尸体与夜袭复用 |
| 2 | 生存/消耗品 | 衰减、惩罚、使用效果 | 数值边界、使用完成扣除、取消不中途扣除 |
| 3 | 制作/基地/夜袭 | 配方校验、奖励和阶段规则 | 仓库与随身背包范围、消耗与发放一致 |
| 4 | 战斗 | 伤害、冷却和效果规则 | 玩家/僵尸受击、近战/投射物、死亡掉落 |
| 5 | UI/背包大模块 | Presenter、操作规则 | 拖拽、旋转、拆分、整理、订阅释放 |
| 6 | 场景业务流程 | 出击、撤离、基地阶段决策 | 成功后提交状态、失败不丢本局数据 |

每次只迁移一个可独立验收的模块。大型 GridInventoryUnityUI、GridInventoryService、玩家及地图组件不做批量目录移动。模块迁移期间的 AOT 适配修改需要更新基础 Player；稳定后该模块的兼容热更改动才可只发布 DLL。

### 阶段 6：构建与发布自动化

提供项目自己的 Editor 构建菜单，区分「构建基础 Player」和「构建远程更新」。

共同检查：无编译错误、目标平台一致、热更程序集配置正确、DLL 地址有效、资源版本唯一、记录 Unity/包版本和提交信息。校验实际输出内容或哈希，不能只依赖修改时间。

基础 Player 流程：

1. 根据安装的 HybridCLR 版本执行必要生成流程（包括相应桥接、裁剪保留等），依据实际构建脚本确认顺序。
2. 编译目标平台 HotUpdate.dll，复制到 Assets/AB/HotDll/HotUpdate.dll.bytes，并刷新导入。
3. 如需补充元数据，使用匹配该 Player 基线的裁剪后 AOT DLL；处理生成过程与最终打包的先后关系，必要时先生成基线再完成资源与最终 Player 构建。
4. 构建 YooAsset 基础版本，选择 ClearAndCopyAll 内置完整基础资源及配套内置目录信息。
5. 确认 Scenes In Build 只启用 BootScene，构建 IL2CPP Player。
6. 归档 Player、元数据、DLL、Manifest、Bundle、报告与校验信息。

远程更新流程：

1. 以已发布 Player 的兼容基线检查本次修改，不能悄悄引入旧 AOT 中不存在的接口。
2. 编译并复制新 HotUpdate.dll.bytes；仅改资源时可复用经过校验的匹配 DLL。
3. 构建新的 YooAsset Package Version；禁用复制到测试客户端的内置目录，具体选项名以当前包为准。
4. 发布完整必要产物：先 Bundle，再版本对应的清单/校验文件，最后切换 version 指针；核对构建报告与当前 API，不能凭旧教程只上传两个文件。
5. 保留旧发布产物供兼容客户端使用和回退，验证通过前不删除旧版本。
6. 用保留的旧安装包测试，记录实际下载列表、DLL 业务版本及资源版本。

BuiltinCatalog 等内置目录信息描述安装包自带内容；随基础内置构建生成，不得与 Bundle 手工删改造成不一致。远程发布文件和内置文件的用途要根据 YooAsset 3.0.5 实际输出分别确认。

## 6. AOT 元数据与版本兼容

- 当前简单 Hello 成功不足以证明复杂泛型路径可用。
- 根据真实使用的泛型与 HybridCLR 输出决定补充元数据集合；需要时在相应业务执行前加载并检查返回结果。
- 补充元数据不等于可以添加旧 Player 没有的原生 API，也不能解决所有代码裁剪和桥接问题。
- 新泛型调用是否需要新 Player 要依据旧 AOT、补充元数据、桥接与实际真机验证判断，不能一律判定必须或无需重打。
- Unity/HybridCLR/原生插件/共享接口变化通常属于新 Player 基线。
- 为 Player 兼容基线设置独立发布范围，例如 PC/基线1；具体目录命名可调整。资源版本与安装包版本分别记录。
- 已加载程序集通常不能在当前进程中卸载替换，首阶段只支持下次启动使用新 DLL。
- 将来接入磁盘存档时增加 schemaVersion、稳定 ID、迁移与回退策略；避免直接序列化热更运行时类型身份。

## 7. 测试步骤（首包 V1 → 远程 V2）

1. 构建并保留 V1 Player，基础资源全部内置。
2. 使用该测试安装的独立沙盒，避免误清真实存档；记录缓存路径。
3. 服务器仍为 V1 时启动，期望基础资源无需下载，能进入完整循环。
4. 修改一个图标/配置和一条 HotUpdate 业务版本日志，构建发布 V2。
5. 不重新构建 V1 Player，不把 V2 CopyAll 到它的 StreamingAssets。
6. 关闭并重新启动 V1，期望只下载缺失/变化的 Bundle，显示 V2 业务日志和资源效果。
7. 再启动一次，期望无需重复下载且仍使用 V2。
8. 测试失败重试、错误地址、下载中退出后再启动，以及场景加载失败提示。

编辑器 Host 测试可验证资源路径，但 LoadDll 的 Editor 分支使用 Unity 已编译的 HotUpdate 程序集，不能单凭编辑器日志证明 CDN DLL 被执行。DLL 更新验收必须包含旧 IL2CPP Player。

不要靠删除 StreamingAssets 的单个 Bundle 制造更新；这可能让内置目录宣称存在的资源实际缺失。ClearUnusedBundleFiles 不会清除当前仍需要的缓存，因此不能用它模拟首次下载。全量清理仅限明确的测试缓存，不是正式启动常规操作。

## 8. 离线启动（独立后续阶段）

当前不承诺离线可玩。需要实现时：

1. 定义哪些网络错误允许回退，以及强制更新/不兼容版本时是否禁止回退。
2. 选择完整且兼容的最后成功版本，或完整内置基线。
3. 使用同一版本的 DLL、配置和场景，禁止混用下载不完整的新版本。
4. 本地资源缺失时提示联网修复，不能进入半初始化游戏。
5. 验证首次离线、更新成功后离线、下载中断后离线三种情况。

## 9. 完成判据与后续执行记录

最终目标：基础首包首次体验完整，后续兼容业务和资源可增量更新，启动失败可定位、可重试，现有游戏循环保持正常。

每个实际实施阶段需在此补充：

- 实施日期与范围。
- 修改文件及原因。
- 是否改变 AOT/Player 兼容基线。
- Editor 与 IL2CPP Player 的验证结果，未验证项明确标注。
- 首包版本、远程资源版本、DLL 版本/哈希。
- 已知问题及下一步。

当前记录：2026-09-07，仅创建方案文档；阶段 0—6 均未据本文实施。后续从重新核对基线开始，按用户授权的范围逐阶段执行。
