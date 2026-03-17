using System;
using System.IO;
using Precondition.LoopLab.Editor.Export;
using UnityEngine;

namespace Precondition.LoopLab.Editor
{
    public static class LoopLabMp4ExporterBatchValidation
    {
        private const float LoopTolerance = 0.001f;
        private const float EncodedFrameTolerance = 0.08f;

        public static void Run()
        {
            var outputDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "log", "mp4-validation"));
            Directory.CreateDirectory(outputDirectory);

            if (!LoopLabFfmpegUtility.TryResolveFfmpegExecutable(out var ffmpegPath))
            {
                throw new InvalidOperationException(
                    "MP4 export validation requires ffmpeg. No executable was found via " +
                    LoopLabFfmpegUtility.FfmpegSearchDescription + ".");
            }

            foreach (LoopLabPresetKind preset in Enum.GetValues(typeof(LoopLabPresetKind)))
            {
                ValidatePreset(outputDirectory, ffmpegPath, preset);
            }

            Debug.Log("LoopLab MP4 export validation passed.");
        }

        private static void ValidatePreset(string outputDirectory, string ffmpegPath, LoopLabPresetKind preset)
        {
            var settings = LoopLabRenderSettings.Default;
            settings.Preset = preset;
            settings.ContrastMode = LoopLabContrastMode.High;
            settings.DurationSeconds = 3f;
            settings.FramesPerSecond = 24;
            settings.Resolution = 256;
            settings.Seed = LoopLabRenderSettings.DefaultSeedValue + ((int)preset * 101);

            var validatedSettings = settings.GetValidated();
            var outputPath = Mp4Exporter.Export(validatedSettings, outputDirectory);
            var outputFile = new FileInfo(outputPath);

            if (!outputFile.Exists || outputFile.Length == 0)
            {
                throw new InvalidOperationException($"MP4 export did not create a non-empty file for {preset}.");
            }

            var streamInfo = LoopLabFfmpegUtility.ProbeVideoStream(ffmpegPath, outputPath);
            if (!string.Equals(streamInfo.CodecName, "h264", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Expected H.264 video for {preset}, got {streamInfo.CodecName}.");
            }

            if (!string.Equals(streamInfo.PixelFormat, "yuv420p", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Expected yuv420p pixels for {preset}, got {streamInfo.PixelFormat}.");
            }

            if (streamInfo.Width != validatedSettings.ClampedResolution || streamInfo.Height != validatedSettings.ClampedResolution)
            {
                throw new InvalidOperationException(
                    $"Unexpected output dimensions for {preset}: {streamInfo.Width}x{streamInfo.Height}.");
            }

            if (streamInfo.FrameCount != validatedSettings.FrameCount)
            {
                throw new InvalidOperationException(
                    $"Expected {validatedSettings.FrameCount} frames for {preset}, got {streamInfo.FrameCount}.");
            }

            using var renderer = new LoopRenderer();
            var firstFrame = LoopLabTextureCaptureUtility.CaptureTexture(renderer.Render(validatedSettings, 0), validatedSettings.ClampedResolution);
            var wrappedFrame = LoopLabTextureCaptureUtility.CaptureTexture(
                renderer.Render(validatedSettings, validatedSettings.FrameCount),
                validatedSettings.ClampedResolution);
            var middleFrameIndex = validatedSettings.FrameCount / 2;
            var middleFrame = LoopLabTextureCaptureUtility.CaptureTexture(
                renderer.Render(validatedSettings, middleFrameIndex),
                validatedSettings.ClampedResolution);

            var encodedFirstFramePath = Path.Combine(outputDirectory, $"{preset.ToString().ToLowerInvariant()}-encoded-first.png");
            var encodedMiddleFramePath = Path.Combine(outputDirectory, $"{preset.ToString().ToLowerInvariant()}-encoded-mid.png");

            try
            {
                var loopDelta = AverageDelta(firstFrame, wrappedFrame);
                if (loopDelta > LoopTolerance)
                {
                    throw new InvalidOperationException($"Loop boundary drifted for {preset}: {loopDelta:0.0000}");
                }

                LoopLabFfmpegUtility.ExtractFrameAtTime(ffmpegPath, outputPath, 0d, encodedFirstFramePath);
                LoopLabFfmpegUtility.ExtractFrameAtTime(
                    ffmpegPath,
                    outputPath,
                    middleFrameIndex / (double)validatedSettings.FramesPerSecond,
                    encodedMiddleFramePath);

                Texture2D encodedFirstFrame = null;
                Texture2D encodedMiddleFrame = null;

                try
                {
                    encodedFirstFrame = LoopLabTextureCaptureUtility.LoadPng(encodedFirstFramePath);
                    encodedMiddleFrame = LoopLabTextureCaptureUtility.LoadPng(encodedMiddleFramePath);

                    var firstFrameDelta = AverageDelta(firstFrame, encodedFirstFrame);
                    if (firstFrameDelta > EncodedFrameTolerance)
                    {
                        throw new InvalidOperationException(
                            $"Encoded first frame drifted too far for {preset}: {firstFrameDelta:0.0000}");
                    }

                    var middleFrameDelta = AverageDelta(middleFrame, encodedMiddleFrame);
                    if (middleFrameDelta > EncodedFrameTolerance)
                    {
                        throw new InvalidOperationException(
                            $"Encoded mid frame drifted too far for {preset}: {middleFrameDelta:0.0000}");
                    }
                }
                finally
                {
                    if (encodedFirstFrame != null)
                    {
                        UnityEngine.Object.DestroyImmediate(encodedFirstFrame);
                    }

                    if (encodedMiddleFrame != null)
                    {
                        UnityEngine.Object.DestroyImmediate(encodedMiddleFrame);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(firstFrame);
                UnityEngine.Object.DestroyImmediate(wrappedFrame);
                UnityEngine.Object.DestroyImmediate(middleFrame);
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

        private static float ChannelDelta(Color32 left, Color32 right)
        {
            return (
                Mathf.Abs(left.r - right.r) +
                Mathf.Abs(left.g - right.g) +
                Mathf.Abs(left.b - right.b)) / (255f * 3f);
        }
    }
}
