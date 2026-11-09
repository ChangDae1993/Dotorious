using System;
using JYW.Game.ObjectMaker.Editor;
using NUnit.Framework;
using UnityEngine;

namespace JYW.Game.ObjectMaker.Tests
{
    public sealed class MapOperationTests
    {
        static MapPaintDocument Document(int width = 8, int height = 8)
        {
            var data = new MapPaintDocument { width = width, height = height }; data.EnsureValid(); return data;
        }
        [Test] public void BucketFillsEnclosedRegionWithoutCrossingWallAndCanEraseIt()
        {
            var d = Document(); d.FillRectangle(new Vector2(1, 1), new Vector2(7, 7)); d.FillRectangle(new Vector2(2, 2), new Vector2(6, 6), true);
            Assert.AreEqual(16, d.FloodFill(3, 3)); Assert.AreEqual(0, d.mask[0]); Assert.AreEqual(0, d.FloodFill(3, 3));
            Assert.AreEqual(36, d.FloodFill(3, 3, true)); foreach (byte pixel in d.mask) Assert.AreEqual(0, pixel);
        }
        [Test] public void BucketUsesFourNeighborsAndNonContiguousOptionIncludesDisconnectedRegions()
        {
            var d = Document(3, 3); d.FillRectangle(Vector2.zero, new Vector2(3, 3)); d.mask[0] = d.mask[4] = d.mask[8] = 0;
            Assert.AreEqual(1, d.FloodFill(0, 0)); Assert.AreEqual(0, d.mask[4]);
            Assert.AreEqual(2, d.FloodFill(1, 1, false, false)); Assert.AreEqual(0, d.FloodFill(-1, 1));
        }
        [Test] public void LargeEmptyRegionFillsWithoutRecursiveStackOverflow()
        {
            var d = Document(512, 512); Assert.AreEqual(512 * 512, d.FloodFill(0, 0)); Assert.AreEqual(255, d.mask[d.mask.Length - 1]);
        }
        [Test] public void TrimMatchesVisiblePixelsAndKeepsCanvasDimensionsAndInteriorHoles()
        {
            var d = Document(4, 4); d.FillRectangle(Vector2.zero, new Vector2(4, 4));
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                source.SetPixels32(new[] { new Color32(0, 255, 0, 255), new Color32(0, 255, 0, 0), new Color32(0, 255, 0, 127), new Color32(0, 255, 0, 255) }); source.Apply();
                d.terrainPng = source.EncodeToPNG(); d.alphaThreshold = 128;
                d.TrimTransparent(); Assert.AreEqual(4, d.width); Assert.AreEqual(4, d.height);
                Assert.AreEqual(255, d.mask[0]); Assert.AreEqual(0, d.mask[1]); Assert.AreEqual(0, d.mask[4]); Assert.AreEqual(255, d.mask[5]);
                var once = (byte[])d.mask.Clone(); d.TrimTransparent(); CollectionAssert.AreEqual(once, d.mask);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
        [Test] public void TrimRemovesFitMarginsButDoesNotFillUnpaintedPixels()
        {
            var d = Document(); d.FillRectangle(Vector2.zero, new Vector2(8, 8)); d.mask[3 * 8 + 3] = 0;
            var source = new Texture2D(2, 1);
            try
            {
                source.SetPixels(new[] { Color.white, Color.white }); source.Apply(); d.terrainPng = source.EncodeToPNG(); d.textureMode = MapTextureMode.FitWidth;
                d.TrimTransparent(); Assert.AreEqual(0, d.mask[0]); Assert.AreEqual(0, d.mask[7 * 8]); Assert.AreEqual(255, d.mask[2 * 8]); Assert.AreEqual(0, d.mask[3 * 8 + 3]);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
        [Test] public void FlipsAreReversibleOnRectangularCanvas()
        {
            var d = Document(5, 3); d.mask[1] = 255; var original = (byte[])d.mask.Clone();
            d.FlipMask(true); Assert.AreEqual(255, d.mask[3]); d.FlipMask(true); CollectionAssert.AreEqual(original, d.mask);
            d.FlipMask(false); Assert.AreEqual(255, d.mask[11]); d.FlipMask(false); CollectionAssert.AreEqual(original, d.mask);
        }
        [Test] public void MissingImageTrimDoesNotChangeMaskAndCutoffMigratesOldDraft()
        {
            var d = Document(); d.mask[0] = 255; Assert.Throws<InvalidOperationException>(() => d.TrimTransparent()); Assert.AreEqual(255, d.mask[0]);
            d.alphaThreshold = 0; d.EnsureValid(); Assert.AreEqual(1, d.alphaThreshold);
            d.alphaThreshold = 256; Assert.Throws<ArgumentOutOfRangeException>(() => d.EnsureValid());
        }
    }
}
