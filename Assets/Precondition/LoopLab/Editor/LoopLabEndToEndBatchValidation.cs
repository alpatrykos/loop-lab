using System;
using System.IO;
using Precondition.LoopLab.Editor.Export;
using UnityEngine;
using UnityEngine.Rendering;

namespace Precondition.LoopLab.Editor
{
    public static class LoopLabEndToEndBatchValidation
    {
        private static readonly float[] MatrixDurationsSeconds = { 2f, 3f, 4f };
        private static readonly int[] MatrixFramesPerSecond = { 12, 24, 60 };

        private const float SeamTolerance = 0.001f;
        private const float FrameAlignmentTolerance = 0.001f;
        private const float MotionTolerance = 0.004f;
        private const int MatrixResolution = 256;
        private const int PerformanceResolution = 512;
        private const int PerformancePreviewSamples = 6;
        private const double MaxAveragePreviewFrameMilliseconds = 150d;
        private const double MaxGifExportSeconds = 15d;
        private const double MaxMp4ExportSeconds = 15d;
        private const double MinGifThroughputFramesPerSecond = 5d;
        private const double MinMp4ThroughputFramesPerSecond = 5d;

        public static void Run()
        {
            EnsureGraphicsDeviceAvailable();
            LoopLabProjectBootstrap.RunBatchmode();

            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();

            ExecuteStep("boundary validation", LoopBoundaryValidationBatch.RunAllPresets);
            ExecuteStep("editor window validation", LoopLabWindowBatchValidation.Run);
            ExecuteStep("landscape preset validation", LoopLabLandscapePresetValidator.Run);
            ExecuteStep("fluid preset validation", LoopLabFluidPresetValidator.Run);
            ExecuteStep("GIF export validation", LoopLabGifExporterBatchValidation.Run);
            ExecuteStep("MP4 export validation", LoopLabMp4ExporterBatchValidation.Run);
            ExecuteStep("duration/FPS matrix validation", ValidateDurationFpsMatrix);
            ExecuteStep("performance sanity validation", ValidatePerformanceSanity);

            Debug.Log($"[LoopLabE2E] End-to-end validation passed in {totalStopwatch.Elapsed.TotalSeconds:0.00}s.");
        }

