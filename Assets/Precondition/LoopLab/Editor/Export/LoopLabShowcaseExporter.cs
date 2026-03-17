using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Precondition.LoopLab.Editor.Export
{
    public static class LoopLabShowcaseExporter
    {
        internal const string ShowcaseRelativeDirectory = "Assets/Precondition/LoopLab/Exports/Showcase";
        internal const string ShowcaseReadmeFileName = "README.md";
        internal const string ComparisonSheetFileName = "looplab-showcase-comparison-sheet.png";
        internal const int ThumbnailSize = 1024;
        internal const int ComparisonSheetWidth = 1920;
        internal const int ComparisonSheetHeight = 1080;

        private static readonly ShowcasePresetDefinition[] PresetDefinitionsInternal =
        {
            new(LoopLabPresetKind.Landscape, "landscape", 142857, 24, 3f, 512),
            new(LoopLabPresetKind.Fluid, "fluid", 271828, 24, 3f, 512),
            new(LoopLabPresetKind.Geometric, "geometric", 314159, 24, 3f, 512)
        };

        internal static IReadOnlyList<ShowcasePresetDefinition> PresetDefinitions => PresetDefinitionsInternal;

        [MenuItem("Precondition/LoopLab/Export Showcase Assets", priority = 103)]
        public static void ExportInteractive()
        {
            var outputDirectory = ExportAll();
            EditorUtility.RevealInFinder(outputDirectory);
        }

        public static void RunBatchExport()
        {
            ExportAll();
        }

        public static string ExportAll()
        {
            LoopLabBatchGraphicsGuard.EnsureGraphicsBackedOutput("LoopLab showcase export");

            var outputDirectory = GetAbsoluteShowcaseDirectory();
            Directory.CreateDirectory(outputDirectory);

            var exportedThumbnails = new List<Texture2D>();

            try
            {
                foreach (var definition in PresetDefinitionsInternal)
                {
                    var thumbnail = ExportPresetAssets(outputDirectory, definition);
                    exportedThumbnails.Add(thumbnail);
                }

                var comparisonSheet = BuildComparisonSheet(exportedThumbnails);
                try
                {
                    LoopLabTextureCaptureUtility.WritePng(comparisonSheet, GetComparisonSheetPath(outputDirectory));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(comparisonSheet);
                }

                AssetDatabase.Refresh();
                Debug.Log($"[LoopLab.Showcase] Exported showcase assets to {outputDirectory}.");
                return outputDirectory;
            }
            finally
            {
                foreach (var thumbnail in exportedThumbnails)
                {
                    if (thumbnail != null)
                    {
                        UnityEngine.Object.DestroyImmediate(thumbnail);
                    }
                }
            }
        }

        internal static string GetAbsoluteShowcaseDirectory()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                projectRoot = Directory.GetCurrentDirectory();
            }

            return Path.GetFullPath(Path.Combine(projectRoot, ShowcaseRelativeDirectory));
        }

        internal static string GetGifPath(string outputDirectory, ShowcasePresetDefinition definition)
        {
            return Path.Combine(outputDirectory, $"looplab-{definition.Slug}-showcase.gif");
        }

        internal static string GetThumbnailPath(string outputDirectory, ShowcasePresetDefinition definition)
        {
            return Path.Combine(outputDirectory, $"looplab-{definition.Slug}-thumbnail.png");
        }

        internal static string GetComparisonSheetPath(string outputDirectory)
        {
            return Path.Combine(outputDirectory, ComparisonSheetFileName);
        }

        private static Texture2D ExportPresetAssets(string outputDirectory, ShowcasePresetDefinition definition)
        {
            var settings = definition.CreateSettings();
            var gifPath = GifExporter.Export(
                settings,
                outputDirectory,
                new GifExportOptions
                {
                    Dithering = GifDitheringMode.FloydSteinberg
                });
            MoveToStablePath(gifPath, GetGifPath(outputDirectory, definition));

            using var renderer = new LoopRenderer();
            var capturedFrames = CaptureFrames(renderer, settings);

            try
            {
                var thumbnail = BuildThumbnail(definition, capturedFrames);
                LoopLabTextureCaptureUtility.WritePng(thumbnail, GetThumbnailPath(outputDirectory, definition));
                Debug.Log($"[LoopLab.Showcase] Wrote {definition.Preset} GIF and thumbnail.");
                return thumbnail;
            }
            finally
            {
                foreach (var capturedFrame in capturedFrames)
                {
                    if (capturedFrame != null)
                    {
                        UnityEngine.Object.DestroyImmediate(capturedFrame);
                    }
                }
            }
        }

        private static Texture2D[] CaptureFrames(LoopRenderer renderer, LoopLabRenderSettings settings)
        {
            var frameCount = settings.FrameCount;
            var frameIndices = new[]
            {
                0,
                Mathf.Max(0, frameCount / 3),
                Mathf.Max(0, (frameCount * 2) / 3)
            };

            var textures = new Texture2D[frameIndices.Length];

            for (var index = 0; index < frameIndices.Length; index++)
            {
                textures[index] = LoopLabTextureCaptureUtility.CaptureTexture(
                    renderer.Render(settings, frameIndices[index]),
                    settings.ClampedResolution);
            }

            return textures;
        }

        private static Texture2D BuildThumbnail(ShowcasePresetDefinition definition, Texture2D[] capturedFrames)
        {
            var baseColor = LoopLabPresetCatalog.GetBaseColor(definition.Preset);
            var accentColor = LoopLabPresetCatalog.GetAccentColor(definition.Preset);
            var thumbnail = CreateTexture(ThumbnailSize, ThumbnailSize, $"{definition.Slug} showcase thumbnail");
            var pixels = new Color32[ThumbnailSize * ThumbnailSize];

            FillVerticalGradient(
                pixels,
                ThumbnailSize,
                new RectInt(0, 0, ThumbnailSize, ThumbnailSize),
                Lighten(baseColor, 0.08f),
                Darken(baseColor, 0.55f));
            FillDiagonalStripeOverlay(pixels, ThumbnailSize, ThumbnailSize, WithAlpha(accentColor, 0.08f), 28, 6);

            DrawColorBlock(pixels, ThumbnailSize, new RectInt(72, 56, 880, 18), accentColor);
            DrawFramedTexture(
                pixels,
                ThumbnailSize,
                new RectInt(160, 96, 704, 704),
                capturedFrames[1],
                accentColor,
                Darken(baseColor, 0.35f));

            var stripRects = new[]
            {
                new RectInt(180, 824, 200, 200),
                new RectInt(412, 824, 200, 200),
                new RectInt(644, 824, 200, 200)
            };

            for (var index = 0; index < stripRects.Length; index++)
            {
                DrawFramedTexture(
                    pixels,
                    ThumbnailSize,
                    stripRects[index],
                    capturedFrames[index],
                    accentColor,
                    Darken(baseColor, 0.42f));
            }

            DrawColorBlock(pixels, ThumbnailSize, new RectInt(72, 996, 880, 12), accentColor);

            thumbnail.SetPixels32(pixels);
            thumbnail.Apply(false, false);
            return thumbnail;
        }

        private static Texture2D BuildComparisonSheet(IReadOnlyList<Texture2D> thumbnails)
        {
            var sheet = CreateTexture(ComparisonSheetWidth, ComparisonSheetHeight, "LoopLab showcase comparison sheet");
            var pixels = new Color32[ComparisonSheetWidth * ComparisonSheetHeight];

            FillVerticalGradient(
                pixels,
                ComparisonSheetWidth,
                new RectInt(0, 0, ComparisonSheetWidth, ComparisonSheetHeight),
                new Color(0.05f, 0.06f, 0.08f),
                new Color(0.01f, 0.02f, 0.03f));

            var cardWidth = 544;
            var cardHeight = 544;
            var gap = 64;
            var left = (ComparisonSheetWidth - (cardWidth * thumbnails.Count) - (gap * (thumbnails.Count - 1))) / 2;
            var top = 214;

            for (var index = 0; index < thumbnails.Count; index++)
            {
                var definition = PresetDefinitionsInternal[index];
                var accentColor = LoopLabPresetCatalog.GetAccentColor(definition.Preset);
                var cardRect = new RectInt(left + ((cardWidth + gap) * index), top, cardWidth, cardHeight);
                DrawFramedTexture(
                    pixels,
                    ComparisonSheetWidth,
                    cardRect,
                    thumbnails[index],
                    accentColor,
                    new Color(0.08f, 0.09f, 0.12f));
                DrawColorBlock(
                    pixels,
                    ComparisonSheetWidth,
                    new RectInt(cardRect.x, cardRect.yMax + 28, cardRect.width, 18),
                    accentColor);
            }

            sheet.SetPixels32(pixels);
            sheet.Apply(false, false);
            return sheet;
        }

        private static Texture2D CreateTexture(int width, int height, string name)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static void MoveToStablePath(string sourcePath, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(sourcePath, destinationPath);
        }

        private static void DrawFramedTexture(
            Color32[] pixels,
            int canvasWidth,
            RectInt rect,
            Texture2D source,
            Color borderColor,
            Color backingColor)
        {
            var shadowRect = new RectInt(rect.x + 16, rect.y + 16, rect.width, rect.height);
            DrawColorBlock(pixels, canvasWidth, shadowRect, WithAlpha(Color.black, 0.35f));
            DrawColorBlock(pixels, canvasWidth, rect, borderColor);

            var backingRect = Inset(rect, 10);
            DrawColorBlock(pixels, canvasWidth, backingRect, backingColor);

            var imageRect = Inset(backingRect, 8);
            DrawScaledTexture(pixels, canvasWidth, imageRect, source);
        }

        private static void DrawScaledTexture(Color32[] pixels, int canvasWidth, RectInt destinationRect, Texture2D source)
        {
            var sourcePixels = source.GetPixels32();
            var sourceWidth = source.width;
            var sourceHeight = source.height;
            var canvasHeight = pixels.Length / canvasWidth;

            var xMin = Mathf.Clamp(destinationRect.x, 0, canvasWidth);
            var xMax = Mathf.Clamp(destinationRect.xMax, 0, canvasWidth);
            var yMin = Mathf.Clamp(destinationRect.y, 0, canvasHeight);
            var yMax = Mathf.Clamp(destinationRect.yMax, 0, canvasHeight);

            for (var y = yMin; y < yMax; y++)
            {
                var normalizedY = (y - destinationRect.y) / (float)Mathf.Max(1, destinationRect.height);
                var sourceY = Mathf.Clamp(Mathf.FloorToInt(normalizedY * sourceHeight), 0, sourceHeight - 1);
                var destinationRow = y * canvasWidth;
                var sourceRow = sourceY * sourceWidth;

                for (var x = xMin; x < xMax; x++)
                {
                    var normalizedX = (x - destinationRect.x) / (float)Mathf.Max(1, destinationRect.width);
                    var sourceX = Mathf.Clamp(Mathf.FloorToInt(normalizedX * sourceWidth), 0, sourceWidth - 1);
                    pixels[destinationRow + x] = sourcePixels[sourceRow + sourceX];
                }
            }
        }

        private static void DrawColorBlock(Color32[] pixels, int canvasWidth, RectInt rect, Color color)
        {
            var canvasHeight = pixels.Length / canvasWidth;
            var xMin = Mathf.Clamp(rect.x, 0, canvasWidth);
            var xMax = Mathf.Clamp(rect.xMax, 0, canvasWidth);
            var yMin = Mathf.Clamp(rect.y, 0, canvasHeight);
            var yMax = Mathf.Clamp(rect.yMax, 0, canvasHeight);
            var fillColor = (Color32)color;

            for (var y = yMin; y < yMax; y++)
            {
                var rowStart = y * canvasWidth;
                for (var x = xMin; x < xMax; x++)
                {
                    pixels[rowStart + x] = Blend(pixels[rowStart + x], fillColor);
                }
            }
        }

        private static void FillVerticalGradient(
            Color32[] pixels,
            int canvasWidth,
            RectInt rect,
            Color topColor,
            Color bottomColor)
        {
            var canvasHeight = pixels.Length / canvasWidth;
            var xMin = Mathf.Clamp(rect.x, 0, canvasWidth);
            var xMax = Mathf.Clamp(rect.xMax, 0, canvasWidth);
            var yMin = Mathf.Clamp(rect.y, 0, canvasHeight);
            var yMax = Mathf.Clamp(rect.yMax, 0, canvasHeight);
            var height = Mathf.Max(1, yMax - yMin - 1);

            for (var y = yMin; y < yMax; y++)
            {
                var t = (y - yMin) / (float)height;
                var color = (Color32)Color.Lerp(topColor, bottomColor, t);
                var rowStart = y * canvasWidth;
                for (var x = xMin; x < xMax; x++)
                {
                    pixels[rowStart + x] = color;
                }
            }
        }

        private static void FillDiagonalStripeOverlay(
            Color32[] pixels,
            int canvasWidth,
            int canvasHeight,
            Color overlayColor,
            int stripeSpacing,
            int stripeWidth)
        {
            var overlay = (Color32)overlayColor;

            for (var y = 0; y < canvasHeight; y++)
            {
                var rowStart = y * canvasWidth;
                for (var x = 0; x < canvasWidth; x++)
                {
                    var diagonal = (x + y) % stripeSpacing;
                    if (diagonal >= stripeWidth)
                    {
                        continue;
                    }

                    pixels[rowStart + x] = Blend(pixels[rowStart + x], overlay);
                }
            }
        }

        private static RectInt Inset(RectInt rect, int padding)
        {
            return new RectInt(
                rect.x + padding,
                rect.y + padding,
                Mathf.Max(1, rect.width - (padding * 2)),
                Mathf.Max(1, rect.height - (padding * 2)));
        }

        private static Color32 Blend(Color32 under, Color32 over)
        {
            var alpha = over.a / 255f;
            if (alpha <= 0f)
            {
                return under;
            }

            if (alpha >= 1f)
            {
                return over;
            }

            return (Color32)Color.Lerp(under, over, alpha);
        }

        private static Color Lighten(Color color, float amount)
        {
            return Color.Lerp(color, Color.white, Mathf.Clamp01(amount));
        }

        private static Color Darken(Color color, float amount)
        {
            return Color.Lerp(color, Color.black, Mathf.Clamp01(amount));
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        internal readonly struct ShowcasePresetDefinition
        {
            public ShowcasePresetDefinition(
                LoopLabPresetKind preset,
                string slug,
                int seed,
                int framesPerSecond,
                float durationSeconds,
                int resolution)
            {
                Preset = preset;
                Slug = slug;
                Seed = seed;
                FramesPerSecond = framesPerSecond;
                DurationSeconds = durationSeconds;
                Resolution = resolution;
            }

            public LoopLabPresetKind Preset { get; }

            public string Slug { get; }

            public int Seed { get; }

            public int FramesPerSecond { get; }

            public float DurationSeconds { get; }

            public int Resolution { get; }

            public LoopLabRenderSettings CreateSettings()
            {
                return new LoopLabRenderSettings
                {
                    Preset = Preset,
                    ContrastMode = LoopLabContrastMode.High,
                    FramesPerSecond = FramesPerSecond,
                    DurationSeconds = DurationSeconds,
                    Resolution = Resolution,
                    Seed = Seed
                }.GetValidated();
            }
        }
    }
}
