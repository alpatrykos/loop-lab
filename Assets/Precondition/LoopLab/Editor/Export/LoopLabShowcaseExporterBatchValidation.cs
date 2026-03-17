using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Precondition.LoopLab.Editor.Export
{
    public static class LoopLabShowcaseExporterBatchValidation
    {
        public static void Run()
        {
            var outputDirectory = LoopLabShowcaseExporter.ExportAll();

            if (LoopLabExportSession.HasTemporaryWorkspaces(outputDirectory))
            {
                throw new InvalidOperationException($"Found stale temp workspaces in {outputDirectory} after showcase export.");
            }

            var readmePath = Path.Combine(outputDirectory, LoopLabShowcaseExporter.ShowcaseReadmeFileName);
            if (!File.Exists(readmePath))
            {
                throw new InvalidOperationException($"Expected showcase README at {readmePath}.");
            }

            foreach (var definition in LoopLabShowcaseExporter.PresetDefinitions)
            {
                AssertPresetOutputs(outputDirectory, definition);
            }

            var comparisonSheet = LoopLabTextureCaptureUtility.LoadPng(
                LoopLabShowcaseExporter.GetComparisonSheetPath(outputDirectory));
            try
            {
                if (comparisonSheet.width != LoopLabShowcaseExporter.ComparisonSheetWidth ||
                    comparisonSheet.height != LoopLabShowcaseExporter.ComparisonSheetHeight)
                {
                    throw new InvalidOperationException(
                        $"Unexpected comparison sheet dimensions {comparisonSheet.width}x{comparisonSheet.height}.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(comparisonSheet);
            }

            Debug.Log("LoopLab showcase export validation passed.");
        }

        private static void AssertPresetOutputs(
            string outputDirectory,
            LoopLabShowcaseExporter.ShowcasePresetDefinition definition)
        {
            var settings = definition.CreateSettings();
            var gifPath = LoopLabShowcaseExporter.GetGifPath(outputDirectory, definition);
            var thumbnailPath = LoopLabShowcaseExporter.GetThumbnailPath(outputDirectory, definition);

            if (!File.Exists(gifPath))
            {
                throw new InvalidOperationException($"Expected showcase GIF at {gifPath}.");
            }

            if (!File.Exists(thumbnailPath))
            {
                throw new InvalidOperationException($"Expected showcase thumbnail at {thumbnailPath}.");
            }

            var inspection = GifInspector.InspectFile(gifPath);
            if (inspection.Width != settings.ClampedResolution || inspection.Height != settings.ClampedResolution)
            {
                throw new InvalidOperationException(
                    $"Unexpected GIF dimensions for {definition.Preset}: {inspection.Width}x{inspection.Height}.");
            }

            if (inspection.FrameCount != settings.FrameCount)
            {
                throw new InvalidOperationException(
                    $"Unexpected GIF frame count for {definition.Preset}: {inspection.FrameCount}.");
            }

            var thumbnail = LoopLabTextureCaptureUtility.LoadPng(thumbnailPath);
            try
            {
                if (thumbnail.width != LoopLabShowcaseExporter.ThumbnailSize ||
                    thumbnail.height != LoopLabShowcaseExporter.ThumbnailSize)
                {
                    throw new InvalidOperationException(
                        $"Unexpected thumbnail dimensions for {definition.Preset}: {thumbnail.width}x{thumbnail.height}.");
                }

                AssertThumbnailContainsRenderedContent(thumbnail, definition.Preset);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(thumbnail);
            }
        }

        private static void AssertThumbnailContainsRenderedContent(Texture2D thumbnail, LoopLabPresetKind preset)
        {
            var sampledColors = new HashSet<int>();
            var pixels = thumbnail.GetPixels32();
            const int contentStart = 220;
            const int contentEnd = 804;
            const int sampleStep = 16;

            for (var y = contentStart; y < contentEnd; y += sampleStep)
            {
                var rowStart = y * thumbnail.width;
                for (var x = contentStart; x < contentEnd; x += sampleStep)
                {
                    var pixel = pixels[rowStart + x];
                    sampledColors.Add((pixel.r << 16) | (pixel.g << 8) | pixel.b);
                }
            }

            if (sampledColors.Count < 32)
            {
                throw new InvalidOperationException(
                    $"Thumbnail content for {preset} appears flat or placeholder-only ({sampledColors.Count} sampled colors).");
            }
        }
    }
}
