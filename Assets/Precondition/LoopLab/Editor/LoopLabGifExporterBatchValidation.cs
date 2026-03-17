using System;
using System.IO;
using Precondition.LoopLab.Editor.Export;
using UnityEngine;

namespace Precondition.LoopLab.Editor
{
    public static class LoopLabGifExporterBatchValidation
    {
        public static void Run()
        {
            var outputDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "log", "gif-validation"));
            Directory.CreateDirectory(outputDirectory);

            foreach (LoopLabPresetKind preset in Enum.GetValues(typeof(LoopLabPresetKind)))
            {
                ValidatePreset(outputDirectory, preset);
            }

            Debug.Log("LoopLab GIF export validation passed.");
        }

        private static void ValidatePreset(string outputDirectory, LoopLabPresetKind preset)
        {
            var settings = LoopLabRenderSettings.Default;
            settings.Preset = preset;
            settings.ContrastMode = LoopLabContrastMode.High;
            settings.DurationSeconds = 3f;
            settings.FramesPerSecond = 24;
            settings.Resolution = 256;
            settings.Seed = LoopLabRenderSettings.DefaultSeedValue + ((int)preset * 101);

            var validatedSettings = settings.GetValidated();
            var outputPath = GifExporter.Export(validatedSettings, outputDirectory, GifExportOptions.Default);
            var outputFile = new FileInfo(outputPath);

            if (!outputFile.Exists || outputFile.Length == 0)
            {
                throw new InvalidOperationException($"GIF export did not create a non-empty file for {preset}.");
            }

            var inspection = GifInspector.InspectFile(outputPath);
            var expectedDelays = GifEncoder.BuildFrameDelays(validatedSettings.FrameCount, validatedSettings.FramesPerSecond);

            if (!string.Equals(inspection.Version, "GIF89a", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected GIF89a export for {preset}, got {inspection.Version}.");
            }

            if (inspection.Width != validatedSettings.ClampedResolution || inspection.Height != validatedSettings.ClampedResolution)
            {
                throw new InvalidOperationException(
                    $"Unexpected GIF dimensions for {preset}: {inspection.Width}x{inspection.Height}.");
            }

            if (!inspection.IsInfiniteLoop)
            {
                throw new InvalidOperationException($"GIF export is missing infinite-loop metadata for {preset}.");
            }

            if (inspection.FrameCount != validatedSettings.FrameCount)
            {
                throw new InvalidOperationException(
                    $"Expected {validatedSettings.FrameCount} GIF frames for {preset}, got {inspection.FrameCount}.");
            }

            if (inspection.FrameDelays.Length != expectedDelays.Length)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedDelays.Length} GIF delays for {preset}, got {inspection.FrameDelays.Length}.");
            }

            for (var index = 0; index < expectedDelays.Length; index++)
            {
                if (inspection.FrameDelays[index] != expectedDelays[index])
                {
                    throw new InvalidOperationException(
                        $"Unexpected GIF delay for {preset} frame {index}: {inspection.FrameDelays[index]}.");
                }
            }
        }
    }
}
