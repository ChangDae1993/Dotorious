using System;
using System.IO;
using UnityEngine;

namespace JYW.Game.ObjectMaker.Editor
{
    public enum MapTextureMode { Tile, FitWidth, FitHeight, Stretch }

    /// <summary>Pixel coordinates start at bottom left. Collision follows the painted mask, not texture alpha.</summary>
    [Serializable]
    public sealed class MapPaintDocument
    {
        public const int MaxDimension = 4096;
        public int width = 1024, height = 768;
        public float pixelsPerUnit = 100, brushDiameter = 32;
        public string mapName = "New Map";
        public byte[] terrainPng, mask;
        public MapTextureMode textureMode;
        public float tileWidth, tileHeight, tileScale = 1;

        public void EnsureValid()
        {
            ValidateSize(width, height);
            if (!Finite(pixelsPerUnit) || pixelsPerUnit <= 0 || !Finite(brushDiameter) || brushDiameter <= 0 ||
                !Finite(tileWidth) || tileWidth < 0 || !Finite(tileHeight) || tileHeight < 0 || !Finite(tileScale) || tileScale <= 0)
                throw new ArgumentException("Pixel scale, brush diameter and tile scale must be positive finite values; tile sizes must be nonnegative.");
            if (tileScale < .01f || !Finite(tileWidth * tileScale) || !Finite(tileHeight * tileScale) ||
                (tileWidth > 0 && tileWidth * tileScale < .01f) || (tileHeight > 0 && tileHeight * tileScale < .01f))
                throw new ArgumentException("Tile scale must be at least 0.01 and effective tile dimensions must be finite and at least 0.01 pixels.");
            if (!Enum.IsDefined(typeof(MapTextureMode), textureMode)) throw new ArgumentException("Unknown texture mode.");
            if (mask == null || mask.Length == 0) mask = new byte[width * height];
            else if (mask.Length != width * height) throw new ArgumentException("Mask dimensions do not match the canvas. Use Resize to change canvas dimensions.");
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static void ValidateSize(int w, int h)
        {
            if (w < 1 || h < 1 || w > MaxDimension || h > MaxDimension)
                throw new ArgumentOutOfRangeException(nameof(w), "Canvas and source dimensions must be between 1 and 4096 pixels.");
        }
        public void Resize(int newWidth, int newHeight)
        {
            EnsureValid(); ValidateSize(newWidth, newHeight);
            var resized = new byte[newWidth * newHeight];
            for (int y = 0; y < Math.Min(height, newHeight); y++)
                Array.Copy(mask, y * width, resized, y * newWidth, Math.Min(width, newWidth));
            width = newWidth; height = newHeight; mask = resized;
        }
        public void Clear() { EnsureValid(); Array.Clear(mask, 0, mask.Length); }
        public void PaintStroke(Vector2 from, Vector2 to, bool erase = false)
        {
            EnsureValid(); ValidatePoint(from); ValidatePoint(to);
            float r = Mathf.Min(brushDiameter * .5f, MaxDimension * 2f);
            var delta = to - from; float lengthSquared = delta.sqrMagnitude;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(from.x, to.x) - r), 0, width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(from.x, to.x) + r), 0, width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(from.y, to.y) - r), 0, height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(from.y, to.y) + r), 0, height - 1);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                var point = new Vector2(x + .5f, y + .5f);
                float t = lengthSquared > 0 ? Mathf.Clamp01(Vector2.Dot(point - from, delta) / lengthSquared) : 0;
                if ((point - (from + t * delta)).sqrMagnitude <= r * r) mask[y * width + x] = erase ? (byte)0 : (byte)255;
            }
        }
        static void ValidatePoint(Vector2 p)
        {
            if (!Finite(p.x) || !Finite(p.y) || Mathf.Abs(p.x) > 100000 || Mathf.Abs(p.y) > 100000)
                throw new ArgumentException("Paint coordinates must be finite and near the canvas.");
        }
        public void FillRectangle(Vector2 from, Vector2 to, bool erase = false) => FillShape(from, to, false, erase);
        public void FillEllipse(Vector2 from, Vector2 to, bool erase = false) => FillShape(from, to, true, erase);
        void FillShape(Vector2 from, Vector2 to, bool ellipse, bool erase)
        {
            EnsureValid(); ValidatePoint(from); ValidatePoint(to);
            var min = Vector2.Min(from, to); var max = Vector2.Max(from, to);
            var center = (min + max) * .5f; var radii = (max - min) * .5f;
            if (radii.x <= 0 || radii.y <= 0) return;
            for (int y = Mathf.Max(0, Mathf.FloorToInt(min.y)); y < Mathf.Min(height, Mathf.CeilToInt(max.y)); y++)
                for (int x = Mathf.Max(0, Mathf.FloorToInt(min.x)); x < Mathf.Min(width, Mathf.CeilToInt(max.x)); x++)
                {
                    float px = x + .5f, py = y + .5f;
                    if (px < min.x || px > max.x || py < min.y || py > max.y) continue;
                    if (!ellipse || Mathf.Pow((px - center.x) / radii.x, 2) + Mathf.Pow((py - center.y) / radii.y, 2) <= 1)
                        mask[y * width + x] = erase ? (byte)0 : (byte)255;
                }
        }
        public void LoadTerrainFile(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg") throw new ArgumentException("Choose a PNG or JPG image.");
            if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new ArgumentException("Choose an image smaller than 64 MB.");
            var source = Decode(File.ReadAllBytes(path));
            try { terrainPng = source.EncodeToPNG(); } finally { UnityEngine.Object.DestroyImmediate(source); }
        }
        public void SetTerrain(Texture2D source)
        {
            if (!source) throw new ArgumentNullException(nameof(source));
            ValidateSize(source.width, source.height);
            var copy = ReadTexture(source, new Rect(0, 0, source.width, source.height));
            try { terrainPng = copy.EncodeToPNG(); } finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
        public void SetTerrain(Sprite source)
        {
            if (!source) throw new ArgumentNullException(nameof(source));
            if (source.packed && source.packingRotation != SpritePackingRotation.None)
                throw new ArgumentException("Use the source texture for rotated packed sprites.");
            var copy = ReadTexture(source.texture, source.textureRect);
            try { terrainPng = copy.EncodeToPNG(); } finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
        static Texture2D ReadTexture(Texture source, Rect rect)
        {
            ValidateSize(source.width, source.height); ValidateSize((int)rect.width, (int)rect.height);
            var previous = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D result = null;
            try
            {
                Graphics.Blit(source, temporary); RenderTexture.active = temporary;
                result = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
                result.ReadPixels(rect, 0, 0); result.Apply(); return result;
            }
            catch { if (result) UnityEngine.Object.DestroyImmediate(result); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); }
        }
        static Texture2D Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) throw new ArgumentException("Choose a terrain image first.");
            if (bytes.Length > 64 * 1024 * 1024) throw new ArgumentException("Choose an image smaller than 64 MB.");
            if (bytes.Length >= 24 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71)
            {
                long w = ((long)bytes[16] << 24) | ((long)bytes[17] << 16) | ((long)bytes[18] << 8) | bytes[19];
                long h = ((long)bytes[20] << 24) | ((long)bytes[21] << 16) | ((long)bytes[22] << 8) | bytes[23];
                if (w < 1 || h < 1 || w > MaxDimension || h > MaxDimension) throw new ArgumentException("Source image cannot exceed 4096 pixels on either side.");
            }
            // JPEG Start Of Frame dimensions are checked before Unity allocates the decoded bitmap.
            if (bytes.Length > 4 && bytes[0] == 0xff && bytes[1] == 0xd8)
            {
                int offset = 2;
                while (offset + 3 < bytes.Length)
                {
                    if (bytes[offset++] != 0xff) continue;
                    while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
                    if (offset >= bytes.Length) break;
                    int marker = bytes[offset++];
                    if (marker == 0xd9 || marker == 0xda) break;
                    if (marker == 0x01 || (marker >= 0xd0 && marker <= 0xd8)) continue;
                    if (offset + 1 >= bytes.Length) break;
                    int length = (bytes[offset] << 8) | bytes[offset + 1];
                    if (length < 2 || offset + length > bytes.Length) break;
                    bool frame = marker >= 0xc0 && marker <= 0xcf && marker != 0xc4 && marker != 0xc8 && marker != 0xcc;
                    if (frame && length >= 7)
                    {
                        ValidateSize((bytes[offset + 5] << 8) | bytes[offset + 6], (bytes[offset + 3] << 8) | bytes[offset + 4]);
                        break;
                    }
                    offset += length;
                }
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(bytes)) throw new ArgumentException("Image is not a valid PNG or JPG.");
                ValidateSize(texture.width, texture.height); return texture;
            }
            catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
        }
        public Texture2D CreateTexture()
        {
            EnsureValid(); Texture2D source = null;
            try
            {
                if (terrainPng != null && terrainPng.Length > 0) source = Decode(terrainPng);
                var pixels = new Color32[width * height];
                var sourcePixels = source ? source.GetPixels32() : null;
                int sw = source ? source.width : 1, sh = source ? source.height : 1;
                float tw = (tileWidth > 0 ? tileWidth : sw) * tileScale, th = (tileHeight > 0 ? tileHeight : sh) * tileScale;
                if (!Finite(tw) || !Finite(th) || tw < .01f || th < .01f) throw new ArgumentException("Effective tile dimensions must be finite and at least 0.01 pixels.");
                float scale = textureMode == MapTextureMode.FitWidth ? width / (float)sw : height / (float)sh;
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    int index = y * width + x; if (mask[index] == 0) continue;
                    Color32 color = new Color32(139, 100, 60, 255);
                    if (source)
                    {
                        float u, v;
                        if (textureMode == MapTextureMode.Tile) { u = Mathf.Repeat((x + .5f) / tw, 1); v = Mathf.Repeat((y + .5f) / th, 1); }
                        else if (textureMode == MapTextureMode.Stretch) { u = (x + .5f) / width; v = (y + .5f) / height; }
                        else { u = ((x + .5f - width * .5f) / scale + sw * .5f) / sw; v = ((y + .5f - height * .5f) / scale + sh * .5f) / sh; }
                        if (u < 0 || v < 0 || u >= 1 || v >= 1) continue;
                        color = sourcePixels[Mathf.Min(sh - 1, (int)(v * sh)) * sw + Mathf.Min(sw - 1, (int)(u * sw))];
                    }
                    color.a = (byte)((color.a * mask[index] + 127) / 255); pixels[index] = color;
                }
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = mapName, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                texture.SetPixels32(pixels); texture.Apply(); return texture;
            }
            finally { if (source) UnityEngine.Object.DestroyImmediate(source); }
        }
    }
}


