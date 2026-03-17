using System.IO;
using System.Reflection;
using Precondition.LoopLab.Editor.Export;
using UnityEditor;
using UnityEngine;

namespace Precondition.LoopLab.Editor
{
    public static class LoopLabWindowShowcaseBatchValidation
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            var stateKey = GetStateKey();
            var hadOriginalState = EditorPrefs.HasKey(stateKey);
            var originalState = hadOriginalState ? EditorPrefs.GetString(stateKey) : string.Empty;
            var window = ScriptableObject.CreateInstance<LoopLabWindow>();

            try
            {
                Invoke(window, "ExportShowcaseAssets");

                var statusMessage = GetField<string>(window, "statusMessage");
                if (!statusMessage.Contains("Showcase export wrote GIFs, thumbnails, and a comparison sheet to", System.StringComparison.Ordinal))
                {
                    throw new System.InvalidOperationException($"Unexpected showcase status message: {statusMessage}");
                }

                var showcaseDirectory = LoopLabShowcaseExporter.GetAbsoluteShowcaseDirectory();
                foreach (var definition in LoopLabShowcaseExporter.PresetDefinitions)
                {
                    var gifPath = LoopLabShowcaseExporter.GetGifPath(showcaseDirectory, definition);
                    var thumbnailPath = LoopLabShowcaseExporter.GetThumbnailPath(showcaseDirectory, definition);

                    if (!File.Exists(gifPath))
                    {
                        throw new FileNotFoundException($"Expected showcase GIF for {definition.Preset}.", gifPath);
                    }

                    if (!File.Exists(thumbnailPath))
                    {
                        throw new FileNotFoundException($"Expected showcase thumbnail for {definition.Preset}.", thumbnailPath);
                    }
                }

                var comparisonPath = LoopLabShowcaseExporter.GetComparisonSheetPath(showcaseDirectory);
                if (!File.Exists(comparisonPath))
                {
                    throw new FileNotFoundException("Expected showcase comparison sheet.", comparisonPath);
                }

                Debug.Log("LoopLabWindow showcase batch validation passed.");
            }
            finally
            {
                RestoreWindowState(stateKey, hadOriginalState, originalState);
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        private static string GetStateKey()
        {
            var field = typeof(LoopLabWindow).GetField("StateKey", BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
            {
                throw new System.MissingFieldException(typeof(LoopLabWindow).FullName, "StateKey");
            }

            return (string)field.GetRawConstantValue();
        }

        private static T GetField<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, InstanceFlags);
            if (field == null)
            {
                throw new System.MissingFieldException(instance.GetType().FullName, fieldName);
            }

            return (T)field.GetValue(instance);
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

        private static object Invoke(object instance, string methodName, params object[] args)
        {
            var method = instance.GetType().GetMethod(methodName, InstanceFlags);
            if (method == null)
            {
                throw new System.MissingMethodException(instance.GetType().FullName, methodName);
            }

            return method.Invoke(instance, args);
        }
    }
}
