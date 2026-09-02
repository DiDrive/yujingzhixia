# 卷四：C#、Unity 底层、性能、资源与工程化

## 1. Unity 对象为什么有托管与原生两层？

多数 `UnityEngine.Object` 是 C# 包装对象，背后对应引擎 C++ 对象。`Destroy` 安排销毁原生对象后，托管包装可能仍被字段或事件引用；Unity 重载 `==`，使它比较起来像 null，但 `ReferenceEquals` 可能仍为 false。这就是常说的 fake null。

因此内存不能只看 GC：Texture/Mesh/Audio 有原生和 GPU 内存；失去 C# 引用不等于原生资源立即卸载；Destroy 也不等于静态事件释放托管引用。应使用 Memory Profiler Snapshot 分析 Managed、Native、Graphics 和引用链，并在生命周期结束时解绑事件、Release Handle、Destroy 实例。

## 2. 值类型、引用类型、栈和堆为什么不能简单画等号？

struct 是值语义，赋值通常复制；class 是引用语义，变量保存引用。但值类型可作为 class 字段存在托管堆，也可因装箱成为独立对象；JIT/AOT 还可能做逃逸和寄存器优化，所以“struct 在栈、class 在堆”不准确。

大型可变 struct 放入 List 后 `var x=list[i]` 得到副本，修改 x 不会自动写回；频繁传参也会复制。小型不可变坐标/结果适合 readonly struct；有身份、共享可变状态和继承需求的实例适合 class。选择首先看语义，再用 Profiler 看成本。

## 3. 装箱、闭包和 LINQ 分配如何真正定位？

值类型转 object/接口可能装箱；捕获局部变量的 Lambda 生成 DisplayClass；LINQ 的迭代器、排序、ToList 和委托可能分配。它们并非任何地方都不能用：初始化、Editor 工具和低频业务优先可读性；每帧热点要测量。

用 Profiler Timeline 找 GC.Alloc，展开到具体方法；Deep Profile 会改变性能，只用于定位；Development Build 真机复测。优化包括泛型接口、避免热点字符串拼接、缓存固定委托/列表、普通 for、预设 Capacity、TMP.SetText。不要凭代码风格宣称“零 GC”。

## 4. Coroutine、Task、Job/Burst 的执行模型有什么本质区别？

Coroutine 是主线程上的协作式跨帧状态机，yield 只让出执行，不会把重 CPU 工作变并行。Task 适合异步 IO 或后台普通 .NET 计算，但大多数 Unity API 只能主线程调用，并要处理 CancellationToken、场景卸载和 async void 异常。Job System 把受限数据任务交给工作线程，NativeContainer 和 JobHandle 保证依赖；Burst 把支持的 C# 子集编译成本地 SIMD 优化代码。

选择依据：等待动画/分帧用 Coroutine；文件网络用 async；大量同构数学计算用 Jobs/Burst。小任务调度成本可能超过收益。对象销毁时取消异步，Job 中使用的 NativeArray 在依赖完成后 Dispose。

## 5. MonoBehaviour 生命周期背后的真正风险是什么？

背顺序只是基础。真正风险是不同对象同类回调顺序不保证、OnEnable 可能早于外部 Initialize、禁用/启用反复订阅、Instantiate 立即触发 Awake、切场景时常驻对象引用失效、关闭 Domain Reload 后静态字段保留。

解决方式是明确初始化阶段：Awake 只建立自身不变量；Composition Root 注入依赖；Initialize 后再 Enable 或 Start；OnEnable/OnDisable 成对订阅；SubsystemRegistration 重置静态状态。只有确实需要时设置 Script Execution Order，不能用全局顺序掩盖隐式依赖。

## 6. 性能优化为什么必须从预算和证据开始？

60 FPS 每帧约 16.67ms，但 CPU 主线程、Render Thread 和 GPU 可并行，不能把各模块耗时简单相加。先在目标设备固定场景抓 Development Build，判断 CPU/GPU/GC/IO/加载哪类瓶颈，再向下定位。

平均 FPS 会掩盖尖峰，应看 P95/P99 帧时间、GC 峰值、内存高水位和长时间热降频。优化后用相同设备、路径和构建重复测试。对象池、LINQ 重写、降低小地图刷新率都只有在数据支持时才能声称收益。

针对本项目优先测：GridInventoryUnityUI 刷新和对象创建、小地图第二相机、僵尸 Update/Find、投射物 Instantiate/Destroy、Resources.Load 首次峰值和场景激活。

## 7. 对象池为什么不是一个 Queue 就结束？

池解决高频创建/销毁带来的引擎注册、组件生命周期和分配峰值，但引入状态复用风险。投射物取出时要设置 owner、伤害、方向、速度、楼层和寿命；回收时取消 Invoke/协程、清 velocity、恢复 Collider、Animator、isHit、ignored collision 和事件。任何遗漏都会产生跨生命周期幽灵 Bug。

池需要最大容量、溢出策略、预热量、场景 Scope 和统计。低频大型对象池化会长期占内存；Addressables 实例还涉及 Handle 所有权。先以投射物、命中特效、飘字和 Item View 为候选，用 Profiler 验证。

## 8. Draw Call、SetPass、Overdraw 和显存为什么是不同问题？

