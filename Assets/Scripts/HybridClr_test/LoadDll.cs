using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using YooAsset;

public class LoadDll : MonoBehaviour
{
    /// <summary>
    /// 本次加载是否成功。由MyYooAsset在资源准备完成后读取。
    /// </summary>
    public bool IsLoadSucceeded { get; private set; }

    /// <summary>
    /// 由MyYooAsset在Package初始化、Manifest加载和资源下载完成后调用。
    /// 不使用Start()，避免YooAsset尚未初始化完成就访问资源包。
    /// </summary>
    public IEnumerator LoadHotUpdateDll(ResourcePackage package)
    {
        IsLoadSucceeded = false;

        if (package == null)
        {
            Debug.LogError("加载热更新DLL失败：ResourcePackage为空");
            yield break;
        }

        Assembly hotUpdateAss;

        // Editor环境下，HotUpdate程序集已经被Unity编译并加载。
#if !UNITY_EDITOR
        // 注意：当前Collector使用AddressByFileName，构建报告中的地址是HotUpdate.dll。
        const string location = "HotUpdate.dll";
        Debug.Log($"开始通过YooAsset加载热更新DLL：{location}");

        AssetHandle handle = package.LoadAssetAsync<TextAsset>(location);
        yield return handle;

        if (handle.Status != EOperationStatus.Succeeded)
        {
            Debug.LogError($"加载热更新DLL失败：{handle.Error}");
            handle.Release();
            yield break;
        }

        TextAsset text = handle.AssetObject as TextAsset;
        if (text == null)
        {
            Debug.LogError("加载热更新DLL失败：资源不是TextAsset");
            handle.Release();
            yield break;
        }

        try
        {
            hotUpdateAss = Assembly.Load(text.bytes);
            Debug.Log($"热更新程序集加载成功：{hotUpdateAss.GetName().Name}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"Assembly.Load HotUpdate.dll失败：{exception}");
            handle.Release();
            yield break;
        }

        // Assembly.Load后DLL已进入运行时内存，可以释放YooAsset资源句柄。
        handle.Release();
#else
        hotUpdateAss = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "HotUpdate");

        if (hotUpdateAss == null)
        {
            Debug.LogError("Editor中没有找到HotUpdate程序集，请检查HotUpdate.asmdef和编译错误");
            yield break;
        }
#endif

        Type type = hotUpdateAss.GetType("Hello");
        if (type == null)
        {
            Debug.LogError("热更新程序集内没有找到Hello类型");
            yield break;
        }

        MethodInfo method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
        if (method == null)
        {
            Debug.LogError("Hello内没有找到public static Run方法");
            yield break;
        }

        try
        {
            method.Invoke(null, null);
            IsLoadSucceeded = true;
            Debug.Log("Hello.Run调用完成");
        }
        catch (Exception exception)
        {
            Debug.LogError($"Hello.Run执行失败：{exception}");
        }
    }
}
