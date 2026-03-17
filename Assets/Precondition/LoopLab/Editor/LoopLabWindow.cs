using System;
using System.Collections.Generic;
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
        private readonly List<SavedPresetState> savedPresets = new();
        private int selectedSavedPresetIndex = -1;
        private string savedPresetName = string.Empty;
        private string exportDirectoryPath = string.Empty;

        private enum PreviewMode
        {
            Single = 0,
            Tiled2x2 = 1
        }

        [Serializable]
        private sealed class SavedPresetState
        {
            public string Name = string.Empty;
            public LoopLabRenderSettings Settings = LoopLabRenderSettings.Default;

            public SavedPresetState Clone()
            {
                return new SavedPresetState
                {
                    Name = Name,
                    Settings = Settings
                };
            }
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
            public SavedPresetState[] SavedPresets = Array.Empty<SavedPresetState>();
            public int SelectedSavedPresetIndex = -1;
            public string SavedPresetName = string.Empty;
            public string ExportDirectoryPath = string.Empty;
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
            var randomizeSeedRequested = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                settings.Seed = EditorGUILayout.IntField("Seed", settings.Seed);
                randomizeSeedRequested = GUILayout.Button("Randomize", GUILayout.Width(96f));
            }

            if (EditorGUI.EndChangeCheck())
            {
                MarkSettingsDirty("Settings updated. Generate to refresh loop.");
            }

            if (randomizeSeedRequested)
            {
                RandomizeSeed();
            }

            EditorGUI.BeginChangeCheck();
            previewMode = (PreviewMode)EditorGUILayout.EnumPopup("Preview Mode", previewMode);
            if (EditorGUI.EndChangeCheck())
            {
                PersistState();
                Repaint();
            }

            if (previewMode == PreviewMode.Tiled2x2)
            {
                EditorGUILayout.HelpBox("Tiled mode repeats the current frame in a 2x2 layout and highlights the seam boundaries.", MessageType.Info);
            }

            GUILayout.Space(8f);
            DrawSavedPresetControls();
            GUILayout.Space(8f);
            DrawExportDestinationControls();
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

                if (GUILayout.Button("Open Export Location"))
                {
                    RevealExportDirectory();
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
            settings = generatedSettings;
            hasGenerated = true;
            hasPendingSettings = false;
            isPreviewing = false;
            previewStartTime = EditorApplication.timeSinceStartup;
            statusMessage =
                $"Generating {LoopLabPresetCatalog.GetDisplayName(generatedSettings.Preset)} with seed {generatedSettings.Seed}...";
            renderer.Render(generatedSettings, 0);
            RefreshBoundaryValidation();
            var generationSummary =
                $"Generated {LoopLabPresetCatalog.GetDisplayName(generatedSettings.Preset)} with {generatedSettings.FrameCount} frames @ " +
                $"{generatedSettings.FramesPerSecond} FPS using seed {generatedSettings.Seed}.";
            statusMessage = boundaryValidation == null
                ? generationSummary
                : boundaryValidation.MatchesVisually
                    ? generationSummary + " Boundary check passed."
                    : generationSummary + " Boundary mismatch detected.";
            PersistState();
            Repaint();
        }

        private void TogglePreview()
        {
            if (!hasGenerated || hasPendingSettings)
            {
                statusMessage = "Generate before previewing.";
                PersistState();
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

            PersistState();
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
                PersistState();
                return false;
            }

            if (!hasGenerated)
            {
                statusMessage = $"{formatLabel} export unavailable. Generate first.";
                PersistState();
                return false;
            }

            return true;
        }

        private void ExportWith(Action<LoopLabRenderSettings, string> exporter, string formatLabel)
        {
            if (!TryGetAbsoluteExportDirectory(out var outputDirectory, out var exportDirectoryError))
            {
                statusMessage = $"{formatLabel} export failed. {exportDirectoryError}";
                PersistState();
                return;
            }

            statusMessage = $"{formatLabel} export started for {LoopLabPresetCatalog.GetDisplayName(generatedSettings.Preset)} -> {outputDirectory}.";
            Repaint();

            try
            {
                Directory.CreateDirectory(outputDirectory);
                exporter(generatedSettings, outputDirectory);
                statusMessage = $"{formatLabel} export completed to {outputDirectory}.";
            }
            catch (Exception exception)
            {
                statusMessage = $"{formatLabel} export failed for {outputDirectory}: {exception.Message}";
            }

            PersistState();
        }

        private bool TryGetAbsoluteExportDirectory(out string absoluteDirectory, out string errorMessage)
        {
            absoluteDirectory = string.Empty;
            errorMessage = string.Empty;

            var projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                errorMessage = "Unable to resolve project root path for exports.";
                return false;
            }

            try
            {
                var configuredDirectory = GetConfiguredExportDirectoryInput();
                absoluteDirectory = Path.IsPathRooted(configuredDirectory)
                    ? Path.GetFullPath(configuredDirectory)
                    : Path.GetFullPath(Path.Combine(projectRoot.FullName, configuredDirectory));
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = $"Invalid export destination: {exception.Message}";
                return false;
            }
        }

        private string GetAbsoluteExportDirectory()
        {
            if (!TryGetAbsoluteExportDirectory(out var absoluteDirectory, out var errorMessage))
            {
                throw new InvalidOperationException(errorMessage);
            }

            return absoluteDirectory;
        }

        private string GetConfiguredExportDirectoryInput()
        {
            return string.IsNullOrWhiteSpace(exportDirectoryPath)
                ? ExportFolderPath
                : exportDirectoryPath.Trim();
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

            PersistState();
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
                savedPresets.Clear();
                selectedSavedPresetIndex = -1;
                savedPresetName = string.Empty;
                exportDirectoryPath = string.Empty;
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
                savedPresets.Clear();
                selectedSavedPresetIndex = -1;
                savedPresetName = string.Empty;
                exportDirectoryPath = string.Empty;
                return;
            }

            settings = restoredState.Settings;
            generatedSettings = restoredState.GeneratedSettings.GetValidated();
            hasGenerated = restoredState.HasGenerated;
            hasPendingSettings = restoredState.HasPendingSettings;
            previewMode = restoredState.PreviewMode;
            savedPresets.Clear();
            if (restoredState.SavedPresets != null)
            {
                foreach (var preset in restoredState.SavedPresets)
                {
                    if (preset == null || string.IsNullOrWhiteSpace(preset.Name))
                    {
                        continue;
                    }

                    savedPresets.Add(new SavedPresetState
                    {
                        Name = preset.Name.Trim(),
                        Settings = preset.Settings.GetValidated()
                    });
                }
            }

            selectedSavedPresetIndex = savedPresets.Count == 0
                ? -1
                : Mathf.Clamp(restoredState.SelectedSavedPresetIndex, 0, savedPresets.Count - 1);
            savedPresetName = string.IsNullOrWhiteSpace(restoredState.SavedPresetName)
                ? selectedSavedPresetIndex >= 0
                    ? savedPresets[selectedSavedPresetIndex].Name
                    : string.Empty
                : restoredState.SavedPresetName.Trim();
            exportDirectoryPath = restoredState.ExportDirectoryPath ?? string.Empty;

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
                PreviewStartTime = previewStartTime,
                SavedPresets = GetSavedPresetStateSnapshot(),
                SelectedSavedPresetIndex = selectedSavedPresetIndex,
                SavedPresetName = savedPresetName,
                ExportDirectoryPath = exportDirectoryPath
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

        private void DrawSavedPresetControls()
        {
            EditorGUILayout.LabelField("Saved Presets", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                savedPresetName = EditorGUILayout.TextField("Name", savedPresetName);
                if (GUILayout.Button("Save Current", GUILayout.Width(110f)))
                {
                    SaveCurrentSettingsToPreset();
                }
            }

            if (savedPresets.Count == 0)
            {
                EditorGUILayout.HelpBox("Save the current settings to reload them quickly across sessions.", MessageType.Info);
                return;
            }

            var selectedIndex = GetClampedSelectedSavedPresetIndex();
            var savedPresetOptions = GetSavedPresetOptions();
            EditorGUI.BeginChangeCheck();
            selectedIndex = EditorGUILayout.Popup("Manage Preset", selectedIndex, savedPresetOptions);
            if (EditorGUI.EndChangeCheck())
            {
                SelectSavedPreset(selectedIndex, updateNameField: true);
                PersistState();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledGroupScope(GetClampedSelectedSavedPresetIndex() < 0))
                {
                    if (GUILayout.Button("Load Selected"))
                    {
                        LoadSavedPreset(GetClampedSelectedSavedPresetIndex());
                    }

                    if (GUILayout.Button("Delete Selected"))
                    {
                        DeleteSavedPreset(GetClampedSelectedSavedPresetIndex());
                    }
                }
            }

            EditorGUILayout.LabelField("Quick Load", EditorStyles.miniBoldLabel);
            var quickLoadSelection = GUILayout.SelectionGrid(selectedIndex, savedPresetOptions, Mathf.Min(2, savedPresetOptions.Length));
            if (quickLoadSelection != selectedIndex)
            {
                LoadSavedPreset(quickLoadSelection);
            }
        }

        private void DrawExportDestinationControls()
        {
            EditorGUILayout.LabelField("Export Destination", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            exportDirectoryPath = EditorGUILayout.TextField("Folder", GetConfiguredExportDirectoryInput());
            if (EditorGUI.EndChangeCheck())
            {
                exportDirectoryPath = exportDirectoryPath.Trim();
                statusMessage = $"Export destination updated to {GetConfiguredExportDirectoryInput()}.";
                PersistState();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Browse..."))
                {
                    ChooseExportDirectory();
                }

                if (GUILayout.Button("Reset"))
                {
                    ResetExportDirectory();
                }
            }

            if (TryGetAbsoluteExportDirectory(out var absoluteDirectory, out var errorMessage))
            {
                EditorGUILayout.LabelField("Resolved Path", absoluteDirectory);
            }
            else
            {
                EditorGUILayout.HelpBox(errorMessage, MessageType.Warning);
            }
        }

        private void MarkSettingsDirty(string updatedStatusMessage)
        {
            hasPendingSettings = !hasGenerated || !settings.GetValidated().Equals(generatedSettings);
            isPreviewing = false;
            statusMessage = updatedStatusMessage;
            PersistState();
        }

        private void RandomizeSeed()
        {
            settings.Seed = LoopLabRenderSettings.RandomizeSeed(settings.Seed);
            MarkSettingsDirty($"Seed randomized to {settings.Seed}. Generate to refresh loop.");
        }

        private void SaveCurrentSettingsToPreset()
        {
            settings = settings.GetValidated();
            var normalizedName = GetNormalizedSavedPresetName();
            var existingIndex = FindSavedPresetIndexByName(normalizedName);
            var savedPreset = new SavedPresetState
            {
                Name = normalizedName,
                Settings = settings
            };

            if (existingIndex >= 0)
            {
                savedPresets[existingIndex] = savedPreset;
                selectedSavedPresetIndex = existingIndex;
                statusMessage = $"Updated preset '{normalizedName}'.";
            }
            else
            {
                savedPresets.Add(savedPreset);
                selectedSavedPresetIndex = savedPresets.Count - 1;
                statusMessage = $"Saved preset '{normalizedName}'.";
            }

            savedPresetName = normalizedName;
            hasPendingSettings = !hasGenerated || !settings.Equals(generatedSettings);
            isPreviewing = false;
            PersistState();
        }

        private void LoadSavedPreset(int presetIndex)
        {
            if (!IsValidSavedPresetIndex(presetIndex))
            {
                statusMessage = "Select a saved preset to load.";
                return;
            }

            var savedPreset = savedPresets[presetIndex];
            settings = savedPreset.Settings.GetValidated();
            SelectSavedPreset(presetIndex, updateNameField: true);
            hasPendingSettings = !hasGenerated || !settings.Equals(generatedSettings);
            isPreviewing = false;
            statusMessage = hasPendingSettings
                ? $"Loaded preset '{savedPreset.Name}'. Generate to refresh loop."
                : $"Loaded preset '{savedPreset.Name}'. Current preview matches the generated loop.";
            PersistState();
            Repaint();
        }

        private void DeleteSavedPreset(int presetIndex)
        {
            if (!IsValidSavedPresetIndex(presetIndex))
            {
                statusMessage = "Select a saved preset to delete.";
                return;
            }

            var deletedPresetName = savedPresets[presetIndex].Name;
            savedPresets.RemoveAt(presetIndex);

            if (savedPresets.Count == 0)
            {
                selectedSavedPresetIndex = -1;
                savedPresetName = string.Empty;
            }
            else
            {
                SelectSavedPreset(Mathf.Clamp(presetIndex, 0, savedPresets.Count - 1), updateNameField: true);
            }

            statusMessage = $"Deleted preset '{deletedPresetName}'.";
            PersistState();
        }

        private void SelectSavedPreset(int presetIndex, bool updateNameField)
        {
            selectedSavedPresetIndex = IsValidSavedPresetIndex(presetIndex) ? presetIndex : -1;
            if (updateNameField)
            {
                savedPresetName = selectedSavedPresetIndex >= 0
                    ? savedPresets[selectedSavedPresetIndex].Name
                    : string.Empty;
            }
        }

        private string[] GetSavedPresetOptions()
        {
            var options = new string[savedPresets.Count];
            for (var index = 0; index < savedPresets.Count; index++)
            {
                options[index] = savedPresets[index].Name;
            }

            return options;
        }

        private SavedPresetState[] GetSavedPresetStateSnapshot()
        {
            var snapshot = new SavedPresetState[savedPresets.Count];
            for (var index = 0; index < savedPresets.Count; index++)
            {
                snapshot[index] = savedPresets[index].Clone();
            }

            return snapshot;
        }

        private int GetClampedSelectedSavedPresetIndex()
        {
            if (savedPresets.Count == 0)
            {
                return -1;
            }

            return Mathf.Clamp(selectedSavedPresetIndex, 0, savedPresets.Count - 1);
        }

        private bool IsValidSavedPresetIndex(int presetIndex)
        {
            return presetIndex >= 0 && presetIndex < savedPresets.Count;
        }

        private int FindSavedPresetIndexByName(string presetName)
        {
            for (var index = 0; index < savedPresets.Count; index++)
            {
                if (string.Equals(savedPresets[index].Name, presetName, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private string GetNormalizedSavedPresetName()
        {
            if (!string.IsNullOrWhiteSpace(savedPresetName))
            {
                return savedPresetName.Trim();
            }

            var validatedSettings = settings.GetValidated();
            return $"{validatedSettings.Preset} {validatedSettings.Seed}";
        }

        private void ChooseExportDirectory()
        {
            var startingDirectory = TryGetAbsoluteExportDirectory(out var absoluteDirectory, out _)
                ? absoluteDirectory
                : Directory.GetCurrentDirectory();
            var selectedDirectory = EditorUtility.OpenFolderPanel("Select LoopLab export folder", startingDirectory, string.Empty);
            if (string.IsNullOrEmpty(selectedDirectory))
            {
                return;
            }

            exportDirectoryPath = selectedDirectory;
            statusMessage = $"Export destination updated to {exportDirectoryPath}.";
            PersistState();
        }

        private void ResetExportDirectory()
        {
            exportDirectoryPath = string.Empty;
            statusMessage = $"Export destination reset to {ExportFolderPath}.";
            PersistState();
        }

        private void RevealExportDirectory()
        {
            if (!TryGetAbsoluteExportDirectory(out var outputDirectory, out var errorMessage))
            {
                statusMessage = errorMessage;
                PersistState();
                return;
            }

            Directory.CreateDirectory(outputDirectory);
            EditorUtility.RevealInFinder(outputDirectory);
            statusMessage = $"Opened export location {outputDirectory}.";
            PersistState();
        }

        private void PersistState()
        {
            SaveState();
        }
    }
}
