using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JYW.Game.ObjectMaker.Editor
{
    public sealed class MapExportResult
    {
        public string ScenePath { get; internal set; }
        public string PrefabPath { get; internal set; }
        public string TexturePath { get; internal set; }
    }

    public static class MapSceneExporter
    {
        public const int MaxColliderRectangles = 10000;

        /// <summary>Exact occupied-pixel rectangles, merged vertically. Holes and disconnected islands are preserved.</summary>
        public static List<RectInt> BuildCollisionRectangles(MapPaintDocument document)
        {
            document.EnsureValid();
            var result = new List<RectInt>();
            var previous = new Dictionary<Vector2Int, int>();
            for (int y = 0; y < document.height; y++)
            {
                var current = new Dictionary<Vector2Int, int>();
                int x = 0;
                while (x < document.width)
                {
                    if (document.mask[y * document.width + x] < 128) { x++; continue; }
                    int start = x++;
                    while (x < document.width && document.mask[y * document.width + x] >= 128) x++;
                    var key = new Vector2Int(start, x - start);
                    int index;
                    if (previous.TryGetValue(key, out index))
                    {
                        var rectangle = result[index]; rectangle.height++; result[index] = rectangle;
                    }
                    else
                    {
                        if (result.Count >= MaxColliderRectangles)
                            throw new InvalidOperationException("The mask is too detailed for collision (more than 10,000 rectangles). Erase tiny speckles or use larger filled shapes, then Make again.");
                        index = result.Count; result.Add(new RectInt(start, y, x - start, 1));
                    }
                    current.Add(key, index);
                }
                previous = current;
            }
            return result;
        }

        public static MapExportResult Export(MapPaintDocument document, string outputRoot = "Assets/GeneratedMaps")
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            document.EnsureValid();
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before making a map scene.");
            if (document.terrainPng == null || document.terrainPng.Length == 0) throw new InvalidOperationException("Choose a terrain image before making the scene.");
            var rectangles = BuildCollisionRectangles(document);
            if (rectangles.Count == 0) throw new InvalidOperationException("Paint some ground before making the scene.");
            string safeName = SafeName(document.mapName);
            outputRoot = (outputRoot ?? "").Replace('\\', '/').TrimEnd('/');
            if (outputRoot != "Assets" && !outputRoot.StartsWith("Assets/", StringComparison.Ordinal)) throw new ArgumentException("Output must be inside Assets.");
            foreach (string part in outputRoot.Split('/'))
                if (string.IsNullOrWhiteSpace(part) || part == "." || part == ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new ArgumentException("Output must be a normal folder path inside Assets.");
            Texture2D baked = document.CreateTexture();
            Scene temporaryScene = default;
            Scene activeScene = SceneManager.GetActiveScene();
            string folder = null;
            bool complete = false;
            try
            {
                EnsureFolder(outputRoot);
                folder = AssetDatabase.GenerateUniqueAssetPath(outputRoot + "/" + safeName);
                string guid = AssetDatabase.CreateFolder(outputRoot, Path.GetFileName(folder));
                if (string.IsNullOrEmpty(guid)) throw new IOException("Could not create map output folder.");
                var result = new MapExportResult
                {
                    ScenePath = folder + "/" + safeName + ".unity",
                    PrefabPath = folder + "/" + safeName + ".prefab",
                    TexturePath = folder + "/Terrain.png"
                };
                File.WriteAllBytes(result.TexturePath, baked.EncodeToPNG());
                AssetDatabase.ImportAsset(result.TexturePath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(result.TexturePath);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = document.pixelsPerUnit; importer.spritePivot = new Vector2(.5f, .5f);
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Center; settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = MapPaintDocument.MaxDimension;
                importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp; importer.SaveAndReimport();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(result.TexturePath);
                if (!sprite) throw new InvalidOperationException("Could not import the baked terrain sprite.");
                temporaryScene = EditorSceneManager.NewPreviewScene();
                // Hidden temporary construction avoids modifying or dirtying the user's active scene.
                var terrain = EditorUtility.CreateGameObjectWithHideFlags(safeName, HideFlags.HideAndDontSave);
                SceneManager.MoveGameObjectToScene(terrain, temporaryScene);
                terrain.hideFlags = HideFlags.None; terrain.layer = 15;
                terrain.AddComponent<SpriteRenderer>().sprite = sprite;
                var body = terrain.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Static;
                var composite = terrain.AddComponent<CompositeCollider2D>();
                composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
                composite.generationType = CompositeCollider2D.GenerationType.Manual;
                composite.vertexDistance = Mathf.Min(.0001f, .01f / document.pixelsPerUnit);
                composite.offsetDistance = 0;
                foreach (var rect in rectangles)
                {
                    var box = terrain.AddComponent<BoxCollider2D>();
                    box.size = new Vector2(rect.width, rect.height) / document.pixelsPerUnit;
                    box.offset = (rect.center - new Vector2(document.width * .5f, document.height * .5f)) / document.pixelsPerUnit;
                    box.compositeOperation = Collider2D.CompositeOperation.Merge;
                }
                composite.GenerateGeometry();
                if (composite.pathCount == 0) throw new InvalidOperationException("The ground mask did not produce usable collision. Increase the painted area or adjust Pixels Per Unit.");
                var prefab = PrefabUtility.SaveAsPrefabAsset(terrain, result.PrefabPath);
                if (!prefab) throw new IOException("Could not save the terrain prefab.");
                WritePrefabScene(prefab, result.ScenePath);
                complete = true; return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
                if (temporaryScene.IsValid() && temporaryScene.isLoaded) EditorSceneManager.ClosePreviewScene(temporaryScene);
                if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
                if (!complete && folder != null && AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }
        }
        // A scene containing one prefab instance is the standard Unity serialized form. Writing this small
        // wrapper avoids opening/replacing/saving an untitled user scene, and keeps all geometry in the prefab.
        // Import and reopen through Unity immediately, rather than trusting the serialized output unchecked.
        static void WritePrefabScene(GameObject prefab, string path)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab.transform, out string guid, out long transformId))
                throw new IOException("Could not resolve the saved terrain prefab reference.");
            string yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!1001 &1000\nPrefabInstance:\n  m_ObjectHideFlags: 0\n  serializedVersion: 2\n" +
                "  m_Modification:\n    serializedVersion: 3\n    m_TransformParent: {fileID: 0}\n" +
                "    m_Modifications: []\n    m_RemovedComponents: []\n    m_RemovedGameObjects: []\n    m_AddedGameObjects: []\n    m_AddedComponents: []\n" +
                "  m_SourcePrefab: {fileID: 100100000, guid: " + guid + ", type: 3}\n" +
                "--- !u!4 &1001 stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: " + transformId + ", guid: " + guid + ", type: 3}\n" +
                "  m_PrefabInstance: {fileID: 1000}\n  m_PrefabAsset: {fileID: 0}\n" +
                "--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_ObjectHideFlags: 0\n  m_Roots:\n  - {fileID: 1001}\n";
            File.WriteAllText(path, yaml, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var check = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var roots = check.GetRootGameObjects();
                if (roots.Length != 1 || roots[0].name != prefab.name || !roots[0].GetComponent<SpriteRenderer>() ||
                    !roots[0].GetComponent<SpriteRenderer>().sprite || !roots[0].GetComponent<CompositeCollider2D>())
                    throw new IOException("Generated scene did not reopen with its terrain and collision intact.");
            }
            finally { EditorSceneManager.ClosePreviewScene(check); }
        }
        static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a map name.");
            name = name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.TrimEnd('.', ' ');
            if (name.Length == 0 || name == "." || name == "..") throw new ArgumentException("Enter a valid map name.");
            if (name.Length > 80) name = name.Substring(0, 80);
            string stem = name.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '1' && stem[3] <= '9')) name = "Map_" + name;
            return name;
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/')); EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path)))) throw new IOException("Could not create " + path);
        }
    }
}




