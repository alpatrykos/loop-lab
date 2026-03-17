using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Precondition.LoopLab.Editor.Export
{
    internal sealed class LoopLabExportRequest
    {
        public LoopLabExportRequest(string formatLabel, string fileExtension, LoopLabRenderSettings settings, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(formatLabel))
            {
                throw new ArgumentException("Format label is required.", nameof(formatLabel));
            }

            if (string.IsNullOrWhiteSpace(fileExtension))
            {
                throw new ArgumentException("File extension is required.", nameof(fileExtension));
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
            }

            FormatLabel = formatLabel.Trim();
            FileExtension = fileExtension.StartsWith(".", StringComparison.Ordinal)
                ? fileExtension.ToLowerInvariant()
                : "." + fileExtension.ToLowerInvariant();
            Settings = settings.GetValidated();
            OutputDirectory = Path.GetFullPath(outputDirectory);
            OutputFileName = CreateOutputFileName(Settings, FileExtension);
        }

        public string FormatLabel { get; }

        public string FileExtension { get; }

        public LoopLabRenderSettings Settings { get; }

        public string OutputDirectory { get; }

        public string OutputFileName { get; }

        public string SettingsSummary =>
            $"Requested settings: seed {Settings.Seed}, {Settings.FrameCount} frames @ {Settings.FramesPerSecond} FPS.";

        private static string CreateOutputFileName(LoopLabRenderSettings settings, string fileExtension)
        {
            var preset = SanitizeSegment(settings.Preset.ToString());
            var contrast = SanitizeSegment(settings.ContrastMode.ToString());
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
            return $"looplab-{preset}-{contrast}-seed{settings.Seed}-{settings.FrameCount}f-{settings.FramesPerSecond}fps-{settings.ClampedResolution}px-{timestamp}{fileExtension}";
        }

        private static string SanitizeSegment(string value)
        {
            Span<char> buffer = stackalloc char[value.Length];
            var writeIndex = 0;
            var previousWasSeparator = false;

            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    buffer[writeIndex++] = char.ToLowerInvariant(character);
                    previousWasSeparator = false;
                    continue;
                }

                if (previousWasSeparator)
                {
                    continue;
                }

                buffer[writeIndex++] = '-';
                previousWasSeparator = true;
            }

            var sanitized = new string(buffer[..writeIndex]).Trim('-');
            return string.IsNullOrEmpty(sanitized) ? "looplab" : sanitized;
        }
    }

    internal sealed class LoopLabExportWorkspace
    {
        public LoopLabExportWorkspace(LoopLabExportRequest request, string temporaryDirectoryPath)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            TemporaryDirectoryPath = temporaryDirectoryPath ?? throw new ArgumentNullException(nameof(temporaryDirectoryPath));
            FramesDirectoryPath = Path.Combine(TemporaryDirectoryPath, "frames");
            StagedOutputPath = Path.Combine(TemporaryDirectoryPath, "staged" + Request.FileExtension);
            FinalOutputPath = Path.Combine(Request.OutputDirectory, Request.OutputFileName);
        }

        public LoopLabExportRequest Request { get; }

        public string TemporaryDirectoryPath { get; }

        public string FramesDirectoryPath { get; }

        public string StagedOutputPath { get; }

        public string FinalOutputPath { get; }

        public string GetFramePath(int frameIndex)
        {
            return Path.Combine(FramesDirectoryPath, $"frame-{frameIndex:D4}.png");
        }
    }

    internal static class LoopLabExportSession
    {
        public const string TemporaryDirectoryPrefix = ".looplab-temp-";
        private static readonly TimeSpan StaleWorkspaceRetention = TimeSpan.FromDays(1);

        public static string Run(LoopLabExportRequest request, Action<LoopLabExportWorkspace> exportAction)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (exportAction == null)
            {
                throw new ArgumentNullException(nameof(exportAction));
            }

            Directory.CreateDirectory(request.OutputDirectory);
            CleanupStaleWorkspaces(request.OutputDirectory, DateTime.UtcNow);

            var temporaryDirectoryPath = Path.Combine(
                request.OutputDirectory,
                $"{TemporaryDirectoryPrefix}{SanitizeDirectorySegment(request.FormatLabel)}-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
            var workspace = new LoopLabExportWorkspace(request, temporaryDirectoryPath);

            Directory.CreateDirectory(workspace.TemporaryDirectoryPath);
            Directory.CreateDirectory(workspace.FramesDirectoryPath);
            Debug.Log($"[LoopLabExport] Created temp workspace {workspace.TemporaryDirectoryPath}.");

            try
            {
                exportAction(workspace);
                PublishOutput(workspace);
                CleanupWorkspace(workspace, "success");
                return workspace.FinalOutputPath;
            }
            catch (OperationCanceledException)
            {
                CleanupWorkspace(workspace, "cancel");
                throw;
            }
            catch
            {
                CleanupWorkspace(workspace, "failure");
                throw;
            }
        }

        public static bool HasTemporaryWorkspaces(string outputDirectory)
        {
            if (!Directory.Exists(outputDirectory))
            {
                return false;
            }

            foreach (var directoryPath in Directory.EnumerateDirectories(outputDirectory, TemporaryDirectoryPrefix + "*"))
            {
                if (Directory.Exists(directoryPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CleanupStaleWorkspaces(string outputDirectory, DateTime utcNow)
        {
            if (!Directory.Exists(outputDirectory))
            {
                return;
            }

            foreach (var directoryPath in Directory.EnumerateDirectories(outputDirectory, TemporaryDirectoryPrefix + "*"))
            {
                var age = utcNow - Directory.GetLastWriteTimeUtc(directoryPath);
                if (age < StaleWorkspaceRetention)
                {
                    continue;
                }

                TryDeleteDirectory(directoryPath, $"stale temp workspace cleanup (age {age.TotalHours:0.0}h)");
            }
        }

        private static void PublishOutput(LoopLabExportWorkspace workspace)
        {
            if (!File.Exists(workspace.StagedOutputPath))
            {
                throw new InvalidOperationException(
                    $"Export completed without writing a staged output file. Expected {workspace.StagedOutputPath}.");
            }

            File.Move(workspace.StagedOutputPath, workspace.FinalOutputPath);
            Debug.Log($"[LoopLabExport] Published output to {workspace.FinalOutputPath}.");
        }

        private static void CleanupWorkspace(LoopLabExportWorkspace workspace, string outcomeLabel)
        {
            TryDeleteFile(workspace.StagedOutputPath, $"staged output cleanup after {outcomeLabel}");
            TryDeleteDirectory(workspace.TemporaryDirectoryPath, $"temp workspace cleanup after {outcomeLabel}");
        }

        private static string SanitizeDirectorySegment(string value)
        {
            return value.Replace(' ', '-').ToLowerInvariant();
        }

        private static void TryDeleteFile(string path, string reason)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                File.Delete(path);
                Debug.Log($"[LoopLabExport] Deleted {reason}: {path}.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LoopLabExport] Failed {reason} for {path}: {exception.Message}");
            }
        }

        private static void TryDeleteDirectory(string path, string reason)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                Directory.Delete(path, true);
                Debug.Log($"[LoopLabExport] Completed {reason}: {path}.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LoopLabExport] Failed {reason} for {path}: {exception.Message}");
            }
        }
    }

    internal static class LoopLabExportFrameWriter
    {
        public static void WritePngFrames(LoopLabRenderSettings settings, LoopLabExportWorkspace workspace)
        {
            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            var validatedSettings = settings.GetValidated();
            using var renderer = new LoopRenderer();

            for (var frameIndex = 0; frameIndex < validatedSettings.FrameCount; frameIndex++)
            {
                Texture2D capturedFrame = null;

                try
                {
                    capturedFrame = CaptureTexture(renderer.Render(validatedSettings, frameIndex), validatedSettings.ClampedResolution);
                    File.WriteAllBytes(workspace.GetFramePath(frameIndex), capturedFrame.EncodeToPNG());
                }
                finally
                {
                    if (capturedFrame != null)
                    {
                        Object.DestroyImmediate(capturedFrame);
                    }
                }
            }

            Debug.Log(
                $"[LoopLabExport] Wrote {validatedSettings.FrameCount} PNG frame(s) to {workspace.FramesDirectoryPath} for {workspace.Request.FormatLabel} export.");
        }

        private static Texture2D CaptureTexture(Texture source, int resolution)
        {
            if (source == null)
            {
                throw new InvalidOperationException("Loop renderer returned a null texture.");
            }

            var renderTexture = source as RenderTexture;
            var createdTemporary = false;

            if (renderTexture == null)
            {
                renderTexture = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, renderTexture);
                createdTemporary = true;
            }

            var previousActive = RenderTexture.active;
            var capture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);

            try
            {
                RenderTexture.active = renderTexture;
                capture.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0);
                capture.Apply();
                return capture;
            }
            finally
            {
                RenderTexture.active = previousActive;

                if (createdTemporary)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }
            }
        }
    }

    internal static class LoopLabFfmpegLocator
    {
        public const string OverridePathEnvironmentVariable = "LOOPLAB_FFMPEG_PATH";

        private static readonly string[] CommonExecutablePaths =
        {
            "/opt/homebrew/bin/ffmpeg",
            "/usr/local/bin/ffmpeg",
            "/opt/local/bin/ffmpeg",
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe"
        };

        public static bool TryResolveExecutable(out string executablePath, out string resolutionSource)
        {
            var overridePath = Environment.GetEnvironmentVariable(OverridePathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                executablePath = overridePath;
                resolutionSource = OverridePathEnvironmentVariable;
                return CanExecute(executablePath);
            }

            executablePath = "ffmpeg";
            resolutionSource = "PATH";
            if (CanExecute(executablePath))
            {
                return true;
            }

            foreach (var commonExecutablePath in CommonExecutablePaths)
            {
                if (!CanExecute(commonExecutablePath))
                {
                    continue;
                }

                executablePath = commonExecutablePath;
                resolutionSource = "common install path";
                return true;
            }

            executablePath = string.Empty;
            resolutionSource = $"PATH or {OverridePathEnvironmentVariable}";
            return false;
        }

        public static string GetMissingExecutableMessage(string formatLabel, LoopLabRenderSettings settings, string resolutionSource)
        {
            var validatedSettings = settings.GetValidated();
            return
                $"{formatLabel} export requires ffmpeg, but it was not found via {resolutionSource}. " +
                $"Install ffmpeg or set {OverridePathEnvironmentVariable} to the executable path. " +
                $"No output file was written. Requested settings: seed {validatedSettings.Seed}, {validatedSettings.FrameCount} frames @ {validatedSettings.FramesPerSecond} FPS.";
        }

        private static bool CanExecute(string executablePath)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                if (!process.Start())
                {
                    return false;
                }

                if (!process.WaitForExit(2000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // Best effort: timed out probe already counts as unavailable.
                    }

                    return false;
                }

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
