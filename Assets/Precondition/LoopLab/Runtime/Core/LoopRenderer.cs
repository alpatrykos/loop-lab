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
            var totalFrames = Mathf.Max(1, settings.FrameCount);
            var clampedFrameIndex = settings.FrameCount <= 1
                ? 0
                : ((frameIndex % totalFrames) + totalFrames) % totalFrames;
            var phase = LoopPhase.GetPhase(clampedFrameIndex, totalFrames);
            return RenderAtPhase(settings, phase);
        }

        public Texture RenderAtPhase(LoopLabRenderSettings settings, float phase)
        {
            EnsurePreviewTexture(settings.ClampedResolution);
            EnsurePreviewMaterial(settings.Preset);

            if (previewMaterial == null)
            {
                return Texture2D.grayTexture;
            }

            var loopVector = LoopPhase.GetLoopVector(phase);

            previewMaterial.SetFloat("_Seed", settings.Seed);
            previewMaterial.SetFloat("_Phase", phase);
            previewMaterial.SetFloat("_Duration", Mathf.Max(0.1f, settings.DurationSeconds));
            previewMaterial.SetFloat("_GridScale", LoopLabPresetCatalog.GetGridScale(settings.Preset));
            previewMaterial.SetColor("_BaseColor", LoopLabPresetCatalog.GetBaseColor(settings.Preset));
            previewMaterial.SetColor("_AccentColor", LoopLabPresetCatalog.GetAccentColor(settings.Preset));
            previewMaterial.SetVector("_LoopVector", new Vector4(loopVector.x, loopVector.y, 0f, 0f));

            RenderToOffscreenTexture();
            return previewTexture;
        }

        public Texture RenderPreview(LoopLabRenderSettings settings, float elapsedSeconds)
        {
            var totalFrames = settings.FrameCount;
            if (totalFrames <= 1)
            {
                return Render(settings, 0);
            }

            var safeDuration = Mathf.Max(0.1f, settings.DurationSeconds);
            var safeElapsed = Mathf.Max(0f, elapsedSeconds);
            var normalizedPhase = LoopPhase.GetPhase(safeElapsed, safeDuration);
            var sampledFrame = Mathf.FloorToInt(normalizedPhase * totalFrames) % totalFrames;
            return Render(settings, sampledFrame % totalFrames);
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
