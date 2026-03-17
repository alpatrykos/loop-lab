using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Precondition.LoopLab.Editor
{
    public static class LoopLabProjectBootstrap
    {
        private const string RootPath = "Assets/Precondition/LoopLab";
        private const string ResourcesPath = RootPath + "/Resources";
        private const string MaterialsPath = ResourcesPath + "/Materials";
        private const string ExportsPath = RootPath + "/Exports";
        private const string ScenePath = RootPath + "/LoopLabSandbox.unity";
        private const string RendererAssetPath = MaterialsPath + "/LoopLabUniversalRenderer.asset";
        private const string PipelineAssetPath = MaterialsPath + "/LoopLabUniversalRenderPipeline.asset";

        [MenuItem("Precondition/LoopLab/Rebuild Scaffold", priority = 110)]
        public static void RunInteractive()
        {
            Run();
            Debug.Log("LoopLab scaffold refreshed.");
        }

        public static void RunBatchmode()
        {
            Run();
        }

        [MenuItem("Precondition/LoopLab/Open Exports Folder", priority = 120)]
        public static void RevealExportsFolder()
        {
            EnsureFolders();
            Directory.CreateDirectory(ToAbsoluteProjectPath(ExportsPath));
            EditorUtility.RevealInFinder(ToAbsoluteProjectPath(ExportsPath));
        }

        private static void Run()
        {
            EnsureFolders();
            ConfigureProjectDefaults();
            EnsureRenderPipeline();
            EnsurePreviewMaterials();
            EnsureScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void ConfigureProjectDefaults()
        {
            PlayerSettings.companyName = "Precondition";
            PlayerSettings.productName = "LoopLab";
        }

        private static void EnsureFolders()
        {
            EnsureFolder(RootPath);
            EnsureFolder(RootPath + "/Editor");
            EnsureFolder(RootPath + "/Editor/Export");
            EnsureFolder(RootPath + "/Runtime");
            EnsureFolder(RootPath + "/Runtime/Core");
            EnsureFolder(RootPath + "/Runtime/Presets");
            EnsureFolder(RootPath + "/Runtime/Shaders");
            EnsureFolder(ResourcesPath);
            EnsureFolder(ResourcesPath + "/LUTs");
            EnsureFolder(MaterialsPath);
            EnsureFolder(ExportsPath);
        }

        private static void EnsureFolder(string projectRelativePath)
        {
            var segments = projectRelativePath.Split('/');
            var currentPath = segments[0];

            for (var index = 1; index < segments.Length; index++)
            {
                var nextPath = currentPath + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, segments[index]);
                }

                currentPath = nextPath;
            }
        }

        private static void EnsureRenderPipeline()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererAssetPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                ResourceReloader.ReloadAllNullIn(rendererData, UniversalRenderPipelineAsset.packagePath);
                rendererData.postProcessData = LoadDefaultPostProcessData();
                AssetDatabase.CreateAsset(rendererData, RendererAssetPath);
            }
            else if (rendererData.postProcessData == null)
            {
                rendererData.postProcessData = LoadDefaultPostProcessData();
            }

            var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (pipelineAsset == null)
            {
                pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipelineAsset, PipelineAssetPath);
            }

            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            QualitySettings.renderPipeline = pipelineAsset;

            EditorUtility.SetDirty(rendererData);
            EditorUtility.SetDirty(pipelineAsset);
        }

        private static void EnsurePreviewMaterials()
        {
            foreach (LoopLabPresetKind preset in Enum.GetValues(typeof(LoopLabPresetKind)))
            {
                var shaderName = LoopLabPresetCatalog.GetShaderName(preset);
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    throw new InvalidOperationException("Missing shader: " + shaderName);
                }

                var materialPath = MaterialsPath + "/" + preset + "Preview.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                else
                {
                    material.shader = shader;
                }

                material.SetColor("_BaseColor", LoopLabPresetCatalog.GetBaseColor(preset));
                material.SetColor("_AccentColor", LoopLabPresetCatalog.GetAccentColor(preset));
                material.SetFloat("_GridScale", LoopLabPresetCatalog.GetGridScale(preset));
                EditorUtility.SetDirty(material);
            }
        }

        private static void EnsureScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                EnsureSceneInBuildSettings();
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.08f, 0.11f);

            var cameraObject = new GameObject("LoopLab Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.05f, 0.08f);
            camera.orthographic = true;
            camera.orthographicSize = 1f;
            camera.allowHDR = true;
            camera.allowMSAA = true;

            var lightObject = new GameObject("Preview Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.color = new Color(1f, 0.95f, 0.88f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureSceneInBuildSettings();
        }

        private static PostProcessData LoadDefaultPostProcessData()
        {
            var postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                Path.Combine(UniversalRenderPipelineAsset.packagePath, "Runtime/Data/PostProcessData.asset"));

            if (postProcessData == null)
            {
                throw new InvalidOperationException("Missing default URP post-process data asset.");
            }

            return postProcessData;
        }

        private static void EnsureSceneInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            for (var index = 0; index < scenes.Length; index++)
            {
                if (scenes[index].path != ScenePath)
                {
                    continue;
                }

                scenes[index] = new EditorBuildSettingsScene(ScenePath, true);
                EditorBuildSettings.scenes = scenes;
                return;
            }

            Array.Resize(ref scenes, scenes.Length + 1);
            scenes[^1] = new EditorBuildSettingsScene(ScenePath, true);
            EditorBuildSettings.scenes = scenes;
        }

        private static string ToAbsoluteProjectPath(string projectRelativePath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                throw new InvalidOperationException("Unable to resolve project root for LoopLab bootstrap.");
            }

            return Path.GetFullPath(Path.Combine(projectRoot.FullName, projectRelativePath));
        }
    }
}
