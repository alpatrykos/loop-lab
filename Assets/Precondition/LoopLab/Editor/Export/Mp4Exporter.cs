using System;

namespace Precondition.LoopLab.Editor.Export
{
    public static class Mp4Exporter
    {
        public static void Export(LoopLabRenderSettings settings, string outputDirectory)
        {
            throw new InvalidOperationException(
                $"MP4 export is not implemented in AUT-74. This scaffold only establishes the project structure. " +
                $"Received generated settings: seed {settings.Seed}, {settings.FrameCount} frames @ {settings.FramesPerSecond} FPS.");
        }
    }
}
