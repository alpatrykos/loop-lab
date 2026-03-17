using UnityEngine;

namespace Precondition.LoopLab
{
    [CreateAssetMenu(fileName = "GeometricPreset", menuName = "LoopLab/Presets/Geometric")]
    public sealed class GeometricPreset : PresetConfig
    {
        public override void ResetToDefaults()
        {
            ApplyDefaults(
                LoopLabPresetCatalog.GetDisplayName(LoopLabPresetKind.Geometric),
                LoopLabPresetCatalog.GetShaderName(LoopLabPresetKind.Geometric),
                LoopLabPresetCatalog.GetAccentColor(LoopLabPresetKind.Geometric));
        }
    }
}
