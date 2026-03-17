using UnityEngine;

namespace Precondition.LoopLab
{
    [CreateAssetMenu(fileName = "FluidPreset", menuName = "LoopLab/Presets/Fluid")]
    public sealed class FluidPreset : PresetConfig
    {
        public override void ResetToDefaults()
        {
            ApplyDefaults(
                LoopLabPresetCatalog.GetDisplayName(LoopLabPresetKind.Fluid),
                LoopLabPresetCatalog.GetShaderName(LoopLabPresetKind.Fluid),
                LoopLabPresetCatalog.GetAccentColor(LoopLabPresetKind.Fluid));
        }
    }
}
