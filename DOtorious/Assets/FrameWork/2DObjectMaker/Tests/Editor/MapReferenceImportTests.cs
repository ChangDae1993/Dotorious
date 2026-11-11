using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using JYW.Game.ObjectMaker.Editor;
using NUnit.Framework;
using UnityEditor;
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

        [Test] public void PngCreatesNativeCanvasWithEveryNonzeroAlphaPaintedAndExactEmbeddedColors()
        {
            string path = Write("Reference island.PNG", Texture().EncodeToPNG());
            byte[] original = File.ReadAllBytes(path);
            var document = MapPaintDocument.FromReferenceImage(path);
            Assert.AreEqual(3, document.width); Assert.AreEqual(2, document.height);
            Assert.AreEqual("Reference island", document.mapName); Assert.AreEqual("Reference island.PNG", document.terrainSourceName);
            Assert.IsTrue(string.IsNullOrEmpty(document.terrainSourceId));
            Assert.AreEqual(MapTextureMode.Tile, document.textureMode); Assert.AreEqual(1, document.tileScale);
            Assert.AreEqual(0, document.tileWidth); Assert.AreEqual(0, document.tileHeight); Assert.AreEqual(1, document.alphaThreshold);
            CollectionAssert.AreEqual(new byte[] { 0, 255, 255, 255, 255, 255 }, document.mask);
            CollectionAssert.AreEqual(document.mask, document.GetVisibleMask());
            var embedded = Own(new Texture2D(2, 2)); Assert.IsTrue(embedded.LoadImage(document.terrainPng));
            CollectionAssert.AreEqual(Pixels, embedded.GetPixels32());
            var rendered = Own(document.CreateTexture()).GetPixels32();
            Assert.AreEqual(0, rendered[0].a);
            for (int i = 1; i < Pixels.Length; i++) Assert.AreEqual(Pixels[i], rendered[i], "Pixel " + i);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.IsFalse(File.Exists(path + ".meta")); Assert.IsNull(AssetImporter.GetAtPath(path));
        }

        [Test] public void FullyTransparentReferenceIsAnEmptyEditableCanvas()
        {
            string path = Write("Empty.png", Texture(new Color32[6]).EncodeToPNG());
            var document = MapPaintDocument.FromReferenceImage(path);
            CollectionAssert.AreEqual(new byte[6], document.mask); Assert.AreEqual(3, document.width); Assert.AreEqual(2, document.height);
            document.PaintStroke(new Vector2(.5f, .5f), new Vector2(.5f, .5f));
            Assert.AreEqual(255, document.mask[0]);
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
            Assert.AreEqual(137, document.terrainPng[0]); Assert.AreEqual(80, document.terrainPng[1]);
        }

        [TestCase("bmp")]
        [TestCase("tga")]
        [TestCase("tif")]
        [TestCase("tiff")]
        [TestCase("psd")]
        public void NativeRasterFormatsPreserveDimensionsColorsAndTransparency(string extension)
        {
            string path = Write("Native." + extension, Raster(extension)); byte[] original = File.ReadAllBytes(path);
            var document = MapPaintDocument.FromReferenceImage(path);
            Assert.AreEqual(3, document.width); Assert.AreEqual(2, document.height);
            var embedded = Own(new Texture2D(2, 2)); Assert.IsTrue(embedded.LoadImage(document.terrainPng));
            var actual = embedded.GetPixels32();
            for (int i = 0; i < Pixels.Length; i++)
            {
                Color32 expected = Pixels[i]; if (extension == "bmp") expected.a = 255;
                Assert.AreEqual(expected, actual[i], "Embedded pixel " + i);
                Assert.AreEqual(expected.a == 0 ? 0 : 255, document.mask[i], "Mask pixel " + i);
            }
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path)); Assert.IsFalse(File.Exists(path + ".meta"));
        }

        [Test] public void LargeNonPowerOfTwoReferenceKeepsNativeSizeRatherThanDefaultImporterDownscaling()
        {
            const int width = 2051;
            var bytes = new byte[18 + width * 4]; bytes[2] = 2; bytes[12] = (byte)(width & 255); bytes[13] = (byte)(width >> 8);
            bytes[14] = 1; bytes[16] = 32; bytes[17] = 8;
            for (int i = 0; i < width; i++) { bytes[18 + i * 4] = 17; bytes[19 + i * 4] = 31; bytes[20 + i * 4] = 47; bytes[21 + i * 4] = 1; }
            var document = MapPaintDocument.FromReferenceImage(Write("Wide.tga", bytes));
            Assert.AreEqual(width, document.width); Assert.AreEqual(1, document.height); Assert.IsTrue(document.mask.All(value => value == 255));
            Assert.AreEqual(new Color32(47, 31, 17, 1), Own(document.CreateTexture()).GetPixels32()[width - 1]);
        }

        [Test] public void MaximumSupportedPngDimensionRemainsAccepted()
        {
            var texture = Own(new Texture2D(MapPaintDocument.MaxDimension, 1, TextureFormat.RGBA32, false));
            var pixels = Enumerable.Repeat(new Color32(37, 91, 143, 1), texture.width).ToArray(); texture.SetPixels32(pixels); texture.Apply();
            var document = MapPaintDocument.FromReferenceImage(Write("Maximum.png", texture.EncodeToPNG()));
            Assert.AreEqual(MapPaintDocument.MaxDimension, document.width); Assert.IsTrue(document.mask.All(value => value == 255));
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
            CollectionAssert.AreEqual(imported.mask, restored.mask); CollectionAssert.AreEqual(imported.terrainPng, restored.terrainPng);
            CollectionAssert.AreEqual(Own(imported.CreateTexture()).GetPixels32(), Own(restored.CreateTexture()).GetPixels32());
            restored.brushDiameter = 1; restored.PaintStroke(new Vector2(1.5f, .5f), new Vector2(1.5f, .5f), true);
            Assert.AreEqual(0, restored.mask[1]); restored.PaintStroke(new Vector2(1.5f, .5f), new Vector2(1.5f, .5f));
            Assert.AreEqual(255, restored.mask[1]); Assert.AreEqual(1, Own(restored.CreateTexture()).GetPixels32()[1].a);
        }

        [Test] public void RepeatedImportsAreIndependentAndFailureCannotChangeExistingDocument()
        {
            string path = Write("Independent.png", Texture().EncodeToPNG());
            var previous = MapPaintDocument.FromReferenceImage(path); string before = JsonUtility.ToJson(previous);
            var another = MapPaintDocument.FromReferenceImage(path); another.Clear(); another.terrainPng[0] = 0;
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
            Assert.AreEqual(document.mapName.Trim('.', ' '), document.mapName); Assert.AreEqual(name, document.terrainSourceName);
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
