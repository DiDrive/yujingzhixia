using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 正式存档槽 V1。负责稳定 DTO、磁盘原子替换、备份恢复和核心服务状态装配。
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class SaveGameService : MonoBehaviour
{
    private const string SaveDirectoryName = "Saves";
    private const string SaveFileName = "slot_0.json";
    private const string BackupExtension = ".bak";
    private const string TempExtension = ".tmp";
    private const float AutomaticSaveDebounceSeconds = 2f;

    private static SaveGameService instance;
    private bool loadCompleted;
    private bool loadRoutineRunning;
    private Coroutine autoSaveRoutine;
    private float lastSaveRealtime = float.NegativeInfinity;

    [SerializeField, Min(10f)] private float autoSaveIntervalSeconds = 60f;

    public static SaveGameService Instance => instance;
    public bool LoadCompleted => loadCompleted;
    public string SavePath => Path.Combine(Application.persistentDataPath, SaveDirectoryName, SaveFileName);
    public bool HasSave => File.Exists(SavePath) || File.Exists(SavePath + BackupExtension);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureRuntimeInstance()
    {
        if (instance != null) return;
        var root = new GameObject("SaveGameService");
        root.AddComponent<SaveGameService>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (autoSaveRoutine == null)
            autoSaveRoutine = StartCoroutine(AutoSaveLoop());
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (autoSaveRoutine != null)
        {
            StopCoroutine(autoSaveRoutine);
            autoSaveRoutine = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!loadCompleted && !loadRoutineRunning)
            StartCoroutine(LoadOrCreateInitialSaveWhenServicesReady());
        else if (loadCompleted)
            ApplyDiscoveredMarkersToScene();
    }

    private IEnumerator LoadOrCreateInitialSaveWhenServicesReady()
    {
        loadRoutineRunning = true;
        // 等待玩法场景中的核心服务完成 Awake/Start 和物品定义加载。
        for (var frame = 0; frame < 300; frame++)
        {
            if (ResolveService<GridInventoryService>() != null &&
                ResolveService<SurvivalSystem>() != null &&
                ResolveService<GameStateMachine>() != null)
            {
                yield return null;
                if (HasSave)
                    Load();
                else
                    Save("initial");
                break;
            }
            yield return null;
        }
        loadRoutineRunning = false;
    }

    private IEnumerator AutoSaveLoop()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(10f, autoSaveIntervalSeconds));
            TryAutomaticSave("periodic");
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus) TryAutomaticSave("application-pause");
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) TryAutomaticSave("application-focus-lost");
    }

    private void OnApplicationQuit()
    {
        TryAutomaticSave("application-quit");
    }

    [ContextMenu("Save Slot 0")]
    public bool Save(string reason = "manual")
    {
        var inventory = ResolveService<GridInventoryService>();
        var survival = ResolveService<SurvivalSystem>();
        var stateMachine = ResolveService<GameStateMachine>();
        if (inventory == null || survival == null || stateMachine == null)
        {
            Debug.LogWarning("存档失败：核心服务尚未就绪。", this);
            return false;
        }

        var data = new SaveGameData
        {
            savedAtUtc = DateTime.UtcNow.ToString("O"),
            applicationVersion = Application.version,
            saveReason = string.IsNullOrWhiteSpace(reason) ? "manual" : reason,
            gamePhase = (int)stateMachine.Current,
            survival = survival.CaptureSaveData(),
            inventory = inventory.CapturePersistentSaveData(),
            searchableLoot = SearchableLootRuntimeStateStore.CaptureAll(),
            discoveredMarkerIds = MinimapDiscoveryStateStore.CaptureAll()
        };

        var shelter = ResolveService<ShelterModuleService>();
        if (shelter != null) data.shelterHasPower = shelter.HasPower;

        try
        {
            WriteAtomically(JsonUtility.ToJson(data, true));
            loadCompleted = true;
            lastSaveRealtime = Time.realtimeSinceStartup;
            Debug.Log($"存档完成：{SavePath}（{data.saveReason}）", this);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError("写入存档失败：\n" + exception, this);
            return false;
        }
    }

    [ContextMenu("Load Slot 0")]
    public bool Load()
    {
        if (!TryReadData(out var data, out var sourcePath, out var error))
        {
            if (!string.IsNullOrWhiteSpace(error)) Debug.LogWarning(error, this);
            return false;
        }

        var inventory = ResolveService<GridInventoryService>();
        var survival = ResolveService<SurvivalSystem>();
        var stateMachine = ResolveService<GameStateMachine>();
        if (inventory == null || survival == null || stateMachine == null)
        {
            Debug.LogWarning("读档失败：核心服务尚未就绪。", this);
            return false;
        }

        if (!inventory.RestorePersistentSaveData(data.inventory, out error))
        {
            Debug.LogError("读档失败，背包状态未通过校验：" + error, this);
            return false;
        }

        survival.RestoreSaveData(data.survival);
        var savedPhase = (GamePhase)data.gamePhase;
        var restoredPhase = ResolvePhaseForActiveScene(savedPhase);
        stateMachine.RestoreSavedPhase(restoredPhase);
        if (restoredPhase != savedPhase)
        {
            Debug.LogWarning(
                $"存档阶段 {savedPhase} 与当前场景 {SceneManager.GetActiveScene().name} 不匹配，已按当前场景恢复为 {restoredPhase}。",
                this);
        }
        ResolveService<ShelterModuleService>()?.SetPowerState(data.shelterHasPower);
        SearchableLootRuntimeStateStore.RestoreAll(data.searchableLoot);
        ApplySearchableLootStateToScene();
        MinimapDiscoveryStateStore.RestoreAll(data.discoveredMarkerIds);
        ApplyDiscoveredMarkersToScene();
        loadCompleted = true;
        Debug.Log($"读档完成：{sourcePath}，保存时间={data.savedAtUtc}", this);
        return true;
    }

    [ContextMenu("Delete Slot 0")]
    public bool DeleteSave()
    {
        try
        {
            DeleteIfExists(SavePath);
            DeleteIfExists(SavePath + BackupExtension);
            DeleteIfExists(SavePath + TempExtension);
            loadCompleted = false;
            SearchableLootRuntimeStateStore.ClearAll();
            MinimapDiscoveryStateStore.ClearAll();
            Debug.Log("存档槽 0 已清空。", this);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError("清空存档失败：\n" + exception, this);
            return false;
        }
    }

    private bool TryReadData(out SaveGameData data, out string sourcePath, out string error)
    {
        data = null;
        sourcePath = null;
        error = null;
        var candidates = new[] { SavePath, SavePath + BackupExtension };
        for (var i = 0; i < candidates.Length; i++)
        {
            var path = candidates[i];
            if (!File.Exists(path)) continue;
            try
            {
                var candidate = JsonUtility.FromJson<SaveGameData>(File.ReadAllText(path));
                if (candidate == null || candidate.schemaVersion != SaveGameData.CurrentSchemaVersion)
                {
                    error = $"不支持的存档版本：{candidate?.schemaVersion ?? 0}。";
                    continue;
                }
                data = candidate;
                sourcePath = path;
                return true;
            }
            catch (Exception exception)
            {
                error = $"读取存档失败：{path}\n{exception.Message}";
            }
        }

        if (error == null) error = "没有找到可读取的存档。";
        return false;
    }

    private void WriteAtomically(string json)
    {
        var path = SavePath;
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("存档目录无效。");
        Directory.CreateDirectory(directory);

        var tempPath = path + TempExtension;
        var backupPath = path + BackupExtension;
        File.WriteAllText(tempPath, json);
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, backupPath, true);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private bool TryAutomaticSave(string reason)
    {
        // 已存在旧档时必须先成功读档，绝不能用尚未初始化好的空状态覆盖它。
        if (loadRoutineRunning || (HasSave && !loadCompleted)) return false;
        if (Time.realtimeSinceStartup - lastSaveRealtime < AutomaticSaveDebounceSeconds) return false;
        if (ResolveService<GridInventoryService>() == null ||
            ResolveService<SurvivalSystem>() == null ||
            ResolveService<GameStateMachine>() == null) return false;
        return Save(reason);
    }

    private static T ResolveService<T>() where T : Component
    {
        var persistent = PersistentGameServices.Instance;
        var service = persistent != null ? persistent.GetService<T>() : null;
        return service != null ? service : FindObjectOfType<T>();
    }

    private static GamePhase ResolvePhaseForActiveScene(GamePhase savedPhase)
    {
        // V1 的启动入口固定先加载 RaidScene。场景路由接入存档前，不能把 Shelter 阶段
        // 直接套在局内场景上；否则生存衰减、输入和流程 UI 会处于互相矛盾的状态。
        if (FindObjectOfType<ShelterSceneController>() != null)
            return GamePhase.Shelter;

        if (FindObjectOfType<GameBootstrap>() != null)
            return GamePhase.Raid;

        return savedPhase;
    }

    private static void ApplyDiscoveredMarkersToScene()
    {
        var markers = FindObjectsOfType<MinimapMarker>(true);
        for (var i = 0; i < markers.Length; i++)
        {
            var marker = markers[i];
            if (marker != null && MinimapDiscoveryStateStore.Contains(marker.MarkerId))
                marker.Discover();
        }
    }

    private static void ApplySearchableLootStateToScene()
    {
        var containers = FindObjectsOfType<SearchableLootContainer>(true);
        for (var i = 0; i < containers.Length; i++)
        {
            if (containers[i] != null) containers[i].RestorePersistentState();
        }
    }
}
