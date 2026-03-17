using System;
using System.Linq;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Precondition.LoopLab.Editor.Export
{
    public static class Mp4Exporter
    {
        private const string RecorderPackageName = "com.unity.recorder";

        public static string Export(LoopLabRenderSettings settings, string outputDirectory)
        {
            var request = new LoopLabExportRequest("MP4", ".mp4", settings, outputDirectory);
            if (!LoopLabFfmpegUtility.TryResolveFfmpegExecutable(out var ffmpegPath))
            {
                var missingFfmpegMessage = BuildMissingDependencyMessage(request);
                Debug.LogWarning($"[LoopLabExport] {missingFfmpegMessage}");
                throw new InvalidOperationException(missingFfmpegMessage);
            }

            var finalOutputPath = LoopLabExportSession.Run(request, workspace =>
            {
                LoopLabExportFrameWriter.WritePngFrames(request.Settings, workspace);
                LoopLabFfmpegUtility.EncodePngSequenceToMp4(
                    ffmpegPath,
                    System.IO.Path.Combine(workspace.FramesDirectoryPath, "frame-%04d.png"),
                    request.Settings.FrameCount,
                    request.Settings.FramesPerSecond,
                    workspace.StagedOutputPath);
            });

            Debug.Log($"[LoopLabExport] MP4 export created {finalOutputPath}.");
            return finalOutputPath;
        }

        private static string BuildMissingDependencyMessage(LoopLabExportRequest request)
        {
            var builder = new StringBuilder();
            builder.Append("MP4 export requires ffmpeg for the current LoopLab export path. ");

            var recorderPackage = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(packageInfo => string.Equals(packageInfo.name, RecorderPackageName, StringComparison.Ordinal));

            if (recorderPackage == null)
            {
                builder.Append("Unity Recorder is not installed in this project. ");
            }
            else
            {
                builder.Append(
                    $"Unity Recorder {recorderPackage.version} is installed, but no Recorder bridge is configured for this LoopLab export flow. ");
            }

            builder.Append($"No ffmpeg executable was found via {LoopLabFfmpegUtility.FfmpegSearchDescription}. ");
            builder.Append("No output file was written. ");
            builder.Append(request.SettingsSummary);
            return builder.ToString();
        }
    }
}
