# 卷三：战斗、AI、物理、动画与场景流程

## 1. 角色移动为什么同时涉及 Update、FixedUpdate 和 LateUpdate？

输入边沿如 GetKeyDown 应在 Update 读取，否则一个渲染帧内的按下可能错过；移动意图缓存后，在 FixedUpdate 通过 Rigidbody2D 执行，保持物理步长一致；相机跟随和 Y 排序在 LateUpdate 使用本帧最终位置，减少抖动和一帧滞后。

`deltaTime` 适合渲染帧逻辑，`fixedDeltaTime` 适合物理步；Interpolate 用前后物理状态平滑渲染。直接每帧改 Dynamic Rigidbody 的 Transform 可能绕过碰撞解算。暂停、Hitstop 或 UI 持有进度是否继续，需要明确 scaled/unscaled time，而不是混用。

## 2. 武器 ScriptableObject 解决什么，又不能保存什么？

WeaponDefinition 共享武器 ID、类型、姿势、伤害、冷却、范围、投射物和 Animator Controller，适合设计师创建资产并被多个角色引用。它是静态定义，不应保存当前弹药、耐久、冷却剩余或持有者；这些是实例状态，否则一个角色修改会影响所有引用同一资产的角色。

近战/远程参数目前以两个 struct 共存在一个资产，通过 type 选择，简单但会有无效字段。扩展多种攻击模式时可用子资产策略、SerializeReference 或组合 AttackDefinition，避免巨型配置对象。

## 3. 动画事件驱动命中为什么既准确又危险？

优势是判定帧与挥刀/枪口画面对齐。输入只启动攻击和锁定状态，Animation Event 在关键帧调用判定或发射。危险在于方法名字符串、资源漏配、动画被打断、重复事件、换 Controller 后事件消失。

项目设置 fallback 延迟，避免漏事件完全无伤害；但必须防止事件和 fallback 都执行，可用 pendingDamage 标记或 attackSequenceId 保证一次攻击只结算一次。状态退出、死亡和切武器时取消 pending。配置校验应扫描所有攻击 Clip 是否包含期望事件。

## 4. IDamageable 与 IHitReceiver 为什么同时存在？

IDamageable 只表达“可受数值伤害”，适合饥饿、陷阱等不需要方向的信息；IHitReceiver 接收 DamageInfo，包含来源、方向和击退，适合战斗命中。命中优先完整接口，旧对象回退简单接口，支持渐进迁移。

长期可进一步区分 DamageRequest、DamageResult：请求包含攻击者、类型、暴击、穿甲；目标计算护甲、无敌和实际伤害后返回结果，表现系统再根据结果播放数字和反馈。不要让攻击者直接修改目标 HP 字段。

## 5. 投射物命中链需要哪些防线？

生成时记录 owner 并 IgnoreCollision owner Colliders；设置 LayerMask；命中时再次排除 owner 子节点、验证同楼层和敌对关系；优先 IHitReceiver；播放命中特效或销毁。Rigidbody2D Continuous 降低穿透，但高速或小 Collider 仍可用上一帧到当前帧 CircleCast 获取最近命中。

贴墙生成保护只能解决枪口轻微嵌入，时间过长会允许穿墙。更稳是发射前 Cast，校正 muzzle 到安全点。池化后必须重置 velocity、owner、ignored collision、isHit、Collider、Animator、Invoke 和生命周期，否则继承上一发状态。

## 6. Hitstop 和相机震动为什么不应直接抢占全局状态？

直接改 `Time.timeScale` 会影响物理、Animator、冷却和 WaitForSeconds；暂停菜单、慢动作和多个 Hitstop 同时存在时，各自保存 prevTimeScale 再恢复会互相覆盖。应有 TimeScaleService，把请求作为 Token，以优先级或最小值合成，释放后重新计算；fixedDeltaTime 从基础值统一派生。

相机震动若直接写 Camera.localPosition，会与 CameraFollow 或 Cinemachine 同帧争用。应加独立 Shake Pivot 叠加偏移，或用 Cinemachine Impulse。所有反馈用 unscaled time，才能在 Hitstop 期间继续衰减。

## 7. 当前僵尸 AI 的边界在哪里，如何演进？

现状是轻量状态控制加可组合 Movement/Attack 策略：找 Player、计算距离、追击、进入攻击、受击、死亡和尸体生命周期。它不是完整行为树，也没有听觉、最后已知位置、寻路或群体避障。

演进先拆感知、决策、执行：感知生成黑板数据；分层 FSM 处理 Dead/Hit/Combat，Combat 内处理 Chase/Attack；条件组合复杂、需设计师可视化时再引入行为树。AI 只决定“尝试攻击”，真正命中仍由战斗系统负责。大量敌人前先 Profiler，再考虑分帧 Tick、空间索引、Jobs/Burst 或混合 ECS。

## 8. 多楼层为什么必须统一逻辑层、物理层和渲染层？

Sorting Layer 只控制绘制，Unity Layer 控制 Physics/Camera，`FloorSortingState` 表示业务楼层。若三者各自推断，会出现二楼子弹打到一楼敌人、二楼墙挡住一楼搜索、小地图显示错层。

`FloorLayerUtility` 把当前楼层转换为 Actor_Fx、Wall_Fx 和 Sorting Layer；交互、投射物、敌对判断和可见性都使用同一来源。楼梯切换要原子更新 Rigidbody Layer、Sorting、Visibility 和临时渲染覆盖。测试要把不同楼层对象投影到同一 XY，验证仍不互相命中。

## 9. 跨场景为何只保留服务而不保留 Player？

Player 持有 Rigidbody、Collider、Animator、相机与 UI 引用，这些都属于场景语境；让它常驻容易带着旧引用、重复相机和错误碰撞层。背包和生存数据才是跨场景身份，因此保留服务，新场景生成表现 Player 并重新绑定。

两个场景预摆核心便于直接 Play，但加载时会短暂出现重复对象。Awake 尽早识别并销毁副本，调用方优先从 PersistentGameServices 取旧实例。更正式方案是唯一 Bootstrap 场景常驻、内容场景 Additive 加载，Editor 直接 Play 时自动注入 Bootstrap。

## 10. 撤离为什么不应该直接倒计时结束就 LoadScene？

完整流程应是：锁定撤离点→倒计时→再次验证玩家与状态→冻结输入→生成结算快照→把本局战利品提交到永久数据→写盘→显示结果→加载基地。任何一步失败都要明确重试或回滚。

当前 Demo 直接通过 GameBootstrap/SceneFlow 切场景并依靠内存服务保存，足以证明闭环，但不具备崩溃恢复和写盘事务。面试时主动说明这一层差距，并给出 runId、pending settlement 和原子存档方案。

