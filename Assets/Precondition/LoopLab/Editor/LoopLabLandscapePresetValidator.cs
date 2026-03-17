using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Precondition.LoopLab.Editor
{
    public static class LoopLabLandscapePresetValidator
    {
        private const float EdgeTolerance = 0.03f;
        private const float LoopTolerance = 0.001f;
        private const float MotionLowerBound = 0.01f;

        public static void Run()
        {
            var outputDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "log", "landscape-validation"));
            Directory.CreateDirectory(outputDirectory);

            ValidateWindowFlow(outputDirectory);

            using var renderer = new LoopRenderer();
            ValidateRenderedLoop(renderer, outputDirectory, 2f);
            ValidateRenderedLoop(renderer, outputDirectory, 3f);
            ValidateRenderedLoop(renderer, outputDirectory, 4f);

            Debug.Log("LoopLab Landscape preset validation passed.");
        }

        private static void ValidateWindowFlow(string outputDirectory)
        {
            var window = ScriptableObject.CreateInstance<LoopLabWindow>();
            var renderer = new LoopRenderer();

            try
            {
                var settings = LoopLabRenderSettings.Default;
                settings.Preset = LoopLabPresetKind.Landscape;
                settings.DurationSeconds = 3f;
                settings.Resolution = 256;

                SetField(window, "renderer", renderer);
                SetField(window, "settings", settings);
                SetField(window, "generatedSettings", settings);
                SetField(window, "statusMessage", "Ready");

                Invoke(window, "GenerateLoop");

                var generatedSettings = GetField<LoopLabRenderSettings>(window, "generatedSettings");
                var hasGenerated = GetField<bool>(window, "hasGenerated");
                var hasPendingSettings = GetField<bool>(window, "hasPendingSettings");
                var statusMessage = GetField<string>(window, "statusMessage");

                if (!hasGenerated || hasPendingSettings)
                {
                    throw new InvalidOperationException("GenerateLoop did not leave the window in a generated-ready state.");
                }

                if (generatedSettings.Preset != LoopLabPresetKind.Landscape)
                {
                    throw new InvalidOperationException("GenerateLoop did not retain the Landscape preset.");
                }

                if (!statusMessage.StartsWith("Generated ", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Unexpected Generate status: {statusMessage}");
                }

                Invoke(window, "TogglePreview");

                var isPreviewing = GetField<bool>(window, "isPreviewing");
                statusMessage = GetField<string>(window, "statusMessage");
                if (!isPreviewing || !string.Equals(statusMessage, "Preview running.", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Preview did not enter the running state.");
                }

                SetField(window, "previewStartTime", EditorApplication.timeSinceStartup - 1.25d);
                var previewTexture = (Texture)Invoke(window, "RenderCurrentPreviewFrame");
                var capturedPreview = CaptureTexture(previewTexture, settings.ClampedResolution);
                try
                {
                    WritePng(capturedPreview, Path.Combine(outputDirectory, "window-preview-3s.png"));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(capturedPreview);
                }
            }
            finally
            {
                renderer.Dispose();
                ScriptableObject.DestroyImmediate(window);
            }
        }

        private static void ValidateRenderedLoop(LoopRenderer renderer, string outputDirectory, float durationSeconds)
        {
            var settings = LoopLabRenderSettings.Default;
            settings.Preset = LoopLabPresetKind.Landscape;
            settings.DurationSeconds = durationSeconds;
            settings.Resolution = 256;

            var startFrame = CaptureTexture(renderer.Render(settings, 0), settings.ClampedResolution);
            var wrappedFrame = CaptureTexture(renderer.Render(settings, settings.FrameCount), settings.ClampedResolution);
            var previewStart = CaptureTexture(renderer.RenderPreview(settings, 0f), settings.ClampedResolution);
            var previewLoop = CaptureTexture(renderer.RenderPreview(settings, durationSeconds), settings.ClampedResolution);
            var midFrame = CaptureTexture(renderer.Render(settings, settings.FrameCount / 2), settings.ClampedResolution);

            try
            {
                var loopDelta = AverageDelta(startFrame, wrappedFrame);
                var previewLoopDelta = AverageDelta(previewStart, previewLoop);
                var horizontalEdgeDelta = AverageEdgeDelta(startFrame, compareHorizontalEdges: true);
                var verticalEdgeDelta = AverageEdgeDelta(startFrame, compareHorizontalEdges: false);
                var motionDelta = AverageDelta(startFrame, midFrame);

                if (loopDelta > LoopTolerance)
                {
                    throw new InvalidOperationException($"Frame wrapping drifted at {durationSeconds:0.0}s: {loopDelta:0.0000}");
                }

                if (previewLoopDelta > LoopTolerance)
                {
                    throw new InvalidOperationException($"Preview loop drifted at {durationSeconds:0.0}s: {previewLoopDelta:0.0000}");
                }

                if (horizontalEdgeDelta > EdgeTolerance || verticalEdgeDelta > EdgeTolerance)
                {
                    throw new InvalidOperationException(
                        $"Landscape edges are not tile-safe at {durationSeconds:0.0}s. Horizontal={horizontalEdgeDelta:0.0000}, Vertical={verticalEdgeDelta:0.0000}");
                }

                if (motionDelta < MotionLowerBound)
                {
                    throw new InvalidOperationException($"Landscape motion is too subtle at {durationSeconds:0.0}s: {motionDelta:0.0000}");
                }

                var durationLabel = $"{durationSeconds:0}s";
                WritePng(startFrame, Path.Combine(outputDirectory, $"landscape-start-{durationLabel}.png"));
                WritePng(midFrame, Path.Combine(outputDirectory, $"landscape-mid-{durationLabel}.png"));

                Debug.Log(
                    $"Landscape {durationLabel} validation passed. loop={loopDelta:0.0000}, preview={previewLoopDelta:0.0000}, " +
                    $"edges=({horizontalEdgeDelta:0.0000}, {verticalEdgeDelta:0.0000}), motion={motionDelta:0.0000}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(startFrame);
                UnityEngine.Object.DestroyImmediate(wrappedFrame);
                UnityEngine.Object.DestroyImmediate(previewStart);
                UnityEngine.Object.DestroyImmediate(previewLoop);
                UnityEngine.Object.DestroyImmediate(midFrame);
            }
        }

        private static Texture2D CaptureTexture(Texture source, int resolution)
        {
            if (source == null)
            {
                throw new InvalidOperationException("Loop renderer returned a null texture.");
            }

            var temporary = source as RenderTexture;
            var createdTemporary = false;

            if (temporary == null)
            {
                temporary = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, temporary);
                createdTemporary = true;
            }

            var previousActive = RenderTexture.active;
            var capture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);

            try
            {
                RenderTexture.active = temporary;
                capture.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0);
                capture.Apply();
                return capture;
            }
            finally
            {
                RenderTexture.active = previousActive;

                if (createdTemporary)
                {
                    RenderTexture.ReleaseTemporary(temporary);
                }
            }
        }

        private static float AverageDelta(Texture2D left, Texture2D right)
        {
            var leftPixels = left.GetPixels32();
            var rightPixels = right.GetPixels32();
            var total = 0f;

            for (var index = 0; index < leftPixels.Length; index++)
            {
                total += ChannelDelta(leftPixels[index], rightPixels[index]);
            }

            return total / leftPixels.Length;
        }

        private static float AverageEdgeDelta(Texture2D texture, bool compareHorizontalEdges)
        {
            var total = 0f;
            var samples = compareHorizontalEdges ? texture.height : texture.width;

            for (var index = 0; index < samples; index++)
            {
                var left = compareHorizontalEdges
                    ? texture.GetPixel(0, index)
                    : texture.GetPixel(index, 0);
                var right = compareHorizontalEdges
                    ? texture.GetPixel(texture.width - 1, index)
                    : texture.GetPixel(index, texture.height - 1);
                total += ChannelDelta(left, right);
            }

            return total / samples;
        }

        private static float ChannelDelta(Color32 left, Color32 right)
        {
            return (
                Mathf.Abs(left.r - right.r) +
                Mathf.Abs(left.g - right.g) +
                Mathf.Abs(left.b - right.b)) / (255f * 3f);
        }

        private static void WritePng(Texture2D texture, string path)
        {
            var bytes = texture.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
        }

        private static object Invoke(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(instance.GetType().FullName, methodName);
            }

            return method.Invoke(instance, null);
        }

        private static T GetField<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(instance.GetType().FullName, fieldName);
            }

            return (T)field.GetValue(instance);
        }

        private static void SetField(object instance, string fieldName, object value)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(instance.GetType().FullName, fieldName);
            }

            field.SetValue(instance, value);
        }
    }
}
