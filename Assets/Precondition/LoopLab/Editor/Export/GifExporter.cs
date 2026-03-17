using System;
using Debug = UnityEngine.Debug;

namespace Precondition.LoopLab.Editor.Export
{
    public static class GifExporter
    {
        public static string Export(LoopLabRenderSettings settings, string outputDirectory)
        {
            var request = new LoopLabExportRequest("GIF", ".gif", settings, outputDirectory);
            if (!LoopLabFfmpegLocator.TryResolveExecutable(out _, out var resolutionSource))
            {
                var missingFfmpegMessage = LoopLabFfmpegLocator.GetMissingExecutableMessage(request.FormatLabel, request.Settings, resolutionSource);
                Debug.LogWarning($"[LoopLabExport] {missingFfmpegMessage}");
                throw new InvalidOperationException(missingFfmpegMessage);
            }

            return LoopLabExportSession.Run(request, workspace =>
            {
                LoopLabExportFrameWriter.WritePngFrames(request.Settings, workspace);

                throw new InvalidOperationException(
                    "GIF export encoding is not implemented yet. " +
                    "Temporary frames were cleaned up and no output file was written. " +
                    request.SettingsSummary);
            });
        }
    }
}
