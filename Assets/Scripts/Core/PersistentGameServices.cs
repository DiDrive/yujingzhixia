using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 让一组核心数据服务跨场景保留，并在加载另一场景时清理新场景自带的重复核心。
/// 场景地图、玩家和 UI 不应放入此列表，它们仍由各自场景管理。
/// </summary>
public sealed class PersistentGameServices : MonoBehaviour
{
    [Tooltip("需要跨场景保留的根对象，例如状态机、生存系统、背包、工作台和庇护所服务。不要加入玩家、相机、Canvas 或 GameBootstrap。")]
    [SerializeField] private List<GameObject> serviceRoots = new();

    private static PersistentGameServices instance;
    public static PersistentGameServices Instance
    {
        get
        {
            if (instance == null) instance = FindObjectOfType<PersistentGameServices>(true);
            return instance;
        }
        private set => instance = value;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticInstance()
    {
        instance = null;
    }

    /// <summary>
    /// 从第一套常驻服务中取得指定组件。
    /// 再次加载 StartScene 的当帧，新旧服务会短暂同时存在；调用方应优先使用这里返回的旧实例。
    /// </summary>
    public T GetService<T>() where T : Component
    {
        for (var i = 0; i < serviceRoots.Count; i++)
        {
            var root = serviceRoots[i];
            if (root == null) continue;
            var service = root.GetComponentInChildren<T>(true);
            if (service != null) return service;
        }
        return null;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            DestroyDuplicateServices();
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        for (var i = 0; i < serviceRoots.Count; i++)
        {
            var root = serviceRoots[i];
            if (root == null) continue;

            // 核心预制体中的服务都是本对象的子节点。父节点一旦进入 DontDestroyOnLoad，
            // 整棵层级会一起保留，不需要把每个服务拆成 DontDestroyOnLoad 根节点。
            if (root.transform.IsChildOf(transform)) continue;
            DontDestroyOnLoad(root.transform.root.gameObject);
        }
    }

    /// <summary>供编辑器场景构建工具写入需要常驻的服务根节点。</summary>
    public void Configure(IEnumerable<GameObject> roots)
    {
        serviceRoots.Clear();
        if (roots == null) return;

        foreach (var candidate in roots)
        {
            if (candidate == null) continue;
            var serviceObject = candidate.transform.IsChildOf(transform)
                ? candidate
                : candidate.transform.root.gameObject;
            if (serviceObject == gameObject || serviceRoots.Contains(serviceObject)) continue;
            serviceRoots.Add(serviceObject);
        }
    }

    /// <summary>
    /// 回到 StartScene 时，场景文件会带入一套默认核心服务。
    /// 旧的常驻服务才保存着玩家当前数据，因此销毁新加载的副本。
    /// </summary>
    private void DestroyDuplicateServices()
    {
        for (var i = 0; i < serviceRoots.Count; i++)
        {
            var root = serviceRoots[i];
            if (root == null) continue;
            // 同一核心预制体内的服务会随重复的父对象一起销毁，不能逐个销毁，
            // 否则会把当前重复实例的层级拆散并产生一帧残留对象。
            if (root.transform.IsChildOf(transform)) continue;
            Destroy(root.transform.root.gameObject);
        }
    }
}
