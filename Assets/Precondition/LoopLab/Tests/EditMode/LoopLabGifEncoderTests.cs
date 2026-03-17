using NUnit.Framework;
using Precondition.LoopLab.Editor.Export;
using UnityEngine;

namespace Precondition.LoopLab.Tests
{
    public sealed class LoopLabGifEncoderTests
    {
        [Test]
        public void BuildFrameDelays_DistributesCentisecondsAcrossFrames()
        {
            Assert.That(GifEncoder.BuildFrameDelays(6, 24), Is.EqualTo(new[] { 4, 4, 4, 5, 4, 4 }));
        }

        [Test]
        public void Encode_WritesLoopingGifWithExpectedMetadata()
        {
            var frames = new[]
            {
                CreateSolidFrame(2, 2, new Color32(255, 64, 32, 255)),
                CreateSolidFrame(2, 2, new Color32(32, 192, 255, 255)),
                CreateSolidFrame(2, 2, new Color32(16, 24, 200, 255))
            };

            var gifBytes = GifEncoder.Encode(
                frames,
                2,
                2,
                24,
                new GifExportOptions
                {
                    Dithering = GifDitheringMode.None
                });
            var inspection = GifInspector.Inspect(gifBytes);

            Assert.That(inspection.Version, Is.EqualTo("GIF89a"));
            Assert.That(inspection.Width, Is.EqualTo(2));
            Assert.That(inspection.Height, Is.EqualTo(2));
            Assert.That(inspection.IsInfiniteLoop, Is.True);
            Assert.That(inspection.FrameCount, Is.EqualTo(frames.Length));
            Assert.That(inspection.FrameDelays, Is.EqualTo(GifEncoder.BuildFrameDelays(frames.Length, 24)));
            Assert.That(gifBytes[^1], Is.EqualTo(0x3B));
        }

        [Test]
        public void Encode_SupportsFloydSteinbergDithering()
        {
            var frames = new[]
            {
                CreateGradientFrame(4, 4, 0),
                CreateGradientFrame(4, 4, 32)
            };

            var gifBytes = GifEncoder.Encode(
                frames,
                4,
                4,
                12,
                new GifExportOptions
                {
                    Dithering = GifDitheringMode.FloydSteinberg
                });
            var inspection = GifInspector.Inspect(gifBytes);

            Assert.That(inspection.FrameCount, Is.EqualTo(frames.Length));
            Assert.That(inspection.IsInfiniteLoop, Is.True);
            Assert.That(inspection.FrameDelays, Is.EqualTo(GifEncoder.BuildFrameDelays(frames.Length, 12)));
        }

        private static Color32[] CreateSolidFrame(int width, int height, Color32 color)
        {
            var pixels = new Color32[width * height];
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index] = color;
            }

            return pixels;
        }

        private static Color32[] CreateGradientFrame(int width, int height, byte offset)
        {
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = (y * width) + x;
                    pixels[index] = new Color32(
                        (byte)(offset + (x * 28)),
                        (byte)(offset + (y * 28)),
                        (byte)(offset + ((x + y) * 18)),
                        255);
                }
            }

            return pixels;
        }
    }
}
