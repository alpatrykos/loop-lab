namespace Precondition.LoopLab.Editor.Export
{
    public enum GifDitheringMode
    {
        None = 0,
        FloydSteinberg = 1
    }

    public struct GifExportOptions
    {
        public static GifExportOptions Default => new()
        {
            Dithering = GifDitheringMode.None
        };

        public GifDitheringMode Dithering;
    }
}
