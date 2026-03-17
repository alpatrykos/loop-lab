using UnityEngine;

namespace Precondition.LoopLab
{
    public static class LoopLabPresetCatalog
    {
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
            return preset switch
            {
                LoopLabPresetKind.Landscape => new Color(0.17f, 0.28f, 0.41f),
                LoopLabPresetKind.Fluid => new Color(0.04f, 0.25f, 0.52f),
                LoopLabPresetKind.Geometric => new Color(0.08f, 0.09f, 0.13f),
                _ => Color.gray
            };
        }

        public static Color GetAccentColor(LoopLabPresetKind preset)
        {
            return preset switch
            {
                LoopLabPresetKind.Landscape => new Color(0.97f, 0.72f, 0.47f),
                LoopLabPresetKind.Fluid => new Color(0.18f, 0.84f, 0.88f),
                LoopLabPresetKind.Geometric => new Color(0.95f, 0.44f, 0.18f),
                _ => Color.white
            };
        }

        public static float GetGridScale(LoopLabPresetKind preset)
        {
            return preset switch
            {
                LoopLabPresetKind.Landscape => 5.5f,
                LoopLabPresetKind.Fluid => 6f,
                LoopLabPresetKind.Geometric => 8f,
                _ => 5f
            };
        }
    }
}
