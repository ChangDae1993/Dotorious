using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace JYW.Game.ObjectMaker.Editor
{
    /// <summary>Lossless editor-only conversion. No source asset or importer is modified.</summary>
    internal static class MapReferenceImageImporter
    {
        const int MaxFileBytes = 64 * 1024 * 1024;

        internal static Texture2D Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a reference image.");
            string extension = Path.GetExtension(path).ToLowerInvariant();
            switch (extension)
            {
                case ".png": case ".jpg": case ".jpeg": case ".bmp": case ".tga": case ".tif": case ".tiff": case ".psd": break;
                default: throw new ArgumentException("Choose a PNG, JPG, BMP, TGA, TIFF or PSD image.");
            }
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length == 0) throw new ArgumentException("The reference image is empty.");
                if (stream.Length > MaxFileBytes) throw new ArgumentException("Choose an image no larger than 64 MB.");
                using (var reader = new BinaryReader(stream)) bytes = reader.ReadBytes((int)stream.Length);
            }
            Vector2Int size = ReadSize(bytes, extension);
            MapPaintDocument.ValidateSize(size.x, size.y);
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
                return MapPaintDocument.Decode(bytes);
            return ImportCopy(bytes, extension, size);
        }

        static Texture2D ImportCopy(byte[] bytes, string extension, Vector2Int size)
        {
            string folderName = "__MapReferenceImport_" + Guid.NewGuid().ToString("N");
            string folder = "Assets/" + folderName;
            string assetPath = folder + "/Reference" + extension;
            string absoluteFolder = Path.Combine(Application.dataPath, folderName);
            Texture2D result = null;
            bool created = false;
            try
            {
                if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", folderName)))
                    throw new IOException("Could not create a temporary reference import folder.");
                created = true;
                File.WriteAllBytes(Path.Combine(absoluteFolder, "Reference" + extension), bytes);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (!importer) throw new ArgumentException("Unity could not read this reference image.");
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.isReadable = true;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                // Alpha dilation/compression would alter the source, including alpha values near zero.
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.crunchedCompression = false;
                importer.maxTextureSize = MapPaintDocument.MaxDimension;
                var platform = importer.GetDefaultPlatformTextureSettings();
                platform.format = TextureImporterFormat.RGBA32;
                platform.textureCompression = TextureImporterCompression.Uncompressed;
                platform.maxTextureSize = MapPaintDocument.MaxDimension;
                importer.SetPlatformTextureSettings(platform);
                importer.SaveAndReimport();
                var source = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (!source) throw new ArgumentException("Unity could not decode this reference image.");
                if (source.width != size.x || source.height != size.y)
                    throw new ArgumentException("The reference image could not be imported at its original dimensions.");
                result = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
                result.SetPixels32(source.GetPixels32());
                result.Apply();
                return result;
            }
            catch { if (result) UnityEngine.Object.DestroyImmediate(result); throw; }
            finally
            {
                // Only our uniquely created temporary folder is removed, even after a failed import.
                if (created)
                {
                    try
                    {
                        if (!AssetDatabase.DeleteAsset(folder))
                        {
                            // An unsuccessful import may leave files that AssetDatabase does not know about.
                            if (Directory.Exists(absoluteFolder)) Directory.Delete(absoluteFolder, true);
                            if (File.Exists(absoluteFolder + ".meta")) File.Delete(absoluteFolder + ".meta");
                            AssetDatabase.Refresh();
                        }
                    }
                    catch { if (result) UnityEngine.Object.DestroyImmediate(result); throw; }
                }
            }
        }

        // Check headers before native decoders allocate memory. TIFF is limited to single-page classic TIFF.
        static Vector2Int ReadSize(byte[] bytes, string extension)
        {
            long width = 0, height = 0;
            if (extension == ".png")
            {
                Require(bytes, 0, 33);
                if (U32(bytes, 0, false) != 0x89504e47 || U32(bytes, 4, false) != 0x0d0a1a0a ||
                    U32(bytes, 8, false) != 13 || U32(bytes, 12, false) != 0x49484452) throw InvalidImage();
                width = U32(bytes, 16, false); height = U32(bytes, 20, false);
            }
            else if (extension == ".jpg" || extension == ".jpeg")
            {
                if (U16(bytes, 0, false) != 0xffd8) throw InvalidImage();
                int offset = 2;
                while (offset < bytes.Length)
                {
                    if (bytes[offset++] != 0xff) throw InvalidImage();
                    while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
                    Require(bytes, offset, 1); int marker = bytes[offset++];
                    if (marker == 0xd9 || marker == 0xda) break;
                    if (marker == 0x01 || (marker >= 0xd0 && marker <= 0xd8)) continue;
                    int length = U16(bytes, offset, false);
                    if (length < 2) throw InvalidImage();
                    Require(bytes, offset, length);
                    if (marker >= 0xc0 && marker <= 0xcf && marker != 0xc4 && marker != 0xc8 && marker != 0xcc)
                    {
                        if (length < 8) throw InvalidImage();
                        height = U16(bytes, offset + 3, false); width = U16(bytes, offset + 5, false); break;
                    }
                    offset += length;
                }
            }
            else if (extension == ".bmp")
            {
                if (U16(bytes, 0, false) != 0x424d) throw InvalidImage();
                // Unity accepts some truncated BMPs by filling missing pixels. Do not silently paint those.
                if (U32(bytes, 2, true) > bytes.Length) throw InvalidImage();
                long dibSize = U32(bytes, 14, true);
                if (dibSize == 12) { width = U16(bytes, 18, true); height = U16(bytes, 20, true); }
                else if (dibSize >= 40)
                {
                    Require(bytes, 14, dibSize);
                    width = unchecked((int)U32(bytes, 18, true));
                    height = Math.Abs((long)unchecked((int)U32(bytes, 22, true)));
                }
                else throw InvalidImage();
            }
            else if (extension == ".tga")
            {
                Require(bytes, 0, 18);
                int type = bytes[2];
                if (type != 1 && type != 2 && type != 3 && type != 9 && type != 10 && type != 11) throw InvalidImage();
                width = U16(bytes, 12, true); height = U16(bytes, 14, true);
            }
            else if (extension == ".psd")
            {
                Require(bytes, 0, 26);
                if (U32(bytes, 0, false) != 0x38425053 || U16(bytes, 4, false) != 1) throw InvalidImage();
                height = U32(bytes, 14, false); width = U32(bytes, 18, false);
            }
            else
            {
                int order = U16(bytes, 0, true);
                if (order != 0x4949 && order != 0x4d4d) throw InvalidImage();
                bool littleEndian = order == 0x4949;
                if (U16(bytes, 2, littleEndian) != 42) throw new ArgumentException("Use a classic, single-page TIFF image (not BigTIFF).");
                long directory = U32(bytes, 4, littleEndian);
                int count = U16(bytes, directory, littleEndian);
                Require(bytes, directory + 2, count * 12L + 4);
                for (int i = 0; i < count; i++)
                {
                    long entry = directory + 2 + i * 12L;
                    int tag = U16(bytes, entry, littleEndian);
                    if (tag != 256 && tag != 257) continue;
                    int type = U16(bytes, entry + 2, littleEndian);
                    if (U32(bytes, entry + 4, littleEndian) != 1 || (type != 3 && type != 4)) throw InvalidImage();
                    long dimension = type == 3 ? U16(bytes, entry + 8, littleEndian) : U32(bytes, entry + 8, littleEndian);
                    // Validate every occurrence, so duplicate tags cannot hide an oversized dimension.
                    if (dimension < 1 || dimension > MapPaintDocument.MaxDimension) throw SizeError();
                    if (tag == 256) width = dimension; else height = dimension;
                }
                if (U32(bytes, directory + 2 + count * 12L, littleEndian) != 0)
                    throw new ArgumentException("Choose a single-page TIFF reference image.");
            }
            if (width < 1 || height < 1 || width > MapPaintDocument.MaxDimension || height > MapPaintDocument.MaxDimension) throw SizeError();
            return new Vector2Int((int)width, (int)height);
        }
        static void Require(byte[] bytes, long offset, long length)
        {
            if (offset < 0 || length < 0 || offset > bytes.Length || length > bytes.Length - offset) throw InvalidImage();
        }
        static int U16(byte[] bytes, long offset, bool littleEndian)
        {
            Require(bytes, offset, 2); int p = (int)offset;
            return littleEndian ? bytes[p] | bytes[p + 1] << 8 : bytes[p] << 8 | bytes[p + 1];
        }
        static uint U32(byte[] bytes, long offset, bool littleEndian)
        {
            Require(bytes, offset, 4); int p = (int)offset;
            return littleEndian ? (uint)(bytes[p] | bytes[p + 1] << 8 | bytes[p + 2] << 16 | bytes[p + 3] << 24) :
                (uint)(bytes[p] << 24 | bytes[p + 1] << 16 | bytes[p + 2] << 8 | bytes[p + 3]);
        }
        static ArgumentException InvalidImage() => new ArgumentException("The reference image header is invalid or unsupported.");
        static ArgumentException SizeError() => new ArgumentException("Reference image dimensions must be between 1 and 4096 pixels.");

        internal static string CanvasName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path).Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            foreach (char c in "\\/:*?\"<>|") name = name.Replace(c, '_');
            name = name.Trim('.', ' ');
            if (name.Length > 80) name = name.Substring(0, 80).TrimEnd('.', ' ');
            if (name.Length == 0) return "Reference Map";
            string stem = name.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '1' && stem[3] <= '9')) name = "Map_" + name;
            return name;
        }
    }
}
