using UnityEngine;

namespace Precondition.LoopLab
{
    [CreateAssetMenu(fileName = "LandscapePreset", menuName = "LoopLab/Presets/Landscape")]
    public sealed class LandscapePreset : PresetConfig
    {
        public override void ResetToDefaults()
        {
            ApplyDefaults(
                LoopLabPresetCatalog.GetDisplayName(LoopLabPresetKind.Landscape),
                LoopLabPresetCatalog.GetShaderName(LoopLabPresetKind.Landscape),
                LoopLabPresetCatalog.GetAccentColor(LoopLabPresetKind.Landscape));
        }
    }
}
