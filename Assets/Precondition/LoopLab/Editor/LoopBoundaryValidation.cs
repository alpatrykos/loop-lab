using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Precondition.LoopLab.Editor
{
    internal sealed class LoopBoundaryValidationResult : IDisposable
    {
        private const float AverageDeltaThreshold = 0.0025f;
        private const float MaxDeltaThreshold = 0.015f;

        public LoopBoundaryValidationResult(
            Texture2D firstFrame,
            Texture2D boundaryFrame,
            Texture2D differenceTexture,
            int comparedFrameIndex,
            float averageDelta,
            float maxDelta)
        {
            FirstFrame = firstFrame;
            BoundaryFrame = boundaryFrame;
            DifferenceTexture = differenceTexture;
            ComparedFrameIndex = comparedFrameIndex;
            AverageDelta = averageDelta;
            MaxDelta = maxDelta;
        }

        public Texture2D FirstFrame { get; }
        public Texture2D BoundaryFrame { get; }
        public Texture2D DifferenceTexture { get; }
        public int ComparedFrameIndex { get; }
        public float AverageDelta { get; }
        public float MaxDelta { get; }
        public bool MatchesVisually => AverageDelta <= AverageDeltaThreshold && MaxDelta <= MaxDeltaThreshold;

        public string Summary => MatchesVisually
            ? $"Frame 0 and frame {ComparedFrameIndex} match within tolerance. Avg delta {AverageDelta * 100f:0.00}% and max delta {MaxDelta * 100f:0.00}%."
            : $"Boundary mismatch detected between frame 0 and frame {ComparedFrameIndex}. Avg delta {AverageDelta * 100f:0.00}% and max delta {MaxDelta * 100f:0.00}%.";

        public void Dispose()
        {
            DestroyTexture(FirstFrame);
            DestroyTexture(BoundaryFrame);
            DestroyTexture(DifferenceTexture);
        }

        public void WriteImages(string outputDirectory, string filePrefix)
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllBytes(Path.Combine(outputDirectory, $"{filePrefix}-frame-0.png"), FirstFrame.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(outputDirectory, $"{filePrefix}-frame-{ComparedFrameIndex}.png"), BoundaryFrame.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(outputDirectory, $"{filePrefix}-diff.png"), DifferenceTexture.EncodeToPNG());
        }

        private static void DestroyTexture(Texture texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(texture);
            }
            else
            {
                Object.DestroyImmediate(texture);
            }
        }
    }

    internal static class LoopBoundaryValidator
    {
        public static LoopBoundaryValidationResult Capture(LoopRenderer renderer, LoopLabRenderSettings settings)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            var firstFrame = CaptureSnapshot(renderer.Render(settings, 0), "Loop Boundary Frame 0");
            var boundaryFrame = CaptureSnapshot(renderer.RenderAtPhase(settings, 1f), $"Loop Boundary Frame {settings.FrameCount}");
            var differenceTexture = CreateDifferenceTexture(firstFrame, boundaryFrame, out var averageDelta, out var maxDelta);

            return new LoopBoundaryValidationResult(
                firstFrame,
                boundaryFrame,
                differenceTexture,
                settings.FrameCount,
                averageDelta,
                maxDelta);
        }

        public static string GetDefaultOutputDirectory()
        {
            var projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                throw new InvalidOperationException("Unable to resolve the project root for loop boundary validation output.");
            }

            return Path.Combine(projectRoot.FullName, "log", "boundary-validation");
        }

        private static Texture2D CaptureSnapshot(Texture renderedTexture, string textureName)
        {
            if (renderedTexture is not RenderTexture renderTexture)
            {
                throw new InvalidOperationException("Loop boundary validation expected a render texture snapshot.");
            }

            var previousRenderTarget = RenderTexture.active;
            try
            {
                RenderTexture.active = renderTexture;
                var snapshot = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false)
                {
                    name = textureName,
                    hideFlags = HideFlags.HideAndDontSave
                };

                snapshot.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
                snapshot.Apply(false, false);
                return snapshot;
            }
            finally
            {
                RenderTexture.active = previousRenderTarget;
            }
        }

        private static Texture2D CreateDifferenceTexture(
            Texture2D firstFrame,
            Texture2D boundaryFrame,
            out float averageDelta,
            out float maxDelta)
        {
            var firstPixels = firstFrame.GetPixels32();
            var boundaryPixels = boundaryFrame.GetPixels32();

            if (firstPixels.Length != boundaryPixels.Length)
            {
                throw new InvalidOperationException("Loop boundary validation requires equal-sized textures.");
            }

            var differencePixels = new Color32[firstPixels.Length];
            averageDelta = 0f;
            maxDelta = 0f;

            for (var index = 0; index < firstPixels.Length; index++)
            {
                var redDelta = Mathf.Abs(firstPixels[index].r - boundaryPixels[index].r) / 255f;
                var greenDelta = Mathf.Abs(firstPixels[index].g - boundaryPixels[index].g) / 255f;
                var blueDelta = Mathf.Abs(firstPixels[index].b - boundaryPixels[index].b) / 255f;

                var pixelDelta = Mathf.Max(redDelta, Mathf.Max(greenDelta, blueDelta));
                averageDelta += (redDelta + greenDelta + blueDelta) / 3f;
                maxDelta = Mathf.Max(maxDelta, pixelDelta);

                var intensity = Mathf.Clamp01(pixelDelta * 6f);
                differencePixels[index] = new Color(intensity, intensity * 0.45f, 0f, 1f);
            }

            averageDelta /= firstPixels.Length;

            var differenceTexture = new Texture2D(firstFrame.width, firstFrame.height, TextureFormat.RGBA32, false)
            {
                name = "Loop Boundary Difference",
                hideFlags = HideFlags.HideAndDontSave
            };
            differenceTexture.SetPixels32(differencePixels);
            differenceTexture.Apply(false, false);
            return differenceTexture;
        }
    }

    public static class LoopBoundaryValidationBatch
    {
        public static void RunAllPresets()
        {
            RunAllPresets(LoopBoundaryValidator.GetDefaultOutputDirectory());
        }

        [MenuItem("Precondition/LoopLab/Validate All Presets", priority = 130)]
        public static void RunAllPresetsFromMenu()
        {
            var outputDirectory = RunAllPresetsToDefaultOutput();
            Debug.Log($"[LoopBoundaryValidation] Boundary QA snapshots written to {outputDirectory}.");
        }

        public static string RunAllPresetsToDefaultOutput()
        {
            var outputDirectory = LoopBoundaryValidator.GetDefaultOutputDirectory();
            RunAllPresets(outputDirectory);
            return outputDirectory;
        }

        private static void RunAllPresets(string outputDirectory)
        {
            LoopLabProjectBootstrap.RunBatchmode();

            Directory.CreateDirectory(outputDirectory);

            var failures = new List<string>();
            using var renderer = new LoopRenderer();

            foreach (LoopLabPresetKind preset in Enum.GetValues(typeof(LoopLabPresetKind)))
            {
                foreach (var contrastMode in GetContrastModes(preset))
                {
                    var settings = LoopLabRenderSettings.Default;
                    settings.Preset = preset;
                    settings.ContrastMode = contrastMode;

                    using var result = LoopBoundaryValidator.Capture(renderer, settings);
                    var filePrefix = preset.ToString().ToLowerInvariant();
                    var variantLabel = preset.ToString();
                    if (LoopLabPresetCatalog.SupportsContrastMode(preset))
                    {
                        filePrefix += "-" + contrastMode.ToString().ToLowerInvariant();
                        variantLabel += $" ({contrastMode})";
                    }

                    result.WriteImages(outputDirectory, filePrefix);

                    Debug.Log(
                        $"[LoopBoundaryValidation] {variantLabel}: compared frame 0 vs frame {result.ComparedFrameIndex}, avg delta {(result.AverageDelta * 100f):0.00}%, max delta {(result.MaxDelta * 100f):0.00}%.");

                    if (!result.MatchesVisually)
                    {
                        failures.Add($"{variantLabel} (avg {(result.AverageDelta * 100f):0.00}%, max {(result.MaxDelta * 100f):0.00}%)");
                    }
                }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException("Loop boundary validation failed for: " + string.Join(", ", failures));
            }

            Debug.Log("[LoopBoundaryValidation] All presets match at the loop restart boundary.");
        }

        private static LoopLabContrastMode[] GetContrastModes(LoopLabPresetKind preset)
        {
            if (!LoopLabPresetCatalog.SupportsContrastMode(preset))
            {
                return new[] { LoopLabContrastMode.High };
            }

            return new[] { LoopLabContrastMode.High, LoopLabContrastMode.Low };
        }
    }
}
