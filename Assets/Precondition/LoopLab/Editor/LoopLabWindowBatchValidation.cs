using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Precondition.LoopLab.Editor
{
    public static class LoopLabWindowBatchValidation
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            var window = ScriptableObject.CreateInstance<LoopLabWindow>();

            try
            {
                var seedlessSettings = new LoopLabRenderSettings
                {
                    Preset = LoopLabPresetKind.Geometric,
                    FramesPerSecond = 0,
                    DurationSeconds = 0f,
                    Resolution = 8192,
                    Seed = 0
                };

                SetField(window, "settings", seedlessSettings);

                Invoke(window, "GenerateLoop");

                var firstGenerated = (LoopLabRenderSettings)GetField(window, "generatedSettings");
                var firstStatus = (string)GetField(window, "statusMessage");
                AssertGeneratedState(firstGenerated, firstStatus);

                SetField(window, "settings", seedlessSettings);
                Invoke(window, "GenerateLoop");

                var secondGenerated = (LoopLabRenderSettings)GetField(window, "generatedSettings");
                if (!firstGenerated.Equals(secondGenerated))
                {
                    throw new InvalidOperationException("GenerateLoop did not preserve deterministic normalized settings.");
                }

                Invoke(window, "TogglePreview");

                var previewStatus = (string)GetField(window, "statusMessage");
                if (previewStatus != "Preview running.")
                {
                    throw new InvalidOperationException($"Unexpected preview status: {previewStatus}");
                }

                var previewTexture = (Texture)Invoke(window, "RenderCurrentPreviewFrame");
                if (previewTexture == null)
                {
                    throw new InvalidOperationException("Preview render returned a null texture.");
                }

                Invoke(window, "ExportGif");

                var exportStatus = (string)GetField(window, "statusMessage");
                if (!exportStatus.Contains($"seed {firstGenerated.Seed}", StringComparison.Ordinal) ||
                    !exportStatus.Contains($"{firstGenerated.FrameCount} frames @ {firstGenerated.FramesPerSecond} FPS", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Export failure did not preserve normalized settings context: {exportStatus}");
                }

                Debug.Log("LoopLabWindow batch validation passed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        private static void AssertGeneratedState(LoopLabRenderSettings generatedSettings, string statusMessage)
        {
            if (generatedSettings.Seed != LoopLabRenderSettings.DefaultSeedValue)
            {
                throw new InvalidOperationException($"Expected default seed {LoopLabRenderSettings.DefaultSeedValue}, got {generatedSettings.Seed}.");
            }

            if (generatedSettings.FramesPerSecond != LoopLabRenderSettings.MinimumFramesPerSecond)
            {
                throw new InvalidOperationException($"Expected normalized FPS {LoopLabRenderSettings.MinimumFramesPerSecond}, got {generatedSettings.FramesPerSecond}.");
            }

            if (!Mathf.Approximately(generatedSettings.DurationSeconds, LoopLabRenderSettings.MinimumDurationSeconds))
            {
                throw new InvalidOperationException($"Expected normalized duration {LoopLabRenderSettings.MinimumDurationSeconds}, got {generatedSettings.DurationSeconds}.");
            }

            if (generatedSettings.Resolution != 2048)
            {
                throw new InvalidOperationException($"Expected clamped resolution 2048, got {generatedSettings.Resolution}.");
            }

            if (generatedSettings.FrameCount != 1)
            {
                throw new InvalidOperationException($"Expected 1 generated frame for normalized timing, got {generatedSettings.FrameCount}.");
            }

            if (!statusMessage.Contains($"using seed {generatedSettings.Seed}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Status message did not expose the generated seed: {statusMessage}");
            }
        }

        private static object GetField(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, InstanceFlags);
            if (field == null)
            {
                throw new MissingFieldException(instance.GetType().FullName, fieldName);
            }

            return field.GetValue(instance);
        }

        private static void SetField(object instance, string fieldName, object value)
        {
            var field = instance.GetType().GetField(fieldName, InstanceFlags);
            if (field == null)
            {
                throw new MissingFieldException(instance.GetType().FullName, fieldName);
            }

            field.SetValue(instance, value);
        }

        private static object Invoke(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, InstanceFlags);
            if (method == null)
            {
                throw new MissingMethodException(instance.GetType().FullName, methodName);
            }

            return method.Invoke(instance, null);
        }
    }
}
