using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Precondition.LoopLab.Editor.Export;

namespace Precondition.LoopLab.Editor
{
    public sealed class LoopLabWindow : EditorWindow
    {
        private const string StateKey = "Precondition.LoopLab.EditorWindow.State.v1";
        private const string ExportFolderPath = "Assets/Precondition/LoopLab/Exports";

        private LoopLabRenderSettings settings = LoopLabRenderSettings.Default;
        private LoopLabRenderSettings generatedSettings = LoopLabRenderSettings.Default;
        private LoopRenderer renderer;
        private Vector2 scrollPosition;
        private bool hasGenerated;
        private bool hasPendingSettings;
        private bool isPreviewing;
        private double previewStartTime;
        private string statusMessage = "Ready";

        [Serializable]
        private sealed class WindowState
        {
            public LoopLabRenderSettings Settings = LoopLabRenderSettings.Default;
            public LoopLabRenderSettings GeneratedSettings = LoopLabRenderSettings.Default;
            public bool HasGenerated;
            public bool HasPendingSettings;
            public bool IsPreviewing;
            public double PreviewStartTime;
        }

        [MenuItem("Precondition/LoopLab", priority = 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<LoopLabWindow>("LoopLab");
            window.minSize = new Vector2(440f, 560f);
            window.Focus();
            window.Repaint();
        }

        private void OnEnable()
        {
            LoadState();
            renderer ??= new LoopRenderer();
            EditorApplication.update += HandleEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= HandleEditorUpdate;
            SaveState();
            renderer?.Dispose();
            renderer = null;
        }

        private void OnGUI()
        {
            using var scrollView = new EditorGUILayout.ScrollViewScope(scrollPosition);
            scrollPosition = scrollView.scrollPosition;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("LoopLab", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Generation, preview, and export controls for LoopLab loops.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            settings.Preset = (LoopLabPresetKind)EditorGUILayout.EnumPopup("Preset", settings.Preset);
            settings.FramesPerSecond = EditorGUILayout.IntSlider("FPS", settings.FramesPerSecond, 12, 60);
            settings.DurationSeconds = EditorGUILayout.Slider("Duration (s)", settings.DurationSeconds, 2f, 4f);
            settings.Resolution = EditorGUILayout.IntPopup(
                "Resolution",
                settings.Resolution,
                new[] { "256", "512", "1024" },
                new[] { 256, 512, 1024 });
            settings.Seed = EditorGUILayout.IntField("Seed", settings.Seed);

            if (EditorGUI.EndChangeCheck())
            {
                hasPendingSettings = true;
                isPreviewing = false;
                statusMessage = "Settings updated. Generate to refresh loop.";
            }

            GUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate"))
                {
                    GenerateLoop();
                }

                using (new EditorGUI.DisabledGroupScope(!hasGenerated || hasPendingSettings))
                {
                    if (GUILayout.Button(isPreviewing ? "Pause Preview" : "Preview"))
                    {
                        TogglePreview();
                    }
                }

                using (new EditorGUI.DisabledGroupScope(!hasGenerated || hasPendingSettings))
                {
                    if (GUILayout.Button("Export GIF"))
                    {
                        ExportGif();
                    }
                }

                using (new EditorGUI.DisabledGroupScope(!hasGenerated || hasPendingSettings))
                {
                    if (GUILayout.Button("Export MP4"))
                    {
                        ExportMp4();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild Scaffold"))
                {
                    LoopLabProjectBootstrap.RunInteractive();
                }

                if (GUILayout.Button("Open Exports Folder"))
                {
                    LoopLabProjectBootstrap.RevealExportsFolder();
                }
            }

            GUILayout.Space(8f);

            EditorGUILayout.LabelField("Status", statusMessage);
            GUILayout.Space(4f);

            if (!hasGenerated)
            {
                EditorGUILayout.HelpBox("A test frame is rendered for the current settings. Generate to lock loop state for exports.", MessageType.Info);
            }

            if (hasPendingSettings)
            {
                EditorGUILayout.HelpBox("Settings have changed since last generation.", MessageType.Warning);
            }

            DrawPreview();

            GUILayout.Space(12f);
            EditorGUILayout.LabelField("Current Preset", LoopLabPresetCatalog.GetDisplayName(settings.Preset));
            EditorGUILayout.LabelField("Generated Preset", LoopLabPresetCatalog.GetDisplayName(generatedSettings.Preset));
            EditorGUILayout.LabelField("Generated Frame Count", generatedSettings.FrameCount.ToString());
            EditorGUILayout.LabelField("Shader", LoopLabPresetCatalog.GetShaderName(generatedSettings.Preset));
        }

        private void DrawPreview()
        {
            var previewTexture = RenderCurrentPreviewFrame();

            var rect = GUILayoutUtility.GetAspectRect(1f, GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(rect, new Color(0.09f, 0.1f, 0.12f));

            if (previewTexture != null)
            {
                GUI.DrawTexture(rect, previewTexture, ScaleMode.ScaleToFit, false);
            }
        }

        private void GenerateLoop()
        {
            renderer ??= new LoopRenderer();
            generatedSettings = settings;
            hasGenerated = true;
            hasPendingSettings = false;
            isPreviewing = false;
            previewStartTime = EditorApplication.timeSinceStartup;
            renderer.Render(generatedSettings, 0);
            statusMessage = $"Generated {generatedSettings.FrameCount} frames @ {generatedSettings.FramesPerSecond} FPS.";
            Repaint();
        }

        private void TogglePreview()
        {
            if (!hasGenerated || hasPendingSettings)
            {
                statusMessage = "Generate before previewing.";
                return;
            }

            isPreviewing = !isPreviewing;
            if (isPreviewing)
            {
                previewStartTime = EditorApplication.timeSinceStartup;
                statusMessage = "Preview running.";
            }
            else
            {
                statusMessage = "Preview paused.";
            }

            Repaint();
        }

        private void ExportGif()
        {
            if (!EnsureReadyForExport("GIF"))
            {
                return;
            }

            ExportWith(GifExporter.Export, "GIF");
        }

        private void ExportMp4()
        {
            if (!EnsureReadyForExport("MP4"))
            {
                return;
            }

            ExportWith(Mp4Exporter.Export, "MP4");
        }

        private bool EnsureReadyForExport(string formatLabel)
        {
            if (hasPendingSettings)
            {
                statusMessage = "Generate before exporting after changing settings.";
                return false;
            }

            if (!hasGenerated)
            {
                statusMessage = $"{formatLabel} export unavailable. Generate first.";
                return false;
            }

            return true;
        }

        private void ExportWith(Action<string> exporter, string formatLabel)
        {
            try
            {
                var outputDirectory = GetAbsoluteExportDirectory();
                Directory.CreateDirectory(outputDirectory);
                exporter(outputDirectory);
                statusMessage = $"{formatLabel} export requested to {outputDirectory}.";
            }
            catch (Exception exception)
            {
                statusMessage = $"{formatLabel} export failed: {exception.Message}";
            }
        }

        private static string GetAbsoluteExportDirectory()
        {
            var projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                throw new InvalidOperationException("Unable to resolve project root path for exports.");
            }

            return Path.GetFullPath(Path.Combine(projectRoot.FullName, ExportFolderPath));
        }

        private Texture RenderCurrentPreviewFrame()
        {
            if (renderer == null)
            {
                return null;
            }

            if (isPreviewing && hasGenerated && !hasPendingSettings)
            {
                var elapsed = (float)(EditorApplication.timeSinceStartup - previewStartTime);
                return renderer.RenderPreview(generatedSettings, elapsed);
            }

            if (hasGenerated && !hasPendingSettings)
            {
                return renderer.Render(generatedSettings, 0);
            }

            return renderer.Render(settings, 0);
        }

        private void LoadState()
        {
            var serializedState = EditorPrefs.GetString(StateKey);
            if (string.IsNullOrEmpty(serializedState))
            {
                settings = LoopLabRenderSettings.Default;
                generatedSettings = settings;
                hasGenerated = false;
                hasPendingSettings = false;
                isPreviewing = false;
                return;
            }

            var restoredState = JsonUtility.FromJson<WindowState>(serializedState);
            if (restoredState == null)
            {
                settings = LoopLabRenderSettings.Default;
                generatedSettings = settings;
                hasGenerated = false;
                hasPendingSettings = false;
                isPreviewing = false;
                return;
            }

            settings = restoredState.Settings;
            generatedSettings = restoredState.GeneratedSettings;
            hasGenerated = restoredState.HasGenerated;
            hasPendingSettings = restoredState.HasPendingSettings;

            if (!hasGenerated || hasPendingSettings)
            {
                isPreviewing = false;
            }
            else
            {
                isPreviewing = restoredState.IsPreviewing;
            }

            previewStartTime = isPreviewing && restoredState.PreviewStartTime > EditorApplication.timeSinceStartup
                ? EditorApplication.timeSinceStartup
                : restoredState.PreviewStartTime;
        }

        private void SaveState()
        {
            var state = new WindowState
            {
                Settings = settings,
                GeneratedSettings = generatedSettings,
                HasGenerated = hasGenerated,
                HasPendingSettings = hasPendingSettings,
                IsPreviewing = isPreviewing,
                PreviewStartTime = previewStartTime
            };

            EditorPrefs.SetString(StateKey, JsonUtility.ToJson(state));
        }

        private void HandleEditorUpdate()
        {
            if (renderer == null)
            {
                return;
            }

            if (isPreviewing)
            {
                Repaint();
            }
        }
    }
}
