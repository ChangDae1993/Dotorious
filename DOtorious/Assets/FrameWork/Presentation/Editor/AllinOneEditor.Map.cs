using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using JYW.Game.ObjectMaker.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class AllinOneEditor
{
    private enum MapTool { Brush, Eraser, Line, Rectangle, Ellipse }
    [SerializeField] private MapEditorDraft mapDraft;
    [SerializeField] private MapTool mapTool;
    [SerializeField] private float mapZoom = 1;
    [SerializeField] private Vector2 mapPan;
    [SerializeField] private bool mapGrid, mapMaskOnly;
    [SerializeField] private string mapDraftPath, mapLastScene, mapLastPrefab;
    private VisualElement documentWorkspace, mapWorkspace;
    private ScrollView mapSettings;
    private IMGUIContainer mapCanvas;
    private Label mapNoticeLabel, mapInfo;
    private Texture2D mapPreview;
    private bool mapPreviewDirty = true, mapRecoveryDirty, mapRecoveryBlocked, mapFitPending = true;
    private bool mapStroke, mapPanning;
    private int mapControl, mapUndoGroup;
    private Vector2 mapStrokeStart, mapPrevious, mapMouse, mapPanStart;
    private double mapLastPreview;
    private string mapNotice = "하얀 도화지 위에 지형을 그리세요. 이미지 없이도 윤곽을 먼저 그릴 수 있습니다.";
    public static string MapRecoveryPath => Path.GetFullPath("UserSettings/AllinOneEditor.mapdraft");
    public MapPaintDocument MapDocument { get { RestoreMapDraft(); return mapDraft.Data; } }
    private bool IsMapArea => activeArea == WorkspaceArea.Map;

    private void RestoreMapDraft()
    {
        if (mapDraft != null && mapDraft.Data != null) return;
        mapDraft = CreateInstance<MapEditorDraft>(); mapDraft.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            if (File.Exists(MapRecoveryPath)) mapDraft.Data = ReadMapFile(MapRecoveryPath);
            mapDraft.Data.EnsureValid();
        }
        catch (Exception ex)
        {
            mapRecoveryBlocked = true;
            mapNotice = "자동 복구 파일을 읽지 못했습니다. 원본을 보존합니다. 새 도화지를 시작하거나 초안을 불러오세요. " + ex.Message;
        }
        mapPreviewDirty = true;
    }

    private static MapPaintDocument ReadMapFile(string path)
    {
        using (var file = File.OpenRead(path))
        using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        using (var buffer = new MemoryStream())
        {
            var chunk = new byte[8192]; int count;
            while ((count = gzip.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + count > 160 * 1024 * 1024) throw new InvalidDataException("초안 파일이 너무 큽니다.");
                buffer.Write(chunk, 0, count);
            }
            var data = JsonUtility.FromJson<MapPaintDocument>(Encoding.UTF8.GetString(buffer.ToArray()));
            if (data == null) throw new InvalidDataException("Map 초안이 아닙니다.");
            data.EnsureValid();
            if (data.terrainPng != null && data.terrainPng.Length > 0) { var image = data.CreateTexture(); DestroyImmediate(image); }
            return data;
        }
    }

    private static void WriteMapFile(string path, MapPaintDocument data)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path)); Directory.CreateDirectory(folder);
        string temp = path + ".tmp";
        using (var file = File.Create(temp))
        using (var gzip = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)); gzip.Write(bytes, 0, bytes.Length);
        }
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
    }

    private void SaveMapRecovery()
    {
        if (mapDraft == null || !mapRecoveryDirty || mapRecoveryBlocked) return;
        try { WriteMapFile(MapRecoveryPath, mapDraft.Data); mapRecoveryDirty = false; }
        catch (Exception ex) { SetMapNotice("자동 복구 저장 실패: " + ex.Message, true); }
    }
    private void ReleaseMapTextures()
    { if (mapPreview != null) DestroyImmediate(mapPreview); mapPreview = null; mapPreviewDirty = true; }
    private void DisposeMapDraft()
    {
        FinishMapStroke(); SaveMapRecovery(); ReleaseMapTextures();
        if (mapDraft != null) { Undo.ClearUndo(mapDraft); DestroyImmediate(mapDraft); mapDraft = null; }
    }
    private void OnMapUndoRedo()
    {
        if (mapDraft == null) return;
        mapPreviewDirty = mapRecoveryDirty = true;
        if (IsMapArea) { RebuildMapSettings(); RefreshMapStatus(); mapCanvas?.MarkDirtyRepaint(); }
    }
    private void MapChanged()
    {
        mapPreviewDirty = mapRecoveryDirty = true;
        EditorUtility.SetDirty(mapDraft); mapCanvas?.MarkDirtyRepaint(); RefreshMapStatus();
    }
    private void ChangeMap(string label, Action<MapPaintDocument> change, bool rebuild = false)
    {
        FinishMapStroke(); RestoreMapDraft();
        var current = mapDraft.Data;
        var before = new MapPaintDocument
        {
            width = current.width, height = current.height, pixelsPerUnit = current.pixelsPerUnit,
            brushDiameter = current.brushDiameter, mapName = current.mapName, terrainPng = current.terrainPng,
            textureMode = current.textureMode, tileWidth = current.tileWidth, tileHeight = current.tileHeight,
            tileScale = current.tileScale, mask = current.mask == null ? null : (byte[])current.mask.Clone()
        };
        Undo.RegisterCompleteObjectUndo(mapDraft, label);
        try { change(mapDraft.Data); mapDraft.Data.EnsureValid(); MapChanged(); if (rebuild) RebuildMapSettings(); }
        catch (Exception ex) { mapDraft.Data = before; mapPreviewDirty = true; RebuildMapSettings(); SetMapNotice(ex.Message, true); }
    }

    private void DrawMapWorkspace(VisualElement root)
    {
        mapWorkspace = new VisualElement { name = "map-workspace" };
        mapWorkspace.AddToClassList("map-workspace"); root.Add(mapWorkspace);
        var split = new TwoPaneSplitView(0, 320, TwoPaneSplitViewOrientation.Horizontal);
        split.style.flexGrow = 1; split.viewDataKey = "AllinOneEditor.MapSettingsWidth"; mapWorkspace.Add(split);
        mapSettings = new ScrollView { name = "map-settings" }; mapSettings.AddToClassList("map-settings"); split.Add(mapSettings);
        var body = new VisualElement(); body.style.flexGrow = 1; body.style.minWidth = 350; split.Add(body);
        var tools = new VisualElement(); tools.AddToClassList("map-toolbar"); body.Add(tools);
        var tool = new ToolbarMenu { text = "도구" };
        string[] names = { "브러시 · B", "지우개 · E", "직선 · L", "사각형 · R", "타원 · O" };
        for (int i = 0; i < names.Length; i++) { var choice = (MapTool)i; tool.menu.AppendAction(names[i], _ => { FinishMapStroke(); mapTool = choice; RebuildMapSettings(); mapCanvas.Focus(); }); }
        tools.Add(tool);
        tools.Add(ActionButton("화면 맞춤 · F", () => { mapFitPending = true; mapCanvas.MarkDirtyRepaint(); }));
        tools.Add(ActionButton("1:1", () => { mapZoom = 1; mapPan = Vector2.zero; mapCanvas.MarkDirtyRepaint(); }));
        tools.Add(ActionButton("−", () => { mapZoom = Mathf.Max(.05f, mapZoom / 1.25f); mapCanvas.MarkDirtyRepaint(); }));
        tools.Add(ActionButton("+", () => { mapZoom = Mathf.Min(8, mapZoom * 1.25f); mapCanvas.MarkDirtyRepaint(); }));
        mapInfo = new Label(); mapInfo.AddToClassList("map-info"); tools.Add(mapInfo);
        mapCanvas = new IMGUIContainer(DrawMapCanvas) { name = "map-canvas", focusable = true };
        mapCanvas.style.flexGrow = 1; mapCanvas.style.minHeight = 200; body.Add(mapCanvas);
        mapNoticeLabel = new Label(mapNotice); mapNoticeLabel.AddToClassList("map-notice"); body.Add(mapNoticeLabel);
        body.Add(Text("휠: 확대/축소  ·  가운데 버튼/Alt+드래그: 이동  ·  [ ]: 브러시 크기  ·  Ctrl+Z/Y: 실행 취소/다시 실행  ·  Ctrl+S: 초안 저장", "map-shortcuts"));
        root.schedule.Execute(() => { if (!mapStroke) SaveMapRecovery(); }).Every(1500);
        RebuildMapSettings();
    }
    private VisualElement MapSection(string title)
    {
        var section = new VisualElement(); section.AddToClassList("map-section");
        section.Add(Text(title, "map-heading")); mapSettings.Add(section); return section;
    }
    private T MapField<T, V>(VisualElement parent, T field, V value, Action<V> changed, string name) where T : BaseField<V>
    {
        field.name = name; field.SetValueWithoutNotify(value); field.AddToClassList("map-field");
        field.RegisterValueChangedCallback(e => changed(e.newValue)); parent.Add(field); return field;
    }
    private void RebuildMapSettings()
    {
        if (mapSettings == null) return;
        RestoreMapDraft(); var d = mapDraft.Data; var scroll = mapSettings.scrollOffset; mapSettings.Clear();
        var file = MapSection("MAP / 지형 제작");
        MapField(file, new TextField("씬 이름") { isDelayed = true }, d.mapName, v => ChangeMap("지형 이름", m => m.mapName = v), "map-name");
        var files = new VisualElement(); files.AddToClassList("map-row"); file.Add(files);
        files.Add(ActionButton("새 도화지", NewMap)); files.Add(ActionButton("불러오기", LoadMapDraftFile)); files.Add(ActionButton("초안 저장", SaveMapDraftFile));
        file.Add(Text("초안은 자동 복구됩니다. Make는 별도 씬과 재사용 가능한 지형 Prefab을 만듭니다.", "map-help"));
        var size = MapSection("도화지 / 실제 크기");
        var width = MapField(size, new IntegerField("가로 px") { isDelayed = true }, d.width, _ => { }, "map-width");
        var height = MapField(size, new IntegerField("세로 px") { isDelayed = true }, d.height, _ => { }, "map-height");
        size.Add(ActionButton("크기 적용 · 좌하단 유지", () => ChangeMap("도화지 크기", m => { m.Resize(width.value, height.value); mapFitPending = true; }, true)));
        MapField(size, new FloatField("Pixels / Unit") { isDelayed = true }, d.pixelsPerUnit, v => ChangeMap("월드 크기", m => m.pixelsPerUnit = Mathf.Clamp(v, 1, 10000)), "map-ppu");
        size.Add(Text("1~4096 px. 축소하면 바깥 그림이 잘립니다. 실행 취소 가능. 원점은 지형 중앙입니다.", "map-help"));
        var brush = MapSection("그리기");
        MapField(brush, new EnumField("도구", mapTool), (Enum)mapTool, v => { FinishMapStroke(); mapTool = (MapTool)v; mapCanvas.Focus(); }, "map-tool");
        MapField(brush, new SliderInt("브러시 지름", 1, 512), Mathf.RoundToInt(d.brushDiameter), v => ChangeMap("브러시 크기", m => m.brushDiameter = v), "map-brush");
        var operations = new VisualElement(); operations.AddToClassList("map-row"); brush.Add(operations);
        operations.Add(ActionButton("전체 채우기", () => ChangeMap("지형 채우기", m => { for (int i = 0; i < m.mask.Length; i++) m.mask[i] = 255; })));
        operations.Add(ActionButton("비우기", () => ChangeMap("지형 비우기", m => m.Clear())));
        operations.Add(ActionButton("반전", () => ChangeMap("지형 반전", m => { for (int i = 0; i < m.mask.Length; i++) m.mask[i] = (byte)(255 - m.mask[i]); })));
        var terrain = MapSection("지형 이미지");
        terrain.Add(ActionButton("이미지 파일 불러오기…", ImportMapImage));
        MapField(terrain, new UnityEditor.UIElements.ObjectField("프로젝트 이미지") { objectType = typeof(UnityEngine.Object), allowSceneObjects = false }, (UnityEngine.Object)null, o =>
        {
            if (o is Texture2D texture) ChangeMap("지형 이미지", m => { m.SetTerrain(texture); m.tileWidth = m.tileHeight = 0; }, true);
            else if (o is Sprite sprite) ChangeMap("지형 이미지", m => { m.SetTerrain(sprite); m.tileWidth = m.tileHeight = 0; }, true);
            else if (o != null) SetMapNotice("Texture2D 또는 Sprite를 선택하세요.", true);
        }, "map-image");
        terrain.Add(Text(d.terrainPng != null && d.terrainPng.Length > 0 ? "✓ 이미지가 초안에 포함되어 있습니다." : "PNG / JPG를 선택하세요. 지금은 갈색으로 마스크를 미리 봅니다.", "map-help"));
        var modes = new PopupField<string>("배치", new System.Collections.Generic.List<string> { "연속 타일", "한 장 · 가로 맞춤", "한 장 · 세로 맞춤", "한 장 · 도화지 채움" }, (int)d.textureMode) { name = "map-texture-mode" };
        modes.AddToClassList("map-field"); modes.RegisterValueChangedCallback(e => ChangeMap("이미지 배치", m => m.textureMode = (MapTextureMode)modes.index, true)); terrain.Add(modes);
        if (d.textureMode == MapTextureMode.Tile)
        {
            MapField(terrain, new FloatField("타일 가로 px") { isDelayed = true }, d.tileWidth, v => ChangeMap("타일 가로", m => m.tileWidth = Mathf.Max(0, v)), "map-tile-width");
            MapField(terrain, new FloatField("타일 세로 px") { isDelayed = true }, d.tileHeight, v => ChangeMap("타일 세로", m => m.tileHeight = Mathf.Max(0, v)), "map-tile-height");
            MapField(terrain, new FloatField("타일 배율") { isDelayed = true }, d.tileScale, v => ChangeMap("타일 배율", m => m.tileScale = Mathf.Clamp(v, .01f, 100)), "map-tile-scale");
            terrain.Add(Text("가로/세로 0 = 원본 크기. 배율 1 = 기본 크기로 가로·세로 연속 반복.", "map-help"));
        }
        else terrain.Add(Text("가로/세로 맞춤은 원본 비율을 유지하고 가운데 정렬합니다. 도화지 밖은 잘리고, 이미지가 없는 곳은 투명합니다.", "map-help"));
        var view = MapSection("보기 / 결과");
        MapField(view, new Toggle("32px 격자"), mapGrid, v => { mapGrid = v; mapCanvas.MarkDirtyRepaint(); }, "map-grid");
        MapField(view, new Toggle("마스크만 보기"), mapMaskOnly, v => { mapMaskOnly = v; mapPreviewDirty = true; mapCanvas.MarkDirtyRepaint(); }, "map-mask");
        view.Add(Text("충돌은 그린 마스크 기준입니다. 투명한 이미지 부분에도 마스크가 있으면 지형으로 충돌합니다. 지우개로 구멍을 만드세요.", "map-help"));
        view.Add(ActionButton("Make · 지형 씬 만들기", MakeMap, "ew-primary-button"));
        view.Add(ActionButton("마스킹 PNG 내보내기…", ExportMapPng));
        if (!string.IsNullOrEmpty(mapLastScene))
        {
            view.Add(Text(mapLastScene, "map-help"));
            view.Add(ActionButton("생성한 씬 찾기", () => EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(mapLastScene))));
            view.Add(ActionButton("재사용 Prefab 찾기", () => EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(mapLastPrefab))));
        }
        mapSettings.scrollOffset = scroll;
    }
    private void RefreshMapWorkspace()
    {
        RestoreMapDraft();
        for (int i = 0; i < areaButtons.Count; i++) areaButtons[i].EnableInClassList("is-active", i == (int)WorkspaceArea.Map);
        rootVisualElement.Q<ToolbarMenu>(className: "ew-file-menu")?.SetEnabled(false);
        RefreshMapStatus(); mapCanvas?.MarkDirtyRepaint();
    }
    private void RefreshMapStatus()
    {
        if (!IsMapArea || documentTitle == null || mapDraft == null) return;
        var d = mapDraft.Data; documentTitle.text = "Map / " + d.mapName;
        documentState.text = mapRecoveryBlocked ? "복구 확인 필요" : mapRecoveryDirty ? "● 변경됨" : "✓ 초안 복구됨";
        documentState.EnableInClassList("is-dirty", mapRecoveryDirty || mapRecoveryBlocked);
        saveButton.text = "Make · 씬 생성"; saveButton.tooltip = "지형만 있는 새 씬과 Prefab 생성. Ctrl+S는 초안 파일 저장.";
        saveButton.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling);
        if (mapInfo != null) mapInfo.text = d.width + " × " + d.height + " px  |  " + (d.width / d.pixelsPerUnit).ToString("0.##") + " × " + (d.height / d.pixelsPerUnit).ToString("0.##") + " units  |  " + Mathf.RoundToInt(mapZoom * 100) + "%";
    }
    private void SetMapNotice(string message, bool error = false)
    {
        mapNotice = message;
        if (mapNoticeLabel != null) { mapNoticeLabel.text = message; mapNoticeLabel.EnableInClassList("is-error", error); }
    }
    private void NewMap()
    {
        if (!EditorUtility.DisplayDialog("새 도화지", "현재 초안을 새 도화지로 바꿉니다. 실행 취소로 복구할 수 있습니다. 필요한 초안은 파일로 저장하세요.", "새 도화지", "취소")) return;
        ChangeMap("새 지형", _ => { mapDraft.Data = new MapPaintDocument(); mapDraft.Data.EnsureValid(); mapRecoveryBlocked = false; mapDraftPath = null; mapFitPending = true; }, true);
        SetMapNotice("새 도화지입니다. 지형 이미지를 불러오고 브러시로 그리세요.");
    }
    private void ImportMapImage()
    {
        string path = EditorUtility.OpenFilePanelWithFilters("지형 이미지 불러오기", "", new[] { "PNG / JPG", "png,jpg,jpeg" });
        if (string.IsNullOrEmpty(path)) return;
        ChangeMap("지형 이미지 불러오기", m => { m.LoadTerrainFile(path); m.tileWidth = m.tileHeight = 0; }, true);
    }
    private void SaveMapDraftFile()
    {
        FinishMapStroke(); RestoreMapDraft();
        string path = EditorUtility.SaveFilePanel("Map 초안 저장", string.IsNullOrEmpty(mapDraftPath) ? Application.dataPath : Path.GetDirectoryName(mapDraftPath), mapDraft.Data.mapName, "mapdraft");
        if (string.IsNullOrEmpty(path)) return;
        try { WriteMapFile(path, mapDraft.Data); mapDraftPath = path; SaveMapRecovery(); SetMapNotice("초안 저장 완료: " + path); }
        catch (Exception ex) { SetMapNotice("초안 저장 실패: " + ex.Message, true); }
    }
    private void LoadMapDraftFile()
    {
        string path = EditorUtility.OpenFilePanel("Map 초안 불러오기", Application.dataPath, "mapdraft");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var data = ReadMapFile(path);
            ChangeMap("지형 초안 불러오기", _ => { mapDraft.Data = data; mapRecoveryBlocked = false; mapDraftPath = path; mapFitPending = true; }, true);
            SetMapNotice("초안을 불러왔습니다. 이전 그림은 실행 취소로 복구할 수 있습니다.");
        }
        catch (Exception ex) { SetMapNotice("불러오기 실패: " + ex.Message, true); }
    }
    private void ExportMapPng()
    {
        string path = EditorUtility.SaveFilePanel("마스킹 지형 PNG", Application.dataPath, mapDraft.Data.mapName, "png");
        if (string.IsNullOrEmpty(path)) return;
        Texture2D texture = null;
        try { texture = mapDraft.Data.CreateTexture(); File.WriteAllBytes(path, texture.EncodeToPNG()); SetMapNotice("PNG 내보내기 완료: " + path); }
        catch (Exception ex) { SetMapNotice(ex.Message, true); }
        finally { if (texture != null) DestroyImmediate(texture); }
    }
    private void MakeMap()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { SetMapNotice("Play Mode / 컴파일이 끝난 뒤 Make를 사용하세요.", true); return; }
        FinishMapStroke(); rootVisualElement.Focus(); SaveMapRecovery();
        try
        {
            var result = MapSceneExporter.Export(mapDraft.Data);
            mapLastScene = result.ScenePath; mapLastPrefab = result.PrefabPath;
            SetMapNotice("생성 완료: " + result.ScenePath + " · 지형 오브젝트 또는 Prefab을 메인 씬으로 옮겨 사용하세요.");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(result.ScenePath)); RebuildMapSettings();
        }
        catch (Exception ex) { SetMapNotice("Make 실패: " + ex.Message, true); }
    }

    private Rect MapImageRect(Rect viewport)
    {
        var d = mapDraft.Data; Vector2 size = new Vector2(d.width, d.height) * mapZoom;
        return new Rect(viewport.center - size / 2 + mapPan, size);
    }
    private Vector2 MapPixel(Vector2 mouse, Rect image) => new Vector2((mouse.x - image.x) / mapZoom, (image.yMax - mouse.y) / mapZoom);
    private void DrawMapCanvas()
    {
        RestoreMapDraft(); var d = mapDraft.Data;
        Rect viewport = new Rect(0, 0, mapCanvas.contentRect.width, mapCanvas.contentRect.height);
        if (viewport.width < 2 || viewport.height < 2) return;
        if (mapFitPending) { mapZoom = Mathf.Clamp(Mathf.Min((viewport.width - 48) / d.width, (viewport.height - 48) / d.height), .05f, 8); mapPan = Vector2.zero; mapFitPending = false; }
        var evt = Event.current; int control = GUIUtility.GetControlID("MapPaintCanvas".GetHashCode(), FocusType.Passive);
        Rect canvas = MapImageRect(viewport); mapMouse = evt.mousePosition;
        if (evt.type == EventType.Repaint)
        {
            if (mapPreviewDirty && (!mapStroke || EditorApplication.timeSinceStartup - mapLastPreview > .04))
            {
                ReleaseMapTextures();
                if (mapMaskOnly)
                {
                    mapPreview = new Texture2D(d.width, d.height, TextureFormat.RGBA32, false);
                    var colors = new Color32[d.mask.Length];
                    for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(42, 61, 80, d.mask[i]);
                    mapPreview.SetPixels32(colors); mapPreview.Apply();
                }
                else mapPreview = d.CreateTexture();
                mapPreview.filterMode = FilterMode.Point; mapPreview.hideFlags = HideFlags.HideAndDontSave;
                mapPreviewDirty = false; mapLastPreview = EditorApplication.timeSinceStartup;
            }
            EditorGUI.DrawRect(viewport, new Color(.085f, .11f, .15f));
            GUI.BeginClip(viewport);
            EditorGUI.DrawRect(new Rect(canvas.x + 4, canvas.y + 4, canvas.width, canvas.height), new Color(0, 0, 0, .3f));
            EditorGUI.DrawRect(canvas, Color.white);
            if (mapPreview != null) GUI.DrawTexture(canvas, mapPreview, ScaleMode.StretchToFill, true);
            Handles.BeginGUI();
            if (mapGrid && 32 * mapZoom >= 6)
            {
                Handles.color = new Color(.15f, .25f, .35f, .18f);
                for (int x = 0; x <= d.width; x += 32) { float px = canvas.x + x * mapZoom; if (px >= 0 && px <= viewport.width) Handles.DrawLine(new Vector3(px, Mathf.Max(0, canvas.y)), new Vector3(px, Mathf.Min(viewport.height, canvas.yMax))); }
                for (int y = 0; y <= d.height; y += 32) { float py = canvas.y + y * mapZoom; if (py >= 0 && py <= viewport.height) Handles.DrawLine(new Vector3(Mathf.Max(0, canvas.x), py), new Vector3(Mathf.Min(viewport.width, canvas.xMax), py)); }
            }
            Handles.color = mapTool == MapTool.Eraser ? new Color(.95f, .35f, .3f) : new Color(.15f, .45f, .9f);
            if (canvas.Contains(mapMouse)) Handles.DrawWireDisc(mapMouse, Vector3.forward, Mathf.Max(1, d.brushDiameter * mapZoom * .5f));
            if (mapStroke && mapTool >= MapTool.Line)
            {
                Vector2 start = new Vector2(canvas.x + mapStrokeStart.x * mapZoom, canvas.yMax - mapStrokeStart.y * mapZoom);
                if (mapTool == MapTool.Line) Handles.DrawAAPolyLine(2, start, mapMouse);
                else
                {
                    Rect shape = Rect.MinMaxRect(Mathf.Min(start.x, mapMouse.x), Mathf.Min(start.y, mapMouse.y), Mathf.Max(start.x, mapMouse.x), Mathf.Max(start.y, mapMouse.y));
                    if (mapTool == MapTool.Rectangle) Handles.DrawAAPolyLine(2, new Vector3(shape.x, shape.y), new Vector3(shape.xMax, shape.y), new Vector3(shape.xMax, shape.yMax), new Vector3(shape.x, shape.yMax), new Vector3(shape.x, shape.y));
                    else { var points = new Vector3[65]; for (int i = 0; i < points.Length; i++) { float a = i * Mathf.PI * 2 / 64; points[i] = shape.center + new Vector2(Mathf.Cos(a) * shape.width / 2, Mathf.Sin(a) * shape.height / 2); } Handles.DrawAAPolyLine(2, points); }
                }
            }
            Handles.EndGUI(); GUI.EndClip(); RefreshMapStatus();
        }
        if (evt.type == EventType.ScrollWheel && viewport.Contains(evt.mousePosition))
        {
            Vector2 anchor = (evt.mousePosition - viewport.center - mapPan) / mapZoom;
            mapZoom = Mathf.Clamp(mapZoom * Mathf.Pow(1.12f, -evt.delta.y), .05f, 8);
            mapPan = evt.mousePosition - viewport.center - anchor * mapZoom;
            evt.Use(); mapCanvas.MarkDirtyRepaint();
        }
        if (evt.type == EventType.MouseDown && viewport.Contains(evt.mousePosition))
        {
            if (evt.button == 2 || evt.button == 0 && evt.alt)
            { FinishMapStroke(); mapPanning = true; mapPanStart = evt.mousePosition - mapPan; mapControl = control; GUIUtility.hotControl = control; evt.Use(); }
            else if (evt.button == 0 && canvas.Contains(evt.mousePosition) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                mapCanvas.Focus(); Undo.IncrementCurrentGroup(); mapUndoGroup = Undo.GetCurrentGroup(); Undo.RegisterCompleteObjectUndo(mapDraft, "지형 " + mapTool);
                mapControl = control; GUIUtility.hotControl = control; mapStroke = true; mapStrokeStart = mapPrevious = MapPixel(evt.mousePosition, canvas);
                if (mapTool == MapTool.Brush || mapTool == MapTool.Eraser) { d.PaintStroke(mapPrevious, mapPrevious, mapTool == MapTool.Eraser); MapChanged(); }
                evt.Use();
            }
        }
        if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == mapControl)
        {
            if (mapPanning) mapPan = evt.mousePosition - mapPanStart;
            else if (mapStroke && (mapTool == MapTool.Brush || mapTool == MapTool.Eraser)) { Vector2 point = MapPixel(evt.mousePosition, canvas); d.PaintStroke(mapPrevious, point, mapTool == MapTool.Eraser); mapPrevious = point; MapChanged(); }
            evt.Use(); mapCanvas.MarkDirtyRepaint();
        }
        if (evt.type == EventType.MouseUp && GUIUtility.hotControl == mapControl && (mapPanning || mapStroke))
        {
            if (mapStroke)
            {
                var point = MapPixel(evt.mousePosition, canvas);
                if (mapTool == MapTool.Brush || mapTool == MapTool.Eraser) d.PaintStroke(mapPrevious, point, mapTool == MapTool.Eraser);
                if (mapTool == MapTool.Line) d.PaintStroke(mapStrokeStart, point);
                if (mapTool == MapTool.Rectangle) d.FillRectangle(mapStrokeStart, point);
                if (mapTool == MapTool.Ellipse) d.FillEllipse(mapStrokeStart, point);
                MapChanged();
            }
            FinishMapStroke(); evt.Use(); mapCanvas.MarkDirtyRepaint();
        }
        if (evt.type == EventType.KeyDown)
        {
            if (evt.keyCode == KeyCode.Escape) { FinishMapStroke(); evt.Use(); }
            else if (!evt.control && !evt.command && !evt.alt)
            {
                if (HandleMapShortcut(evt.keyCode)) evt.Use();
            }
        }
        if (evt.type == EventType.MouseMove) mapCanvas.MarkDirtyRepaint();
    }
    private bool MapShortcutTargetAllowsTyping()
    {
        // IMGUI releases keyboard focus after a mouse stroke. Treat no focused field as canvas input,
        // while preserving ordinary typing and native navigation in all sidebar controls.
        var focused = rootVisualElement.focusController.focusedElement as VisualElement;
        return mapStroke || focused == null || focused == rootVisualElement || focused == mapCanvas ||
            (mapCanvas != null && mapCanvas.Contains(focused));
    }
    private bool HandleMapShortcut(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.B: FinishMapStroke(); mapTool = MapTool.Brush; break;
            case KeyCode.E: FinishMapStroke(); mapTool = MapTool.Eraser; break;
            case KeyCode.L: FinishMapStroke(); mapTool = MapTool.Line; break;
            case KeyCode.R: FinishMapStroke(); mapTool = MapTool.Rectangle; break;
            case KeyCode.O: FinishMapStroke(); mapTool = MapTool.Ellipse; break;
            case KeyCode.F: FinishMapStroke(); mapFitPending = true; break;
            case KeyCode.LeftBracket: ChangeMap("브러시 크기", m => m.brushDiameter = Mathf.Max(1, m.brushDiameter - 4)); break;
            case KeyCode.RightBracket: ChangeMap("브러시 크기", m => m.brushDiameter = Mathf.Min(512, m.brushDiameter + 4)); break;
            default: return false;
        }
        RebuildMapSettings(); mapCanvas?.Focus(); mapCanvas?.MarkDirtyRepaint(); return true;
    }
    private void FinishMapStroke()
    {
        if (mapStroke) { Undo.CollapseUndoOperations(mapUndoGroup); mapPreviewDirty = mapRecoveryDirty = true; }
        mapStroke = mapPanning = false;
        if (mapControl != 0 && GUIUtility.hotControl == mapControl) GUIUtility.hotControl = 0;
        mapControl = 0;
    }
    private void OnLostFocus() { FinishMapStroke(); SaveMapRecovery(); }
}