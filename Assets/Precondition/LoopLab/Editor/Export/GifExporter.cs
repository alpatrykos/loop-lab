using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Precondition.LoopLab.Editor.Export
{
    public static class GifExporter
    {
        public static string Export(LoopLabRenderSettings settings, string outputDirectory, GifExportOptions options)
        {
            var request = new LoopLabExportRequest("GIF", ".gif", settings, outputDirectory);
            var resolution = request.Settings.ClampedResolution;

            using var renderer = new LoopRenderer();
            var captureTexture = CreateCaptureTexture(resolution);

            try
            {
                var outputPath = LoopLabExportSession.Run(
                    request,
                    workspace =>
                    {
                        var gifBytes = GifEncoder.Encode(
                            request.Settings.FrameCount,
                            resolution,
                            resolution,
                            request.Settings.FramesPerSecond,
                            frameIndex => CaptureFramePixels(renderer, captureTexture, request.Settings, frameIndex),
                            options);

                        File.WriteAllBytes(workspace.StagedOutputPath, gifBytes);
                    });
                Debug.Log($"[LoopLab.Export] GIF export wrote {outputPath}.");
                return outputPath;
            }
            finally
            {
                Object.DestroyImmediate(captureTexture);
            }
        }

        private static Texture2D CreateCaptureTexture(int resolution)
        {
            return new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "LoopLab GIF Capture",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static Color32[] CaptureFramePixels(
            LoopRenderer renderer,
            Texture2D captureTexture,
            LoopLabRenderSettings settings,
            int frameIndex)
        {
            if (renderer.Render(settings, frameIndex) is not RenderTexture renderTexture)
            {
                throw new InvalidOperationException("GIF export expected each rendered frame to be a render texture.");
            }

            var previousRenderTarget = RenderTexture.active;
            try
            {
                RenderTexture.active = renderTexture;
                captureTexture.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
                captureTexture.Apply(false, false);
                return FlipVertical(captureTexture.GetPixels32(), renderTexture.width, renderTexture.height);
            }
            finally
            {
                RenderTexture.active = previousRenderTarget;
            }
        }

        private static Color32[] FlipVertical(Color32[] pixels, int width, int height)
        {
            var flippedPixels = new Color32[pixels.Length];
            for (var y = 0; y < height; y++)
            {
                var sourceRowOffset = y * width;
                var destinationRowOffset = (height - 1 - y) * width;
                Array.Copy(pixels, sourceRowOffset, flippedPixels, destinationRowOffset, width);
            }

            return flippedPixels;
        }
    }
}
