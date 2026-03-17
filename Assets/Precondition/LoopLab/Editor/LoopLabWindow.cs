using UnityEditor;
using UnityEngine;

namespace Precondition.LoopLab.Editor
{
    public sealed class LoopLabWindow : EditorWindow
    {
        private LoopLabRenderSettings settings = LoopLabRenderSettings.Default;
        private LoopRenderer renderer;
        private Vector2 scrollPosition;

        [MenuItem("Precondition/LoopLab", priority = 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<LoopLabWindow>("LoopLab");
            window.minSize = new Vector2(440f, 560f);
            window.GeneratePreview();
        }

        private void OnEnable()
        {
            renderer ??= new LoopRenderer();
        }

        private void OnDisable()
        {
            renderer?.Dispose();
            renderer = null;
        }

        private void OnGUI()
        {
            using var scrollView = new EditorGUILayout.ScrollViewScope(scrollPosition);
            scrollPosition = scrollView.scrollPosition;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("LoopLab", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Foundation scaffold for the LoopLab editor workflow. The preview uses placeholder URP shaders so the project opens with a working render surface.",
                MessageType.Info);

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
                GeneratePreview();
            }

            GUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate Preview"))
                {
                    GeneratePreview();
                }

                if (GUILayout.Button("Rebuild Scaffold"))
                {
                    LoopLabProjectBootstrap.RunInteractive();
                    GeneratePreview();
                }

                if (GUILayout.Button("Open Exports Folder"))
                {
                    LoopLabProjectBootstrap.RevealExportsFolder();
                }
            }

            GUILayout.Space(12f);

            DrawPreview();

            GUILayout.Space(12f);
            EditorGUILayout.LabelField("Current Preset", LoopLabPresetCatalog.GetDisplayName(settings.Preset));
            EditorGUILayout.LabelField("Frame Count", settings.FrameCount.ToString());
            EditorGUILayout.LabelField("Shader", LoopLabPresetCatalog.GetShaderName(settings.Preset));
        }

        private void DrawPreview()
        {
            var texture = renderer?.Render(settings);
            var rect = GUILayoutUtility.GetAspectRect(1f, GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(rect, new Color(0.09f, 0.1f, 0.12f));

            if (texture != null)
            {
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);
            }
        }

        private void GeneratePreview()
        {
            renderer ??= new LoopRenderer();
            renderer.Render(settings);
            Repaint();
        }
    }
}
