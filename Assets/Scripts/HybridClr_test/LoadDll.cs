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
    /// 热更新入口是否执行完成。
    /// </summary>
    public bool IsEntrySucceeded { get; private set; }

    private Assembly _hotUpdateAssembly;

    /// <summary>
    /// 由MyYooAsset在Package初始化、Manifest加载和资源下载完成后调用。
    /// 不使用Start()，避免YooAsset尚未初始化完成就访问资源包。
    /// </summary>
    public IEnumerator LoadHotUpdateDll(ResourcePackage package)
    {
        IsLoadSucceeded = false;
        IsEntrySucceeded = false;
        _hotUpdateAssembly = null;

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

        Type entryType = hotUpdateAss.GetType("GameEntry");
        if (entryType == null)
        {
            Debug.LogError("热更新程序集内没有找到GameEntry类型");
            yield break;
        }

        MethodInfo entryMethod = entryType.GetMethod(
            "Run",
            BindingFlags.Public | BindingFlags.Static);
        if (entryMethod == null)
        {
            Debug.LogError("GameEntry内没有找到public static Run(ResourcePackage)方法");
            yield break;
        }

        _hotUpdateAssembly = hotUpdateAss;
        IsLoadSucceeded = true;
        Debug.Log("HotUpdate.dll已就绪，等待执行GameEntry.Run");
    }

    /// <summary>
    /// 通过反射调用热更新入口。AOT程序集不直接引用任何热更新类型。
    /// </summary>
    public IEnumerator RunHotUpdateEntry(ResourcePackage package)
    {
        IsEntrySucceeded = false;

        if (!IsLoadSucceeded || _hotUpdateAssembly == null)
        {
            Debug.LogError("HotUpdate.dll尚未加载，不能执行热更新入口");
            yield break;
        }

        Type entryType = _hotUpdateAssembly.GetType("GameEntry");
        MethodInfo entryMethod = entryType?.GetMethod(
            "Run",
            BindingFlags.Public | BindingFlags.Static);

        if (entryMethod == null)
        {
            Debug.LogError("没有找到热更新入口GameEntry.Run");
            yield break;
        }

        IEnumerator entryRoutine;
        try
        {
            entryRoutine = entryMethod.Invoke(null, new object[] { package }) as IEnumerator;
        }
        catch (Exception exception)
        {
            Debug.LogError($"调用GameEntry.Run失败：{exception}");
            yield break;
        }

        if (entryRoutine == null)
        {
            Debug.LogError("GameEntry.Run必须返回IEnumerator");
            yield break;
        }

        yield return entryRoutine;

        PropertyInfo succeededProperty = entryType.GetProperty(
            "Succeeded",
            BindingFlags.Public | BindingFlags.Static);
        IsEntrySucceeded = succeededProperty != null &&
                           succeededProperty.PropertyType == typeof(bool) &&
                           (bool)succeededProperty.GetValue(null);

        if (IsEntrySucceeded)
            Debug.Log("热更新入口GameEntry.Run执行完成");
        else
            Debug.LogError("热更新入口GameEntry.Run执行失败");
    }
}
