using System;
using System.IO;
using System.Security.Cryptography;
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
        private LoopBoundaryValidationResult boundaryValidation;
        private Vector2 scrollPosition;
        private bool hasGenerated;
        private bool hasPendingSettings;
        private bool isPreviewing;
        private float previewElapsedSeconds;
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
            public float PreviewElapsedSeconds;
            public double PreviewStartTime;
        }

        [MenuItem("Precondition/LoopLab", priority = 100)]
        public static void ShowWindow()
        {
            GetOrCreateWindow();
        }

        [MenuItem("Precondition/LoopLab/Generate Current Loop", priority = 101)]
        private static void GenerateCurrentLoopFromMenu()
        {
            var window = GetOrCreateWindow();
            window.GenerateLoop();
        }

        [MenuItem("Precondition/LoopLab/Toggle Preview", priority = 102)]
        private static void TogglePreviewFromMenu()
        {
            var window = GetOrCreateWindow();
            window.TogglePreview();
        }

        private static LoopLabWindow GetOrCreateWindow()
        {
            var window = GetWindow<LoopLabWindow>("LoopLab");
            window.minSize = new Vector2(440f, 560f);
            window.Focus();
            window.Repaint();
            return window;
        }

        private void OnEnable()
        {
            LoadState();
            renderer ??= new LoopRenderer();
            RefreshBoundaryValidation();
            previewStartTime = EditorApplication.timeSinceStartup - previewElapsedSeconds;
            EditorApplication.update += HandleEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= HandleEditorUpdate;
            SaveState();
            boundaryValidation?.Dispose();
            boundaryValidation = null;
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
                HandleSettingsChanged("Settings updated.");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Randomize Seed", GUILayout.Width(128f)))
                {
                    RandomizeSeed();
                }
            }

            GUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate"))
                {
                    GenerateLoop();
                }

                if (GUILayout.Button(isPreviewing ? "Pause" : "Play"))
                {
                    TogglePreview();
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

                if (GUILayout.Button("Validate All Presets"))
                {
                    RunBoundaryValidationBatch();
                }
            }

            GUILayout.Space(8f);

            EditorGUILayout.LabelField("Status", statusMessage);
            GUILayout.Space(4f);

            if (!hasGenerated)
            {
                EditorGUILayout.HelpBox(
                    "Preview renders current settings live. Generate to lock a loop for exports and boundary QA.",
                    MessageType.Info);
            }

            if (hasGenerated && hasPendingSettings)
            {
                EditorGUILayout.HelpBox(
                    "Preview reflects live settings. Generate again to refresh the saved loop used for exports and boundary QA.",
                    MessageType.Warning);
            }

            DrawBoundaryValidationSummary();
            DrawPreviewControls();

            DrawPreview();

            DrawBoundaryValidation();

            GUILayout.Space(12f);
            EditorGUILayout.LabelField("Current Preset", LoopLabPresetCatalog.GetDisplayName(settings.Preset));
            EditorGUILayout.LabelField(
                "Generated Preset",
                hasGenerated ? LoopLabPresetCatalog.GetDisplayName(generatedSettings.Preset) : "Not generated yet");
            EditorGUILayout.LabelField("Generated Seed", hasGenerated ? generatedSettings.Seed.ToString() : "Not generated yet");
            EditorGUILayout.LabelField(
                "Generated Frame Count",
                hasGenerated ? generatedSettings.FrameCount.ToString() : "Not generated yet");
            EditorGUILayout.LabelField("Shader", LoopLabPresetCatalog.GetShaderName(GetPreviewSettings().Preset));
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

        private void DrawPreviewControls()
        {
            var previewSettings = GetPreviewSettings();
            var previewElapsed = GetPreviewElapsedForSettings(previewSettings);
            var previewFrame = LoopLabRenderSettings.GetPreviewFrameIndex(
                previewElapsed,
                previewSettings.DurationSeconds,
                previewSettings.FrameCount);
            var previewSource = hasGenerated && !hasPendingSettings ? "Generated loop" : "Live settings";

            GUILayout.Space(12f);
            EditorGUILayout.LabelField("Preview Controls", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Source", previewSource);
                EditorGUILayout.LabelField("Frame", $"{previewFrame + 1} / {previewSettings.FrameCount}", GUILayout.Width(112f));
            }

            EditorGUI.BeginChangeCheck();
            var scrubbedTime = EditorGUILayout.Slider(
                "Timeline",
                previewElapsed,
                0f,
                GetPreviewScrubMaximum(previewSettings));

            if (EditorGUI.EndChangeCheck())
            {
                ScrubPreview(scrubbedTime);
            }

            EditorGUILayout.LabelField(
                "Preview Time",
                $"{previewElapsed:0.00}s / {previewSettings.ValidatedDurationSeconds:0.00}s");
        }

        private void GenerateLoop()
        {
            renderer ??= new LoopRenderer();
            previewElapsedSeconds = GetPreviewElapsedForSettings(settings.GetValidated());
            generatedSettings = settings.GetValidated();
            hasGenerated = true;
            UpdatePendingState();
            previewStartTime = EditorApplication.timeSinceStartup - previewElapsedSeconds;
            renderer.Render(generatedSettings, 0);
            RefreshBoundaryValidation();
            var generationSummary =
                $"Generated {generatedSettings.FrameCount} frames @ {generatedSettings.FramesPerSecond} FPS using seed {generatedSettings.Seed}.";
            statusMessage = boundaryValidation == null
                ? generationSummary
                : boundaryValidation.MatchesVisually
                    ? generationSummary + " Boundary check passed."
                    : generationSummary + " Boundary mismatch detected.";
            Repaint();
        }

        private void TogglePreview()
        {
            if (isPreviewing)
            {
                previewElapsedSeconds = GetCurrentPreviewElapsedSeconds();
                isPreviewing = false;
                statusMessage = "Preview paused.";
            }
            else
            {
                previewElapsedSeconds = GetCurrentPreviewElapsedSeconds();
                previewStartTime = EditorApplication.timeSinceStartup - previewElapsedSeconds;
                isPreviewing = true;
                statusMessage = "Preview running.";
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

        private void ExportWith(Action<LoopLabRenderSettings, string> exporter, string formatLabel)
        {
            try
            {
                var outputDirectory = GetAbsoluteExportDirectory();
                Directory.CreateDirectory(outputDirectory);
                exporter(generatedSettings, outputDirectory);
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

            var previewSettings = GetPreviewSettings();
            var previewElapsed = GetPreviewElapsedForSettings(previewSettings);
            return renderer.RenderPreview(previewSettings, previewElapsed);
        }

        private void DrawBoundaryValidation()
        {
            GUILayout.Space(12f);
            EditorGUILayout.LabelField("Loop Boundary QA", EditorStyles.boldLabel);

            if (!hasGenerated)
            {
                EditorGUILayout.HelpBox("Generate a loop to compare frame 0 with the restart frame.", MessageType.Info);
                return;
            }

            if (boundaryValidation == null)
            {
                EditorGUILayout.HelpBox("Boundary validation is unavailable for the current generated loop.", MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                boundaryValidation.Summary + " Frame N is sampled at normalized phase 1.0 for the restart sanity check.",
                boundaryValidation.MatchesVisually ? MessageType.Info : MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawValidationTexture("Frame 0", boundaryValidation.FirstFrame);
                DrawValidationTexture($"Frame {boundaryValidation.ComparedFrameIndex}", boundaryValidation.BoundaryFrame);
                DrawValidationTexture("Difference", boundaryValidation.DifferenceTexture);
            }
        }

        private void DrawBoundaryValidationSummary()
        {
            if (!hasGenerated)
            {
                return;
            }

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Boundary Status", EditorStyles.miniBoldLabel);

            if (boundaryValidation == null)
            {
                EditorGUILayout.HelpBox("Boundary validation is unavailable for the current generated loop.", MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                boundaryValidation.Summary,
                boundaryValidation.MatchesVisually ? MessageType.Info : MessageType.Warning);

            if (hasPendingSettings)
            {
                EditorGUILayout.HelpBox(
                    "Boundary QA still reflects the last generated loop. Generate again to re-check the current settings.",
                    MessageType.Warning);
            }
        }

        private static void DrawValidationTexture(string label, Texture texture)
        {
            using var column = new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

            var rect = GUILayoutUtility.GetAspectRect(1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.09f, 0.1f, 0.12f));

            if (texture != null)
            {
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);
            }
        }

        private void RefreshBoundaryValidation()
        {
            boundaryValidation?.Dispose();
            boundaryValidation = null;

            if (!hasGenerated)
            {
                return;
            }

            renderer ??= new LoopRenderer();
            boundaryValidation = LoopBoundaryValidator.Capture(renderer, generatedSettings);
        }

        private void RunBoundaryValidationBatch()
        {
            try
            {
                var outputDirectory = LoopBoundaryValidationBatch.RunAllPresetsToDefaultOutput();
                statusMessage = $"Boundary validation passed for all presets. Snapshots saved to {outputDirectory}.";
            }
            catch (Exception exception)
            {
                statusMessage = "Boundary validation failed. Check the Console for details.";
                Debug.LogException(exception);
            }

            Repaint();
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
                previewElapsedSeconds = 0f;
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
                previewElapsedSeconds = 0f;
                return;
            }

            settings = restoredState.Settings;
            generatedSettings = restoredState.GeneratedSettings.GetValidated();
            hasGenerated = restoredState.HasGenerated;
            UpdatePendingState();
            isPreviewing = restoredState.IsPreviewing;
            previewElapsedSeconds = Mathf.Clamp(
                restoredState.PreviewElapsedSeconds,
                0f,
                GetPreviewScrubMaximum(GetPreviewSettings()));
            previewStartTime = EditorApplication.timeSinceStartup - previewElapsedSeconds;
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
                PreviewElapsedSeconds = GetCurrentPreviewElapsedSeconds(),
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
                previewElapsedSeconds = GetCurrentPreviewElapsedSeconds();
                Repaint();
            }
        }

        private void HandleSettingsChanged(string changeSummary)
        {
            previewElapsedSeconds = GetPreviewElapsedForSettings(settings.GetValidated());
            UpdatePendingState();
            previewStartTime = EditorApplication.timeSinceStartup - previewElapsedSeconds;
            statusMessage = BuildSettingsChangedStatus(changeSummary);
            Repaint();
        }

        private string BuildSettingsChangedStatus(string changeSummary)
        {
            if (hasGenerated && hasPendingSettings)
            {
                return changeSummary + " Preview reflects live settings. Generate to refresh exports.";
            }

            if (hasGenerated)
            {
                return changeSummary + " Preview matches the generated loop.";
            }

            return changeSummary + " Preview updated.";
        }

        private LoopLabRenderSettings GetPreviewSettings()
        {
            return hasGenerated && !hasPendingSettings
                ? generatedSettings
                : settings.GetValidated();
        }

        private float GetPreviewElapsedForSettings(LoopLabRenderSettings previewSettings)
        {
            if (isPreviewing)
            {
                return LoopLabRenderSettings.NormalizePreviewElapsedSeconds(
                    (float)(EditorApplication.timeSinceStartup - previewStartTime),
                    previewSettings.DurationSeconds);
            }

            return Mathf.Clamp(
                previewElapsedSeconds,
                0f,
                GetPreviewScrubMaximum(previewSettings));
        }

        private static float GetPreviewScrubMaximum(LoopLabRenderSettings previewSettings)
        {
            if (previewSettings.FrameCount <= 1)
            {
                return 0f;
            }

            return previewSettings.ValidatedDurationSeconds - (previewSettings.ValidatedDurationSeconds / previewSettings.FrameCount);
        }

        private float GetCurrentPreviewElapsedSeconds()
        {
            return GetPreviewElapsedForSettings(GetPreviewSettings());
        }

        private void RandomizeSeed()
        {
            settings.Seed = RandomNumberGenerator.GetInt32(1, LoopLabRenderSettings.MaxSupportedSeedValue + 1);
            HandleSettingsChanged("Seed randomized.");
        }

        private void ScrubPreview(float previewSeconds)
        {
            previewElapsedSeconds = Mathf.Clamp(previewSeconds, 0f, GetPreviewScrubMaximum(GetPreviewSettings()));
            previewStartTime = EditorApplication.timeSinceStartup - previewElapsedSeconds;
            Repaint();
        }

        private void UpdatePendingState()
        {
            hasPendingSettings = hasGenerated && !settings.GetValidated().Equals(generatedSettings);
        }
    }
}
