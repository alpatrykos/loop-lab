using UnityEngine;

namespace Precondition.LoopLab
{
    public static class LoopLabPresetCatalog
    {
        public static bool SupportsContrastMode(LoopLabPresetKind preset)
        {
            return preset == LoopLabPresetKind.Geometric;
        }

        public static string GetDisplayName(LoopLabPresetKind preset)
        {
            return preset switch
            {
                LoopLabPresetKind.Landscape => "Landscape - Dawn Ridge",
                LoopLabPresetKind.Fluid => "Fluid - Azure Vortex",
                LoopLabPresetKind.Geometric => "Geometric - Iso Hex Drift",
                _ => preset.ToString()
            };
        }

        public static string GetShaderName(LoopLabPresetKind preset)
        {
            return preset switch
            {
                LoopLabPresetKind.Landscape => "LoopLab/Landscape",
                LoopLabPresetKind.Fluid => "LoopLab/Fluid",
                LoopLabPresetKind.Geometric => "LoopLab/Geometric",
                _ => "LoopLab/Landscape"
            };
        }

        public static string GetMaterialResourcePath(LoopLabPresetKind preset)
        {
            return "Materials/" + preset + "Preview";
        }

        public static Color GetBaseColor(LoopLabPresetKind preset)
        {
            return GetBaseColor(preset, LoopLabContrastMode.High);
        }

        public static Color GetBaseColor(LoopLabPresetKind preset, LoopLabContrastMode contrastMode)
        {
            return preset switch
            {
                LoopLabPresetKind.Landscape => new Color(0.18f, 0.31f, 0.44f),
                LoopLabPresetKind.Fluid => new Color(0.04f, 0.25f, 0.52f),
                LoopLabPresetKind.Geometric when contrastMode == LoopLabContrastMode.Low => new Color(0.18f, 0.20f, 0.23f),
                LoopLabPresetKind.Geometric => new Color(0.04f, 0.05f, 0.08f),
                _ => Color.gray
            };
        }

        public static Color GetAccentColor(LoopLabPresetKind preset)
        {
            return GetAccentColor(preset, LoopLabContrastMode.High);
        }

        public static Color GetAccentColor(LoopLabPresetKind preset, LoopLabContrastMode contrastMode)
        {
            return preset switch
            {
                LoopLabPresetKind.Landscape => new Color(0.92f, 0.73f, 0.43f),
                LoopLabPresetKind.Fluid => new Color(0.18f, 0.84f, 0.88f),
                LoopLabPresetKind.Geometric when contrastMode == LoopLabContrastMode.Low => new Color(0.60f, 0.65f, 0.70f),
                LoopLabPresetKind.Geometric => new Color(0.97f, 0.76f, 0.27f),
                _ => Color.white
            };
        }

        public static float GetGridScale(LoopLabPresetKind preset)
        {
            return preset switch
            {
                LoopLabPresetKind.Landscape => 4.5f,
                LoopLabPresetKind.Fluid => 6f,
                LoopLabPresetKind.Geometric => 8f,
                _ => 5f
            };
        }
    }
}
