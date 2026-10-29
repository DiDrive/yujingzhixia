using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 将 Resources/Characters/food 下的物品资源同步到背包编辑器列表与 JSON 定义表。
/// 该工具只补齐缺失条目，不覆盖策划已经手工调整的物品参数。
/// </summary>
public static class SyncFoodInventoryItems
{
    private const string FoodFolder = "Assets/Resources/Characters/food";
    private const string ItemDefinitionsJsonPath = "Assets/GameRes/Config/item_definitions.json";

    [System.Serializable]
    private sealed class ItemDefinitionJsonFile
    {
        public List<GridItemDefinition> items = new();
    }

    [MenuItem("Tools/Inventory/Sync Food Items From Characters Food")]
    public static void Sync()
    {
        var service = Object.FindObjectOfType<GridInventoryService>(true);
        var ui = GridInventoryEditorLayoutBuilder.FindMainInventoryUi();

        if (service == null)
        {
            Debug.LogError("SyncFoodInventoryItems: GridInventoryService not found in the current scene.");
            return;
        }

        if (ui == null)
        {
            Debug.LogError("SyncFoodInventoryItems: GridInventoryUnityUI not found in the current scene.");
            return;
        }

        var foodAssets = AssetDatabase.FindAssets("t:Texture2D", new[] { FoodFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => string.Equals(Path.GetExtension(path), ".png", System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileNameWithoutExtension(path))
            .Select(path => new FoodItemAsset(path, ToItemId(Path.GetFileNameWithoutExtension(path))))
            .Where(asset => !string.IsNullOrWhiteSpace(asset.ItemId))
            .ToList();

        foreach (var asset in foodAssets)
        {
            EnsureSpriteImport(asset.Path);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        MergeItemDefinitionsJson(foodAssets);
        WriteItemDefinitions(service, foodAssets);
        WriteItemVisuals(ui, foodAssets);
        BuildSpawnSelector(ui);
        GridInventoryEditorLayoutBuilder.SyncContainerViews(service);

        EditorUtility.SetDirty(service);
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(service.gameObject.scene);
        EditorSceneManager.SaveScene(service.gameObject.scene);

        Debug.Log($"SyncFoodInventoryItems: synced {foodAssets.Count} food items from {FoodFolder} and rebuilt the editor-visible spawn dropdown.");
    }

    /// <summary>
    /// 合并食物资源到物品定义表，并为新增条目写入尺寸、品质和图标路径等默认值。
    /// </summary>
    private static void MergeItemDefinitionsJson(IReadOnlyList<FoodItemAsset> foodAssets)
    {
        ItemDefinitionJsonFile table = null;
        if (File.Exists(ItemDefinitionsJsonPath))
        {
            table = JsonUtility.FromJson<ItemDefinitionJsonFile>(File.ReadAllText(ItemDefinitionsJsonPath));
        }
        table ??= new ItemDefinitionJsonFile();
        table.items ??= new List<GridItemDefinition>();

        var indexById = new Dictionary<string, int>();
        for (var i = 0; i < table.items.Count; i++)
        {
            var id = table.items[i].itemId;
            if (!string.IsNullOrWhiteSpace(id) && !indexById.ContainsKey(id)) indexById.Add(id, i);
        }

        // 使用字典查重，保留 JSON 中已有条目的人工配置，仅创建尚不存在的资源。
        foreach (var asset in foodAssets)
        {
            var resourcePath = asset.Path
                .Replace("Assets/Resources/", string.Empty)
                .Replace(Path.GetExtension(asset.Path), string.Empty)
                .Replace('\\', '/');

            if (indexById.TryGetValue(asset.ItemId, out var existingIndex))
            {
                var existing = table.items[existingIndex];
                if (string.IsNullOrWhiteSpace(existing.displayName)) existing.displayName = asset.ItemId;
                if (string.IsNullOrWhiteSpace(existing.iconResourcePath)) existing.iconResourcePath = resourcePath;
                table.items[existingIndex] = existing;
                continue;
            }

            indexById.Add(asset.ItemId, table.items.Count);
            table.items.Add(new GridItemDefinition
            {
                itemId = asset.ItemId,
                displayName = asset.ItemId,
                iconResourcePath = resourcePath,
                width = 1,
                height = 1,
                canRotate = false,
                maxStack = 10,
                itemTags = new List<string> { "loot" },
                weight = 0.1f,
                quality = GridItemQuality.Common
            });
        }

        File.WriteAllText(ItemDefinitionsJsonPath, JsonUtility.ToJson(table, true));
        AssetDatabase.ImportAsset(ItemDefinitionsJsonPath, ImportAssetOptions.ForceUpdate);
    }

    [MenuItem("Tools/Inventory/Build Side Container Views")]
    public static void BuildSideContainerViewsMenu()
    {
        var service = Object.FindObjectOfType<GridInventoryService>(true);

        if (service == null)
        {
            Debug.LogError("SyncFoodInventoryItems: GridInventoryService not found in the current scene.");
            return;
        }

        GridInventoryEditorLayoutBuilder.SyncContainerViews(service);
    }

    public static void SyncContainerViews(GridInventoryService service)
    {
        GridInventoryEditorLayoutBuilder.SyncContainerViews(service);
    }

    private static void EnsureSpriteImport(string assetPath)
    {
        if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer) return;
        var changed = false;

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }

        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            changed = true;
        }

        if (!importer.alphaIsTransparency)
        {
            importer.alphaIsTransparency = true;
            changed = true;
        }

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            changed = true;
        }

