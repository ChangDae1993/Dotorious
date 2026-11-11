using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using JYW.Game.ObjectMaker.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace JYW.Game.ObjectMaker.Tests
{
    public sealed class MapReferenceImportTests
    {
        readonly List<Object> owned = new List<Object>();
        string folder, assetFolder;
        string[] temporaryAssetsBefore;
        static readonly Color32[] Pixels =
        {
            new Color32(17, 31, 47, 0), new Color32(91, 53, 21, 1), new Color32(19, 173, 73, 127),
            new Color32(207, 45, 119, 255), new Color32(81, 177, 223, 255), new Color32(255, 143, 35, 255)
        };
        static readonly Color32[] LaterTerrainPixels =
        {
            new Color32(9, 81, 213, 255), new Color32(23, 147, 191, 255), new Color32(57, 99, 201, 0),
            new Color32(191, 63, 77, 127), new Color32(131, 235, 49, 128), new Color32(211, 19, 87, 255)
        };
        T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        Texture2D Texture(Color32[] pixels = null)
        {
            var texture = Own(new Texture2D(3, 2, TextureFormat.RGBA32, false));
            texture.SetPixels32(pixels ?? Pixels); texture.Apply(); return texture;
        }
        string Write(string name, byte[] bytes)
        {
            string path = Path.Combine(folder, name); File.WriteAllBytes(path, bytes); return path;
        }
        static string[] ImportFolders() => Directory.GetDirectories(Application.dataPath, "__MapReferenceImport_*").OrderBy(p => p).ToArray();
        static void AssertNoTerrain(MapPaintDocument document)
        {
            Assert.IsTrue(document.terrainPng == null || document.terrainPng.Length == 0, "A reference must not become the terrain texture.");
            Assert.IsTrue(string.IsNullOrEmpty(document.terrainSourceId));
            Assert.IsTrue(string.IsNullOrEmpty(document.terrainSourceName));
        }
        Texture2D DecodeReference(string path)
        {
            var importer = typeof(MapPaintDocument).Assembly.GetType("JYW.Game.ObjectMaker.Editor.MapReferenceImageImporter", true);
            var load = importer.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(load);
            return Own((Texture2D)load.Invoke(null, new object[] { path }));
        }
        static MapPaintDocument PaintReferencePixels(Color32[] pixels, int width, int height)
        {
            var document = new MapPaintDocument { width = width, height = height, brushDiameter = 1 };
            document.EnsureValid();
            for (int i = 0; i < pixels.Length; i++) if (pixels[i].a > 0)
            {
                var point = new Vector2(i % width + .5f, i / width + .5f);
                document.PaintStroke(point, point);
            }
            return document;
        }

        [SetUp] public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "MapReferenceTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder); temporaryAssetsBefore = ImportFolders();
        }
        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value) Object.DestroyImmediate(value);
            owned.Clear();
            if (assetFolder != null) AssetDatabase.DeleteAsset(assetFolder);
            assetFolder = null;
            if (folder != null && Directory.Exists(folder))
            {
                foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
                Directory.Delete(folder);
            }
            CollectionAssert.AreEqual(temporaryAssetsBefore, ImportFolders(), "No temporary imported assets may remain.");
        }

        [Test] public void PngCreatesNativeCanvasExactlyLikePenPaintingEveryNonzeroAlphaPixel()
        {
            string path = Write("Reference island.PNG", Texture().EncodeToPNG());
            byte[] original = File.ReadAllBytes(path);
            var document = MapPaintDocument.FromReferenceImage(path);
            Assert.AreEqual(3, document.width); Assert.AreEqual(2, document.height);
            Assert.AreEqual("Reference island", document.mapName); AssertNoTerrain(document);
            Assert.AreEqual(MapTextureMode.Tile, document.textureMode); Assert.AreEqual(1, document.tileScale);
            Assert.AreEqual(0, document.tileWidth); Assert.AreEqual(0, document.tileHeight); Assert.AreEqual(1, document.alphaThreshold);
            Assert.AreEqual(100, document.pixelsPerUnit); Assert.AreEqual(32, document.brushDiameter);
            CollectionAssert.AreEqual(new byte[] { 0, 255, 255, 255, 255, 255 }, document.mask);
            CollectionAssert.AreEqual(document.mask, document.GetVisibleMask());
            CollectionAssert.AreEqual(Pixels, DecodeReference(path).GetPixels32(), "The decoder still preserves source colors and alpha before mask conversion.");
            var painted = PaintReferencePixels(Pixels, 3, 2);
            CollectionAssert.AreEqual(painted.mask, document.mask);
            var rendered = Own(document.CreateTexture()).GetPixels32();
            CollectionAssert.AreEqual(Own(painted.CreateTexture()).GetPixels32(), rendered);
            Assert.AreEqual(0, rendered[0].a);
            for (int i = 1; i < Pixels.Length; i++) Assert.AreEqual(new Color32(139, 100, 60, 255), rendered[i], "Pen-rendered pixel " + i);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.IsFalse(File.Exists(path + ".meta")); Assert.IsNull(AssetImporter.GetAtPath(path));
        }

        [Test] public void FullyTransparentReferenceIsAnEmptyEditableCanvas()
        {
            string path = Write("Empty.png", Texture(new Color32[6]).EncodeToPNG());
            var document = MapPaintDocument.FromReferenceImage(path);
            AssertNoTerrain(document);
            CollectionAssert.AreEqual(new byte[6], document.mask); Assert.AreEqual(3, document.width); Assert.AreEqual(2, document.height);
            document.PaintStroke(new Vector2(.5f, .5f), new Vector2(.5f, .5f));
            Assert.AreEqual(255, document.mask[0]);
            Assert.AreEqual(255, Own(document.CreateTexture()).GetPixels32()[0].a);
        }

        [TestCase("jpg")]
        [TestCase("JPEG")]
        public void JpegCreatesCompletelyPaintedCanvasWithoutInventingTransparency(string extension)
        {
            string path = Write("Photo." + extension, Texture().EncodeToJPG(95));
            var document = MapPaintDocument.FromReferenceImage(path);
            Assert.AreEqual(3, document.width); Assert.AreEqual(2, document.height);
            Assert.IsTrue(document.mask.All(value => value == 255));
            Assert.IsTrue(Own(document.CreateTexture()).GetPixels32().All(pixel => pixel.a == 255));
            AssertNoTerrain(document);
        }

        [TestCase("bmp")]
        [TestCase("tga")]
        [TestCase("tif")]
        [TestCase("tiff")]
        [TestCase("psd")]
        public void NativeRasterFormatsDecodeExactColorsAndConvertAlphaToPenPaintedMask(string extension)
        {
            string path = Write("Native." + extension, Raster(extension)); byte[] original = File.ReadAllBytes(path);
            var document = MapPaintDocument.FromReferenceImage(path);
            Assert.AreEqual(3, document.width); Assert.AreEqual(2, document.height);
            AssertNoTerrain(document);
            var decoded = DecodeReference(path);
            Assert.AreEqual(3, decoded.width); Assert.AreEqual(2, decoded.height);
            var actual = decoded.GetPixels32();
            for (int i = 0; i < Pixels.Length; i++)
            {
                Color32 expected = Pixels[i]; if (extension == "bmp") expected.a = 255;
                Assert.AreEqual(expected, actual[i], "Decoded source pixel " + i);
                Assert.AreEqual(expected.a == 0 ? 0 : 255, document.mask[i], "Mask pixel " + i);
            }
            var painted = PaintReferencePixels(actual, 3, 2);
            CollectionAssert.AreEqual(painted.mask, document.mask);
            CollectionAssert.AreEqual(Own(painted.CreateTexture()).GetPixels32(), Own(document.CreateTexture()).GetPixels32());
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path)); Assert.IsFalse(File.Exists(path + ".meta"));
        }

        [Test] public void LargeNonPowerOfTwoReferenceKeepsNativeSizeRatherThanDefaultImporterDownscaling()
        {
            const int width = 2051;
            var bytes = new byte[18 + width * 4]; bytes[2] = 2; bytes[12] = (byte)(width & 255); bytes[13] = (byte)(width >> 8);
            bytes[14] = 1; bytes[16] = 32; bytes[17] = 8;
            for (int i = 0; i < width; i++) { bytes[18 + i * 4] = 17; bytes[19 + i * 4] = 31; bytes[20 + i * 4] = 47; bytes[21 + i * 4] = 1; }
            string path = Write("Wide.tga", bytes);
            var document = MapPaintDocument.FromReferenceImage(path);
            Assert.AreEqual(width, document.width); Assert.AreEqual(1, document.height); Assert.IsTrue(document.mask.All(value => value == 255));
            AssertNoTerrain(document);
            Assert.AreEqual(new Color32(47, 31, 17, 1), DecodeReference(path).GetPixels32()[width - 1]);
            Assert.AreEqual(new Color32(139, 100, 60, 255), Own(document.CreateTexture()).GetPixels32()[width - 1]);
        }

        [Test] public void MaximumSupportedPngDimensionRemainsAccepted()
        {
            var texture = Own(new Texture2D(MapPaintDocument.MaxDimension, 1, TextureFormat.RGBA32, false));
            var pixels = Enumerable.Repeat(new Color32(37, 91, 143, 1), texture.width).ToArray(); texture.SetPixels32(pixels); texture.Apply();
            var document = MapPaintDocument.FromReferenceImage(Write("Maximum.png", texture.EncodeToPNG()));
            Assert.AreEqual(MapPaintDocument.MaxDimension, document.width); Assert.IsTrue(document.mask.All(value => value == 255));
            AssertNoTerrain(document); Assert.IsTrue(Own(document.CreateTexture()).GetPixels32().All(pixel => pixel.a == 255));
        }

        [Test] public void NativeDecoderFailureAlsoRemovesTheTemporaryAssets()
        {
            byte[] bytes = Raster("psd"); bytes[38] = 255; bytes[39] = 255; // Invalid compression method after a valid PSD header.
            string path = Write("Corrupt.psd", bytes);
            bool previous = LogAssert.ignoreFailingMessages;
            try
            {
                // Native import logs an error as well as failing. The exception and cleanup are the contract here.
                LogAssert.ignoreFailingMessages = true;
                Assert.Throws<ArgumentException>(() => MapPaintDocument.FromReferenceImage(path));
            }
            finally { LogAssert.ignoreFailingMessages = previous; }
            CollectionAssert.AreEqual(temporaryAssetsBefore, ImportFolders());
        }

        [Test] public void TruncatedBmpIsRejectedInsteadOfPaintingPixelsFilledByTheNativeDecoder()
        {
            string path = Write("Truncated.bmp", Raster("bmp").Take(54).ToArray());
            Assert.Throws<ArgumentException>(() => MapPaintDocument.FromReferenceImage(path));
        }

        [Test] public void ReferenceDraftSurvivesSourceRemovalAndCanStillBeErasedAndPainted()
        {
            string path = Write("Portable.png", Texture().EncodeToPNG());
            var imported = MapPaintDocument.FromReferenceImage(path);
            string saved = JsonUtility.ToJson(imported); File.Delete(path);
            var restored = JsonUtility.FromJson<MapPaintDocument>(saved); restored.EnsureValid();
            CollectionAssert.AreEqual(imported.mask, restored.mask); AssertNoTerrain(imported); AssertNoTerrain(restored);
            CollectionAssert.AreEqual(Own(imported.CreateTexture()).GetPixels32(), Own(restored.CreateTexture()).GetPixels32());
            restored.brushDiameter = 1; restored.PaintStroke(new Vector2(1.5f, .5f), new Vector2(1.5f, .5f), true);
            Assert.AreEqual(0, restored.mask[1]); restored.PaintStroke(new Vector2(1.5f, .5f), new Vector2(1.5f, .5f));
            Assert.AreEqual(255, restored.mask[1]); Assert.AreEqual(255, Own(restored.CreateTexture()).GetPixels32()[1].a);
            restored.PaintStroke(new Vector2(.5f, .5f), new Vector2(.5f, .5f));
            Assert.AreEqual(255, restored.mask[0]); Assert.AreEqual(255, Own(restored.CreateTexture()).GetPixels32()[0].a);
        }

        [TestCase(MapTextureMode.Tile)]
        [TestCase(MapTextureMode.FitWidth)]
        [TestCase(MapTextureMode.FitHeight)]
        [TestCase(MapTextureMode.Stretch)]
        public void TerrainChosenLaterUsesOnlyTheImportedMaskAndItsOwnColorsAndAlpha(MapTextureMode mode)
        {
            string referencePath = Write("Mask.png", Texture().EncodeToPNG());
            var document = MapPaintDocument.FromReferenceImage(referencePath); File.Delete(referencePath);
            AssertNoTerrain(document);
            byte[] mask = (byte[])document.mask.Clone();
            string terrainPath = Write("Later terrain.png", Texture(LaterTerrainPixels).EncodeToPNG());
            document.LoadTerrainFile(terrainPath); document.textureMode = mode; File.Delete(terrainPath);
            var rendered = Own(document.CreateTexture()).GetPixels32();
            for (int i = 0; i < rendered.Length; i++)
                Assert.AreEqual(mask[i] == 0 ? new Color32() : LaterTerrainPixels[i], rendered[i], "Later terrain pixel " + i);
            CollectionAssert.AreEqual(mask, document.mask, "Choosing terrain must not change the painted mask.");
            var restored = JsonUtility.FromJson<MapPaintDocument>(JsonUtility.ToJson(document)); restored.EnsureValid();
            CollectionAssert.AreEqual(mask, restored.mask);
            CollectionAssert.AreEqual(rendered, Own(restored.CreateTexture()).GetPixels32());
            document.terrainPng = null;
            CollectionAssert.AreEqual(mask, document.GetVisibleMask(), "Removing terrain restores all painted pixels, including pixels hidden by its alpha.");
        }

        [Test] public void ReferenceMaskRequiresLaterTerrainBeforeExportAndColliderUsesThatTerrainAlpha()
        {
            var document = MapPaintDocument.FromReferenceImage(Write("Reference export.png", Texture().EncodeToPNG()));
            document.pixelsPerUnit = 1; document.alphaThreshold = 128;
            assetFolder = "Assets/__MapReferenceExportTests_" + Guid.NewGuid().ToString("N");
            Assert.Throws<InvalidOperationException>(() => MapSceneExporter.Export(document, assetFolder));
            Assert.IsFalse(AssetDatabase.IsValidFolder(assetFolder), "An untextured mask must not produce export assets.");
            byte[] mask = (byte[])document.mask.Clone();
            document.LoadTerrainFile(Write("Explicit terrain.png", Texture(LaterTerrainPixels).EncodeToPNG()));
            var result = MapSceneExporter.Export(document, assetFolder);
            var baked = Own(new Texture2D(2, 2)); Assert.IsTrue(baked.LoadImage(File.ReadAllBytes(result.TexturePath)));
            CollectionAssert.AreEqual(Own(document.CreateTexture()).GetPixels32(), baked.GetPixels32());
            CollectionAssert.AreEqual(mask, document.mask);
            CollectionAssert.AreEqual(new byte[] { 0, 255, 0, 0, 255, 255 }, document.GetVisibleMask());
            var scene = EditorSceneManager.OpenPreviewScene(result.ScenePath);
            try
            {
                var roots = scene.GetRootGameObjects(); Assert.AreEqual(1, roots.Length);
                var collider = roots[0].GetComponent<CompositeCollider2D>(); Assert.IsNotNull(collider);
                Physics2D.SyncTransforms();
                var visible = document.GetVisibleMask();
                for (int y = 0; y < document.height; y++) for (int x = 0; x < document.width; x++)
                {
                    var point = new Vector2(x + .5f - document.width * .5f, y + .5f - document.height * .5f) / document.pixelsPerUnit;
                    Assert.AreEqual(visible[y * document.width + x] != 0, collider.OverlapPoint(point), "Exported collider pixel " + x + "," + y);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test] public void RepeatedImportsAreIndependentAndFailureCannotChangeExistingDocument()
        {
            string path = Write("Independent.png", Texture().EncodeToPNG());
            var previous = MapPaintDocument.FromReferenceImage(path); string before = JsonUtility.ToJson(previous);
            var another = MapPaintDocument.FromReferenceImage(path); another.Clear(); another.terrainPng = Texture().EncodeToPNG();
            Assert.AreEqual(before, JsonUtility.ToJson(previous));
            string malformed = Write("Broken.png", new byte[] { 1, 2, 3, 4 });
            Assert.Throws<ArgumentException>(() => previous = MapPaintDocument.FromReferenceImage(malformed));
            Assert.AreEqual(before, JsonUtility.ToJson(previous));
        }

        [Test] public void ExistingProjectSourceBytesAndImporterSettingsAreNotChanged()
        {
            assetFolder = "Assets/__MapReferenceSourceTests_" + Guid.NewGuid().ToString("N");
            Assert.IsNotEmpty(AssetDatabase.CreateFolder("Assets", Path.GetFileName(assetFolder)));
            string source = assetFolder + "/Original.tga"; File.WriteAllBytes(source, Raster("tga"));
            AssetDatabase.ImportAsset(source, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(source);
            importer.isReadable = false; importer.maxTextureSize = 32; importer.alphaIsTransparency = true; importer.SaveAndReimport();
            byte[] original = File.ReadAllBytes(source), metadata = File.ReadAllBytes(source + ".meta");
            var document = MapPaintDocument.FromReferenceImage(Path.GetFullPath(source));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(source)); CollectionAssert.AreEqual(metadata, File.ReadAllBytes(source + ".meta"));
            Assert.IsFalse(((TextureImporter)AssetImporter.GetAtPath(source)).isReadable);
            Assert.AreEqual(3, document.width); Assert.AreEqual(2, document.height); Assert.AreEqual(255, document.mask[1]);
        }

        [TestCase(".png")]
        [TestCase("  map with spaces  .png")]
        [TestCase("a...................................................................................png")]
        public void FileNamesBecomeSafeNonemptyCanvasNames(string name)
        {
            var document = MapPaintDocument.FromReferenceImage(Write(name, Texture().EncodeToPNG()));
            Assert.IsFalse(string.IsNullOrWhiteSpace(document.mapName)); Assert.LessOrEqual(document.mapName.Length, 80);
            Assert.AreEqual(document.mapName.Trim('.', ' '), document.mapName); AssertNoTerrain(document);
            Assert.AreEqual(-1, document.mapName.IndexOfAny(Path.GetInvalidFileNameChars()));
        }

        [TestCase("png")]
        [TestCase("jpg")]
        [TestCase("bmp")]
        [TestCase("tga")]
        [TestCase("tif")]
        [TestCase("psd")]
        public void MalformedAndEmptyImagesAreRejectedBeforeImport(string extension)
        {
            string path = Write("Bad." + extension, new byte[] { 1, 2, 3, 4 });
            Assert.Throws<ArgumentException>(() => MapPaintDocument.FromReferenceImage(path));
            File.WriteAllBytes(path, new byte[0]); Assert.Throws<ArgumentException>(() => MapPaintDocument.FromReferenceImage(path));
        }

        [TestCase("png")]
        [TestCase("jpg")]
        [TestCase("bmp")]
        [TestCase("tga")]
        [TestCase("tif")]
        [TestCase("psd")]
        public void OversizedDimensionsAreRejectedFromHeadersBeforeNativeDecode(string extension)
        {
            byte[] bytes;
            if (extension == "png") { bytes = Texture().EncodeToPNG(); SetBigEndian(bytes, 16, 4097); }
            else if (extension == "jpg") bytes = new byte[] { 255, 216, 255, 192, 0, 8, 8, 0, 1, 16, 1, 1 };
            else
            {
                bytes = Raster(extension);
                if (extension == "bmp") { bytes[18] = 1; bytes[19] = 16; }
                if (extension == "tga") { bytes[12] = 1; bytes[13] = 16; }
                if (extension == "psd") SetBigEndian(bytes, 18, 4097);
                if (extension == "tif") { bytes[18] = 1; bytes[19] = 16; }
            }
            string path = Write("Too large." + extension, bytes);
            var error = Assert.Throws<ArgumentException>(() => MapPaintDocument.FromReferenceImage(path)); StringAssert.Contains("4096", error.Message);
        }

        [Test] public void ExcessiveFileSizeUnsupportedFormatsMissingFilesAndCancelledPathsFailSafely()
        {
            string large = Path.Combine(folder, "Large.png"); using (var stream = File.Create(large)) stream.SetLength(64L * 1024 * 1024 + 1);
            StringAssert.Contains("64 MB", Assert.Throws<ArgumentException>(() => MapPaintDocument.FromReferenceImage(large)).Message);
            Assert.Throws<ArgumentException>(() => MapPaintDocument.FromReferenceImage(Write("Not supported.gif", Texture().EncodeToPNG())));
            Assert.Throws<FileNotFoundException>(() => MapPaintDocument.FromReferenceImage(Path.Combine(folder, "Missing.png")));
            Assert.Throws<ArgumentException>(() => MapPaintDocument.FromReferenceImage(null));
            Assert.Throws<ArgumentException>(() => MapPaintDocument.FromReferenceImage(""));
        }

        [Test] public void TiffDirectoryOffsetsAndMultipageFilesFailWithoutLeavingAssets()
        {
            byte[] bytes = Raster("tif"); bytes[4] = 255; bytes[5] = 255; bytes[6] = 255; bytes[7] = 255;
            Assert.Throws<ArgumentException>(() => MapPaintDocument.FromReferenceImage(Write("Offset.tif", bytes)));
            bytes = Raster("tif"); bytes[10 + 11 * 12] = 8;
            StringAssert.Contains("single-page", Assert.Throws<ArgumentException>(() => MapPaintDocument.FromReferenceImage(Write("Pages.tif", bytes))).Message);
        }

        [Test] public void PickerFiltersListOnlyTheImplementedFormatsAndCannotBeMutatedGlobally()
        {
            string[] filter = MapPaintDocument.ReferenceImageFilters; Assert.AreEqual(2, filter.Length);
            CollectionAssert.AreEquivalent(new[] { "png", "jpg", "jpeg", "bmp", "tga", "tif", "tiff", "psd" }, filter[1].Split(','));
            filter[1] = "gif"; Assert.AreNotEqual(filter[1], MapPaintDocument.ReferenceImageFilters[1]);
        }

        // Small, uncompressed fixtures keep every expected color and alpha byte explicit.
        static byte[] Raster(string extension)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                if (extension == "bmp")
                {
                    writer.Write((ushort)0x4d42); writer.Write(78); writer.Write(0); writer.Write(54);
                    writer.Write(40); writer.Write(3); writer.Write(2); writer.Write((ushort)1); writer.Write((ushort)24);
                    writer.Write(0); writer.Write(24); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                    for (int y = 0; y < 2; y++) { for (int x = 0; x < 3; x++) { Color32 c = Pixels[y * 3 + x]; writer.Write(c.b); writer.Write(c.g); writer.Write(c.r); } writer.Write(new byte[3]); }
                }
                else if (extension == "tga")
                {
                    writer.Write(new byte[] { 0, 0, 2 }); writer.Write(new byte[9]); writer.Write((ushort)3); writer.Write((ushort)2); writer.Write((byte)32); writer.Write((byte)8);
                    foreach (Color32 c in Pixels) { writer.Write(c.b); writer.Write(c.g); writer.Write(c.r); writer.Write(c.a); }
                }
                else if (extension == "psd")
                {
                    writer.Write(Encoding.ASCII.GetBytes("8BPS")); BigEndian(writer, 1, 2); writer.Write(new byte[6]); BigEndian(writer, 4, 2);
                    BigEndian(writer, 2, 4); BigEndian(writer, 3, 4); BigEndian(writer, 8, 2); BigEndian(writer, 3, 2);
                    BigEndian(writer, 0, 4); BigEndian(writer, 0, 4); BigEndian(writer, 0, 4); BigEndian(writer, 0, 2);
                    for (int channel = 0; channel < 4; channel++) for (int y = 1; y >= 0; y--) for (int x = 0; x < 3; x++)
                    { Color32 c = Pixels[y * 3 + x]; writer.Write(channel == 0 ? c.r : channel == 1 ? c.g : channel == 2 ? c.b : c.a); }
                }
                else
                {
                    const int entries = 11, bitsOffset = 8 + 2 + entries * 12 + 4, pixelOffset = bitsOffset + 8;
                    writer.Write((ushort)0x4949); writer.Write((ushort)42); writer.Write(8); writer.Write((ushort)entries);
                    Entry(writer, 256, 4, 1, 3); Entry(writer, 257, 4, 1, 2); Entry(writer, 258, 3, 4, bitsOffset);
                    Entry(writer, 259, 3, 1, 1); Entry(writer, 262, 3, 1, 2); Entry(writer, 273, 4, 1, pixelOffset);
                    Entry(writer, 277, 3, 1, 4); Entry(writer, 278, 4, 1, 2); Entry(writer, 279, 4, 1, 24);
                    Entry(writer, 284, 3, 1, 1); Entry(writer, 338, 3, 1, 2); writer.Write(0);
                    for (int i = 0; i < 4; i++) writer.Write((ushort)8);
                    for (int y = 1; y >= 0; y--) for (int x = 0; x < 3; x++) { Color32 c = Pixels[y * 3 + x]; writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a); }
                }
                return stream.ToArray();
            }
        }
        static void Entry(BinaryWriter writer, int tag, int type, int count, int value)
        { writer.Write((ushort)tag); writer.Write((ushort)type); writer.Write(count); writer.Write(value); }
        static void BigEndian(BinaryWriter writer, int value, int count)
        { for (int i = count - 1; i >= 0; i--) writer.Write((byte)(value >> (i * 8))); }
        static void SetBigEndian(byte[] bytes, int offset, int value)
        { for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> ((3 - i) * 8)); }
    }
}
