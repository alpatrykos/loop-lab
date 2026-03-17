using System;
using Debug = UnityEngine.Debug;

namespace Precondition.LoopLab.Editor.Export
{
    public static class Mp4Exporter
    {
        public static void Export(LoopLabRenderSettings settings, string outputDirectory)
        {
            var request = new LoopLabExportRequest("MP4", ".mp4", settings, outputDirectory);
            if (!LoopLabFfmpegLocator.TryResolveExecutable(out _, out var resolutionSource))
            {
                var missingFfmpegMessage = LoopLabFfmpegLocator.GetMissingExecutableMessage(request.FormatLabel, request.Settings, resolutionSource);
                Debug.LogWarning($"[LoopLabExport] {missingFfmpegMessage}");
                throw new InvalidOperationException(missingFfmpegMessage);
            }

            LoopLabExportSession.Run(request, workspace =>
            {
                LoopLabExportFrameWriter.WritePngFrames(request.Settings, workspace);

                throw new InvalidOperationException(
                    "MP4 export encoding is not implemented yet. " +
                    "Temporary frames were cleaned up and no output file was written. " +
                    request.SettingsSummary);
            });
        }
    }
}
