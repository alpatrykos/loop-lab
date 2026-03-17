using UnityEngine;

namespace Precondition.LoopLab
{
    public static class LoopLabPresetCatalog
    {
        public static bool SupportsContrastMode(LoopLabPresetKind preset)
        {
            return false;
        }

        public static string GetDisplayName(LoopLabPresetKind preset)
        {
            return preset switch
            {
                LoopLabPresetKind.Landscape => "Landscape - Dawn Ridge",
                LoopLabPresetKind.Fluid => "Fluid - Azure Vortex",
                LoopLabPresetKind.Geometric => "Geometric - Brass Lattice",
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
                LoopLabPresetKind.Landscape => new Color(0.15f, 0.24f, 0.38f),
                LoopLabPresetKind.Fluid => new Color(0.03f, 0.13f, 0.30f),
                LoopLabPresetKind.Geometric => new Color(0.06f, 0.07f, 0.10f),
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
                LoopLabPresetKind.Landscape => new Color(0.98f, 0.63f, 0.38f),
                LoopLabPresetKind.Fluid => new Color(0.30f, 0.90f, 0.95f),
                LoopLabPresetKind.Geometric => new Color(0.95f, 0.74f, 0.29f),
                _ => Color.white
            };
        }

        public static float GetGridScale(LoopLabPresetKind preset)
        {
            return preset switch
            {
                LoopLabPresetKind.Landscape => 4.9f,
                LoopLabPresetKind.Fluid => 4.75f,
                LoopLabPresetKind.Geometric => 6.5f,
                _ => 5f
            };
        }
    }
}
