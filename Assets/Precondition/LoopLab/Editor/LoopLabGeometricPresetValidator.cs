using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Precondition.LoopLab.Editor
{
    public static class LoopLabGeometricPresetValidator
    {
        private const float EdgeTolerance = 0.01f;
        private const float LoopTolerance = 0.001f;

        [MenuItem("Precondition/LoopLab/Validate Geometric Edges", priority = 131)]
        public static void RunFromMenu()
        {
            Run();
        }

        public static void Run()
        {
            var outputDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "log", "geometric-validation"));
            Directory.CreateDirectory(outputDirectory);

            using var renderer = new LoopRenderer();

            foreach (var resolution in new[] { 256, 512, 1024 })
            {
                ValidateResolution(renderer, outputDirectory, resolution);
            }

            Debug.Log("LoopLab Geometric edge validation passed.");
        }

        private static void ValidateResolution(LoopRenderer renderer, string outputDirectory, int resolution)
        {
            var settings = LoopLabRenderSettings.Default;
            settings.Preset = LoopLabPresetKind.Geometric;
            settings.Resolution = resolution;

            var startFrame = CaptureTexture(renderer.Render(settings, 0), settings.ClampedResolution);
            var wrappedFrame = CaptureTexture(renderer.Render(settings, settings.FrameCount), settings.ClampedResolution);

            try
            {
                var horizontalEdgeDelta = AverageEdgeDelta(startFrame, compareHorizontalEdges: true);
                var verticalEdgeDelta = AverageEdgeDelta(startFrame, compareHorizontalEdges: false);
                var loopDelta = AverageDelta(startFrame, wrappedFrame);

                if (loopDelta > LoopTolerance)
                {
                    throw new InvalidOperationException($"Geometric loop drifted at {resolution}px: {loopDelta:0.0000}");
                }

                if (horizontalEdgeDelta > EdgeTolerance || verticalEdgeDelta > EdgeTolerance)
                {
                    throw new InvalidOperationException(
                        $"Geometric edges are not tile-safe at {resolution}px. Horizontal={horizontalEdgeDelta:0.0000}, Vertical={verticalEdgeDelta:0.0000}");
                }

                WritePng(startFrame, Path.Combine(outputDirectory, $"geometric-start-{resolution}.png"));

                Debug.Log(
                    $"Geometric {resolution}px validation passed. loop={loopDelta:0.0000}, edges=({horizontalEdgeDelta:0.0000}, {verticalEdgeDelta:0.0000})");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(startFrame);
                UnityEngine.Object.DestroyImmediate(wrappedFrame);
            }
        }

        private static Texture2D CaptureTexture(Texture source, int resolution)
        {
            if (source == null)
            {
                throw new InvalidOperationException("Loop renderer returned a null texture.");
            }

            var temporary = source as RenderTexture;
            var createdTemporary = false;

            if (temporary == null)
            {
                temporary = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, temporary);
                createdTemporary = true;
            }

            var previousActive = RenderTexture.active;
            var capture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);

            try
            {
                RenderTexture.active = temporary;
                capture.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0);
                capture.Apply();
                return capture;
            }
            finally
            {
                RenderTexture.active = previousActive;

                if (createdTemporary)
                {
                    RenderTexture.ReleaseTemporary(temporary);
                }
            }
        }

        private static float AverageDelta(Texture2D left, Texture2D right)
        {
            var leftPixels = left.GetPixels32();
            var rightPixels = right.GetPixels32();
            var total = 0f;

            for (var index = 0; index < leftPixels.Length; index++)
            {
                total += ChannelDelta(leftPixels[index], rightPixels[index]);
            }

            return total / leftPixels.Length;
        }

        private static float AverageEdgeDelta(Texture2D texture, bool compareHorizontalEdges)
        {
            var total = 0f;
            var samples = compareHorizontalEdges ? texture.height : texture.width;

            for (var index = 0; index < samples; index++)
            {
                var left = compareHorizontalEdges
                    ? texture.GetPixel(0, index)
                    : texture.GetPixel(index, 0);
                var right = compareHorizontalEdges
                    ? texture.GetPixel(texture.width - 1, index)
                    : texture.GetPixel(index, texture.height - 1);
                total += ChannelDelta(left, right);
            }

            return total / samples;
        }

        private static float ChannelDelta(Color32 left, Color32 right)
        {
            return (
                Mathf.Abs(left.r - right.r) +
                Mathf.Abs(left.g - right.g) +
                Mathf.Abs(left.b - right.b)) / (255f * 3f);
        }

        private static void WritePng(Texture2D texture, string path)
        {
            var bytes = texture.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
        }
    }
}
