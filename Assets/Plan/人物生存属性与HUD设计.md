# 人物生存属性与 HUD 设计

## 当前范围

本阶段完成生命、饥饿、口渴、精力和感染状态的基础闭环。小地图与正式物品使用流程放在后续阶段，避免同时修改过多系统。

## 数值规则

| 属性 | 默认上限 | 默认每分钟变化 | 低值影响 |
|---|---:|---:|---|
| 生命 | 100 | 无自然恢复 | 降到 0 后玩家死亡 |
| 饥饿 | 100 | -1 | 低于警告值时伤害降低；低于危险值时持续掉血 |
| 口渴 | 100 | -1.25 | 低于警告值时移动速度降低；低于危险值时持续掉血 |
| 精力 | 100 | -0.625 | 低于警告值时耐力倍率降低；低于危险值时禁止蓄力攻击 |
| 感染 | 布尔状态 | 无 | 感染期间持续掉血 |

以上数值都能在 `Core_Survival` 的 Inspector 中调整，并带有悬浮提示。

## 流逝阶段

- `Breakout`、`Raid`：默认流逝。
- `NightAssault`：通过 `Decay During Night Assault` 开关控制。
- `Shelter`、`Result`：开启 `Decay Only During Run` 时暂停。
- 撤离成功进入 `Shelter` 后，生存属性不会继续自动减少。

## HUD 编辑方式

HUD 位于主 `Canvas/PlayerHUD`，是普通场景对象，不在运行时生成。

- 调整整体位置：修改 `PlayerHUD/Rect Transform/Anchored Position`。
- 调整整体大小：修改 `PlayerHUD/Rect Transform/Width、Height`。
- 调整单条位置与大小：展开 `HP`、`Hunger`、`Thirst`、`Fatigue`。
- 调整颜色：修改各行 `Bar/Fill` 的 `Image/Color`。
- 调整文字：修改对应 `Label`、`Value` 的 `Text` 参数。
- 调整编辑器预览：修改 `PlayerSurvivalHUD/编辑器预览`。

如果场景中没有 `PlayerHUD`，使用菜单：

`Tools/余烬之下/人物 HUD/创建人物生存 HUD`

该菜单只在编辑器中创建对象；创建后需要保存场景。

## 下一步

下一步应给背包物品增加正式“使用”入口，并在物品 JSON 中配置恢复类型、恢复量、使用时间和可能副作用。完成后，HUD 才能与拾取、消耗和生存状态形成完整玩法循环。
