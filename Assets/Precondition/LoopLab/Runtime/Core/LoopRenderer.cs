using UnityEngine;
using Object = UnityEngine.Object;

namespace Precondition.LoopLab
{
    public sealed class LoopRenderer : System.IDisposable
    {
        private LoopLabPresetKind activePreset;
        private Material previewMaterial;
        private RenderTexture previewTexture;

        public Texture Render(LoopLabRenderSettings settings, int frameIndex)
        {
            var validatedSettings = settings.GetValidated();

            EnsurePreviewTexture(validatedSettings.ClampedResolution);
            EnsurePreviewMaterial(validatedSettings.Preset);

            if (previewMaterial == null)
            {
                return Texture2D.grayTexture;
            }

            var totalFrames = Mathf.Max(1, validatedSettings.FrameCount);
            var clampedFrameIndex = LoopLabRenderSettings.NormalizeFrameIndex(frameIndex, totalFrames);
            var phase = LoopPhase.GetPhase(clampedFrameIndex, totalFrames);
            var loopVector = LoopPhase.GetLoopVector(phase);

            previewMaterial.SetFloat("_Seed", validatedSettings.Seed);
            previewMaterial.SetFloat("_Phase", phase);
            previewMaterial.SetFloat("_Duration", validatedSettings.DurationSeconds);
            previewMaterial.SetFloat("_GridScale", LoopLabPresetCatalog.GetGridScale(validatedSettings.Preset));
            previewMaterial.SetColor("_BaseColor", LoopLabPresetCatalog.GetBaseColor(validatedSettings.Preset));
            previewMaterial.SetColor("_AccentColor", LoopLabPresetCatalog.GetAccentColor(validatedSettings.Preset));
            previewMaterial.SetVector("_LoopVector", new Vector4(loopVector.x, loopVector.y, 0f, 0f));

            RenderToOffscreenTexture();
            return previewTexture;
        }

        public Texture RenderPreview(LoopLabRenderSettings settings, float elapsedSeconds)
        {
            var validatedSettings = settings.GetValidated();
            var totalFrames = validatedSettings.FrameCount;
            var sampledFrame = LoopLabRenderSettings.GetPreviewFrameIndex(
                elapsedSeconds,
                validatedSettings.DurationSeconds,
                totalFrames);

            return Render(validatedSettings, sampledFrame);
        }

        private void RenderToOffscreenTexture()
        {
            var previousTarget = RenderTexture.active;
            try
            {
                RenderTexture.active = previewTexture;
                GL.Clear(true, true, Color.clear);
                Graphics.Blit(Texture2D.whiteTexture, previewTexture, previewMaterial, 0);
            }
            finally
            {
                RenderTexture.active = previousTarget;
            }
        }

        public void Dispose()
        {
            ReleaseMaterial();
            ReleasePreviewTexture();
        }

        private void EnsurePreviewTexture(int resolution)
        {
            if (previewTexture != null && previewTexture.width == resolution && previewTexture.height == resolution)
            {
                return;
            }

            ReleasePreviewTexture();

            previewTexture = new RenderTexture(resolution, resolution, 0)
            {
                name = "LoopLab Preview",
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave
            };
            previewTexture.Create();
        }

        private void EnsurePreviewMaterial(LoopLabPresetKind preset)
        {
            if (previewMaterial != null && activePreset == preset)
            {
                return;
            }

            ReleaseMaterial();

            var template = Resources.Load<Material>(LoopLabPresetCatalog.GetMaterialResourcePath(preset));
            if (template != null)
            {
                previewMaterial = Object.Instantiate(template);
            }
            else
            {
                var shader = Shader.Find(LoopLabPresetCatalog.GetShaderName(preset));
                if (shader != null)
                {
                    previewMaterial = new Material(shader);
                }
            }

            if (previewMaterial != null)
            {
                previewMaterial.hideFlags = HideFlags.HideAndDontSave;
                activePreset = preset;
            }
        }

        private void ReleaseMaterial()
        {
            if (previewMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(previewMaterial);
            }
            else
            {
                Object.DestroyImmediate(previewMaterial);
            }

            previewMaterial = null;
        }

        private void ReleasePreviewTexture()
        {
            if (previewTexture == null)
            {
                return;
            }

            previewTexture.Release();

            if (Application.isPlaying)
            {
                Object.Destroy(previewTexture);
            }
            else
            {
                Object.DestroyImmediate(previewTexture);
            }

            previewTexture = null;
        }
    }
}
