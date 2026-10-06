using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dotorious.Maps;
using Dotorious.Winter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Builds only the generated map library. Never rebuilds the player's definitions or the stage.</summary>
public static class MapIngredientLibraryBuilder
{
    public const string Root = "Assets/Resources/Prefabs/Maps/Ingeredients";
    public const string Art = "Assets/Resources/Art/Maps";
    public const string HudPath = "Assets/Resources/Prefabs/UI/MapMinimapHUD.prefab";
    public static readonly string[] Themes = {
        "SpringMeadow", "SpringBlossom", "SummerForest", "SummerCoast", "AutumnAmber",
        "AutumnCrimson", "WinterSnow", "WinterIce", "SpringMossRuins", "WinterStoneRuins"
    };
    public static readonly string[] ThemeLabels = {
        "봄 초원", "봄 꽃숲", "여름 숲", "여름 해안", "가을 황금숲",
        "가을 단풍숲", "겨울 설원", "겨울 얼음", "봄 이끼 유적", "겨울 석조 유적"
    };
    static readonly string[] Categories = {
        "Ground", "Ramps", "Stairs", "Platforms", "Bridges", "Walls",
        "Foreground", "Background", "FarBackground", "Decorations"
    };
    static readonly Color[] ThemeColors = {
        new Color(.41f,.72f,.31f), new Color(.9f,.55f,.68f), new Color(.19f,.53f,.26f),
        new Color(.8f,.69f,.4f), new Color(.9f,.58f,.19f), new Color(.74f,.25f,.17f),
        new Color(.8f,.89f,.97f), new Color(.48f,.83f,.95f), new Color(.43f,.64f,.4f), new Color(.62f,.74f,.85f)
    };
    static Texture2D terrain;
    static Material terrainMaterial;
    static Material spriteMaterial;
    static PhysicsMaterial2D terrainPhysics;
    static Sprite[] scenery;
    static Sprite[] decoration;

    [MenuItem("Tools/Dotorious/Map Ingredients/Rebuild Prefab Library")]
    public static void BuildLibrary()
    {
        EnsureFolders();
        EnsureTagsAndLayer();
        ImportTerrain();
        ImportScenery();
        BuildMaterials();
        for (int version = 1; version <= 10; version++)
            foreach (MapIngredientKind kind in Enum.GetValues(typeof(MapIngredientKind)))
                BuildPart(kind, version);
        BuildHUD();
        AssetDatabase.SaveAssets();
        Debug.Log("MAP_LIBRARY_BUILT: 100 prefabs, 10 matching seasonal families; existing stage/player untouched.");
    }