        private static void EnsureGraphicsDeviceAvailable()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                throw new InvalidOperationException(
                    "LoopLab end-to-end validation requires graphics-enabled batchmode. Run without -nographics.");
            }
        }

        private static void ExecuteStep(string label, Action action)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            action();
            Debug.Log($"[LoopLabE2E] {label} passed in {stopwatch.Elapsed.TotalSeconds:0.00}s.");
        }

        private static void ValidateDurationFpsMatrix()
        {
            using var renderer = new LoopRenderer();

            foreach (LoopLabPresetKind preset in Enum.GetValues(typeof(LoopLabPresetKind)))
            {
                foreach (var durationSeconds in MatrixDurationsSeconds)
                {
                    foreach (var framesPerSecond in MatrixFramesPerSecond)
                    {
                        var settings = BuildSettings(preset, durationSeconds, framesPerSecond, MatrixResolution);
                        ValidateTimingMatrixEntry(renderer, settings);
                    }
                }
            }
        }

        private static void ValidateTimingMatrixEntry(LoopRenderer renderer, LoopLabRenderSettings settings)
        {
            var validatedSettings = settings.GetValidated();
            var frameCount = validatedSettings.FrameCount;
            var previewLastElapsed = validatedSettings.DurationSeconds - (validatedSettings.DurationSeconds / frameCount);

            Texture2D startFrame = null;
            Texture2D wrappedFrame = null;
            Texture2D previewStartFrame = null;
            Texture2D previewWrappedFrame = null;
            Texture2D midpointFrame = null;
            Texture2D previewMidpointFrame = null;
            Texture2D lastFrame = null;
            Texture2D previewLastFrame = null;

            try
            {
                startFrame = LoopLabTextureCaptureUtility.CaptureTexture(renderer.Render(validatedSettings, 0), validatedSettings.ClampedResolution);
                wrappedFrame = LoopLabTextureCaptureUtility.CaptureTexture(renderer.Render(validatedSettings, frameCount), validatedSettings.ClampedResolution);
                previewStartFrame = LoopLabTextureCaptureUtility.CaptureTexture(renderer.RenderPreview(validatedSettings, 0f), validatedSettings.ClampedResolution);
                previewWrappedFrame = LoopLabTextureCaptureUtility.CaptureTexture(
                    renderer.RenderPreview(validatedSettings, validatedSettings.DurationSeconds),
                    validatedSettings.ClampedResolution);
                midpointFrame = LoopLabTextureCaptureUtility.CaptureTexture(renderer.Render(validatedSettings, frameCount / 2), validatedSettings.ClampedResolution);
                previewMidpointFrame = LoopLabTextureCaptureUtility.CaptureTexture(
                    renderer.RenderPreview(validatedSettings, validatedSettings.DurationSeconds * 0.5f),
                    validatedSettings.ClampedResolution);
                lastFrame = LoopLabTextureCaptureUtility.CaptureTexture(renderer.Render(validatedSettings, frameCount - 1), validatedSettings.ClampedResolution);
                previewLastFrame = LoopLabTextureCaptureUtility.CaptureTexture(
                    renderer.RenderPreview(validatedSettings, previewLastElapsed),
                    validatedSettings.ClampedResolution);

                var loopDelta = AverageDelta(startFrame, wrappedFrame);
                var previewLoopDelta = AverageDelta(previewStartFrame, previewWrappedFrame);
                var midpointAlignmentDelta = AverageDelta(midpointFrame, previewMidpointFrame);
                var lastFrameAlignmentDelta = AverageDelta(lastFrame, previewLastFrame);
                var motionDelta = AverageDelta(startFrame, midpointFrame);

                if (loopDelta > SeamTolerance)
                {
                    throw new InvalidOperationException($"Loop restart drifted for {Describe(validatedSettings)}: {loopDelta:0.0000}");
                }

                if (previewLoopDelta > SeamTolerance)
                {
                    throw new InvalidOperationException($"Preview restart drifted for {Describe(validatedSettings)}: {previewLoopDelta:0.0000}");
                }

                if (midpointAlignmentDelta > FrameAlignmentTolerance)
                {
                    throw new InvalidOperationException(
                        $"Preview midpoint frame misaligned for {Describe(validatedSettings)}: {midpointAlignmentDelta:0.0000}");
                }

                if (lastFrameAlignmentDelta > FrameAlignmentTolerance)
                {
                    throw new InvalidOperationException(
                        $"Preview last frame misaligned for {Describe(validatedSettings)}: {lastFrameAlignmentDelta:0.0000}");
                }

                if (motionDelta <= MotionTolerance)
                {
                    throw new InvalidOperationException($"Motion is too subtle for {Describe(validatedSettings)}: {motionDelta:0.0000}");
                }

                Debug.Log(
                    $"[LoopLabE2E] Matrix {Describe(validatedSettings)} loop={loopDelta:0.0000}, preview={previewLoopDelta:0.0000}, " +
                    $"mid={midpointAlignmentDelta:0.0000}, last={lastFrameAlignmentDelta:0.0000}, motion={motionDelta:0.0000}.");
            }
            finally
            {
                DestroyTexture(startFrame);
                DestroyTexture(wrappedFrame);
                DestroyTexture(previewStartFrame);
                DestroyTexture(previewWrappedFrame);
                DestroyTexture(midpointFrame);
                DestroyTexture(previewMidpointFrame);
                DestroyTexture(lastFrame);
                DestroyTexture(previewLastFrame);
            }
        }

        private static void ValidatePerformanceSanity()
        {
            var outputDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "log", "performance-validation"));
            Directory.CreateDirectory(outputDirectory);

            foreach (LoopLabPresetKind preset in Enum.GetValues(typeof(LoopLabPresetKind)))
            {
                var settings = BuildSettings(preset, 3f, 24, PerformanceResolution);
                ValidatePerformanceForPreset(outputDirectory, settings);
            }
        }

        private static void ValidatePerformanceForPreset(string outputDirectory, LoopLabRenderSettings settings)
        {
            var validatedSettings = settings.GetValidated();

            var averagePreviewFrameMilliseconds = MeasureAveragePreviewFrameMilliseconds(validatedSettings);
            if (averagePreviewFrameMilliseconds > MaxAveragePreviewFrameMilliseconds)
            {
                throw new InvalidOperationException(
                    $"Preview rendering is too slow for {Describe(validatedSettings)}: {averagePreviewFrameMilliseconds:0.0}ms average.");
            }

            var gifStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var gifPath = GifExporter.Export(validatedSettings, outputDirectory, GifExportOptions.Default);
            gifStopwatch.Stop();
            EnsureOutputExists(gifPath, "GIF", validatedSettings);

            var gifSeconds = Math.Max(gifStopwatch.Elapsed.TotalSeconds, 0.001d);
            var gifThroughput = validatedSettings.FrameCount / gifSeconds;
            if (gifSeconds > MaxGifExportSeconds || gifThroughput < MinGifThroughputFramesPerSecond)
            {
                throw new InvalidOperationException(
                    $"GIF export is too slow for {Describe(validatedSettings)}: {gifSeconds:0.00}s ({gifThroughput:0.0} fps).");
            }

            var mp4Stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var mp4Path = Mp4Exporter.Export(validatedSettings, outputDirectory);
            mp4Stopwatch.Stop();
            EnsureOutputExists(mp4Path, "MP4", validatedSettings);

            var mp4Seconds = Math.Max(mp4Stopwatch.Elapsed.TotalSeconds, 0.001d);
            var mp4Throughput = validatedSettings.FrameCount / mp4Seconds;
            if (mp4Seconds > MaxMp4ExportSeconds || mp4Throughput < MinMp4ThroughputFramesPerSecond)
            {
                throw new InvalidOperationException(
                    $"MP4 export is too slow for {Describe(validatedSettings)}: {mp4Seconds:0.00}s ({mp4Throughput:0.0} fps).");
            }

            Debug.Log(
                $"[LoopLabE2E] Performance {Describe(validatedSettings)} preview={averagePreviewFrameMilliseconds:0.0}ms, " +
                $"gif={gifSeconds:0.00}s ({gifThroughput:0.0} fps), mp4={mp4Seconds:0.00}s ({mp4Throughput:0.0} fps).");
        }

        private static double MeasureAveragePreviewFrameMilliseconds(LoopLabRenderSettings settings)
        {
            using var renderer = new LoopRenderer();

            renderer.RenderPreview(settings, 0f);

            var totalMilliseconds = 0d;
            for (var sampleIndex = 0; sampleIndex < PerformancePreviewSamples; sampleIndex++)
            {
                var elapsedSeconds = settings.DurationSeconds * ((sampleIndex + 1f) / (PerformancePreviewSamples + 1f));
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                renderer.RenderPreview(settings, elapsedSeconds);
                stopwatch.Stop();
                totalMilliseconds += stopwatch.Elapsed.TotalMilliseconds;
            }

            return totalMilliseconds / PerformancePreviewSamples;
        }

        private static LoopLabRenderSettings BuildSettings(
            LoopLabPresetKind preset,
            float durationSeconds,
            int framesPerSecond,
            int resolution)
        {
            var settings = LoopLabRenderSettings.Default;
            settings.Preset = preset;
            settings.ContrastMode = LoopLabContrastMode.High;
            settings.DurationSeconds = durationSeconds;
            settings.FramesPerSecond = framesPerSecond;
            settings.Resolution = resolution;
            settings.Seed = LoopLabRenderSettings.DefaultSeedValue + ((int)preset * 101);
            return settings;
        }

        private static void EnsureOutputExists(string outputPath, string formatLabel, LoopLabRenderSettings settings)
        {
            var outputFile = new FileInfo(outputPath);
            if (!outputFile.Exists || outputFile.Length == 0)
            {
                throw new InvalidOperationException($"{formatLabel} export did not create a non-empty file for {Describe(settings)}.");
            }
        }

        private static string Describe(LoopLabRenderSettings settings)
        {
            return $"{settings.Preset} {settings.DurationSeconds:0.##}s {settings.FramesPerSecond}fps {settings.ClampedResolution}px";
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

        private static float ChannelDelta(Color32 left, Color32 right)
        {
            return (
                Mathf.Abs(left.r - right.r) +
                Mathf.Abs(left.g - right.g) +
                Mathf.Abs(left.b - right.b)) / (255f * 3f);
        }

        private static void DestroyTexture(Texture texture)
        {
            if (texture == null)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
