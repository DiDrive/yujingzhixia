using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

/// <summary>
/// HotUpdate.dll的统一业务入口。
/// 资源更新和DLL加载完成后，由AOT层通过反射调用Run。
/// </summary>
public static class GameEntry
{
    private const string FirstSceneLocation = "RaidScene";

    public static bool Succeeded { get; private set; }

    public static IEnumerator Run(ResourcePackage package)
    {
        Succeeded = false;

        if (package == null)
        {
            Debug.LogError("GameEntry启动失败：ResourcePackage为空");
            yield break;
        }

        Debug.Log("进入HotUpdate.GameEntry，业务层开始启动");

        // 在加载远程业务场景前验证热更新程序集自身和 AOT 调用协议。
        // 检查失败时保持 Succeeded=false，让基础客户端停留在错误界面而不是带病进入游戏。
        if (!HotUpdateStartupSelfCheck.Run(out string selfCheckError))
        {
            Debug.LogError($"GameEntry启动失败：热更新自检未通过\n{selfCheckError}");
            yield break;
        }

        // 保留原来的热更新代码验证。以后可以删除Hello，把业务初始化放在这里。
        Hello.Run();

        yield return LoadScene(package, FirstSceneLocation, LoadSceneMode.Single);

        if (SceneManager.GetActiveScene().name != FirstSceneLocation)
        {
            Debug.LogError($"GameEntry启动失败：当前场景不是{FirstSceneLocation}");
            yield break;
        }

        Succeeded = true;
        Debug.Log($"HotUpdate.GameEntry启动完成，已进入场景：{FirstSceneLocation}");
    }

    /// <summary>
    /// 热更新业务层的YooAsset场景加载入口。
    /// </summary>
    public static IEnumerator LoadScene(
        ResourcePackage package,
        string sceneLocation,
        LoadSceneMode loadMode = LoadSceneMode.Single)
    {
        if (package == null)
        {
            Debug.LogError("加载场景失败：ResourcePackage为空");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(sceneLocation))
        {
            Debug.LogError("加载场景失败：场景地址为空");
            yield break;
        }

        Debug.Log($"HotUpdate开始加载YooAsset场景：{sceneLocation}");

        SceneHandle handle = package.LoadSceneAsync(
            sceneLocation,
            loadMode,
            LocalPhysicsMode.None,
            true);

        yield return handle;

        if (handle.Status != EOperationStatus.Succeeded)
        {
            Debug.LogError($"HotUpdate场景加载失败：{sceneLocation}\n{handle.Error}");
            yield break;
        }

        Debug.Log($"HotUpdate场景加载完成：{handle.SceneName}");
    }
}
