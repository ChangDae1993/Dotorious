using System;
using System.Collections.Generic;
using System.IO;
using JYW.Game.ObjectMaker.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace JYW.Game.ObjectMaker.Tests
{
    public sealed class MapPaintTests
    {
        readonly List<Object> owned = new List<Object>();
        readonly List<Scene> scenes = new List<Scene>();
        string folder;
        T Own<T>(T obj) where T : Object { owned.Add(obj); return obj; }
        MapPaintDocument Document(int w = 16, int h = 16)
        {
            var doc = new MapPaintDocument { width = w, height = h, pixelsPerUnit = 1, brushDiameter = 2 };
            doc.EnsureValid(); return doc;
        }
        void Terrain(MapPaintDocument doc)
        {
            var texture = Own(new Texture2D(2, 1, TextureFormat.RGBA32, false));
            texture.SetPixels32(new[] { new Color32(255, 0, 0, 255), new Color32(0, 0, 255, 128) }); texture.Apply();
            doc.terrainPng = texture.EncodeToPNG();
        }
        string OutputFolder()
        {
            if (folder == null) { folder = "Assets/__MapPaintTests_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder)); }
            return folder;
        }
        [TearDown] public void Cleanup()
        {
            foreach (var scene in scenes) if (scene.IsValid() && scene.isLoaded)
            { if (EditorSceneManager.IsPreviewScene(scene)) EditorSceneManager.ClosePreviewScene(scene); else EditorSceneManager.CloseScene(scene, true); }
            scenes.Clear();
            foreach (var obj in owned) if (obj) Object.DestroyImmediate(obj);
            owned.Clear();
            if (folder != null) AssetDatabase.DeleteAsset(folder);
            folder = null;
        }
        [Test] public void StrokeHasNoGapsAndEraseClearsOnlySelectedPixels()
        {
            var doc = Document(128, 16); doc.PaintStroke(new Vector2(1, 8), new Vector2(127, 8));
            for (int x = 1; x < 127; x++) Assert.AreEqual(255, doc.mask[8 * doc.width + x]);
            doc.PaintStroke(new Vector2(64, 8), new Vector2(64, 8), true);
            Assert.AreEqual(0, doc.mask[8 * doc.width + 64]); Assert.AreEqual(255, doc.mask[8 * doc.width + 20]);
        }
        [Test] public void ResizePreservesBottomLeftAndCropsWithoutWrappingRows()
        {
            var doc = Document(4, 4); doc.mask[1 + 4] = 255; doc.mask[15] = 255;
            doc.Resize(2, 3); Assert.AreEqual(255, doc.mask[3]); Assert.AreEqual(6, doc.mask.Length);
            doc.Resize(6, 6); Assert.AreEqual(255, doc.mask[7]); Assert.AreEqual(0, doc.mask[35]);
        }
        [Test] public void RectangleAndEllipseClipToCanvas()
        {
            var doc = Document(8, 8); doc.FillRectangle(new Vector2(-4, -4), new Vector2(8, 8));
            foreach (byte b in doc.mask) Assert.AreEqual(255, b);
            doc.Clear(); doc.FillEllipse(Vector2.zero, new Vector2(8, 8));
            Assert.AreEqual(0, doc.mask[0]); Assert.AreEqual(255, doc.mask[4 * 8 + 4]);
        }
        [Test] public void TilingUsesNativeSizeAndScaleAndPreservesSourceAlpha()
        {
            var doc = Document(8, 2); Terrain(doc); doc.FillRectangle(Vector2.zero, new Vector2(8, 2));
            var native = Own(doc.CreateTexture());
            Assert.AreEqual(native.GetPixel(0, 0), native.GetPixel(2, 0)); Assert.AreNotEqual(native.GetPixel(0, 0), native.GetPixel(1, 0));
            Assert.That(native.GetPixel(1, 0).a, Is.EqualTo(128f / 255).Within(.005));
            doc.tileScale = 2; var scaled = Own(doc.CreateTexture());
            Assert.AreEqual(scaled.GetPixel(0, 0), scaled.GetPixel(1, 0)); Assert.AreNotEqual(scaled.GetPixel(1, 0), scaled.GetPixel(2, 0));
            doc.tileScale = 1; doc.tileWidth = 4; doc.tileHeight = 2; var sized = Own(doc.CreateTexture());
            Assert.AreEqual(scaled.GetPixel(1, 0), sized.GetPixel(1, 0));
        }
        [Test] public void FitWidthLeavesCenteredTransparentBandsWhileStretchFills()
        {
            var doc = Document(8, 8); Terrain(doc); doc.FillRectangle(Vector2.zero, new Vector2(8, 8));
            doc.textureMode = MapTextureMode.FitWidth; var fit = Own(doc.CreateTexture());
            Assert.AreEqual(0, fit.GetPixel(4, 0).a); Assert.Greater(fit.GetPixel(4, 3).a, 0); Assert.AreEqual(0, fit.GetPixel(4, 7).a);
            doc.textureMode = MapTextureMode.Stretch; var stretch = Own(doc.CreateTexture()); Assert.Greater(stretch.GetPixel(4, 0).a, 0);
            doc.textureMode = MapTextureMode.FitHeight; var crop = Own(doc.CreateTexture()); Assert.Greater(crop.GetPixel(0, 0).a, 0);
        }
        [Test] public void PreviewCanShowUntexturedGroundButExportRequiresTexture()
        {
            var doc = Document(); doc.mask[0] = 255;
            var texture = Own(doc.CreateTexture()); Assert.AreEqual(1, texture.GetPixel(0, 0).a); Assert.AreEqual(0, texture.GetPixel(1, 0).a);
            Assert.Throws<InvalidOperationException>(() => MapSceneExporter.Export(doc, OutputFolder()));
        }
        [Test] public void InvalidDimensionsAndCorruptMasksAreRejected()
        {
            var doc = Document(); Assert.Throws<ArgumentOutOfRangeException>(() => doc.Resize(4097, 2));
            doc.mask = new byte[3]; Assert.Throws<ArgumentException>(() => doc.EnsureValid());
            doc = Document(); doc.tileScale = float.NaN; Assert.Throws<ArgumentException>(() => doc.EnsureValid());
            doc = Document(); doc.brushDiameter = float.PositiveInfinity; Assert.Throws<ArgumentException>(() => doc.EnsureValid());
        }
        [Test] public void TinyOrOverflowingTileSizesFailBeforeRendering()
        {
            var doc = Document(); doc.tileWidth = .0001f;
            Assert.Throws<ArgumentException>(() => doc.EnsureValid());
            doc = Document(); doc.tileScale = .0001f;
            Assert.Throws<ArgumentException>(() => doc.EnsureValid());
            doc = Document(); doc.tileHeight = float.MaxValue; doc.tileScale = 2;
            Assert.Throws<ArgumentException>(() => doc.EnsureValid());
            doc = Document(); doc.tileWidth = .01f; doc.tileScale = 1;
            Assert.DoesNotThrow(() => doc.EnsureValid());
        }
        [Test] public void CollisionRectanglesCoverExactlyTheMaskIncludingHolesAndIslands()
        {
            var doc = Document(12, 12); doc.FillRectangle(new Vector2(1, 1), new Vector2(9, 9));
            doc.FillRectangle(new Vector2(3, 3), new Vector2(7, 7), true); doc.mask[10 * 12 + 10] = 255;
            var rectangles = MapSceneExporter.BuildCollisionRectangles(doc);
            for (int y = 0; y < 12; y++) for (int x = 0; x < 12; x++)
            {
                int count = 0; foreach (var r in rectangles) if (r.Contains(new Vector2Int(x, y))) count++;
                Assert.AreEqual(doc.mask[y * 12 + x] == 255 ? 1 : 0, count, "Pixel " + x + "," + y);
            }
            Assert.Less(rectangles.Count, 10);
        }
        [Test] public void CollisionComplexityGuardRejectsSpeckles()
        {
            var doc = Document(202, 202);
            for (int y = 0; y < 202; y++) for (int x = 0; x < 202; x++) if ((x + y) % 2 == 0) doc.mask[y * 202 + x] = 255;
            Assert.Throws<InvalidOperationException>(() => MapSceneExporter.BuildCollisionRectangles(doc));
        }
        [Test] public void ExportIsUniquePortableAndPreservesSceneAndExactCollision()
        {
            var doc = Document(12, 12); Terrain(doc); doc.mapName = "Test Terrain";
            doc.FillRectangle(new Vector2(1, 1), new Vector2(9, 9)); doc.FillRectangle(new Vector2(3, 3), new Vector2(7, 7), true); doc.mask[10 * 12 + 10] = 255;
            Scene original = SceneManager.GetActiveScene(); bool dirty = original.isDirty; int rootCount = original.rootCount;
            var result = MapSceneExporter.Export(doc, OutputFolder());
            Assert.AreEqual(original, SceneManager.GetActiveScene()); Assert.AreEqual(dirty, original.isDirty); Assert.AreEqual(rootCount, original.rootCount);
            var second = MapSceneExporter.Export(doc, OutputFolder()); Assert.AreNotEqual(result.ScenePath, second.ScenePath);
            Assert.AreEqual("Test Terrain.unity", Path.GetFileName(result.ScenePath));
            var scene = EditorSceneManager.OpenPreviewScene(result.ScenePath); scenes.Add(scene);
            Assert.AreEqual(1, scene.rootCount); var root = scene.GetRootGameObjects()[0];
            Assert.AreEqual(0, root.transform.childCount); Assert.AreEqual(15, root.layer);
            Assert.IsNotNull(root.GetComponent<SpriteRenderer>().sprite); Assert.AreEqual(RigidbodyType2D.Static, root.GetComponent<Rigidbody2D>().bodyType);

            var destination = EditorSceneManager.NewPreviewScene(); scenes.Add(destination);
            var copy = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath), destination);
            EditorSceneManager.ClosePreviewScene(scene);
            Assert.IsNotNull(copy.GetComponent<SpriteRenderer>().sprite.texture);
            Physics2D.SyncTransforms(); var collider = copy.GetComponent<CompositeCollider2D>();
            Assert.IsTrue(collider.OverlapPoint(new Vector2(-4.5f, -4.5f)), "ring");
            Assert.IsFalse(collider.OverlapPoint(new Vector2(-1.5f, -1.5f)), "hole");
            Assert.IsTrue(collider.OverlapPoint(new Vector2(4.5f, 4.5f)), "island");
        }
        [Test] public void ExportedTerrainStopsDynamicBodyInIsolatedPhysicsScene()
        {
            var doc = Document(16, 8); Terrain(doc); doc.FillRectangle(Vector2.zero, new Vector2(16, 2));
            var result = MapSceneExporter.Export(doc, OutputFolder());
            var simulation = EditorSceneManager.NewPreviewScene(); scenes.Add(simulation);
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath), simulation);
            var ball = EditorUtility.CreateGameObjectWithHideFlags("Map test ball", HideFlags.HideAndDontSave); SceneManager.MoveGameObjectToScene(ball, simulation); ball.hideFlags = HideFlags.None;
            ball.transform.position = new Vector3(0, 2, 0); ball.AddComponent<CircleCollider2D>().radius = .25f;
            var body = ball.AddComponent<Rigidbody2D>(); body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var physics = simulation.GetPhysicsScene2D();
            Assert.IsTrue(physics.IsValid(), "Preview scene must provide physics.");
            Assert.AreNotEqual(Physics2D.defaultPhysicsScene, physics, "Never simulate the user scene during an editor test.");
            Physics2D.SyncTransforms(); for (int i = 0; i < 180; i++) physics.Simulate(1f / 60);
            Assert.That(ball.transform.position.y, Is.InRange(-1.8f, -1.65f)); Assert.Less(Mathf.Abs(body.linearVelocity.y), .1f);
        }
        [Test] public void ExternalPngAndJpegAreEmbeddedWithoutProjectImport()
        {
            var doc = Document(8, 4); Terrain(doc);
            string png = Path.Combine(Path.GetTempPath(), "MapPaint_" + Guid.NewGuid().ToString("N") + ".png");
            string jpg = Path.ChangeExtension(png, ".jpg");
            try
            {
                File.WriteAllBytes(png, doc.terrainPng); doc.LoadTerrainFile(png);
                var source = Own(new Texture2D(2, 2)); source.SetPixels(new[] { Color.green, Color.green, Color.green, Color.green }); source.Apply();
                File.WriteAllBytes(jpg, source.EncodeToJPG()); doc.LoadTerrainFile(jpg);
                Assert.AreEqual(137, doc.terrainPng[0]); Assert.AreEqual(80, doc.terrainPng[1]);
                Assert.IsNull(AssetImporter.GetAtPath(png)); Assert.IsNull(AssetImporter.GetAtPath(jpg));
            }
            finally { if (File.Exists(png)) File.Delete(png); if (File.Exists(jpg)) File.Delete(jpg); }
        }
        [Test] public void GpuReadbackSupportsNonReadableTexturesAndSpriteRect()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("GPU readback requires a graphics device.");
            var source = Own(new Texture2D(4, 2, TextureFormat.RGBA32, false));
            source.SetPixels(new[] { Color.red, Color.red, Color.blue, Color.blue, Color.red, Color.red, Color.blue, Color.blue }); source.Apply(false, true);
            Assert.IsFalse(source.isReadable);
            var doc = Document(4, 2); doc.SetTerrain(source); Assert.IsFalse(source.isReadable);
            var sprite = Own(Sprite.Create(source, new Rect(2, 0, 2, 2), Vector2.one * .5f)); doc.SetTerrain(sprite);
            doc.FillRectangle(Vector2.zero, new Vector2(4, 2)); var rendered = Own(doc.CreateTexture());
            Assert.Greater(rendered.GetPixel(0, 0).b, .9f); Assert.Less(rendered.GetPixel(0, 0).r, .1f);
        }
        [Test] public void JsonRoundTripKeepsEmbeddedImageAndMask()
        {
            var doc = Document(8, 4); Terrain(doc); doc.mask[10] = 255; doc.mapName = "Portable";
            var restored = JsonUtility.FromJson<MapPaintDocument>(JsonUtility.ToJson(doc)); restored.EnsureValid();
            CollectionAssert.AreEqual(doc.mask, restored.mask); CollectionAssert.AreEqual(doc.terrainPng, restored.terrainPng);
            var texture = Own(restored.CreateTexture()); Assert.Greater(texture.GetPixel(2, 1).a, 0);
        }
    }
}




