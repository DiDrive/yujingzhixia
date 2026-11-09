using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 正式存档槽 V1.1。负责稳定 DTO、磁盘原子替换、备份恢复、局内事务和核心服务状态装配。
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class SaveGameService : MonoBehaviour
{
    private const string SaveDirectoryName = "Saves";
    private const string SaveFileName = "slot_0.json";
    private const string BackupExtension = ".bak";
    private const string TempExtension = ".tmp";
    private const string RunCheckpointExtension = ".runbase";
    private const string RunMarkerExtension = ".run";
    private const string RunStatusActive = "active";
    private const string RunStatusCommitting = "committing";
    private const float AutomaticSaveDebounceSeconds = 2f;

    [Serializable]
    private sealed class RunTransactionMarker
    {
        public string runId;
        public string status;
        public string startedAtUtc;
    }

    private static SaveGameService instance;
    private bool loadCompleted;
    private bool loadRoutineRunning;
    private Coroutine autoSaveRoutine;
    private float lastSaveRealtime = float.NegativeInfinity;

    [SerializeField, Min(10f)] private float autoSaveIntervalSeconds = 60f;

    public static SaveGameService Instance => instance;
    public bool LoadCompleted => loadCompleted;
    public string SavePath => Path.Combine(Application.persistentDataPath, SaveDirectoryName, SaveFileName);
    public string RunCheckpointPath => SavePath + RunCheckpointExtension;
    public string RunMarkerPath => SavePath + RunMarkerExtension;
    public bool HasActiveRun => File.Exists(RunMarkerPath);
    public bool HasSave => File.Exists(SavePath) || File.Exists(SavePath + BackupExtension) ||
                           (HasActiveRun && File.Exists(RunCheckpointPath));

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
        return SaveInternal(reason, false);
    }

    private bool SaveInternal(string reason, bool allowDuringRun)
    {
        if (HasActiveRun && !allowDuringRun)
        {
            Debug.LogWarning("永久存档已暂停：当前处于未结算的局内事务中。", this);
            return false;
        }

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
        RecoverInterruptedRunIfNeeded(sourcePath);
        Debug.Log($"读档完成：{sourcePath}，保存时间={data.savedAtUtc}", this);
        return true;
    }

    /// <summary>从基地出击前固定永久进度，并创建本轮局内事务标记。</summary>
    public bool BeginRunTransaction()
    {
        if (HasSave && !loadCompleted)
        {
            Debug.LogWarning("无法开始出击：永久存档尚未完成读取。", this);
            return false;
        }

        if (HasActiveRun)
        {
            Debug.LogWarning("无法开始出击：已有未完成的局内事务。", this);
            return false;
        }

        if (!SaveInternal("before-raid-base", true)) return false;

        try
        {
            WriteTextAtomically(RunCheckpointPath, File.ReadAllText(SavePath), null);
            WriteRunMarker(new RunTransactionMarker
            {
                runId = Guid.NewGuid().ToString("N"),
                status = RunStatusActive,
                startedAtUtc = DateTime.UtcNow.ToString("O")
            });
            Debug.Log($"局内事务已开始，基地快照：{RunCheckpointPath}", this);
            return true;
        }
        catch (Exception exception)
        {
            DeleteIfExists(RunMarkerPath);
            DeleteIfExists(RunMarkerPath + TempExtension);
            DeleteIfExists(RunCheckpointPath);
            DeleteIfExists(RunCheckpointPath + TempExtension);
            Debug.LogError("创建出击前快照失败：\n" + exception, this);
            return false;
        }
    }

    /// <summary>撤离成功后把当前背包与基地数据提交为新的永久存档。</summary>
    public bool CommitRunTransaction()
    {
        if (!HasActiveRun)
            return SaveInternal("return-to-shelter", true);

        try
        {
            var marker = ReadRunMarker();
            marker.status = RunStatusCommitting;
            WriteRunMarker(marker);

            if (!SaveInternal("raid-extraction-commit", true))
            {
                marker.status = RunStatusActive;
                WriteRunMarker(marker);
                return false;
            }

            CleanupRunTransactionFiles();
            Debug.Log("局内事务提交完成，撤离物资已写入永久存档。", this);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError("提交局内事务失败：\n" + exception, this);
            return false;
        }
    }

    [ContextMenu("Delete Slot 0")]
    public bool DeleteSave()
    {
        try
        {
            DeleteIfExists(SavePath);
            DeleteIfExists(SavePath + BackupExtension);
            DeleteIfExists(SavePath + TempExtension);
            CleanupRunTransactionFiles();
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
        var marker = ReadRunMarker();
        var candidates = HasActiveRun && marker.status != RunStatusCommitting
            ? new[] { RunCheckpointPath, SavePath, SavePath + BackupExtension }
            : new[] { SavePath, SavePath + BackupExtension, RunCheckpointPath };
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
        WriteTextAtomically(SavePath, json, SavePath + BackupExtension);
    }

    private static void WriteTextAtomically(string path, string text, string backupPath)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("存档目录无效。");
        Directory.CreateDirectory(directory);

        var tempPath = path + TempExtension;
        File.WriteAllText(tempPath, text);
        if (File.Exists(path))
        {
            var keepBackup = !string.IsNullOrWhiteSpace(backupPath);
            var effectiveBackupPath = keepBackup ? backupPath : path + ".replace.bak";
            DeleteIfExists(effectiveBackupPath);
            File.Replace(tempPath, path, effectiveBackupPath, true);
            if (!keepBackup) DeleteIfExists(effectiveBackupPath);
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
        // 局内物资必须撤离成功后才能进入永久档；暂停、失焦、周期保存和强退均不得提交。
        if (HasActiveRun) return false;
        if (Time.realtimeSinceStartup - lastSaveRealtime < AutomaticSaveDebounceSeconds) return false;
        if (ResolveService<GridInventoryService>() == null ||
            ResolveService<SurvivalSystem>() == null ||
            ResolveService<GameStateMachine>() == null) return false;
        return Save(reason);
    }

    private void WriteRunMarker(RunTransactionMarker marker)
    {
        if (marker == null) throw new ArgumentNullException(nameof(marker));
        WriteTextAtomically(RunMarkerPath, JsonUtility.ToJson(marker, true), null);
    }

    private RunTransactionMarker ReadRunMarker()
    {
        if (!File.Exists(RunMarkerPath))
            return new RunTransactionMarker { status = RunStatusActive };

        try
        {
            var marker = JsonUtility.FromJson<RunTransactionMarker>(File.ReadAllText(RunMarkerPath));
            if (marker != null && !string.IsNullOrWhiteSpace(marker.status)) return marker;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("局内事务标记损坏，将按未完成出击回滚：" + exception.Message, this);
        }

        return new RunTransactionMarker { status = RunStatusActive };
    }

    private void RecoverInterruptedRunIfNeeded(string loadedSourcePath)
    {
        if (!HasActiveRun) return;

        try
        {
            if (string.Equals(loadedSourcePath, RunCheckpointPath, StringComparison.OrdinalIgnoreCase))
            {
                WriteTextAtomically(SavePath, File.ReadAllText(RunCheckpointPath), SavePath + BackupExtension);
                Debug.LogWarning("检测到未完成的局内流程，已回滚到出击前基地快照。", this);
            }
            else
            {
                Debug.Log("检测到提交阶段中断，已使用完整永久存档完成恢复。", this);
            }

            CleanupRunTransactionFiles();
        }
        catch (Exception exception)
        {
            Debug.LogError("清理中断的局内事务失败：\n" + exception, this);
        }
    }

    private void CleanupRunTransactionFiles()
    {
        DeleteIfExists(RunMarkerPath);
        DeleteIfExists(RunMarkerPath + TempExtension);
        DeleteIfExists(RunCheckpointPath);
        DeleteIfExists(RunCheckpointPath + TempExtension);
    }

    private static T ResolveService<T>() where T : Component
    {
        var persistent = PersistentGameServices.Instance;
        var service = persistent != null ? persistent.GetService<T>() : null;
        return service != null ? service : FindObjectOfType<T>();
    }

    private static GamePhase ResolvePhaseForActiveScene(GamePhase savedPhase)
    {
        // 业务入口默认加载基地；直接打开任意业务场景调试时，仍以实际场景约束流程状态。
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
