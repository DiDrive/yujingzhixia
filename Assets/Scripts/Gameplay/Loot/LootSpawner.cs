using System;
using UnityEngine;

/// <summary>
/// 旧 Inspector 掉落生成器的迁移占位组件。
/// 正式掉落逻辑已统一到 LootTableDatabase 与 loot_tables.json；
/// 保留此空类型仅用于让已打开的旧场景安全移除组件，不再参与任何运行时流程。
/// </summary>
[Obsolete("掉落系统已统一到 LootTableDatabase 和 loot_tables.json。")]
[AddComponentMenu("")]
public sealed class LootSpawner : MonoBehaviour
{
}
