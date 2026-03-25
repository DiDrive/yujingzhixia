using UnityEngine;
using Sirenix.OdinInspector;

/// <summary>
/// 主循环状态机，负责驱动玩法阶段切换。
/// </summary>
public class GameStateMachine : MonoBehaviour
{
    [PropertyTooltip("当前玩法阶段。运行时会随流程切换，通常无需手动改动。")]
    [SerializeField] private GamePhase current = GamePhase.Shelter;

    public GamePhase Current => current;

    /// <summary>
    /// 从庇护所出发进入突围阶段。
    /// </summary>
    public void StartRun()
    {
        ChangeState(GamePhase.Breakout);
    }

    /// <summary>
    /// 从突围阶段进入摸金阶段。
    /// </summary>
    public void EnterRaid()
    {
        ChangeState(GamePhase.Raid);
    }

    /// <summary>
    /// 返回庇护所进行夜袭前准备。
    /// </summary>
    public void ReturnShelterBeforeNight()
    {
        ChangeState(GamePhase.Shelter);
    }

    /// <summary>
    /// 进入夜袭防守阶段。
    /// </summary>
    public void StartNightAssault()
    {
        ChangeState(GamePhase.NightAssault);
    }

    /// <summary>
    /// 结束当前循环并进入结算阶段。
    /// </summary>
    public void EndCycleToResult()
    {
        ChangeState(GamePhase.Result);
    }

    /// <summary>
    /// 从结算返回新一轮庇护所阶段。
    /// </summary>
    public void StartNextCycle()
    {
        ChangeState(GamePhase.Shelter);
    }

    private void ChangeState(GamePhase next)
    {
        if (next == current) return;
        var from = current;
        current = next;
        EventBus.Publish(new GamePhaseChangedEvent(from, next));
    }
}
