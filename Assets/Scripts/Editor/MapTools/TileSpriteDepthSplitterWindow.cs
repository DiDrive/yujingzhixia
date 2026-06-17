using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class TileSpriteDepthSplitterWindow : EditorWindow
{
    private const string DefaultOutputFolder = "Assets/Tiles/SplitDepth";
    private const string BackSuffix = "_Back";
    private const string FrontSuffix = "_Front";

    private enum SplitMode
    {
        Horizontal,
        DiagonalLeftToRight
    }

    [SerializeField] private Object sourceObject;
    [SerializeField] private SplitMode splitMode = SplitMode.Horizontal;
    [SerializeField] private int splitYFromBottom = 16;
    [SerializeField] private int splitYLeftFromBottom = 16;
    [SerializeField] private int splitYRightFromBottom = 16;
    [SerializeField] private string outputFolder = DefaultOutputFolder;
    [SerializeField] private bool createTileAssets = true;
    [SerializeField] private bool overwriteExisting = false;
    private int draggingLineEndpoint = -1;

    [MenuItem("Tools/Map/Split Selected Sprite For Depth")]
    public static void SplitSelectedSpriteForDepth()
    {
        var window = GetWindow<TileSpriteDepthSplitterWindow>("Split Depth Sprite");
        window.sourceObject = Selection.activeObject;
        window.Show();
    }

    [MenuItem("Tools/Map/Split Selected Sprite For Depth", true)]
    public static bool ValidateSplitSelectedSpriteForDepth()
    {
        return CanSplit(Selection.activeObject);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Wall / Corner Depth Split", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Creates two aligned transparent sprites from one wall/corner sprite. Use Back on WallBack and Front on WallFront.",
            MessageType.Info);

        sourceObject = EditorGUILayout.ObjectField("Source Sprite/Texture", sourceObject, typeof(Object), false);
        splitMode = (SplitMode)EditorGUILayout.EnumPopup("Split Mode", splitMode);
        if (splitMode == SplitMode.Horizontal)
        {
            splitYFromBottom = EditorGUILayout.IntField("Split Y From Bottom", Mathf.Max(1, splitYFromBottom));
            splitYLeftFromBottom = splitYFromBottom;
            splitYRightFromBottom = splitYFromBottom;
        }
        else
        {
            splitYLeftFromBottom = EditorGUILayout.IntField("Left Y From Bottom", Mathf.Max(1, splitYLeftFromBottom));
            splitYRightFromBottom = EditorGUILayout.IntField("Right Y From Bottom", Mathf.Max(1, splitYRightFromBottom));
        }

        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        createTileAssets = EditorGUILayout.Toggle("Create Tile Assets", createTileAssets);
        overwriteExisting = EditorGUILayout.Toggle("Overwrite Existing", overwriteExisting);

        DrawManualSplitPreview();

        using (new EditorGUI.DisabledScope(!CanSplit(sourceObject)))
        {
            if (GUILayout.Button("Auto Guess From Alpha Bounds"))
            {
                GuessSplitFromAlphaBounds(sourceObject);
            }
        }

        using (new EditorGUI.DisabledScope(!CanSplit(sourceObject)))
        {
            if (GUILayout.Button("Split Source"))
            {
                SplitObject(sourceObject);
            }
        }

        using (new EditorGUI.DisabledScope(Selection.objects.Length == 0))
        {
            if (GUILayout.Button("Split All Selected"))
            {
                SplitSelection();
            }
        }
    }

    private void SplitSelection()
    {
        var count = 0;
        foreach (var selected in Selection.objects)
        {
            if (!CanSplit(selected)) continue;
            SplitObject(selected);
            count++;
        }

        Debug.Log($"Depth split finished. Processed {count} selected sprite/texture asset(s).");
    }

    private void SplitObject(Object target)
    {
        if (!TryResolveSource(target, out var texture, out var rect, out var sourceAssetPath, out var sourceName, out var pixelsPerUnit, out var pivot))
        {
            Debug.LogWarning($"Cannot split {target}. Select a Texture2D or Sprite asset.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(outputFolder))
        {
            EnsureFolder(outputFolder);
        }

        var sourceImporter = AssetImporter.GetAtPath(sourceAssetPath) as TextureImporter;
        var restoreReadable = sourceImporter != null && !sourceImporter.isReadable;
        if (restoreReadable)
        {
            sourceImporter.isReadable = true;
            sourceImporter.SaveAndReimport();
        }

        try
        {
            var width = Mathf.RoundToInt(rect.width);
            var height = Mathf.RoundToInt(rect.height);
            if (width <= 0 || height <= 0)
            {
                Debug.LogWarning($"Cannot split {sourceName}: invalid sprite size {width}x{height}.");
                return;
            }

            var splitYLeft = Mathf.Clamp(splitMode == SplitMode.Horizontal ? splitYFromBottom : splitYLeftFromBottom, 1, height - 1);
            var splitYRight = Mathf.Clamp(splitMode == SplitMode.Horizontal ? splitYFromBottom : splitYRightFromBottom, 1, height - 1);
            var pixels = texture.GetPixels(
                Mathf.RoundToInt(rect.x),
                Mathf.RoundToInt(rect.y),
                width,
                height);

            var backPixels = new Color[pixels.Length];
            var frontPixels = new Color[pixels.Length];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (IsFrontPixel(x, y, width, splitYLeft, splitYRight))
                    {
                        backPixels[index] = Color.clear;
                        frontPixels[index] = pixels[index];
                    }
                    else
                    {
                        backPixels[index] = pixels[index];
                        frontPixels[index] = Color.clear;
                    }
                }
            }

            var backPath = GetUniqueOrOverwritePath(Path.Combine(outputFolder, $"{sourceName}{BackSuffix}.png"));
            var frontPath = GetUniqueOrOverwritePath(Path.Combine(outputFolder, $"{sourceName}{FrontSuffix}.png"));
            WritePng(backPath, width, height, backPixels);
            WritePng(frontPath, width, height, frontPixels);

            AssetDatabase.ImportAsset(backPath);
            AssetDatabase.ImportAsset(frontPath);
            ConfigureOutputTexture(backPath, pixelsPerUnit, pivot);
            ConfigureOutputTexture(frontPath, pixelsPerUnit, pivot);

            if (createTileAssets)
            {
                CreateTileAsset(backPath, $"{sourceName}{BackSuffix}");
                CreateTileAsset(frontPath, $"{sourceName}{FrontSuffix}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Split {sourceName}. LeftY={splitYLeft}, RightY={splitYRight}. Back: {backPath}, Front: {frontPath}");
        }
        finally
        {
            if (restoreReadable && sourceImporter != null)
            {
                sourceImporter.isReadable = false;
                sourceImporter.SaveAndReimport();
            }
        }
    }

    private void GuessSplitFromAlphaBounds(Object target)
    {
        if (!TryResolveSource(target, out var texture, out var rect, out var sourceAssetPath, out var sourceName, out _, out _))
        {
            Debug.LogWarning($"Cannot inspect {target}. Select a Texture2D or Sprite asset.");
            return;
        }

        var sourceImporter = AssetImporter.GetAtPath(sourceAssetPath) as TextureImporter;
        var restoreReadable = sourceImporter != null && !sourceImporter.isReadable;
        if (restoreReadable)
        {
            sourceImporter.isReadable = true;
            sourceImporter.SaveAndReimport();
        }

        try
        {
            var width = Mathf.RoundToInt(rect.width);
            var height = Mathf.RoundToInt(rect.height);
            var pixels = texture.GetPixels(Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y), width, height);
            if (!TryGetAlphaBounds(pixels, width, height, out var minY, out var maxY))
            {
                splitYFromBottom = Mathf.Max(1, height / 2);
                splitYLeftFromBottom = splitYFromBottom;
                splitYRightFromBottom = splitYFromBottom;
                Debug.LogWarning($"No visible pixels found in {sourceName}. Using middle split.");
                return;
            }

            var visibleHeight = maxY - minY + 1;
            splitYFromBottom = Mathf.Clamp(minY + Mathf.RoundToInt(visibleHeight * 0.5f), 1, height - 1);
            splitYLeftFromBottom = splitYFromBottom;
            splitYRightFromBottom = splitYFromBottom;
            Repaint();
            Debug.Log($"Suggested split for {sourceName}: Y={splitYFromBottom}. Adjust to a diagonal for corner pieces.");
        }
        finally
        {
            if (restoreReadable && sourceImporter != null)
            {
                sourceImporter.isReadable = false;
                sourceImporter.SaveAndReimport();
            }
        }
    }

    private static bool IsFrontPixel(int x, int y, int width, int splitYLeft, int splitYRight)
    {
        var t = width <= 1 ? 0f : x / (float)(width - 1);
        var splitY = Mathf.Lerp(splitYLeft, splitYRight, t);
        return y >= splitY;
    }

    private static bool TryGetAlphaBounds(IReadOnlyList<Color> pixels, int width, int height, out int minY, out int maxY)
    {
        minY = height;
        maxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a <= 0.01f) continue;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        return maxY >= minY;
    }

    private static bool CanSplit(Object target)
    {
        if (target is Texture2D || target is Sprite) return true;
        return target is Tile tile && tile.sprite != null;
    }

    private static bool TryResolveSource(
        Object target,
        out Texture2D texture,
        out Rect rect,
        out string assetPath,
        out string sourceName,
        out float pixelsPerUnit,
        out Vector2 pivot)
    {
        texture = null;
        rect = default;
        assetPath = null;
        sourceName = null;
        pixelsPerUnit = 100f;
        pivot = new Vector2(0.5f, 0.5f);

        if (target is Sprite sprite)
        {
            return TryResolveSprite(sprite, out texture, out rect, out assetPath, out sourceName, out pixelsPerUnit, out pivot);
        }

        if (target is Tile tile && tile.sprite != null)
        {
            return TryResolveSprite(tile.sprite, out texture, out rect, out assetPath, out sourceName, out pixelsPerUnit, out pivot);
        }

        if (target is Texture2D texture2D)
        {
            texture = texture2D;
            rect = new Rect(0, 0, texture.width, texture.height);
            assetPath = AssetDatabase.GetAssetPath(texture);
            sourceName = SanitizeFileName(texture.name);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                pixelsPerUnit = importer.spritePixelsPerUnit;
                pivot = importer.spritePivot;
            }

            return texture != null && !string.IsNullOrEmpty(assetPath);
        }

        return false;
    }

    private static bool TryResolveSprite(
        Sprite sprite,
        out Texture2D texture,
        out Rect rect,
        out string assetPath,
        out string sourceName,
        out float pixelsPerUnit,
        out Vector2 pivot)
    {
        texture = sprite.texture;
        rect = sprite.rect;
        assetPath = AssetDatabase.GetAssetPath(texture);
        sourceName = SanitizeFileName(sprite.name);
        pixelsPerUnit = sprite.pixelsPerUnit;
        pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
        return texture != null && !string.IsNullOrEmpty(assetPath);
    }

    private static void WritePng(string assetPath, int width, int height, IReadOnlyList<Color> pixels)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels as Color[] ?? new List<Color>(pixels).ToArray());
        texture.Apply();

        var absolutePath = ToAbsolutePath(assetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
        File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
        DestroyImmediate(texture);
    }

    private static void ConfigureOutputTexture(string assetPath, float pixelsPerUnit, Vector2 pivot)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.spritePivot = pivot;
        var textureSettings = new TextureImporterSettings();
        importer.ReadTextureSettings(textureSettings);
        textureSettings.spriteAlignment = (int)SpriteAlignment.Custom;
        textureSettings.spritePivot = pivot;
        importer.SetTextureSettings(textureSettings);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Point;
        importer.SaveAndReimport();
    }

    private static void CreateTileAsset(string spritePath, string tileName)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (sprite == null) return;

        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = sprite;
        tile.name = tileName;

        var tilePath = Path.ChangeExtension(spritePath, ".asset");
        var existingTile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
        if (existingTile == null)
        {
            AssetDatabase.CreateAsset(tile, tilePath);
            return;
        }

        EditorUtility.CopySerialized(tile, existingTile);
        DestroyImmediate(tile);
    }

    private static void EnsureFolder(string assetFolder)
    {
        var normalized = assetFolder.Replace("\\", "/").Trim('/');
        if (!normalized.StartsWith("Assets"))
        {
            normalized = DefaultOutputFolder;
        }

        var parts = normalized.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private string GetUniqueOrOverwritePath(string assetPath)
    {
        var normalized = assetPath.Replace("\\", "/");
        if (overwriteExisting) return normalized;
        return AssetDatabase.GenerateUniqueAssetPath(normalized);
    }

    private static string ToAbsolutePath(string assetPath)
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        return Path.Combine(projectRoot ?? string.Empty, assetPath);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidChar, '_');
        }

        return value;
    }

    private void DrawManualSplitPreview()
    {
        if (!TryResolveSource(sourceObject, out var texture, out var rect, out _, out _, out _, out var pivot))
        {
            EditorGUILayout.HelpBox("Select a Sprite, Texture2D, or Tile asset to draw the split line.", MessageType.None);
            return;
        }

        var width = Mathf.RoundToInt(rect.width);
        var height = Mathf.RoundToInt(rect.height);
        if (width <= 0 || height <= 0) return;

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Manual Split Preview", EditorStyles.boldLabel);

        var maxPreviewWidth = Mathf.Min(position.width - 24f, 420f);
        var previewWidth = Mathf.Max(96f, maxPreviewWidth);
        var previewHeight = Mathf.Clamp(previewWidth * height / width, 96f, 360f);
        var previewRect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.ExpandWidth(false));
        previewRect.x = Mathf.Max(previewRect.x, (position.width - previewRect.width) * 0.5f);

        var texCoords = new Rect(
            rect.x / texture.width,
            rect.y / texture.height,
            rect.width / texture.width,
            rect.height / texture.height);

        EditorGUI.DrawRect(previewRect, new Color(0.09f, 0.09f, 0.09f, 1f));
        GUI.DrawTextureWithTexCoords(previewRect, texture, texCoords, true);

        var splitYLeft = Mathf.Clamp(splitMode == SplitMode.Horizontal ? splitYFromBottom : splitYLeftFromBottom, 1, height - 1);
        var splitYRight = Mathf.Clamp(splitMode == SplitMode.Horizontal ? splitYFromBottom : splitYRightFromBottom, 1, height - 1);
        var leftPoint = PixelToPreviewPoint(previewRect, 0f, splitYLeft, width, height);
        var rightPoint = PixelToPreviewPoint(previewRect, width - 1f, splitYRight, width, height);
        var pivotPoint = PixelToPreviewPoint(previewRect, pivot.x * width, pivot.y * height, width, height);

        Handles.BeginGUI();
        Handles.color = new Color(1f, 0.45f, 0f, 1f);
        Handles.DrawAAPolyLine(4f, leftPoint, rightPoint);
        Handles.DrawSolidDisc(leftPoint, Vector3.forward, 5f);
        Handles.DrawSolidDisc(rightPoint, Vector3.forward, 5f);
        Handles.color = new Color(0.2f, 0.8f, 1f, 1f);
        Handles.DrawAAPolyLine(2f, pivotPoint + Vector2.left * 6f, pivotPoint + Vector2.right * 6f);
        Handles.DrawAAPolyLine(2f, pivotPoint + Vector2.down * 6f, pivotPoint + Vector2.up * 6f);
        Handles.EndGUI();

        EditorGUILayout.LabelField("Preview: drag orange handles to draw the split line. Blue cross is the copied pivot.");
        HandlePreviewInput(previewRect, width, height, leftPoint, rightPoint);
    }

    private void HandlePreviewInput(Rect previewRect, int width, int height, Vector2 leftPoint, Vector2 rightPoint)
    {
        var currentEvent = Event.current;
        if (currentEvent == null) return;

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && previewRect.Contains(currentEvent.mousePosition))
        {
            draggingLineEndpoint = Vector2.Distance(currentEvent.mousePosition, leftPoint) <= Vector2.Distance(currentEvent.mousePosition, rightPoint) ? 0 : 1;
            currentEvent.Use();
        }

        if (currentEvent.type == EventType.MouseDrag && draggingLineEndpoint >= 0)
        {
            var y = PreviewPointToPixelY(previewRect, currentEvent.mousePosition.y, height);
            if (splitMode == SplitMode.Horizontal)
            {
                splitYFromBottom = y;
                splitYLeftFromBottom = y;
                splitYRightFromBottom = y;
            }
            else if (draggingLineEndpoint == 0)
            {
                splitYLeftFromBottom = y;
            }
            else
            {
                splitYRightFromBottom = y;
            }

            Repaint();
            currentEvent.Use();
        }

        if (currentEvent.type == EventType.MouseUp && draggingLineEndpoint >= 0)
        {
            draggingLineEndpoint = -1;
            currentEvent.Use();
        }
    }

    private static Vector2 PixelToPreviewPoint(Rect previewRect, float x, float yFromBottom, int width, int height)
    {
        var normalizedX = width <= 1 ? 0f : x / (width - 1f);
        var normalizedY = height <= 1 ? 0f : yFromBottom / (height - 1f);
        return new Vector2(
            previewRect.xMin + normalizedX * previewRect.width,
            previewRect.yMax - normalizedY * previewRect.height);
    }

    private static int PreviewPointToPixelY(Rect previewRect, float guiY, int height)
    {
        var normalizedY = Mathf.InverseLerp(previewRect.yMax, previewRect.yMin, guiY);
        return Mathf.Clamp(Mathf.RoundToInt(normalizedY * (height - 1)), 1, height - 1);
    }
}
