using System;
using System.IO;
using NUnit.Framework;
using Precondition.LoopLab.Editor.Export;
using UnityEngine;
using UnityEngine.Rendering;

namespace Precondition.LoopLab.Tests
{
    public sealed class LoopLabLoopContractTests
    {
        private static readonly int[] BoundarySeeds =
        {
            LoopLabRenderSettings.DefaultSeedValue,
            271828,
            314159
        };

        private static readonly string[] ShaderPaths =
        {
            "Assets/Precondition/LoopLab/Runtime/Shaders/Landscape.shader",
            "Assets/Precondition/LoopLab/Runtime/Shaders/Fluid.shader",
            "Assets/Precondition/LoopLab/Runtime/Shaders/Geometric.shader",
            "Assets/Precondition/LoopLab/Runtime/Shaders/LoopLabLooping.hlsl"
        };

        private static readonly string[] BannedShaderTokens =
        {
            "_Time",
            "_SinTime",
            "_CosTime",
            "unity_DeltaTime",
            "unity_Time"
        };

        private const float AverageDeltaThreshold = 0.0025f;
        private const float MaxDeltaThreshold = 0.015f;
        private const int ValidationResolution = 128;

        [Test]
        public void LoopPhase_FrameBoundaryWrapsBackToZero()
        {
            Assert.That(LoopPhase.GetPhase(0, 24), Is.EqualTo(0f));
            Assert.That(LoopPhase.GetPhase(24, 24), Is.EqualTo(0f));
        }

        [Test]
        public void LoopPhase_LoopVectorMatchesAtStartAndEnd()
        {
            var start = LoopPhase.GetLoopVector(0f);
            var end = LoopPhase.GetLoopVector(1f);

            Assert.That(Vector2.Distance(start, end), Is.LessThanOrEqualTo(0.00001f));
        }

        [Test]
        public void ShippedShaders_DoNotReferenceNonPeriodicTimeGlobals()
        {
            var projectRoot = GetProjectRoot();

            foreach (var relativePath in ShaderPaths)
            {
                var contents = File.ReadAllText(Path.Combine(projectRoot, relativePath));
                foreach (var bannedToken in BannedShaderTokens)
                {
                    Assert.That(contents.Contains(bannedToken, StringComparison.Ordinal), Is.False, $"{relativePath} references {bannedToken}.");
                }
            }
        }

        [Test]
        public void RenderBoundaryPhase_MatchesFrameZeroAcrossPresetsAndSeeds()
        {
            AssumeGraphicsBacked();

            using var renderer = new LoopRenderer();

            foreach (LoopLabPresetKind preset in Enum.GetValues(typeof(LoopLabPresetKind)))
            {
                foreach (var seed in BoundarySeeds)
                {
                    var settings = CreateValidationSettings(preset, seed);
                    AssertTexturesMatchWithinTolerance(
                        renderer.Render(settings, 0),
                        renderer.RenderAtPhase(settings, 1f),
                        settings.ClampedResolution,
                        $"{preset} seed {seed} boundary");
                }
            }
        }

        [Test]
        public void PreviewDuration_WrapsToSameFrameAsStartAcrossPresets()
        {
            AssumeGraphicsBacked();

            using var renderer = new LoopRenderer();

            foreach (LoopLabPresetKind preset in Enum.GetValues(typeof(LoopLabPresetKind)))
            {
                var settings = CreateValidationSettings(preset, LoopLabRenderSettings.DefaultSeedValue);
                AssertTexturesMatchWithinTolerance(
                    renderer.RenderPreview(settings, 0f),
                    renderer.RenderPreview(settings, settings.DurationSeconds),
                    settings.ClampedResolution,
                    $"{preset} preview duration wrap");
            }
        }

