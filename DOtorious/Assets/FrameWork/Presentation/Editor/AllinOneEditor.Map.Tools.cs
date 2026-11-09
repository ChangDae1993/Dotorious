using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class AllinOneEditor
{
    [SerializeField] private bool mapFillContiguous = true, mapFillErase, mapCheckerboard, mapCollisionOverlay;
    private bool mapCollisionDirty = true;
    private List<Vector2[]> mapCollisionContours;
    private Texture2D mapSourcePreview;

    private void RestoreMapTerrainSource()
    {
        mapDraft.TerrainSource = null;
        if (!string.IsNullOrEmpty(mapDraft.Data.terrainSourceId) && GlobalObjectId.TryParse(mapDraft.Data.terrainSourceId, out var id))
            mapDraft.TerrainSource = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
    }
    private void AddMapImagePicker(VisualElement section)
    {
        // Keep this control alive while the Object Picker emits repeated selection changes.
        MapField(section, new ObjectField("이미지 / 오브젝트") { objectType = typeof(UnityEngine.Object), allowSceneObjects = true },
            mapDraft.TerrainSource, SelectMapTerrainSource, "map-image");
        var reload = ActionButton("선택 이미지 새로고침", () => SelectMapTerrainSource(mapDraft.TerrainSource));
        reload.name = "map-image-reload"; section.Add(reload);
        var label = Text("", "map-help"); label.name = "map-image-label"; section.Add(label);
        var preview = new Image { name = "map-source-preview", scaleMode = ScaleMode.ScaleToFit };
        preview.style.height = 90; preview.style.backgroundColor = new Color(.15f, .17f, .2f); section.Add(preview);
        SyncMapImagePicker();
    }
    private void SelectMapTerrainSource(UnityEngine.Object source)
    {
        if (!source)
        {
            // Clearing the reference does not silently discard the embedded terrain image.
            ChangeMap("이미지 참조 해제", m => { mapDraft.TerrainSource = null; m.terrainSourceId = null; });
            SyncMapImagePicker(); return;
        }
        var input = source;
        if (source is GameObject go) source = go.GetComponentInChildren<SpriteRenderer>(true);
        if (source is SpriteRenderer renderer) source = renderer.sprite;
        if (!(source is Texture2D) && !(source is Sprite))
        {
            SetMapNotice("Texture2D, Sprite 또는 SpriteRenderer가 있는 GameObject를 선택하세요.", true);
            SyncMapImagePicker(); return;
        }
        bool applied = ChangeMap("지형 이미지", m =>
        {
            if (source is Texture2D texture) m.SetTerrain(texture); else m.SetTerrain((Sprite)source);
            m.terrainSourceName = input.name;
            m.terrainSourceId = GlobalObjectId.GetGlobalObjectIdSlow(input).ToString();
            mapDraft.TerrainSource = input;
        }, false, false);
        SyncMapImagePicker();
        if (applied) SetMapNotice("지형 이미지 적용: " + input.name + " · 같은 에셋의 내용이 바뀌면 선택 이미지 새로고침을 누르세요.");
    }
    private void SyncMapImagePicker()
    {
        if (mapSettings == null) return;
        mapSettings.Q<ObjectField>("map-image")?.SetValueWithoutNotify(mapDraft.TerrainSource);
        mapSettings.Q<Button>("map-image-reload")?.SetEnabled(mapDraft.TerrainSource != null);
        var data = mapDraft.Data; bool hasImage = data.terrainPng != null && data.terrainPng.Length > 0;
        mapSettings.Q<Button>("map-trim-transparent")?.SetEnabled(hasImage);
        var label = mapSettings.Q<Label>("map-image-label");
        if (label != null) label.text = hasImage ? "✓ " + (string.IsNullOrEmpty(data.terrainSourceName) ? "초안에 포함된 이미지" : data.terrainSourceName) + " · 이미지 데이터 포함" : "PNG / JPG, Texture2D, Sprite 또는 SpriteRenderer 오브젝트를 선택하세요.";
        mapSettings.Q<FloatField>("map-tile-width")?.SetValueWithoutNotify(data.tileWidth);
        mapSettings.Q<FloatField>("map-tile-height")?.SetValueWithoutNotify(data.tileHeight);
        if (mapSourcePreview) DestroyImmediate(mapSourcePreview);
        mapSourcePreview = null;
        if (hasImage)
        {
            mapSourcePreview = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            mapSourcePreview.LoadImage(data.terrainPng); mapSourcePreview.filterMode = FilterMode.Point;
        }
        var preview = mapSettings.Q<Image>("map-source-preview");
        if (preview != null) { preview.image = mapSourcePreview; preview.style.display = hasImage ? DisplayStyle.Flex : DisplayStyle.None; }
    }
    private void AddMapFillSettings(VisualElement section)
    {
        MapField(section, new Toggle("채우기 · 연결된 영역만"), mapFillContiguous, v => mapFillContiguous = v, "map-fill-contiguous");
        MapField(section, new Toggle("채우기 · 지우기"), mapFillErase, v => mapFillErase = v, "map-fill-erase");
        section.Add(Text("G: 채우기. 클릭한 마스크 영역을 상하좌우로 채웁니다. 연결 옵션을 끄면 같은 상태인 모든 영역에 적용합니다.", "map-help"));
    }
    private void ApplyMapBucket(Vector2 point)
    {
        FinishMapStroke(); var data = mapDraft.Data;
        int x = Mathf.FloorToInt(point.x), y = Mathf.FloorToInt(point.y);
        if (x < 0 || y < 0 || x >= data.width || y >= data.height || (data.mask[y * data.width + x] >= 128) == !mapFillErase) return;
        Undo.IncrementCurrentGroup();
        ChangeMap(mapFillErase ? "연결 영역 지우기" : "영역 채우기", m => m.FloodFill(x, y, mapFillErase, mapFillContiguous));
        mapCanvas?.Focus();
    }
    private void AddMapPreviewSettings(VisualElement section)
    {
        MapField(section, new Toggle("투명 체크무늬"), mapCheckerboard, v => { mapCheckerboard = v; mapCanvas?.MarkDirtyRepaint(); }, "map-checkerboard");
        MapField(section, new Toggle("충돌 윤곽 보기"), mapCollisionOverlay, v => { mapCollisionOverlay = v; mapCollisionDirty = true; mapCanvas?.MarkDirtyRepaint(); }, "map-collision-overlay");
    }
    private void DrawMapBackground(Rect canvas, Rect viewport)
    {
        EditorGUI.DrawRect(canvas, Color.white);
        if (!mapCheckerboard) return;
        const int cell = 16;
        float left = Mathf.Max(0, canvas.x), right = Mathf.Min(viewport.width, canvas.xMax);
        float top = Mathf.Max(0, canvas.y), bottom = Mathf.Min(viewport.height, canvas.yMax);
        for (int y = Mathf.FloorToInt((top - canvas.y) / cell); canvas.y + y * cell < bottom; y++)
            for (int x = Mathf.FloorToInt((left - canvas.x) / cell); canvas.x + x * cell < right; x++)
            {
                if ((x + y) % 2 == 0) continue;
                float x0 = Mathf.Max(left, canvas.x + x * cell), y0 = Mathf.Max(top, canvas.y + y * cell);
                EditorGUI.DrawRect(Rect.MinMaxRect(x0, y0, Mathf.Min(right, canvas.x + (x + 1) * cell), Mathf.Min(bottom, canvas.y + (y + 1) * cell)), new Color(.82f, .84f, .87f));
            }
    }
    private void DrawMapCollision(Rect canvas)
    {
        if (!mapCollisionOverlay) return;
        if (mapCollisionDirty && !mapStroke)
        {
            try { mapCollisionContours = JYW.Game.ObjectMaker.Editor.MapSceneExporter.BuildCollisionContours(mapDraft.Data); }
            catch (Exception ex) { mapCollisionContours = null; SetMapNotice("충돌 미리보기: " + ex.Message, true); }
            mapCollisionDirty = false;
        }
        if (mapCollisionContours == null) return;
        Handles.color = new Color(.1f, .85f, .3f, .95f);
        foreach (var loop in mapCollisionContours)
        {
            var line = new Vector3[loop.Length + 1];
            for (int i = 0; i < loop.Length; i++) line[i] = new Vector3(canvas.x + loop[i].x * mapZoom, canvas.yMax - loop[i].y * mapZoom);
            line[loop.Length] = line[0]; Handles.DrawAAPolyLine(2, line);
        }
    }
}
