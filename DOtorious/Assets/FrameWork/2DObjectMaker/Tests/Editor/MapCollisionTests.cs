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
    public sealed class MapCollisionTests
    {
        readonly List<Scene> scenes = new List<Scene>();
        string folder;

        static MapPaintDocument Document(int width, int height, float pixelsPerUnit = 1)
        {
            var document = new MapPaintDocument { width = width, height = height, pixelsPerUnit = pixelsPerUnit };
            document.EnsureValid();
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try { texture.SetPixel(0, 0, Color.white); texture.Apply(); document.terrainPng = texture.EncodeToPNG(); }
            finally { Object.DestroyImmediate(texture); }
            return document;
        }

        static void SetAlphaImage(MapPaintDocument document, params byte[] alpha)
        {
            var texture = new Texture2D(alpha.Length, 1, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[alpha.Length];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, alpha[i]);
                texture.SetPixels32(pixels); texture.Apply(); document.terrainPng = texture.EncodeToPNG();
            }
            finally { Object.DestroyImmediate(texture); }
        }

        MapExportResult Export(MapPaintDocument document)
        {
            if (folder == null)
            {
                folder = "Assets/__MapCollisionTests_" + Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            }
            return MapSceneExporter.Export(document, folder);
        }

        Scene Preview()
        {
            var scene = EditorSceneManager.NewPreviewScene(); scenes.Add(scene); return scene;
        }

        GameObject LoadPrefab(MapExportResult result, Scene scene)
        {
            AssetDatabase.ImportAsset(result.PrefabPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath), scene);
        }

        [TearDown] public void Cleanup()
        {
            foreach (var scene in scenes) if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
            scenes.Clear();
            if (folder != null) AssetDatabase.DeleteAsset(folder);
            folder = null;
        }

        static float SignedArea(Vector2[] path)
        {
            float sum = 0;
            for (int i = 0; i < path.Length; i++)
            {
                var a = path[i]; var b = path[(i + 1) % path.Length]; sum += a.x * b.y - b.x * a.y;
            }
            return sum * .5f;
        }

        static bool Inside(Vector2 point, Vector2[] path)
        {
            bool inside = false;
            for (int i = 0, j = path.Length - 1; i < path.Length; j = i++)
            {
                var a = path[i]; var b = path[j];
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        static void AssertContoursMatchEveryPixel(MapPaintDocument document)
        {
            var contours = MapSceneExporter.BuildCollisionContours(document);
            var visible = document.GetVisibleMask();
            float area = 0;
            int occupied = 0;
            foreach (var path in contours)
            {
                Assert.GreaterOrEqual(path.Length, 4);
                Assert.AreNotEqual(path[0], path[path.Length - 1], "The closing vertex should not be duplicated.");
                area += SignedArea(path);
                for (int i = 0; i < path.Length; i++)
                {
                    var previous = path[(i + path.Length - 1) % path.Length];
                    var current = path[i]; var next = path[(i + 1) % path.Length];
                    Assert.IsFalse(previous.x == current.x && current.x == next.x || previous.y == current.y && current.y == next.y,
                        "Straight-line vertices should be collapsed.");
                }
            }
            for (int y = 0; y < document.height; y++) for (int x = 0; x < document.width; x++)
            {
                bool inside = false;
                foreach (var path in contours) if (Inside(new Vector2(x + .5f, y + .5f), path)) inside = !inside;
                Assert.AreEqual(visible[y * document.width + x] != 0, inside, "Contour pixel " + x + "," + y);
                if (inside) occupied++;
            }
            Assert.AreEqual(occupied, area, "Outer and hole winding must preserve exact filled area.");
        }

        static Vector2 PixelCenter(MapPaintDocument document, int x, int y) =>
            new Vector2(x + .5f - document.width * .5f, y + .5f - document.height * .5f) / document.pixelsPerUnit;

        static void AssertColliderMatchesEveryPixel(MapPaintDocument document, GameObject terrain)
        {
            Physics2D.SyncTransforms();
            var collider = terrain.GetComponent<CompositeCollider2D>();
            Assert.IsNotNull(collider);
            Assert.AreEqual(0, terrain.GetComponents<BoxCollider2D>().Length, "Do not emit a component for every scanline strip.");
            Assert.AreEqual(1, terrain.GetComponents<PolygonCollider2D>().Length);
            Assert.AreEqual(1, terrain.GetComponents<CompositeCollider2D>().Length);
            Assert.AreEqual(2, terrain.GetComponents<Collider2D>().Length);
            var visible = document.GetVisibleMask();
            for (int y = 0; y < document.height; y++) for (int x = 0; x < document.width; x++)
                Assert.AreEqual(visible[y * document.width + x] != 0, collider.OverlapPoint(terrain.transform.TransformPoint(PixelCenter(document, x, y))),
                    "Collider pixel " + x + "," + y + " at PPU " + document.pixelsPerUnit);
        }

        [Test] public void RectanglePreviewHasOnlyFourCornersInPixelCoordinates()
        {
            var document = Document(12, 8, 100);
            document.FillRectangle(new Vector2(2, 1), new Vector2(10, 6));
            var contours = MapSceneExporter.BuildCollisionContours(document);
            Assert.AreEqual(1, contours.Count); Assert.AreEqual(4, contours[0].Length);
            Assert.AreEqual(40, SignedArea(contours[0]));
            CollectionAssert.AreEquivalent(new[] { new Vector2(2, 1), new Vector2(10, 1), new Vector2(10, 6), new Vector2(2, 6) }, contours[0]);
        }

        [Test] public void ConcaveContoursRetainHoleIslandAndOnePixelChannel()
        {
            var document = Document(16, 14);
            document.FillRectangle(new Vector2(1, 1), new Vector2(13, 12));
            document.FillRectangle(new Vector2(3, 3), new Vector2(9, 9), true);
            document.FillRectangle(new Vector2(10, 6), new Vector2(13, 7), true);
            document.mask[5 * 16 + 5] = 255;
            document.mask[12 * 16 + 14] = 255;
            AssertContoursMatchEveryPixel(document);
        }

        [Test] public void DiagonalTouchingPixelsRemainSeparateSimpleLoops()
        {
            var document = Document(8, 8);
            for (int i = 0; i < 8; i++) document.mask[i * 8 + i] = 255;
            var paths = MapSceneExporter.BuildCollisionContours(document);
            Assert.AreEqual(8, paths.Count);
            foreach (var path in paths) { Assert.AreEqual(4, path.Length); Assert.AreEqual(1, SignedArea(path)); }
            AssertContoursMatchEveryPixel(document);
        }

        [Test] public void RandomSmallMasksKeepExactContourCoverageAndWinding()
        {
            var document = Document(9, 9); var random = new System.Random(7213);
            for (int sample = 0; sample < 30; sample++)
            {
                for (int i = 0; i < document.mask.Length; i++) document.mask[i] = random.Next(3) == 0 ? (byte)255 : (byte)0;
                AssertContoursMatchEveryPixel(document);
            }
        }

        [Test] public void FitWidthTransparentMarginsDoNotCreateInvisibleCollision()
        {
            var document = Document(8, 8);
            SetAlphaImage(document, 255, 255, 255, 255);
            document.textureMode = MapTextureMode.FitWidth;
            document.FillRectangle(Vector2.zero, new Vector2(8, 8));
            var rectangles = MapSceneExporter.BuildCollisionRectangles(document);
            Assert.AreEqual(1, rectangles.Count); Assert.AreEqual(new RectInt(0, 3, 8, 2), rectangles[0]);
            AssertContoursMatchEveryPixel(document);
            AssertColliderMatchesEveryPixel(document, LoadPrefab(Export(document), Preview()));
        }

        [Test] public void TextureAlphaAndThresholdMatchExportedCollisionBeforeAnyTrim()
        {
            var document = Document(8, 4);
            SetAlphaImage(document, 0, 127, 128, 255);
            document.alphaThreshold = 128;
            document.FillRectangle(Vector2.zero, new Vector2(8, 4));
            AssertColliderMatchesEveryPixel(document, LoadPrefab(Export(document), Preview()));
            AssertContoursMatchEveryPixel(document);
            foreach (byte pixel in document.mask) Assert.AreEqual(255, pixel, "Export must not silently rewrite the editable mask.");
        }

        [TestCase(1f)] [TestCase(99.5f)] [TestCase(100f)]
        public void ConcavePrefabReloadPreservesSinglePixelGapsAndIslandsAtPixelScale(float pixelsPerUnit)
        {
            var document = Document(15, 13, pixelsPerUnit);
            document.FillRectangle(Vector2.zero, new Vector2(12, 11));
            document.FillRectangle(new Vector2(2, 2), new Vector2(9, 9), true);
            document.FillRectangle(new Vector2(10, 4), new Vector2(12, 5), true);
            document.mask[5 * 15 + 5] = 255;
            document.mask[11 * 15 + 13] = 255;
            document.mask[12 * 15 + 14] = 255;
            var result = Export(document);
            var terrain = LoadPrefab(result, Preview());
            AssertColliderMatchesEveryPixel(document, terrain);
            var scene = EditorSceneManager.OpenPreviewScene(result.ScenePath); scenes.Add(scene);
            var sceneTerrain = scene.GetRootGameObjects()[0];
            AssertColliderMatchesEveryPixel(document, sceneTerrain);
            var sprite = sceneTerrain.GetComponent<SpriteRenderer>().sprite;
            Assert.That(sprite.bounds.size.x, Is.EqualTo(document.width / pixelsPerUnit).Within(.00001f));
            Assert.That(sprite.bounds.size.y, Is.EqualTo(document.height / pixelsPerUnit).Within(.00001f));
            Assert.AreEqual(Vector3.one, sceneTerrain.transform.localScale, "Export must retain an ordinary unit-scale terrain root.");
            terrain.transform.position = new Vector3(3, -2, 0);
            terrain.transform.localScale = new Vector3(1.5f, 2f, 1);
            terrain.transform.rotation = Quaternion.Euler(0, 0, 17);
            Physics2D.SyncTransforms();
            AssertColliderMatchesEveryPixel(document, terrain);
        }

        [TestCase(.5f)] [TestCase(100.001f)] [TestCase(1000f)] [TestCase(10000f)]
        public void UnsupportedPixelScaleFailsBeforeExportingAssetsButDraftStillPreviews(float pixelsPerUnit)
        {
            var document = Document(15, 13, pixelsPerUnit);
            document.FillRectangle(Vector2.zero, new Vector2(12, 11));
            document.FillRectangle(new Vector2(2, 2), new Vector2(9, 9), true);
            document.mask[5 * 15 + 5] = 255;
            byte[] originalMask = (byte[])document.mask.Clone();
            AssertContoursMatchEveryPixel(document);
            var error = Assert.Throws<InvalidOperationException>(() => Export(document));
            StringAssert.Contains("1 to 100", error.Message);
            Assert.AreEqual(0, Directory.GetFileSystemEntries(folder).Length, "Unsupported scales must not create any output assets.");
            Assert.AreEqual(pixelsPerUnit, document.pixelsPerUnit, "Do not silently clamp older drafts.");
            CollectionAssert.AreEqual(originalMask, document.mask);
        }
        [Test] public void FullyTransparentPaintFailsWithoutCreatingExportAssets()
        {
            var document = Document(8, 8); SetAlphaImage(document, 0);
            document.FillRectangle(Vector2.zero, new Vector2(8, 8));
            Assert.AreEqual(0, MapSceneExporter.BuildCollisionRectangles(document).Count);
            Assert.AreEqual(0, MapSceneExporter.BuildCollisionContours(document).Count);
            Assert.Throws<InvalidOperationException>(() => Export(document));
            Assert.AreEqual(0, Directory.GetDirectories(folder).Length);
        }

        static Rigidbody2D Ball(Scene scene, string name, Vector2 position)
        {
            var ball = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(ball, scene); ball.hideFlags = HideFlags.None;
            ball.transform.position = position; ball.AddComponent<CircleCollider2D>().radius = .2f;
            var body = ball.AddComponent<Rigidbody2D>(); body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            return body;
        }

        [Test] public void FallingBodiesLandOnGroundAndPassThroughItsGap()
        {
            var document = Document(16, 8);
            document.FillRectangle(Vector2.zero, new Vector2(16, 2));
            document.FillRectangle(new Vector2(6, 0), new Vector2(10, 2), true);
            var scene = Preview(); LoadPrefab(Export(document), scene);
            var supported = Ball(scene, "Supported body", new Vector2(-5, 2));
            var falling = Ball(scene, "Body over gap", new Vector2(0, 2));
            var physics = scene.GetPhysicsScene2D();
            Assert.IsTrue(physics.IsValid()); Assert.AreNotEqual(Physics2D.defaultPhysicsScene, physics, "Never simulate the user's physics scene.");
            Physics2D.SyncTransforms();
            for (int i = 0; i < 180; i++) physics.Simulate(1f / 60);
            Assert.That(supported.position.y, Is.InRange(-1.85f, -1.7f));
            Assert.Less(Mathf.Abs(supported.linearVelocity.y), .1f);
            Assert.Less(falling.position.y, -8, "The collider must not bridge an empty channel.");
        }
    }
}