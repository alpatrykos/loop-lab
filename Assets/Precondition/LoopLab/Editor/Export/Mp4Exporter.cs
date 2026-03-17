using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Precondition.LoopLab.Editor.Export
{
    public static class Mp4Exporter
    {
        private const string RecorderPackageName = "com.unity.recorder";

        public static string Export(LoopLabRenderSettings settings, string outputDirectory)
        {
            var validatedSettings = settings.GetValidated();

            if (!LoopLabFfmpegUtility.TryResolveFfmpegExecutable(out var ffmpegPath))
            {
                throw new InvalidOperationException(BuildMissingDependencyMessage());
            }

            Directory.CreateDirectory(outputDirectory);

            var outputPath = Path.Combine(outputDirectory, BuildOutputFileName(validatedSettings));
            var temporaryFrameDirectory = Path.Combine(
                Path.GetTempPath(),
                "LoopLab",
                "mp4-export-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(temporaryFrameDirectory);

            var exportSucceeded = false;
            try
            {
                WriteFrameSequence(validatedSettings, temporaryFrameDirectory);
                LoopLabFfmpegUtility.EncodePngSequenceToMp4(
                    ffmpegPath,
                    Path.Combine(temporaryFrameDirectory, "frame_%04d.png"),
                    validatedSettings.FrameCount,
                    validatedSettings.FramesPerSecond,
                    outputPath);

                exportSucceeded = true;
                Debug.Log($"LoopLab MP4 export created {outputPath}.");
                return outputPath;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                throw new InvalidOperationException(
                    $"{exception.Message} Temporary PNG sequence kept at {temporaryFrameDirectory}.",
                    exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();

                if (exportSucceeded)
                {
                    try
                    {
                        if (Directory.Exists(temporaryFrameDirectory))
                        {
                            Directory.Delete(temporaryFrameDirectory, true);
                        }
                    }
                    catch (Exception cleanupException)
                    {
                        Debug.LogWarning(
                            $"LoopLab MP4 export completed but could not remove temporary frames at {temporaryFrameDirectory}: {cleanupException.Message}");
                    }
                }
            }
        }

        private static void WriteFrameSequence(LoopLabRenderSettings settings, string outputDirectory)
        {
            using var renderer = new LoopRenderer();

            try
            {
                for (var frameIndex = 0; frameIndex < settings.FrameCount; frameIndex++)
                {
                    if (!Application.isBatchMode)
                    {
                        var progress = settings.FrameCount <= 1
                            ? 1f
                            : frameIndex / (float)(settings.FrameCount - 1);
                        EditorUtility.DisplayProgressBar(
                            "LoopLab MP4 Export",
                            $"Rendering frame {frameIndex + 1} of {settings.FrameCount}.",
                            progress);
                    }

                    var frameTexture = LoopLabTextureCaptureUtility.CaptureTexture(
                        renderer.Render(settings, frameIndex),
                        settings.ClampedResolution);

                    try
                    {
                        LoopLabTextureCaptureUtility.WritePng(
                            frameTexture,
                            Path.Combine(outputDirectory, $"frame_{frameIndex:D4}.png"));
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(frameTexture);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static string BuildOutputFileName(LoopLabRenderSettings settings)
        {
            var presetName = settings.Preset.ToString().ToLowerInvariant();
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            return $"looplab-{presetName}-{settings.ClampedResolution}px-{settings.FramesPerSecond}fps-seed{settings.Seed}-{timestamp}.mp4";
        }

        private static string BuildMissingDependencyMessage()
        {
            var builder = new StringBuilder();
            builder.Append("MP4 export requires ffmpeg for this editor-first export path. ");

            var recorderPackage = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(packageInfo => string.Equals(packageInfo.name, RecorderPackageName, StringComparison.Ordinal));

            if (recorderPackage == null)
            {
                builder.Append("Unity Recorder is not installed in this project. ");
            }
            else
            {
                builder.Append($"Unity Recorder {recorderPackage.version} is installed, but no Recorder bridge is configured for this LoopLab export flow. ");
            }

            builder.Append($"No ffmpeg executable was found via {LoopLabFfmpegUtility.FfmpegSearchDescription}.");
            return builder.ToString();
        }
    }
}
