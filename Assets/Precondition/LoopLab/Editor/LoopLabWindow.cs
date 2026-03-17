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
        private static readonly Color PreviewBackgroundColor = new(0.09f, 0.1f, 0.12f);
        private static readonly Color PreviewSeamColor = new(0.93f, 0.79f, 0.42f, 0.92f);
        private static readonly Color PreviewOutlineColor = new(0.22f, 0.24f, 0.29f, 0.95f);

        private LoopLabRenderSettings settings = LoopLabRenderSettings.Default;
        private LoopLabRenderSettings generatedSettings = LoopLabRenderSettings.Default;
        private LoopRenderer renderer;
        private LoopBoundaryValidationResult boundaryValidation;
        private Vector2 scrollPosition;
        private bool hasGenerated;
        private bool hasPendingSettings;
        private bool isPreviewing;
        private PreviewMode previewMode = PreviewMode.Single;
        private double previewStartTime;
        private string statusMessage = "Ready";

        private enum PreviewMode
        {
            Single = 0,
            Tiled2x2 = 1
        }

        [Serializable]
        private sealed class WindowState
        {
            public LoopLabRenderSettings Settings = LoopLabRenderSettings.Default;
            public LoopLabRenderSettings GeneratedSettings = LoopLabRenderSettings.Default;
            public bool HasGenerated;
            public bool HasPendingSettings;
            public bool IsPreviewing;
            public PreviewMode PreviewMode = PreviewMode.Single;
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
            if (LoopLabPresetCatalog.SupportsContrastMode(settings.Preset))
            {
                settings.ContrastMode = (LoopLabContrastMode)EditorGUILayout.EnumPopup("Palette Contrast", settings.ContrastMode);
            }

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

            EditorGUI.BeginChangeCheck();
            previewMode = (PreviewMode)EditorGUILayout.EnumPopup("Preview Mode", previewMode);
            if (EditorGUI.EndChangeCheck())
            {
                Repaint();
            }

            if (previewMode == PreviewMode.Tiled2x2)
            {
                EditorGUILayout.HelpBox("Tiled mode repeats the current frame in a 2x2 layout and highlights the seam boundaries.", MessageType.Info);
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
                EditorGUILayout.HelpBox("A test frame is rendered for the current settings. Generate to lock loop state for exports.", MessageType.Info);
            }

            if (hasPendingSettings)
            {
                EditorGUILayout.HelpBox("Settings have changed since last generation.", MessageType.Warning);
            }

            DrawBoundaryValidationSummary();

            DrawPreview();

            DrawBoundaryValidation();

            GUILayout.Space(12f);
            EditorGUILayout.LabelField("Current Preset", LoopLabPresetCatalog.GetDisplayName(settings.Preset));
            if (LoopLabPresetCatalog.SupportsContrastMode(settings.Preset))
            {
                EditorGUILayout.LabelField("Current Contrast", settings.ContrastMode.ToString());
            }

            EditorGUILayout.LabelField("Generated Preset", LoopLabPresetCatalog.GetDisplayName(generatedSettings.Preset));
            if (LoopLabPresetCatalog.SupportsContrastMode(generatedSettings.Preset))
            {
                EditorGUILayout.LabelField("Generated Contrast", generatedSettings.ContrastMode.ToString());
            }

            EditorGUILayout.LabelField("Generated Seed", generatedSettings.Seed.ToString());
            EditorGUILayout.LabelField("Generated Frame Count", generatedSettings.FrameCount.ToString());
            EditorGUILayout.LabelField("Shader", LoopLabPresetCatalog.GetShaderName(generatedSettings.Preset));
        }

        private void DrawPreview()
        {
            var previewTexture = RenderCurrentPreviewFrame();
            var rect = GUILayoutUtility.GetAspectRect(1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, PreviewBackgroundColor);

            if (previewTexture != null)
            {
                if (previewMode == PreviewMode.Tiled2x2)
                {
                    DrawTiledPreview(rect, previewTexture);
                }
                else
                {
                    DrawPreviewTexture(rect, previewTexture);
                }
            }
        }

        private void GenerateLoop()
        {
            renderer ??= new LoopRenderer();
            generatedSettings = settings.GetValidated();
            hasGenerated = true;
            hasPendingSettings = false;
            isPreviewing = false;
            previewStartTime = EditorApplication.timeSinceStartup;
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

        private void ExportWith(Func<LoopLabRenderSettings, string, string> exporter, string formatLabel)
        {
            try
            {
                var outputDirectory = GetAbsoluteExportDirectory();
                Directory.CreateDirectory(outputDirectory);
                var outputPath = exporter(generatedSettings, outputDirectory);
                statusMessage = $"{formatLabel} exported to {outputPath}.";
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

            return renderer.Render(settings.GetValidated(), 0);
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
            EditorGUI.DrawRect(rect, PreviewBackgroundColor);

            if (texture != null)
            {
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);
            }
        }

        private static void DrawPreviewTexture(Rect rect, Texture previewTexture)
        {
            GUI.DrawTexture(rect, previewTexture, ScaleMode.ScaleToFit, false);
        }

        private static void DrawTiledPreview(Rect rect, Texture previewTexture)
        {
            foreach (var tileRect in GetTiledPreviewTileRects(rect))
            {
                DrawPreviewTexture(tileRect, previewTexture);
            }

            DrawPreviewSeamGuides(rect);
        }

        private static void DrawPreviewSeamGuides(Rect rect)
        {
            foreach (var seamRect in GetPreviewSeamRects(rect))
            {
                EditorGUI.DrawRect(seamRect, PreviewSeamColor);
            }

            var outlineThickness = Mathf.Max(1f, Mathf.Round(rect.width * 0.004f));
            DrawRectOutline(rect, outlineThickness, PreviewOutlineColor);
        }

        private static Rect[] GetTiledPreviewTileRects(Rect rect)
        {
            var halfWidth = rect.width * 0.5f;
            var halfHeight = rect.height * 0.5f;

            return new[]
            {
                new Rect(rect.xMin, rect.yMin, halfWidth, halfHeight),
                new Rect(rect.xMin + halfWidth, rect.yMin, halfWidth, halfHeight),
                new Rect(rect.xMin, rect.yMin + halfHeight, halfWidth, halfHeight),
                new Rect(rect.xMin + halfWidth, rect.yMin + halfHeight, halfWidth, halfHeight)
            };
        }

        private static Rect[] GetPreviewSeamRects(Rect rect)
        {
            var seamThickness = Mathf.Max(2f, Mathf.Round(rect.width * 0.01f));

            return new[]
            {
                new Rect(rect.center.x - (seamThickness * 0.5f), rect.yMin, seamThickness, rect.height),
                new Rect(rect.xMin, rect.center.y - (seamThickness * 0.5f), rect.width, seamThickness)
            };
        }

        private static void DrawRectOutline(Rect rect, float thickness, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
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
                previewMode = PreviewMode.Single;
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
                previewMode = PreviewMode.Single;
                return;
            }

            settings = restoredState.Settings;
            generatedSettings = restoredState.GeneratedSettings.GetValidated();
            hasGenerated = restoredState.HasGenerated;
            hasPendingSettings = restoredState.HasPendingSettings;
            previewMode = restoredState.PreviewMode;

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
                PreviewMode = previewMode,
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
