using UnityEngine;

public enum ShelterInteractionKind
{
    Storage,
    Drink,
    Rest,
    Workbench,
    Deploy
}

/// <summary>
/// 基地内的编辑器可摆放交互点。它复用玩家现有的 F 键交互与提示 UI，
/// 每个点的碰撞器大小就是可交互表面，可在 Inspector 中直接调整。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public sealed class ShelterInteractionPoint : MonoBehaviour, IWorldInteractable
{
    [Tooltip("交互点功能类型。")]
    [SerializeField] private ShelterInteractionKind kind;
    [Tooltip("提示中显示的名称，例如 基地仓库、饮水台。")]
    [SerializeField] private string displayName;
    [Tooltip("需要持续按住 F 的秒数；0 表示按下立即执行。")]
    [Min(0f)] [SerializeField] private float holdDuration;
    [Tooltip("逻辑楼层。基地灰盒默认是一楼，使用 0。")]
    [Min(0)] [SerializeField] private int floorIndex;
    [Tooltip("视线检测点。为空时使用本物体位置。")]
    [SerializeField] private Transform lineOfSightPoint;

    public string InteractionPrompt => $"{GetActionName()} {GetDisplayName()}";
    public float InteractionHoldDuration => Mathf.Max(0f, holdDuration);
    public int InteractionFloorIndex => Mathf.Max(0, floorIndex);
    public Transform InteractionTransform => transform;
    public Vector2 InteractionLineOfSightPoint => lineOfSightPoint != null
        ? lineOfSightPoint.position
        : transform.position;

    private void OnEnable() => WorldInteractableRegistry2D.Register(this);
    private void OnDisable() => WorldInteractableRegistry2D.Unregister(this);

    public bool CanInteract(PlayerInteractor2D interactor)
    {
        return interactor != null && FindObjectOfType<ShelterSceneController>() != null;
    }

    public void Interact(PlayerInteractor2D interactor)
    {
        var shelter = FindObjectOfType<ShelterSceneController>();
        if (shelter == null) return;

        switch (kind)
        {
            case ShelterInteractionKind.Storage:
                shelter.OpenStash();
                break;
            case ShelterInteractionKind.Drink:
                if (shelter.ShelterModules != null && shelter.ShelterModules.UseDrinkStation(shelter.Survival))
                    interactor.ShowFeedback("已补充饮水");
                break;
            case ShelterInteractionKind.Rest:
                if (shelter.ShelterModules != null && shelter.ShelterModules.UseRestArea(shelter.Survival))
                    interactor.ShowFeedback("休息完成");
                break;
            case ShelterInteractionKind.Workbench:
                interactor.ShowFeedback("工作台界面将在下一阶段接入");
                break;
            case ShelterInteractionKind.Deploy:
                shelter.StartRaid();
                break;
        }
    }

    /// <summary>供编辑器构建工具设置类型和显示名。</summary>
    public void Configure(ShelterInteractionKind pointKind, string pointName, float duration = 0f)
    {
        kind = pointKind;
        displayName = pointName;
        holdDuration = Mathf.Max(0f, duration);
    }

    private string GetDisplayName() => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    private string GetActionName()
    {
        return kind switch
        {
            ShelterInteractionKind.Storage => "打开",
            ShelterInteractionKind.Drink => "使用",
            ShelterInteractionKind.Rest => "休息",
            ShelterInteractionKind.Workbench => "使用",
            ShelterInteractionKind.Deploy => "出击",
            _ => "交互"
        };
    }
}
