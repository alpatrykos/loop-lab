using System;
using System.IO;
using Precondition.LoopLab.Editor.Export;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Precondition.LoopLab.Editor
{
    public static class LoopLabFluidPresetValidator
    {
        private const float EdgeTolerance = 0.018f;
        private const float LoopTolerance = 0.001f;
        private const float MotionLowerBound = 0.01f;

        [MenuItem("Precondition/LoopLab/Validate Fluid Edges", priority = 132)]
        public static void RunFromMenu()
        {
            Run();
        }

        public static void Run()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                throw new InvalidOperationException(
                    "LoopLab Fluid preset validation requires graphics-enabled batchmode. Run without -nographics.");
            }

            var outputDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "log", "fluid-validation"));
            Directory.CreateDirectory(outputDirectory);

            using var renderer = new LoopRenderer();

            foreach (var durationSeconds in new[] { 2f, 3f, 4f })
            {
                ValidateDuration(renderer, outputDirectory, durationSeconds);
            }

            Debug.Log("LoopLab Fluid preset validation passed.");
        }

        private static void ValidateDuration(LoopRenderer renderer, string outputDirectory, float durationSeconds)
        {
            var settings = LoopLabRenderSettings.Default;
            settings.Preset = LoopLabPresetKind.Fluid;
            settings.DurationSeconds = durationSeconds;
            settings.Resolution = 256;

            var startFrame = LoopLabTextureCaptureUtility.CaptureTexture(renderer.Render(settings, 0), settings.ClampedResolution);
            var wrappedFrame = LoopLabTextureCaptureUtility.CaptureTexture(renderer.Render(settings, settings.FrameCount), settings.ClampedResolution);
            var previewStart = LoopLabTextureCaptureUtility.CaptureTexture(renderer.RenderPreview(settings, 0f), settings.ClampedResolution);
            var previewLoop = LoopLabTextureCaptureUtility.CaptureTexture(renderer.RenderPreview(settings, durationSeconds), settings.ClampedResolution);
            var midFrame = LoopLabTextureCaptureUtility.CaptureTexture(renderer.Render(settings, settings.FrameCount / 2), settings.ClampedResolution);

            try
            {
                var loopDelta = AverageDelta(startFrame, wrappedFrame);
                var previewLoopDelta = AverageDelta(previewStart, previewLoop);
                var horizontalEdgeDelta = AverageEdgeDelta(startFrame, compareHorizontalEdges: true);
                var verticalEdgeDelta = AverageEdgeDelta(startFrame, compareHorizontalEdges: false);
                var motionDelta = AverageDelta(startFrame, midFrame);

                if (loopDelta > LoopTolerance)
                {
                    throw new InvalidOperationException($"Fluid loop drifted at {durationSeconds:0.0}s: {loopDelta:0.0000}");
                }

                if (previewLoopDelta > LoopTolerance)
                {
                    throw new InvalidOperationException($"Fluid preview loop drifted at {durationSeconds:0.0}s: {previewLoopDelta:0.0000}");
                }

                if (horizontalEdgeDelta > EdgeTolerance || verticalEdgeDelta > EdgeTolerance)
                {
                    throw new InvalidOperationException(
                        $"Fluid edges are not tile-safe at {durationSeconds:0.0}s. Horizontal={horizontalEdgeDelta:0.0000}, Vertical={verticalEdgeDelta:0.0000}");
                }

                if (motionDelta < MotionLowerBound)
                {
                    throw new InvalidOperationException($"Fluid motion is too subtle at {durationSeconds:0.0}s: {motionDelta:0.0000}");
                }

                var durationLabel = $"{durationSeconds:0}s";
                LoopLabTextureCaptureUtility.WritePng(startFrame, Path.Combine(outputDirectory, $"fluid-start-{durationLabel}.png"));
                LoopLabTextureCaptureUtility.WritePng(midFrame, Path.Combine(outputDirectory, $"fluid-mid-{durationLabel}.png"));

                Debug.Log(
                    $"Fluid {durationLabel} validation passed. loop={loopDelta:0.0000}, preview={previewLoopDelta:0.0000}, " +
                    $"edges=({horizontalEdgeDelta:0.0000}, {verticalEdgeDelta:0.0000}), motion={motionDelta:0.0000}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(startFrame);
                UnityEngine.Object.DestroyImmediate(wrappedFrame);
                UnityEngine.Object.DestroyImmediate(previewStart);
                UnityEngine.Object.DestroyImmediate(previewLoop);
                UnityEngine.Object.DestroyImmediate(midFrame);
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
    }
}