        [Test]
        public void GifExport_WritesExactlyOneLoopOfValidatedFrames()
        {
            AssumeGraphicsBacked();

            var outputDirectory = Path.Combine(
                GetProjectRoot(),
                "Temp",
                "LoopLabLoopContractTests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(outputDirectory);

            try
            {
                foreach (LoopLabPresetKind preset in Enum.GetValues(typeof(LoopLabPresetKind)))
                {
                    var settings = new LoopLabRenderSettings
                    {
                        Preset = preset,
                        ContrastMode = LoopLabContrastMode.High,
                        FramesPerSecond = 12,
                        DurationSeconds = 1f,
                        Resolution = ValidationResolution,
                        Seed = LoopLabRenderSettings.DefaultSeedValue + ((int)preset * 101)
                    }.GetValidated();

                    var gifPath = GifExporter.Export(settings, outputDirectory, GifExportOptions.Default);
                    var inspection = GifInspector.InspectFile(gifPath);

                    Assert.That(inspection.FrameCount, Is.EqualTo(settings.FrameCount), $"{preset} GIF frame count");
                    Assert.That(inspection.FrameCount, Is.Not.EqualTo(settings.FrameCount + 1), $"{preset} duplicate end frame");
                }
            }
            finally
            {
                if (Directory.Exists(outputDirectory))
                {
                    Directory.Delete(outputDirectory, true);
                }
            }
        }

        private static LoopLabRenderSettings CreateValidationSettings(LoopLabPresetKind preset, int seed)
        {
            return new LoopLabRenderSettings
            {
                Preset = preset,
                ContrastMode = LoopLabContrastMode.High,
                FramesPerSecond = 24,
                DurationSeconds = 3f,
                Resolution = ValidationResolution,
                Seed = seed
            }.GetValidated();
        }

        private static void AssertTexturesMatchWithinTolerance(
            Texture first,
            Texture second,
            int resolution,
            string label)
        {
            Texture2D firstCapture = null;
            Texture2D secondCapture = null;

            try
            {
                firstCapture = CaptureTexture(first, resolution);
                secondCapture = CaptureTexture(second, resolution);

                CalculateDelta(firstCapture, secondCapture, out var averageDelta, out var maxDelta);
                Assert.That(averageDelta, Is.LessThanOrEqualTo(AverageDeltaThreshold), $"{label} average delta");
                Assert.That(maxDelta, Is.LessThanOrEqualTo(MaxDeltaThreshold), $"{label} max delta");
            }
            finally
            {
                if (firstCapture != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstCapture);
                }

                if (secondCapture != null)
                {
                    UnityEngine.Object.DestroyImmediate(secondCapture);
                }
            }
        }

        private static Texture2D CaptureTexture(Texture source, int resolution)
        {
            if (source is not RenderTexture renderTexture)
            {
                throw new InvalidOperationException("Expected a render texture for loop contract validation.");
            }

            var previousActive = RenderTexture.active;
            var capture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);

            try
            {
                RenderTexture.active = renderTexture;
                capture.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0);
                capture.Apply(false, false);
                return capture;
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }

        private static void CalculateDelta(Texture2D first, Texture2D second, out float averageDelta, out float maxDelta)
        {
            var firstPixels = first.GetPixels32();
            var secondPixels = second.GetPixels32();

            averageDelta = 0f;
            maxDelta = 0f;

            for (var index = 0; index < firstPixels.Length; index++)
            {
                var redDelta = Mathf.Abs(firstPixels[index].r - secondPixels[index].r) / 255f;
                var greenDelta = Mathf.Abs(firstPixels[index].g - secondPixels[index].g) / 255f;
                var blueDelta = Mathf.Abs(firstPixels[index].b - secondPixels[index].b) / 255f;

                var pixelDelta = Mathf.Max(redDelta, Mathf.Max(greenDelta, blueDelta));
                averageDelta += (redDelta + greenDelta + blueDelta) / 3f;
                maxDelta = Mathf.Max(maxDelta, pixelDelta);
            }

            averageDelta /= firstPixels.Length;
        }

        private static void AssumeGraphicsBacked()
        {
            Assume.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null), "Graphics-backed device required for render contract validation.");
        }

        private static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the project root.");
        }
    }
}
