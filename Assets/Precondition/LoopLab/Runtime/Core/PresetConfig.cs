using UnityEngine;

namespace Precondition.LoopLab
{
    public abstract class PresetConfig : ScriptableObject
    {
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string shaderName = string.Empty;
        [SerializeField] private Color previewTint = Color.white;

        public string DisplayName => displayName;
        public string ShaderName => shaderName;
        public Color PreviewTint => previewTint;

        protected void ApplyDefaults(string newDisplayName, string newShaderName, Color newPreviewTint)
        {
            displayName = newDisplayName;
            shaderName = newShaderName;
            previewTint = newPreviewTint;
        }

        public abstract void ResetToDefaults();
    }
}
