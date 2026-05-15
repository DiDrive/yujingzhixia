using System;
using UnityEngine;

public static class AnimatorParamUtil2D
{
    /// <summary>
    /// 检查动画是否包含指定参数。
    /// </summary>
    /// <param name="anim">动画组件。</param>
    /// <param name="name">参数名称。</param>
    /// <param name="type">参数类型。</param>
    /// <returns>是否包含指定参数。</returns>
    public static bool HasParameter(Animator anim, string name, AnimatorControllerParameterType type)
    {
        if (anim == null) return false;
        if (string.IsNullOrWhiteSpace(name)) return false;
        var ps = anim.parameters;
        for (var i = 0; i < ps.Length; i++)
        {
            if (ps[i].type != type) continue;
            if (ps[i].name == name) return true;
        }
        return false;
    }
}
