using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Precondition.LoopLab.Editor.Export
{
    public static class LoopLabBatchGraphicsGuard
    {
        public const string SupportedVisualValidationExecuteMethod =
            "Precondition.LoopLab.Editor.Export.LoopLabShowcaseExporterBatchValidation.Run";

        public const string GuidanceFileName = "VISUAL-VALIDATION-README.md";

        public static void EnsureGraphicsBackedOutput(string operationLabel)
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                return;
            }

            throw new InvalidOperationException(BuildExceptionMessage(operationLabel));
        }

        public static void EnsureGraphicsBackedOutputWithGuidance(
            string operationLabel,
            string guidanceDirectory,
            params string[] staleArtifactPatterns)
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                return;
            }

            Directory.CreateDirectory(guidanceDirectory);

            foreach (var artifactPattern in staleArtifactPatterns)
            {
                DeleteMatchingFiles(guidanceDirectory, artifactPattern);
            }

            var guidancePath = Path.Combine(guidanceDirectory, GuidanceFileName);
            File.WriteAllText(guidancePath, BuildGuidanceMarkdown(operationLabel));

            throw new InvalidOperationException(
                $"{BuildExceptionMessage(operationLabel)} Guidance written to {guidancePath}.");
        }

        public static string BuildSupportedVisualValidationCommand()
        {
            return
                $"-batchmode -projectPath \"$PWD\" -executeMethod {SupportedVisualValidationExecuteMethod} " +
                "-quit -logFile \"$PWD/log/looplab-visual-validation.log\"";
        }

        private static string BuildExceptionMessage(string operationLabel)
        {
            return
                $"{operationLabel} cannot produce meaningful visual output under -nographics because Unity is using the Null graphics device. " +
                $"Run Unity batchmode without -nographics and use {SupportedVisualValidationExecuteMethod} for unattended visual validation.";
        }

        private static string BuildGuidanceMarkdown(string operationLabel)
        {
            return
$@"# LoopLab Batch Visual Validation

`{operationLabel}` cannot produce meaningful capture files under `-nographics` because Unity is using the Null graphics device.

Do not treat `-nographics` output as visual QA evidence. Use it for compile/open checks only.

Supported unattended visual validation command:

```bash
""/Applications/Unity/Hub/Editor/6000.3.7f1/Unity.app/Contents/MacOS/Unity"" \
  {BuildSupportedVisualValidationCommand()}
```

Compile/open-only command:

```bash
""/Applications/Unity/Hub/Editor/6000.3.7f1/Unity.app/Contents/MacOS/Unity"" \
  -batchmode -nographics -projectPath ""$PWD"" -quit \
  -logFile ""$PWD/log/unity-batchmode.log""
```

This graphics-backed validation run exports and verifies showcase GIFs, thumbnails, and a comparison sheet under `Assets/Precondition/LoopLab/Exports/Showcase`.
";
        }

        private static void DeleteMatchingFiles(string directory, string searchPattern)
        {
            if (string.IsNullOrWhiteSpace(searchPattern))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly))
            {
                File.Delete(file);
            }
        }
    }
}
