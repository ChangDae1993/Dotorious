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
        public const float MinExportPixelsPerUnit = 1, MaxExportPixelsPerUnit = 100;

        /// <summary>Exact visible-pixel rectangles, merged vertically. Holes and disconnected islands are preserved.</summary>
        public static List<RectInt> BuildCollisionRectangles(MapPaintDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return BuildCollisionRectangles(document.width, document.height, document.GetVisibleMask());
        }

        static List<RectInt> BuildCollisionRectangles(int width, int height, byte[] mask)
        {
            var result = new List<RectInt>();
            var previous = new Dictionary<Vector2Int, int>();
            for (int y = 0; y < height; y++)
            {
                var current = new Dictionary<Vector2Int, int>();
                int x = 0;
                while (x < width)
                {
                    if (mask[y * width + x] == 0) { x++; continue; }
                    int start = x++;
                    while (x < width && mask[y * width + x] != 0) x++;
                    var key = new Vector2Int(start, x - start);
                    int index;
                    if (previous.TryGetValue(key, out index))
                    {
                        var rectangle = result[index]; rectangle.height++; result[index] = rectangle;
                    }
                    else
                    {
                        if (result.Count >= MaxColliderRectangles)
                            throw new InvalidOperationException("The visible terrain is too detailed for collision (more than 10,000 regions). Erase tiny speckles or use larger filled shapes, then Make again.");
                        index = result.Count; result.Add(new RectInt(start, y, x - start, 1));
                    }
                    current.Add(key, index);
                }
                previous = current;
            }
            return result;
        }

        /// <summary>
        /// Exact collision boundaries in bottom-left canvas pixels, for editor previews. Outer loops run
        /// counterclockwise and holes clockwise. The closing point is not repeated. Only collinear points
        /// are removed: one-pixel channels and diagonal-touching islands are never smoothed or joined.
        /// </summary>
        public static List<Vector2[]> BuildCollisionContours(MapPaintDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            byte[] mask = document.GetVisibleMask();
            // Share the export complexity guard before allocating/tracing potentially noisy image edges.
            BuildCollisionRectangles(document.width, document.height, mask);
            int width = document.width, height = document.height;
            var visited = new byte[mask.Length];
            var contours = new List<Vector2[]>();
            var steps = new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                if (mask[y * width + x] == 0) continue;
                for (int edge = 0; edge < 4; edge++)
                {
                    if ((visited[y * width + x] & (1 << edge)) != 0) continue;
                    var start = new Vector2Int(x + (edge == 1 || edge == 2 ? 1 : 0), y + (edge >= 2 ? 1 : 0));
                    if (!IsBoundary(start, edge, mask, width, height)) continue;
                    var points = new List<Vector2>();
                    var vertex = start;
                    int direction = edge;
                    points.Add(vertex);
                    do
                    {
                        Vector2Int cell = InsideCell(vertex, direction);
                        int index = cell.y * width + cell.x;
                        if ((visited[index] & (1 << direction)) != 0)
                            throw new InvalidOperationException("The terrain boundary could not be traced safely.");
                        visited[index] |= (byte)(1 << direction);
                        Vector2Int next = vertex + steps[direction];
                        // At a diagonal touch there are two possible exits. Keep solid pixels to the left
                        // and take the left turn first, so separate islands do not become a crossing loop.
                        int nextDirection = (direction + 1) % 4;
                        int attempts = 0;
                        while (!IsBoundary(next, nextDirection, mask, width, height))
                        {
                            if (++attempts == 4) throw new InvalidOperationException("The terrain boundary is not closed.");
                            nextDirection = (nextDirection + 3) % 4;
                        }
                        if (next != start && nextDirection != direction) points.Add(next);
                        vertex = next; direction = nextDirection;
                    }
                    while (vertex != start || direction != edge);
                    // A scan can begin midway along a straight boundary; omit that redundant start point.
                    if (points.Count > 3 && Collinear(points[points.Count - 1], points[0], points[1])) points.RemoveAt(0);
                    contours.Add(points.ToArray());
                }
            }
            return contours;
        }

        static bool Collinear(Vector2 a, Vector2 b, Vector2 c) =>
            (b.x - a.x) * (c.y - b.y) == (b.y - a.y) * (c.x - b.x);
        static Vector2Int InsideCell(Vector2Int vertex, int direction) => new Vector2Int(
            vertex.x - (direction == 1 || direction == 2 ? 1 : 0), vertex.y - (direction >= 2 ? 1 : 0));
        static bool IsBoundary(Vector2Int vertex, int direction, byte[] mask, int width, int height)
        {
            Vector2Int cell = InsideCell(vertex, direction);
            if (!Occupied(cell.x, cell.y, mask, width, height)) return false;
            int x = cell.x + (direction == 1 ? 1 : direction == 3 ? -1 : 0);
            int y = cell.y + (direction == 0 ? -1 : direction == 2 ? 1 : 0);
            return !Occupied(x, y, mask, width, height);
        }
        static bool Occupied(int x, int y, byte[] mask, int width, int height) =>
            x >= 0 && y >= 0 && x < width && y < height && mask[y * width + x] != 0;

        static byte[] VisibleMask(Texture2D baked, int alphaThreshold)
        {
            var pixels = baked.GetPixels32();
            var mask = new byte[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) if (pixels[i].a >= alphaThreshold) mask[i] = 255;
            return mask;
        }
        public static MapExportResult Export(MapPaintDocument document, string outputRoot = "Assets/GeneratedMaps")
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            document.EnsureValid();
            // This is the editor's tested support range, not a universal Unity physics limit. Keep
            // older drafts editable/previewable, but never silently export unreliable tiny collision.
            if (document.pixelsPerUnit < MinExportPixelsPerUnit || document.pixelsPerUnit > MaxExportPixelsPerUnit)
                throw new InvalidOperationException("Map export supports Pixels Per Unit from 1 to 100 for validated pixel-accurate collision. Change Pixels Per Unit to this range before Make; the draft can still be previewed.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before making a map scene.");
            if (document.terrainPng == null || document.terrainPng.Length == 0) throw new InvalidOperationException("Choose a terrain image before making the scene.");
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
                var rectangles = BuildCollisionRectangles(document.width, document.height, VisibleMask(baked, document.alphaThreshold));
                if (rectangles.Count == 0) throw new InvalidOperationException("No visible ground remains at the current alpha threshold. Paint ground or choose an opaque terrain image before making the scene.");
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
                composite.useDelaunayMesh = true;
                // One source component replaces thousands of BoxCollider2D components. Each path is
                // an occupied rectangle, not a contour filled across its holes. The composite unions
                // touching rectangles into clean boundaries while retaining channels and cavities.
                var source = terrain.AddComponent<PolygonCollider2D>();
                source.autoTiling = false;
                source.compositeOperation = Collider2D.CompositeOperation.Merge;
                source.pathCount = rectangles.Count;
                var origin = new Vector2(document.width * .5f, document.height * .5f);
                for (int i = 0; i < rectangles.Count; i++)
                {
                    var rect = rectangles[i];
                    source.SetPath(i, new[]
                    {
                        (new Vector2(rect.xMin, rect.yMin) - origin) / document.pixelsPerUnit,
                        (new Vector2(rect.xMax, rect.yMin) - origin) / document.pixelsPerUnit,
                        (new Vector2(rect.xMax, rect.yMax) - origin) / document.pixelsPerUnit,
                        (new Vector2(rect.xMin, rect.yMax) - origin) / document.pixelsPerUnit
                    });
                }
                composite.GenerateGeometry();
                if (composite.pathCount == 0 || composite.shapeCount == 0)
                    throw new InvalidOperationException("The visible terrain did not produce usable collision. Paint larger shapes or reduce Pixels Per Unit.");
                // Build once while assigning the source, then let ordinary prefab transform edits and
                // scene reloads regenerate the union rather than leaving stale manual geometry.
                composite.generationType = CompositeCollider2D.GenerationType.Synchronous;
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
                    !roots[0].GetComponent<SpriteRenderer>().sprite || !roots[0].GetComponent<CompositeCollider2D>() || roots[0].GetComponent<CompositeCollider2D>().shapeCount == 0)
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




