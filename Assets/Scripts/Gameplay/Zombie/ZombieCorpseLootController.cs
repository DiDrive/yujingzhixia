using System;
using UnityEngine;

/// <summary>
/// 在僵尸死亡后激活尸体搜索点，并把它配置成独立的可搜索容器。
/// 活着时尸体搜索点保持关闭，因此不会抢占玩家的交互目标。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ZombieController2D))]
public sealed class ZombieCorpseLootController : MonoBehaviour
{
    [Header("Corpse Container")]
    [Tooltip("僵尸死亡后显示在交互提示和容器标题中的名称。")]
    [SerializeField] private string corpseDisplayName = "僵尸尸体";
    [Tooltip("尸体使用的掉落表 ID，对应 Resources/Data/loot_tables.json。")]
    [SerializeField] private string lootTableId = "zombie_civilian";
    [Tooltip("尸体容器的行数。")]
    [Min(1)] [SerializeField] private int rows = 3;
    [Tooltip("尸体容器的列数。")]
    [Min(1)] [SerializeField] private int columns = 4;

    [Header("Editor-authored UI")]
    [Tooltip("Canvas 下预先制作的共享尸体容器 UI 名称。运行时只绑定数据，不创建 UI。")]
    [SerializeField] private string sharedUiObjectName = "InventoryUI_corpse_shared";

    [Header("Corpse Lifetime")]
    [Tooltip("尸体被搜空且容器 UI 关闭后，延迟多少秒开始淡出。")]
    [Min(0f)] [SerializeField] private float fadeDelayAfterEmptied = 8f;
    [Tooltip("淡出结束后销毁尸体对象，避免不可见的交互物残留。")]
    [SerializeField] private bool destroyAfterFade = true;

    [Header("References")]
    [Tooltip("死亡后启用的搜索点。为空时自动查找名为 CorpseSearchPoint 的子物体。")]
    [SerializeField] private GameObject corpseSearchRoot;
    [Tooltip("尸体搜索点上的容器组件。为空时会从 CorpseSearchPoint 中自动查找。")]
    [SerializeField] private SearchableLootContainer corpseContainer;

    private ZombieController2D zombie;
    private bool corpseActivated;
    private bool fadeReleased;

    private void Awake()
    {
        zombie = GetComponent<ZombieController2D>();
        ResolveCorpseReferences();

        // 必须在死亡前锁住淡出，否则原有死亡流程会在尸体容器激活后马上隐藏尸体。
        zombie.BlockCorpseFadeForLoot();
        zombie.Died += HandleZombieDied;

        if (corpseSearchRoot != null) corpseSearchRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (zombie != null) zombie.Died -= HandleZombieDied;
    }

    private void Update()
    {
        if (!corpseActivated || fadeReleased || corpseContainer == null) return;
        if (!corpseContainer.IsEmpty || corpseContainer.IsContainerUiOpen) return;

        fadeReleased = true;
        if (corpseSearchRoot != null) corpseSearchRoot.SetActive(false);
        zombie.ReleaseCorpseFadeAfterLoot(fadeDelayAfterEmptied, destroyAfterFade);
    }

    /// <summary>死亡瞬间固定尸体楼层、生成唯一会话 ID，并登记为世界交互物。</summary>
    private void HandleZombieDied()
    {
        if (corpseActivated) return;
        ResolveCorpseReferences();
        if (corpseSearchRoot == null || corpseContainer == null)
        {
            Debug.LogWarning($"ZombieCorpseLootController '{name}' 缺少 CorpseSearchPoint 或 SearchableLootContainer。", this);
            zombie.ReleaseCorpseFadeAfterLoot(0f, destroyAfterFade);
            return;
        }

        var floorIndex = FloorLayerUtility.GetFloorIndex(transform);
        if (floorIndex < 0) floorIndex = 0;

        corpseContainer.ConfigureRuntime(
            $"corpse_{Guid.NewGuid():N}",
            corpseDisplayName,
            floorIndex,
            lootTableId,
            rows,
            columns,
            FindSharedSceneUi());

        corpseActivated = true;
        corpseSearchRoot.SetActive(true);
    }

    private void ResolveCorpseReferences()
    {
        if (corpseSearchRoot == null)
        {
            var child = transform.Find("CorpseSearchPoint");
            if (child != null) corpseSearchRoot = child.gameObject;
        }

        if (corpseContainer == null && corpseSearchRoot != null)
            corpseContainer = corpseSearchRoot.GetComponent<SearchableLootContainer>();
    }

    /// <summary>查找编辑器中已经制作好的共享 UI；不会在运行时实例化或创建界面。</summary>
    private GridInventoryUnityUI FindSharedSceneUi()
    {
        var views = FindObjectsOfType<GridInventoryUnityUI>(true);
        for (var i = 0; i < views.Length; i++)
        {
            if (views[i] != null && views[i].name == sharedUiObjectName) return views[i];
        }

        Debug.LogWarning($"没有找到共享尸体 UI '{sharedUiObjectName}'，请在 Canvas 下检查该对象。", this);
        return null;
    }
}
