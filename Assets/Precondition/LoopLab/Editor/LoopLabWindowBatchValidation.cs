using System;
using System.IO;
using System.Reflection;
using Precondition.LoopLab.Editor.Export;
using UnityEditor;
using UnityEngine;

namespace Precondition.LoopLab.Editor
{
    public static class LoopLabWindowBatchValidation
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic;

        public static void Run()
        {
            var stateKey = GetStateKey();
            var hadOriginalState = EditorPrefs.HasKey(stateKey);
            var originalState = hadOriginalState ? EditorPrefs.GetString(stateKey) : string.Empty;
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

                AssertPreviewMode(window, "Single");
                SetPreviewMode(window, "Tiled2x2");
                AssertTiledPreviewGeometry();

                var tiledPreviewTexture = (Texture)Invoke(window, "RenderCurrentPreviewFrame");
                if (tiledPreviewTexture == null)
                {
                    throw new InvalidOperationException("Tiled preview render returned a null texture.");
                }

                Invoke(window, "SaveState");
                AssertSavedPreviewMode("Tiled2x2");

                var exportDirectory = GetAbsoluteExportDirectory();
                var originalFfmpegOverride = Environment.GetEnvironmentVariable(LoopLabFfmpegLocator.OverridePathEnvironmentVariable);

                try
                {
                    Environment.SetEnvironmentVariable(
                        LoopLabFfmpegLocator.OverridePathEnvironmentVariable,
                        Path.Combine(exportDirectory, "missing-ffmpeg"));
                    Invoke(window, "ExportGif");
                }
                finally
                {
                    Environment.SetEnvironmentVariable(LoopLabFfmpegLocator.OverridePathEnvironmentVariable, originalFfmpegOverride);
                }

                var exportStatus = (string)GetField(window, "statusMessage");
                if (!exportStatus.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) ||
                    !exportStatus.Contains($"seed {firstGenerated.Seed}", StringComparison.Ordinal) ||
                    !exportStatus.Contains($"{firstGenerated.FrameCount} frames @ {firstGenerated.FramesPerSecond} FPS", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Export failure did not preserve ffmpeg fallback context: {exportStatus}");
                }

                AssertNoTemporaryWorkspaces(exportDirectory);
                AssertExportLifecycleCleanup(exportDirectory);

                var livePreviewSettings = firstGenerated;
                livePreviewSettings.DurationSeconds = 4f;
                livePreviewSettings.Resolution = 256;
                livePreviewSettings.Seed = firstGenerated.Seed + 17;

                SetField(window, "settings", livePreviewSettings);
                Invoke(window, "HandleSettingsChanged", "Settings updated.");

                if (!GetField<bool>(window, "isPreviewing"))
                {
                    throw new InvalidOperationException("Preview unexpectedly stopped after settings changed.");
                }

                if (!GetField<bool>(window, "hasPendingSettings"))
                {
                    throw new InvalidOperationException("Live preview settings were not marked pending after editing.");
                }

                var livePreviewStatus = GetField<string>(window, "statusMessage");
                if (!livePreviewStatus.Contains("Preview reflects live settings. Generate to refresh exports.", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Unexpected live preview status: {livePreviewStatus}");
                }

                var livePreviewTexture = (Texture)Invoke(window, "RenderCurrentPreviewFrame");
                if (livePreviewTexture is not RenderTexture livePreviewRenderTexture ||
                    livePreviewRenderTexture.width != livePreviewSettings.ClampedResolution)
                {
                    throw new InvalidOperationException("Live preview did not refresh to the updated resolution.");
                }

                Invoke(window, "ScrubPreview", 1.5f);

                var previewElapsed = GetField<float>(window, "previewElapsedSeconds");
                if (!Mathf.Approximately(previewElapsed, 1.5f))
                {
                    throw new InvalidOperationException($"Expected scrubbed preview time 1.5s, got {previewElapsed:0.000}.");
                }

                var randomizedBefore = GetField<LoopLabRenderSettings>(window, "settings");
                Invoke(window, "RandomizeSeed");
                var randomizedAfter = GetField<LoopLabRenderSettings>(window, "settings");

                if (randomizedAfter.Seed == randomizedBefore.Seed)
                {
                    throw new InvalidOperationException("RandomizeSeed did not change the current seed.");
                }

                if (randomizedAfter.Seed <= 0 || randomizedAfter.Seed > LoopLabRenderSettings.MaxSupportedSeedValue)
                {
                    throw new InvalidOperationException($"Randomized seed was out of range: {randomizedAfter.Seed}.");
                }

                Debug.Log("LoopLabWindow batch validation passed.");
            }
            finally
            {
                RestoreWindowState(stateKey, hadOriginalState, originalState);
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

        private static T GetField<T>(object instance, string fieldName)
        {
            return (T)GetField(instance, fieldName);
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

        private static void SetPreviewMode(object instance, string previewModeName)
        {
            var field = instance.GetType().GetField("previewMode", InstanceFlags);
            if (field == null)
            {
                throw new MissingFieldException(instance.GetType().FullName, "previewMode");
            }

            field.SetValue(instance, Enum.Parse(field.FieldType, previewModeName));
        }

        private static void AssertPreviewMode(object instance, string expectedPreviewMode)
        {
            var previewMode = GetField(instance, "previewMode");
            if (!string.Equals(previewMode?.ToString(), expectedPreviewMode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected preview mode {expectedPreviewMode}, got {previewMode}.");
            }
        }

        private static void AssertSavedPreviewMode(string expectedPreviewMode)
        {
            var restoredWindow = ScriptableObject.CreateInstance<LoopLabWindow>();

            try
            {
                Invoke(restoredWindow, "LoadState");
                AssertPreviewMode(restoredWindow, expectedPreviewMode);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(restoredWindow);
            }
        }

        private static void AssertTiledPreviewGeometry()
        {
            var previewRect = new Rect(0f, 0f, 200f, 200f);
            var tileRects = (Rect[])InvokeStatic(typeof(LoopLabWindow), "GetTiledPreviewTileRects", previewRect);
            if (tileRects.Length != 4)
            {
                throw new InvalidOperationException($"Expected 4 tiled preview rects, got {tileRects.Length}.");
            }

            AssertRect(tileRects[0], 0f, 0f, 100f, 100f, "top-left tile");
            AssertRect(tileRects[1], 100f, 0f, 100f, 100f, "top-right tile");
            AssertRect(tileRects[2], 0f, 100f, 100f, 100f, "bottom-left tile");
            AssertRect(tileRects[3], 100f, 100f, 100f, 100f, "bottom-right tile");

            var seamRects = (Rect[])InvokeStatic(typeof(LoopLabWindow), "GetPreviewSeamRects", previewRect);
            if (seamRects.Length != 2)
            {
                throw new InvalidOperationException($"Expected 2 preview seam rects, got {seamRects.Length}.");
            }

            if (!Mathf.Approximately(seamRects[0].center.x, previewRect.center.x) || seamRects[0].height != previewRect.height)
            {
                throw new InvalidOperationException("Vertical seam guide is not centered on the tiled preview.");
            }

            if (!Mathf.Approximately(seamRects[1].center.y, previewRect.center.y) || seamRects[1].width != previewRect.width)
            {
                throw new InvalidOperationException("Horizontal seam guide is not centered on the tiled preview.");
            }
        }

        private static void AssertRect(Rect actual, float x, float y, float width, float height, string label)
        {
            if (!Mathf.Approximately(actual.x, x) ||
                !Mathf.Approximately(actual.y, y) ||
                !Mathf.Approximately(actual.width, width) ||
                !Mathf.Approximately(actual.height, height))
            {
                throw new InvalidOperationException(
                    $"{label} expected ({x}, {y}, {width}, {height}) but got ({actual.x}, {actual.y}, {actual.width}, {actual.height}).");
            }
        }

        private static string GetStateKey()
        {
            var field = typeof(LoopLabWindow).GetField("StateKey", BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
            {
                throw new MissingFieldException(typeof(LoopLabWindow).FullName, "StateKey");
            }

            return (string)field.GetRawConstantValue();
        }

        private static string GetAbsoluteExportDirectory()
        {
            return (string)InvokeStatic(typeof(LoopLabWindow), "GetAbsoluteExportDirectory");
        }

        private static void AssertExportLifecycleCleanup(string exportDirectory)
        {
            var validationDirectory = Path.Combine(exportDirectory, "BatchValidation");
            if (Directory.Exists(validationDirectory))
            {
                Directory.Delete(validationDirectory, true);
            }

            Directory.CreateDirectory(validationDirectory);

            try
            {
                AssertSuccessfulExportPublishesOutput(validationDirectory);
                AssertFailedExportCleansTemporaryFiles(validationDirectory);
                AssertCanceledExportCleansTemporaryFiles(validationDirectory);
                AssertStaleWorkspaceCleanup(validationDirectory);
                AssertNoTemporaryWorkspaces(validationDirectory);
            }
            finally
            {
                if (Directory.Exists(validationDirectory))
                {
                    Directory.Delete(validationDirectory, true);
                }
            }
        }

        private static void AssertSuccessfulExportPublishesOutput(string validationDirectory)
        {
            var request = new LoopLabExportRequest("Validation GIF", ".gif", LoopLabRenderSettings.Default, validationDirectory);
            var finalOutputPath = LoopLabExportSession.Run(request, workspace =>
            {
                File.WriteAllText(workspace.GetFramePath(0), "frame");
                File.WriteAllBytes(workspace.StagedOutputPath, new byte[] { 1, 2, 3, 4 });
            });

            if (!File.Exists(finalOutputPath))
            {
                throw new InvalidOperationException($"Expected a published output at {finalOutputPath}.");
            }

            File.Delete(finalOutputPath);
            AssertNoTemporaryWorkspaces(validationDirectory);
        }

        private static void AssertFailedExportCleansTemporaryFiles(string validationDirectory)
        {
            var request = new LoopLabExportRequest("Validation MP4", ".mp4", LoopLabRenderSettings.Default, validationDirectory);
            var finalOutputPath = Path.Combine(validationDirectory, request.OutputFileName);

            try
            {
                LoopLabExportSession.Run(request, workspace =>
                {
                    File.WriteAllBytes(workspace.StagedOutputPath, new byte[] { 9, 8, 7 });
                    throw new InvalidOperationException("Simulated export failure.");
                });
                throw new InvalidOperationException("Expected simulated export failure.");
            }
            catch (InvalidOperationException exception) when (exception.Message == "Simulated export failure.")
            {
            }

            if (File.Exists(finalOutputPath))
            {
                throw new InvalidOperationException($"Failure path left a partial output at {finalOutputPath}.");
            }

            AssertNoTemporaryWorkspaces(validationDirectory);
        }

        private static void AssertCanceledExportCleansTemporaryFiles(string validationDirectory)
        {
            var request = new LoopLabExportRequest("Validation Cancel", ".gif", LoopLabRenderSettings.Default, validationDirectory);
            var finalOutputPath = Path.Combine(validationDirectory, request.OutputFileName);

            try
            {
                LoopLabExportSession.Run(request, workspace =>
                {
                    File.WriteAllBytes(workspace.StagedOutputPath, new byte[] { 6, 5, 4 });
                    throw new OperationCanceledException("Simulated export cancel.");
                });
                throw new InvalidOperationException("Expected simulated export cancel.");
            }
            catch (OperationCanceledException exception) when (exception.Message == "Simulated export cancel.")
            {
            }

            if (File.Exists(finalOutputPath))
            {
                throw new InvalidOperationException($"Cancel path left a partial output at {finalOutputPath}.");
            }

            AssertNoTemporaryWorkspaces(validationDirectory);
        }

        private static void AssertStaleWorkspaceCleanup(string validationDirectory)
        {
            var staleDirectory = Path.Combine(
                validationDirectory,
                LoopLabExportSession.TemporaryDirectoryPrefix + "stale-validation-workspace");
            Directory.CreateDirectory(staleDirectory);
            File.WriteAllText(Path.Combine(staleDirectory, "stale.txt"), "stale");
            Directory.SetLastWriteTimeUtc(staleDirectory, DateTime.UtcNow - TimeSpan.FromDays(2));

            var request = new LoopLabExportRequest("Validation Cleanup", ".gif", LoopLabRenderSettings.Default, validationDirectory);
            var finalOutputPath = LoopLabExportSession.Run(request, workspace =>
            {
                File.WriteAllBytes(workspace.StagedOutputPath, new byte[] { 0x1 });
            });

            if (Directory.Exists(staleDirectory))
            {
                throw new InvalidOperationException($"Expected stale workspace cleanup for {staleDirectory}.");
            }

            if (File.Exists(finalOutputPath))
            {
                File.Delete(finalOutputPath);
            }
        }

        private static void AssertNoTemporaryWorkspaces(string outputDirectory)
        {
            if (LoopLabExportSession.HasTemporaryWorkspaces(outputDirectory))
            {
                throw new InvalidOperationException($"Temporary workspaces were not cleaned from {outputDirectory}.");
            }
        }

        private static void RestoreWindowState(string stateKey, bool hadOriginalState, string originalState)
        {
            if (hadOriginalState)
            {
                EditorPrefs.SetString(stateKey, originalState);
            }
            else
            {
                EditorPrefs.DeleteKey(stateKey);
            }
        }

        private static object InvokeStatic(Type type, string methodName, params object[] arguments)
        {
            var method = type.GetMethod(methodName, StaticFlags);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }

            return method.Invoke(null, arguments);
        }

        private static object Invoke(object instance, string methodName, params object[] args)
        {
            var argumentTypes = Array.ConvertAll(args, argument => argument.GetType());
            var method = instance.GetType().GetMethod(methodName, InstanceFlags, null, argumentTypes, null);
            if (method == null)
            {
                throw new MissingMethodException(instance.GetType().FullName, methodName);
            }

            return method.Invoke(instance, args);
        }
    }
}