    // Used once for installation, separate from the public library rebuild command.
    public static void BuildAndConnectMain()
    {
        BuildLibrary();
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
        var stage = UnityEngine.Object.FindAnyObjectByType<WinterStageRuntime>();
        Transform parent = stage != null && stage.hud != null ? stage.hud.transform : GameObject.Find("HUD_UI")?.transform;
        if (parent == null) throw new InvalidOperationException("Main HUD was not found; no scene changes saved.");
        var installed = parent.GetComponentInChildren<MapMinimapHUD>(true);
        if (installed == null)
        {
            var hud = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath), scene);
            hud.transform.SetParent(parent, false);
            installed = hud.GetComponent<MapMinimapHUD>();
        }
        // The prefab is a root canvas when authored, but a nested canvas in Main.
        // Recalculate its canvas role before recording the stretch overrides.
        Canvas.ForceUpdateCanvases();
        var rect = installed.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f);
        rect.localScale = Vector3.one;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("MAP_HUD_CONNECTED: added only MapMinimapHUD below the existing gameplay HUD.");
    }

    static void EnsureFolders()
    {
        foreach (string path in new[] {Root, Art, Art + "/Meshes", Art + "/Materials", "Assets/Resources/Prefabs/UI"})
            Directory.CreateDirectory(path);
        foreach (string category in Categories) Directory.CreateDirectory(Root + "/" + category);
        AssetDatabase.Refresh();
    }

    static void EnsureTagsAndLayer()
    {
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tags = settings.FindProperty("tags");
        foreach (string tag in new[] {"Ground", "Background1", "Background2", "Background3"})
        {
            bool present = false;
            for (int i = 0; i < tags.arraySize; i++) if (tags.GetArrayElementAtIndex(i).stringValue == tag) present = true;
            if (!present) { tags.InsertArrayElementAtIndex(tags.arraySize); tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag; }
        }
        var layer = settings.FindProperty("layers").GetArrayElementAtIndex(16);
        if (!string.IsNullOrEmpty(layer.stringValue) && layer.stringValue != "MinimapPreview")
            throw new InvalidOperationException("Layer 16 is already in use. Keep its existing meaning; choose another preview layer first.");
        layer.stringValue = "MinimapPreview";
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureImporter(TextureImporter importer, bool alpha)
    {
        importer.textureType = TextureImporterType.Sprite;
        importer.alphaIsTransparency = alpha;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 4096;
        importer.spritePixelsPerUnit = 100;
        importer.isReadable = false;
    }

    static void ImportTerrain()
    {
        string path = Art + "/SeasonalTerrainAtlas.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        ConfigureImporter(importer, false);
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        terrain = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void ImportScenery()
    {
        string path = Art + "/SeasonalSceneryAtlas.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        ConfigureImporter(importer, true);
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var factories = new SpriteDataProviderFactories(); factories.Init();
        var provider = factories.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
        var existing = provider.GetSpriteRects();
        var rectangles = new List<SpriteRect>();
        for (int i = 0; i < 10; i++)
        {
            int col = i % 5, row = 1 - i / 5;
            float x = Mathf.Round(col * texture.width / 5f), y = Mathf.Round(row * texture.height / 2f);
            float w = Mathf.Round((col + 1) * texture.width / 5f) - x;
            float h = Mathf.Round((row + 1) * texture.height / 2f) - y;
            string name = "Scenery_v" + (i + 1).ToString("00");
            rectangles.Add(new SpriteRect { name = name, rect = new Rect(x + 2, y + 2, w - 4, h - 4),
                alignment = SpriteAlignment.Custom, pivot = new Vector2(.5f, .07f),
                spriteID = existing.FirstOrDefault(r => r.name == name)?.spriteID ?? GUID.Generate() });
            name = "Decoration_v" + (i + 1).ToString("00");
            rectangles.Add(new SpriteRect { name = name, rect = new Rect(x + w * .69f, y + h * .05f, w * .30f, h * .24f),
                alignment = SpriteAlignment.Custom, pivot = new Vector2(.5f, .05f),
                spriteID = existing.FirstOrDefault(r => r.name == name)?.spriteID ?? GUID.Generate() });
        }
        provider.SetSpriteRects(rectangles.ToArray());
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(
            rectangles.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply(); importer.SaveAndReimport();
        var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
        scenery = sprites.Where(s => s.name.StartsWith("Scenery_")).OrderBy(s => s.name).ToArray();
        decoration = sprites.Where(s => s.name.StartsWith("Decoration_")).OrderBy(s => s.name).ToArray();
    }

    static void BuildMaterials()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
        if (shader == null) throw new InvalidOperationException("No sprite shader found.");
        terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/SeasonalTerrain.mat");
        if (terrainMaterial == null) { terrainMaterial = new Material(shader); AssetDatabase.CreateAsset(terrainMaterial, Art + "/Materials/SeasonalTerrain.mat"); }
        terrainMaterial.mainTexture = terrain; EditorUtility.SetDirty(terrainMaterial);
        spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/SceneryUnlit.mat");
        if (spriteMaterial == null) { spriteMaterial = new Material(shader); AssetDatabase.CreateAsset(spriteMaterial, Art + "/Materials/SceneryUnlit.mat"); }
        terrainPhysics = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(Art + "/Materials/MapGround.physicsMaterial2D");
        if (terrainPhysics == null) { terrainPhysics = new PhysicsMaterial2D("MapGround"); AssetDatabase.CreateAsset(terrainPhysics, Art + "/Materials/MapGround.physicsMaterial2D"); }
        terrainPhysics.friction = .8f; terrainPhysics.bounciness = 0; EditorUtility.SetDirty(terrainPhysics);
    }

    static string Category(MapIngredientKind kind)
    {
        switch (kind)
        {
            case MapIngredientKind.Ramp: return "Ramps";
            case MapIngredientKind.Stairs: return "Stairs";
            case MapIngredientKind.Platform: return "Platforms";
            case MapIngredientKind.Bridge: return "Bridges";
            case MapIngredientKind.Wall: return "Walls";
            case MapIngredientKind.Decoration: return "Decorations";
            default: return kind.ToString();
        }
    }

    public static string PartPath(MapIngredientKind kind, int version)
    {
        return Root + "/" + Category(kind) + "/" + kind + "_v" + version.ToString("00") + "_" + Themes[version - 1] + ".prefab";
    }

    static void BuildPart(MapIngredientKind kind, int version)
    {
        string name = kind + "_v" + version.ToString("00") + "_" + Themes[version - 1];
        var root = new GameObject(name);
        var info = root.AddComponent<MapIngredient2D>(); info.Configure(kind, version, ThemeLabels[version - 1]);
        if (info.IsTerrain)
        {
            root.tag = "Ground"; root.layer = LayerMask.NameToLayer("Ground");
            BuildTerrain(root, kind, version);
        }
        else
        {
            root.tag = kind == MapIngredientKind.Foreground ? "Background1" : kind == MapIngredientKind.Background ? "Background2" :
                       kind == MapIngredientKind.FarBackground ? "Background3" : "Untagged";
            root.layer = kind == MapIngredientKind.Foreground ? 14 : kind == MapIngredientKind.FarBackground ? 6 : 10;
            if (kind == MapIngredientKind.Decoration)
                AddSprite(root, "RockAndShrubs", decoration[version - 1], Vector3.zero, 2.2f, Color.white, -2);
            else if (kind == MapIngredientKind.Background)
                AddSprite(root, "Tree", scenery[version - 1], Vector3.zero, 7.5f, Color.white, -40);
            else if (kind == MapIngredientKind.Foreground)
                AddSprite(root, "NearTree", scenery[version - 1], Vector3.zero, 10.5f, new Color(.48f,.54f,.60f,.94f), 40);
            else
            {
                Color tint = Color.Lerp(ThemeColors[version - 1], new Color(.54f,.65f,.77f), .65f); tint.a = .65f;
                AddSprite(root, "DistantTreeLeft", scenery[version - 1], new Vector3(-3, 0, 0), 7.5f, tint, -100);
                AddSprite(root, "DistantTreeRight", scenery[version - 1], new Vector3(3, .35f, 0), 6.8f, tint, -101).flipX = true;
            }
        }
        PrefabUtility.SaveAsPrefabAsset(root, PartPath(kind, version));
        UnityEngine.Object.DestroyImmediate(root);
    }

    static SpriteRenderer AddSprite(GameObject root, string name, Sprite sprite, Vector3 position, float height, Color color, int order)
    {
        var child = new GameObject(name); child.layer = root.layer; child.tag = root.tag;
        child.transform.SetParent(root.transform, false); child.transform.localPosition = position;
        child.transform.localScale = Vector3.one * (height / sprite.bounds.size.y);
        var renderer = child.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
        renderer.sharedMaterial = spriteMaterial; renderer.color = color; renderer.sortingOrder = order;
        return renderer;
    }

    static Vector2 AtlasUV(int version, float u, float v)
    {
        int i = version - 1;
        // Exclude the generated two-pixel separators. This only defines UVs; source PNG remains untouched.
        float paddingX = 3f / terrain.width, paddingY = 3f / terrain.height;
        return new Vector2((i % 5) / 5f + paddingX + u * (.2f - paddingX * 2),
            (1 - i / 5) / 2f + paddingY + v * (.5f - paddingY * 2));
    }

    static void BuildTerrain(GameObject root, MapIngredientKind kind, int version)
    {
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        Action<float,float,float,float,float,float,bool> quad = (x0,x1,bottom,top0,top1,depth,capOnly) => {
            float cap = Mathf.Min(.35f, depth);
            float u0 = kind == MapIngredientKind.Stairs ? x0 / 4 : 0;
            float u1 = kind == MapIngredientKind.Stairs ? x1 / 4 : 1;
            if (!capOnly)
                AddQuad(vertices,uv,triangles,version,x0,x1,bottom,bottom,top0-cap,top1-cap,0,.83f,u0,u1);
            AddQuad(vertices,uv,triangles,version,x0,x1,top0-cap,top1-cap,top0,top1,.83f,1,u0,u1);
        };
        switch (kind)
        {
            case MapIngredientKind.Ground:
                quad(0,4,-2,0,0,2,false); AddBox(root,"GroundCollider",new Vector2(2,-1),new Vector2(4,2)); break;
            case MapIngredientKind.Ramp:
                quad(0,4,-2,0,1,2,false);
                // Small contiguous slope sections also support the existing AI's per-collider step probes.
                for (int s = 0; s < 8; s++)
                {
                    var part = TerrainChild(root,"SlopeCollider_" + s);
                    var collider = part.AddComponent<PolygonCollider2D>(); collider.sharedMaterial = terrainPhysics;
                    float a = s * .5f, b = (s + 1) * .5f;
                    collider.points = new[] {new Vector2(a,-2),new Vector2(b,-2),new Vector2(b,b*.25f),new Vector2(a,a*.25f)};
                }
                break;
            case MapIngredientKind.Stairs:
                for (int s = 0; s < 5; s++)
                {
                    float top = (s + 1) * .2f;
                    quad(s*.8f,(s+1)*.8f,-2,top,top,top+2,false);
                    AddBox(root,"Step_" + s,new Vector2((s+.5f)*.8f,(top-2)*.5f),new Vector2(.8f,top+2));
                }
                break;
            case MapIngredientKind.Platform:
                quad(0,3,-.25f,0,0,.25f,true);
                var platform = AddBox(root,"OneWayPlatform",new Vector2(1.5f,-.125f),new Vector2(3,.25f));
                platform.usedByEffector = true;
                var effector = platform.gameObject.AddComponent<PlatformEffector2D>();
                effector.useOneWay = true; effector.useOneWayGrouping = true; effector.surfaceArc = 160;
                effector.useSideFriction = false; effector.useSideBounce = false;
                break;
            case MapIngredientKind.Bridge:
                quad(0,6,-.3f,0,0,.3f,true);
                AddBox(root,"BridgeDeck",new Vector2(3,-.15f),new Vector2(6,.3f));
                AddQuad(vertices,uv,triangles,version,.4f,.7f,-1.7f,-1.7f,-.3f,-.3f,.1f,.75f);
                AddQuad(vertices,uv,triangles,version,5.3f,5.6f,-1.7f,-1.7f,-.3f,-.3f,.1f,.75f);
                break;
            case MapIngredientKind.Wall:
                quad(0,2,0,4,4,4,false); AddBox(root,"WallCollider",new Vector2(1,2),new Vector2(2,4)); break;
        }
        var mesh = new Mesh { name = root.name };
        mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
        mesh.SetColors(Enumerable.Repeat(Color.white,vertices.Count).ToList());
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        string path = Art + "/Meshes/" + root.name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { EditorUtility.CopySerialized(mesh,existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(mesh); }
        else AssetDatabase.CreateAsset(mesh,path);
        var visual = TerrainChild(root,"Visual");
        visual.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = visual.AddComponent<MeshRenderer>(); renderer.sharedMaterial = terrainMaterial;
        renderer.sortingOrder = 0;
    }

    static GameObject TerrainChild(GameObject parent, string name)
    {
        var child = new GameObject(name); child.transform.SetParent(parent.transform, false);
        child.layer = parent.layer; child.tag = parent.tag; return child;
    }

    static BoxCollider2D AddBox(GameObject root,string name,Vector2 center,Vector2 size)
    {
        var child = TerrainChild(root,name); var collider = child.AddComponent<BoxCollider2D>();
        collider.offset = center; collider.size = size; collider.sharedMaterial = terrainPhysics; return collider;
    }

    static void AddQuad(List<Vector3> vertices,List<Vector2> uv,List<int> triangles,int version,
        float x0,float x1,float b0,float b1,float t0,float t1,float uvBottom,float uvTop,float u0=0,float u1=1)
    {
        int start = vertices.Count;
        vertices.Add(new Vector3(x0,b0,0)); vertices.Add(new Vector3(x0,t0,0));
        vertices.Add(new Vector3(x1,t1,0)); vertices.Add(new Vector3(x1,b1,0));
        uv.Add(AtlasUV(version,u0,uvBottom)); uv.Add(AtlasUV(version,u0,uvTop));
        uv.Add(AtlasUV(version,u1,uvTop)); uv.Add(AtlasUV(version,u1,uvBottom));
        triangles.AddRange(new[] {start,start+1,start+2,start,start+2,start+3});
    }

    static RectTransform Rect(Transform parent,string name,Vector2 min,Vector2 max,Vector2 position,Vector2 size)
    {
        var go = new GameObject(name,typeof(RectTransform)); go.transform.SetParent(parent,false);
        var rect = (RectTransform)go.transform; rect.anchorMin=min; rect.anchorMax=max;
        rect.anchoredPosition=position; rect.sizeDelta=size; return rect;
    }

    static Font UIFont => AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/Fonts/PF스타더스트.ttf");

    static Text Label(Transform parent,string name,string value,int size,Vector2 min,Vector2 max,Vector2 position,Vector2 dimensions)
    {
        var rect=Rect(parent,name,min,max,position,dimensions); var text=rect.gameObject.AddComponent<Text>();
        text.font=UIFont; text.fontSize=size; text.text=value; text.color=new Color(.83f,.92f,.97f);
        text.alignment=TextAnchor.MiddleCenter; text.raycastTarget=false; return text;
    }

    static Button Button(Transform parent,string name,string value,Vector2 anchor,Vector2 position,Vector2 size)
    {
        var rect=Rect(parent,name,anchor,anchor,position,size);
        var image=rect.gameObject.AddComponent<Image>(); image.color=new Color(.09f,.17f,.22f,.97f);
        var button=rect.gameObject.AddComponent<Button>(); button.targetGraphic=image;
        button.navigation=new Navigation { mode=Navigation.Mode.None };
        Label(rect,"Label",value,23,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero); return button;
    }

    static void BuildHUD()
    {
        var root=new GameObject("MapMinimapHUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        var canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.overrideSorting=true; canvas.sortingOrder=1000;
        var scaler=root.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080); scaler.matchWidthOrHeight=.5f;
        var script=root.AddComponent<MapMinimapHUD>(); script.previewMaterial=spriteMaterial;
        script.openButton=Button(root.transform,"OpenMap","미니맵 [M]",new Vector2(0,1),new Vector2(150,-123),new Vector2(230,47));
        var panel=Rect(root.transform,"MapPanel",Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
        panel.gameObject.AddComponent<Image>().color=new Color(.01f,.025f,.04f,.96f); script.panel=panel.gameObject;
        Label(panel,"Title","MAP / 전체 지도",31,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-60),new Vector2(800,50));
        script.closeButton=Button(panel,"CloseMap","닫기 [ESC]",new Vector2(1,1),new Vector2(-170,-62),new Vector2(230,49));
        var frame=Rect(panel,"MapFrame",new Vector2(.06f,.13f),new Vector2(.94f,.86f),Vector2.zero,Vector2.zero);
        frame.gameObject.AddComponent<Image>().color=new Color(.34f,.53f,.63f);
        var view=Rect(frame,"MapImage",Vector2.zero,Vector2.one,Vector2.zero,new Vector2(-8,-8));
        script.mapImage=view.gameObject.AddComponent<RawImage>(); script.mapImage.color=Color.white; script.mapImage.raycastTarget=false;
        var marker=Rect(view,"PlayerMarker",new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(12,12));
        var markerImage=marker.gameObject.AddComponent<Image>(); markerImage.color=new Color(.15f,1,.95f); markerImage.raycastTarget=false;
        script.playerMarker=marker;
        script.statusText=Label(panel,"Status","전체 맵 · 땅 / 뒷배경 · 자동 맞춤",24,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,94),new Vector2(1500,40));
        Label(panel,"Legend","■ 청록: 플레이어     M / ESC: 닫기     앞배경·먼 배경·장식은 표시하지 않습니다",21,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,53),new Vector2(1500,34));
        panel.gameObject.SetActive(false);
        PrefabUtility.SaveAsPrefabAsset(root,HudPath); UnityEngine.Object.DestroyImmediate(root);
    }
}