Draw Call/Batch 关注 CPU 提交；SetPass 关注 Shader Pass 和渲染状态切换；Overdraw 关注同一像素被透明层重复绘制，主要消耗 GPU 填充率；纹理/RT 尺寸和格式决定显存与带宽。减少 Draw Call 不一定减少 Overdraw，合并巨大透明 Sprite 甚至可能更差。

用 Frame Debugger 看批次为何断开，用 GPU Profiler/平台工具看耗时，用 Overdraw 视图看透明覆盖，用 Memory Profiler 看纹理。Sprite Atlas 减少纹理切换，但要权衡大图集加载粒度、Padding、压缩和无用区域。

## 9. 实时小地图怎样做可量化优化？

成本近似受 RT 像素数、可见 Renderer/Shader、透明覆盖和刷新频率影响。项目将相机 disabled，按约 10Hz 手动 Render，关闭 HDR、MSAA、Occlusion，并用 CullingMask 排除角色/UI；玩家与兴趣点走独立 UI 标记。

进一步可只在玩家移动超过阈值、楼层或室内状态变化时刷新；使用小地图专用简化材质；按设备降低 512×274；UI 关闭时停止渲染。若地图静态，预烘焙底图加动态标记可能更便宜。优化结论必须比较 GPU/CPU frame time，而不是只说“10FPS所以性能好”。

## 10. Resources、AssetBundle、Addressables 的层次是什么？

Resources 简单但所有目录内容进入构建、地址是字符串、依赖和卸载粒度不透明，适合小型原型或少量常驻配置。AssetBundle 是底层打包加载单元，需要自己管理 Manifest 依赖、版本、Hash/CRC、缓存和卸载。Addressables 通常基于 Bundle，增加地址、Catalog、依赖下载和 Handle 引用计数。

项目的 LoadFromFile 模块证明本地/远端 Bundle、Manifest、依赖和校验知识，但主项目仍主要用 Resources/直接引用，因此不能声称已完成热更新。真正上线还需 Catalog 更新、失败重试、磁盘空间、共享依赖重复、代码与内容版本兼容和 Release 所有权。

## 11. Addressables 为什么最容易在 Release 上出错？

LoadAsset/Instantiate 返回 Handle，资源及依赖通过引用计数存活。忘记 Release 会常驻；提前或重复 Release 会让仍使用资源失效。Destroy 实例不一定等价于释放所有加载 Handle，具体取决于 API 和所有权。

应建立 Lease/Scope：谁加载谁释放，场景 Scope 结束统一 Dispose；缓存保存 Handle 而不只保存 asset；诊断窗口检查引用和 Bundle。远端更新只更新内容，不等于支持 C# 代码热更。

## 12. IL2CPP 为什么会让 Editor 正常的代码在真机失败？

IL2CPP 是 AOT，反射动态创建、泛型组合、代码裁剪和平台插件可能与 Editor Mono 不同。仅由反射访问的类型可能被裁剪，需要 Preserve/link.xml；某些运行时代码生成不可用；构建时间和符号调试也不同。

因此 CI 必须包含目标平台 IL2CPP 构建；序列化、反射注册、原生插件和 Addressables 至少真机冒烟测试。不要把 Editor Play Mode 通过等同于平台可发布。

## 13. asmdef 怎样真正改善项目，而不是多几个文件？

asmdef 明确程序集边界和单向依赖，减少无关重编译，并隔离 Editor、Tests 和平台代码。建议 Domain 不依赖 Presentation；Application 依赖 Domain；Presentation 依赖 Application；Infrastructure 实现接口；Editor 引用 Runtime；Tests 按层引用。

拆分会暴露循环依赖，这正是价值。可用事件契约、接口下沉或组合根解除循环。当前所有业务大多在 Assembly-CSharp，任何修改都可能全量重编；引入 asmdef 前先画依赖图，不能按文件夹机械建程序集。

## 14. 自动化测试如何覆盖这个项目的真实风险？

EditMode 测试纯逻辑：footprint、边界、锁格、First Fit、堆叠、回滚、数量守恒、权重边界、存档迁移。随机操作序列后运行 `ValidateInvariants`，能发现组合 Bug。

PlayMode 测连接：Prefab 引用、Animation Event、物理触发、搜索 UI、尸体激活、切场景后一套服务和一个 Player。端到端测试完整出击—搜索—撤离—返回。大量便宜单测、少量集成和关键人工视觉回归构成测试金字塔。

测试随机时注入 RNG；测试时间时注入 Clock 或手动推进；测试文件用临时目录；每次清理静态 EventBus/Store，避免测试互相污染。

## 15. CI 与可上线工程还缺哪些环节？

流水线至少锁定 Unity/Packages，批处理导入，运行配置校验、EditMode/PlayMode 测试，构建目标平台，保存 XML、Editor.log 和产物。进一步检查包体、丢失引用、Shader Variant、Addressables 内容、性能基线和存档兼容。

运行质量还需要结构化日志、崩溃收集、版本/设备/场景上下文、发布分支和回滚策略。Unity License、签名证书和平台密钥放安全变量，不能提交仓库。

对当前 Demo 的诚实结论是：玩法闭环和数据建模有深度，但缺正式存档、测试、资源所有权、性能数据和发布管线；面试时给出有优先级的补齐方案，比声称“已经商业化”更可信。