        if (changed) importer.SaveAndReimport();
    }

    private static void WriteItemDefinitions(GridInventoryService service, IReadOnlyList<FoodItemAsset> foodAssets)
    {
        var serializedObject = new SerializedObject(service);
        var definitions = serializedObject.FindProperty("itemDefinitions");
        definitions.ClearArray();

        AddDefinition(definitions, "food_canned", 1, 1, false, 3, 0.5f, "consumable");
        AddDefinition(definitions, "water_bottle", 1, 2, true, 2, 0.8f, "consumable");
        AddDefinition(definitions, "bandage", 1, 1, false, 5, 0.2f, "medical");
        AddDefinition(definitions, "cloth_raw", 2, 1, true, 4, 0.4f, "material");

        var existing = new HashSet<string> { "food_canned", "water_bottle", "bandage", "cloth_raw" };
        foreach (var asset in foodAssets)
        {
            if (!existing.Add(asset.ItemId)) continue;
            AddDefinition(definitions, asset.ItemId, 1, 1, false, 10, 0.1f, "food");
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void AddDefinition(SerializedProperty definitions, string itemId, int width, int height, bool canRotate, int maxStack, float weight, string tag)
    {
        definitions.InsertArrayElementAtIndex(definitions.arraySize);
        var definition = definitions.GetArrayElementAtIndex(definitions.arraySize - 1);
        definition.FindPropertyRelative("itemId").stringValue = itemId;
        definition.FindPropertyRelative("width").intValue = width;
        definition.FindPropertyRelative("height").intValue = height;
        definition.FindPropertyRelative("canRotate").boolValue = canRotate;
        definition.FindPropertyRelative("maxStack").intValue = maxStack;
        definition.FindPropertyRelative("weight").floatValue = weight;

        var tags = definition.FindPropertyRelative("itemTags");
        tags.ClearArray();
        tags.InsertArrayElementAtIndex(0);
        tags.GetArrayElementAtIndex(0).stringValue = tag;
    }

    private static void WriteItemVisuals(GridInventoryUnityUI ui, IReadOnlyList<FoodItemAsset> foodAssets)
    {
        var serializedObject = new SerializedObject(ui);
        var spawnItemIds = serializedObject.FindProperty("spawnItemIds");
        var visuals = serializedObject.FindProperty("itemVisuals");
        var currentSpawnIds = ReadStringArray(spawnItemIds);
        var visualIndexByItemId = ReadVisualIndexByItemId(visuals);

        foreach (var asset in foodAssets)
        {
            if (currentSpawnIds.Add(asset.ItemId))
            {
                spawnItemIds.InsertArrayElementAtIndex(spawnItemIds.arraySize);
                spawnItemIds.GetArrayElementAtIndex(spawnItemIds.arraySize - 1).stringValue = asset.ItemId;
            }

            if (!visualIndexByItemId.TryGetValue(asset.ItemId, out var visualIndex))
            {
                visuals.InsertArrayElementAtIndex(visuals.arraySize);
                visualIndex = visuals.arraySize - 1;
                visualIndexByItemId[asset.ItemId] = visualIndex;
            }

            var visual = visuals.GetArrayElementAtIndex(visualIndex);
            visual.FindPropertyRelative("itemId").stringValue = asset.ItemId;
            visual.FindPropertyRelative("icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(asset.Path);
            visual.FindPropertyRelative("tint").colorValue = Color.white;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void BuildSpawnSelector(GridInventoryUnityUI ui)
    {
        var serializedObject = new SerializedObject(ui);
        serializedObject.FindProperty("showSpawnTools").boolValue = true;
        serializedObject.FindProperty("allowSpawnFromEmptyCells").boolValue = true;
        var spawnRoot = serializedObject.FindProperty("spawnRootOverride").objectReferenceValue as RectTransform;
        var spawnItemIds = ReadStringArray(serializedObject.FindProperty("spawnItemIds")).ToList();
        serializedObject.ApplyModifiedProperties();

        if (spawnRoot == null)
        {
            spawnRoot = FindDeepChild(ui.transform, "SpawnRoot") as RectTransform;
            if (spawnRoot == null) spawnRoot = FindDeepChild(ui.transform, "SpawnToolbar") as RectTransform;
        }

        if (spawnRoot == null)
        {
            Debug.LogWarning("SyncFoodInventoryItems: SpawnRoot not found, so the editor-visible spawn dropdown was not rebuilt.");
            return;
        }

        spawnRoot.gameObject.SetActive(true);

        var dropdown = spawnRoot.GetComponentInChildren<Dropdown>(true);
        if (dropdown == null)
        {
            dropdown = CreateDropdown(spawnRoot, spawnItemIds);
        }
        else
        {
            EnsureDropdownShape(dropdown, spawnItemIds);
        }

        var dragEntryTransform = FindDirectChild(spawnRoot, "SpawnSelectedItem");
        var dragEntry = dragEntryTransform != null ? dragEntryTransform.gameObject : CreateSpawnDragEntry(spawnRoot, spawnItemIds.Count > 0 ? spawnItemIds[0] : string.Empty);
        var handler = dragEntry.GetComponent<GridInventoryUnityUISpawnItem>();
        if (handler == null) handler = dragEntry.AddComponent<GridInventoryUnityUISpawnItem>();
        handler.Bind(ui, spawnItemIds.Count > 0 ? spawnItemIds[0] : string.Empty);
        var selector = AddAndBindSpawnSelector(dropdown.gameObject, ui, handler, spawnItemIds);
        ApplySpawnDragEntryVisual(ui, dragEntry, handler, spawnItemIds.Count > 0 ? spawnItemIds[0] : string.Empty);

        dropdown.onValueChanged.RemoveAllListeners();
        dropdown.onValueChanged.AddListener(index =>
        {
            if (index < 0 || index >= spawnItemIds.Count) return;
            ApplySpawnDragEntryVisual(ui, dragEntry, handler, spawnItemIds[index]);
        });
        AutoAlignSpawnControls(spawnRoot, dropdown.transform as RectTransform, dragEntry.transform as RectTransform);

        EditorUtility.SetDirty(spawnRoot);
        EditorUtility.SetDirty(dropdown);
        EditorUtility.SetDirty(dragEntry);
        if (selector != null) EditorUtility.SetDirty(selector);
    }

    private static Component AddAndBindSpawnSelector(GameObject dropdownObject, GridInventoryUnityUI ui, GridInventoryUnityUISpawnItem handler, IReadOnlyList<string> spawnItemIds)
    {
        var selectorType = System.Type.GetType("GridInventoryUnityUISpawnSelector, Assembly-CSharp");
        if (selectorType == null)
        {
            Debug.LogWarning("SyncFoodInventoryItems: GridInventoryUnityUISpawnSelector type was not available yet. Re-run the sync after Unity finishes compiling.");
            return null;
        }

        var selector = dropdownObject.GetComponent(selectorType);
        if (selector == null) selector = dropdownObject.AddComponent(selectorType);
        selectorType.GetMethod("Bind")?.Invoke(selector, new object[] { ui, handler, spawnItemIds });
        return selector;
    }

    private static Dropdown CreateDropdown(RectTransform parent, IReadOnlyList<string> itemIds)
    {
        var rect = CreateRect("SpawnItemDropdown", parent, new Vector2(420f, 32f), new Vector2(0f, -1f));
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.14f, 0.14f, 0.14f, 0.98f);
        var dropdown = rect.gameObject.AddComponent<Dropdown>();
        dropdown.targetGraphic = image;
        dropdown.captionText = CreateText("Label", rect, itemIds.Count > 0 ? itemIds[0] : string.Empty, 15, TextAnchor.MiddleLeft);
        dropdown.captionText.rectTransform.offsetMin = new Vector2(10f, 0f);
        dropdown.captionText.rectTransform.offsetMax = new Vector2(-28f, 0f);
        var arrow = CreateText("Arrow", rect, "v", 18, TextAnchor.MiddleCenter);
        arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
        arrow.rectTransform.anchorMax = new Vector2(1f, 1f);
        arrow.rectTransform.sizeDelta = new Vector2(26f, 0f);
        arrow.rectTransform.anchoredPosition = new Vector2(-13f, 0f);

        var template = CreateDropdownTemplate(rect);
        dropdown.template = template;
        dropdown.itemText = template.GetComponentInChildren<Toggle>(true).GetComponentInChildren<Text>(true);
        dropdown.ClearOptions();
        dropdown.AddOptions(itemIds.ToList());
        dropdown.value = 0;
        dropdown.RefreshShownValue();
        return dropdown;
    }

    private static void EnsureDropdownShape(Dropdown dropdown, IReadOnlyList<string> itemIds)
    {
        if (dropdown.targetGraphic == null)
        {
            var image = dropdown.GetComponent<Image>();
            if (image == null) image = dropdown.gameObject.AddComponent<Image>();
            image.color = new Color(0.14f, 0.14f, 0.14f, 0.98f);
            dropdown.targetGraphic = image;
        }

        var rect = dropdown.transform as RectTransform;
        if (dropdown.captionText == null && rect != null)
        {
            dropdown.captionText = CreateText("Label", rect, itemIds.Count > 0 ? itemIds[0] : string.Empty, 15, TextAnchor.MiddleLeft);
            dropdown.captionText.rectTransform.offsetMin = new Vector2(10f, 0f);
            dropdown.captionText.rectTransform.offsetMax = new Vector2(-28f, 0f);
        }

        if (dropdown.template == null && rect != null) dropdown.template = CreateDropdownTemplate(rect);
        if (dropdown.itemText == null && dropdown.template != null)
        {
            var toggle = dropdown.template.GetComponentInChildren<Toggle>(true);
            if (toggle != null) dropdown.itemText = toggle.GetComponentInChildren<Text>(true);
        }

        dropdown.ClearOptions();
        dropdown.AddOptions(itemIds.ToList());
        dropdown.value = 0;
        dropdown.RefreshShownValue();
    }

    private static RectTransform CreateDropdownTemplate(RectTransform dropdownRect)
    {
        var template = CreateRect("Template", dropdownRect, new Vector2(420f, 240f), new Vector2(0f, -34f));
        template.anchorMin = new Vector2(0f, 0f);
        template.anchorMax = new Vector2(1f, 0f);
        template.pivot = new Vector2(0f, 1f);
        var templateImage = template.gameObject.AddComponent<Image>();
        templateImage.color = new Color(0.08f, 0.08f, 0.08f, 0.98f);
        var scrollRect = template.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;

        var viewport = CreateStretchRect("Viewport", template);
        var mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        viewport.gameObject.AddComponent<Image>().color = Color.white;

        var content = CreateStretchRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.sizeDelta = new Vector2(0f, 32f);
        scrollRect.viewport = viewport;
        scrollRect.content = content;

        var item = CreateRect("Item", content, new Vector2(420f, 30f), Vector2.zero);
        item.anchorMin = new Vector2(0f, 1f);
        item.anchorMax = new Vector2(1f, 1f);
        item.pivot = new Vector2(0f, 1f);
        var itemBackground = item.gameObject.AddComponent<Image>();
        itemBackground.color = new Color(0.18f, 0.18f, 0.18f, 0.95f);
        var itemToggle = item.gameObject.AddComponent<Toggle>();
        itemToggle.targetGraphic = itemBackground;
        var itemLabel = CreateText("Item Label", item, "Option", 15, TextAnchor.MiddleLeft);
        itemLabel.rectTransform.offsetMin = new Vector2(10f, 0f);
        itemLabel.rectTransform.offsetMax = new Vector2(-10f, 0f);

        template.gameObject.SetActive(false);
        return template;
    }

    private static GameObject CreateSpawnDragEntry(RectTransform parent, string itemId)
    {
        var rect = CreateRect("SpawnSelectedItem", parent, new Vector2(260f, 32f), new Vector2(432f, -1f));
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.16f, 0.16f, 0.16f, 0.95f);
        image.type = Image.Type.Simple;
        var label = CreateText("Text", rect, itemId, 15, TextAnchor.MiddleCenter);
        label.color = Color.white;
        rect.gameObject.AddComponent<Button>();
        return rect.gameObject;
    }

    private static void ApplySpawnDragEntryVisual(GridInventoryUnityUI ui, GameObject dragEntry, GridInventoryUnityUISpawnItem handler, string itemId)
    {
        handler.Bind(ui, itemId);
        var label = dragEntry.GetComponentInChildren<Text>(true);
        if (label != null) label.text = itemId;

        var image = dragEntry.GetComponent<Image>();
        if (image == null) return;

        var visualSprite = FindSpriteForItemId(ui, itemId);
        image.sprite = visualSprite;
        image.type = Image.Type.Simple;
        image.color = visualSprite != null ? Color.white : new Color(0.16f, 0.16f, 0.16f, 0.95f);
    }

    private static void AutoAlignSpawnControls(RectTransform spawnRoot, RectTransform dropdownRect, RectTransform dragRect)
    {
        if (spawnRoot == null || dropdownRect == null || dragRect == null) return;
        var parentWidth = Mathf.Max(spawnRoot.rect.width, spawnRoot.sizeDelta.x, 1f);
        var dropdownBounds = GetTopLeftRect(dropdownRect);
        var dragBounds = GetTopLeftRect(dragRect);
        var overlaps = dropdownBounds.Overlaps(dragBounds);
        var dragOutRight = dragBounds.xMax > parentWidth;
        var dropdownOutRight = dropdownBounds.xMax > parentWidth;
        if (!overlaps && !dragOutRight && !dropdownOutRight) return;

        dragRect.anchorMin = new Vector2(0f, 1f);
        dragRect.anchorMax = new Vector2(0f, 1f);
        dragRect.pivot = new Vector2(0f, 1f);
        const float spacing = 8f;
        var rightSideX = dropdownRect.anchoredPosition.x + dropdownRect.sizeDelta.x + spacing;
        if (!dropdownOutRight && rightSideX + dragRect.sizeDelta.x <= parentWidth)
        {
            dragRect.anchoredPosition = new Vector2(rightSideX, dropdownRect.anchoredPosition.y);
        }
        else
        {
            dragRect.anchoredPosition = new Vector2(dropdownRect.anchoredPosition.x, dropdownRect.anchoredPosition.y - dropdownRect.sizeDelta.y - spacing);
        }

        EditorUtility.SetDirty(dragRect);
    }

    private static Rect GetTopLeftRect(RectTransform rect)
    {
        var size = rect.sizeDelta;
        var x = rect.anchoredPosition.x - size.x * rect.pivot.x;
        var yTop = rect.anchoredPosition.y + size.y * (1f - rect.pivot.y);
        return new Rect(x, yTop - size.y, size.x, size.y);
    }

    private static Sprite FindSpriteForItemId(GridInventoryUnityUI ui, string itemId)
    {
        var serializedObject = new SerializedObject(ui);
        var visuals = serializedObject.FindProperty("itemVisuals");
        for (var i = 0; i < visuals.arraySize; i++)
        {
            var visual = visuals.GetArrayElementAtIndex(i);
            if (visual.FindPropertyRelative("itemId").stringValue != itemId) continue;
            return visual.FindPropertyRelative("icon").objectReferenceValue as Sprite;
        }

        return null;
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 size, Vector2 anchoredPosition)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.layer = parent.gameObject.layer;
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        return rect;
    }

    private static RectTransform CreateStretchRect(string name, Transform parent)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.layer = parent.gameObject.layer;
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static Text CreateText(string name, Transform parent, string value, int fontSize, TextAnchor alignment)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.layer = parent.gameObject.layer;
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var text = obj.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;
        if (parent.name == name) return parent;
        for (var i = 0; i < parent.childCount; i++)
        {
            var result = FindDeepChild(parent.GetChild(i), name);
            if (result != null) return result;
        }

        return null;
    }

    private static Transform FindDirectChild(Transform parent, string name)
    {
        if (parent == null) return null;
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == name) return child;
        }

        return null;
    }

    private static void ClearChildren(Transform root)
    {
        for (var i = root.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(root.GetChild(i).gameObject);
        }
    }

    private static HashSet<string> ReadStringArray(SerializedProperty property)
    {
        var values = new HashSet<string>();
        for (var i = 0; i < property.arraySize; i++)
        {
            var value = property.GetArrayElementAtIndex(i).stringValue;
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
        }

        return values;
    }

    private static HashSet<string> ReadVisualItemIds(SerializedProperty property)
    {
        var values = new HashSet<string>();
        for (var i = 0; i < property.arraySize; i++)
        {
            var value = property.GetArrayElementAtIndex(i).FindPropertyRelative("itemId").stringValue;
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
        }

        return values;
    }

    private static Dictionary<string, int> ReadVisualIndexByItemId(SerializedProperty property)
    {
        var values = new Dictionary<string, int>();
        for (var i = 0; i < property.arraySize; i++)
        {
            var value = property.GetArrayElementAtIndex(i).FindPropertyRelative("itemId").stringValue;
            if (!string.IsNullOrWhiteSpace(value) && !values.ContainsKey(value)) values.Add(value, i);
        }

        return values;
    }

    private static string ToItemId(string name)
    {
        return name.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
    }

    private readonly struct FoodItemAsset
    {
        public FoodItemAsset(string path, string itemId)
        {
            Path = path;
            ItemId = itemId;
        }

        public string Path { get; }
        public string ItemId { get; }
    }
}
