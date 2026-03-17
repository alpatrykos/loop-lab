using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Precondition.LoopLab.Editor.Export
{
    internal readonly struct LoopLabMp4StreamInfo
    {
        public LoopLabMp4StreamInfo(string codecName, string pixelFormat, int width, int height, int frameCount, string averageFrameRate)
        {
            CodecName = codecName;
            PixelFormat = pixelFormat;
            Width = width;
            Height = height;
            FrameCount = frameCount;
            AverageFrameRate = averageFrameRate;
        }

        public string CodecName { get; }
        public string PixelFormat { get; }
        public int Width { get; }
        public int Height { get; }
        public int FrameCount { get; }
        public string AverageFrameRate { get; }
    }

    internal static class LoopLabFfmpegUtility
    {
        private static readonly string[] SearchRoots =
        {
            "/opt/homebrew/bin",
            "/usr/local/bin",
            "/usr/bin",
            @"C:\ffmpeg\bin",
            @"C:\Program Files\ffmpeg\bin"
        };

        public static string FfmpegSearchDescription =>
            "LOOPLAB_FFMPEG_PATH, FFMPEG_PATH, PATH, /opt/homebrew/bin, /usr/local/bin, /usr/bin, C:\\ffmpeg\\bin, C:\\Program Files\\ffmpeg\\bin";

        public static bool TryResolveFfmpegExecutable(out string executablePath)
        {
            return TryResolveExecutable("ffmpeg", new[] { "LOOPLAB_FFMPEG_PATH", "FFMPEG_PATH" }, Array.Empty<string>(), out executablePath);
        }

        public static bool TryResolveFfprobeExecutable(string ffmpegPath, out string executablePath)
        {
            var siblingCandidates = string.IsNullOrEmpty(ffmpegPath)
                ? Array.Empty<string>()
                : GetExecutableNames("ffprobe")
                    .Select(fileName => Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? string.Empty, fileName))
                    .ToArray();

            return TryResolveExecutable("ffprobe", new[] { "LOOPLAB_FFPROBE_PATH", "FFPROBE_PATH" }, siblingCandidates, out executablePath);
        }

        public static void EncodePngSequenceToMp4(
            string ffmpegPath,
            string inputPattern,
            int frameCount,
            int framesPerSecond,
            string outputPath)
        {
            var result = RunProcess(
                ffmpegPath,
                new[]
                {
                    "-hide_banner",
                    "-loglevel", "error",
                    "-y",
                    "-framerate", framesPerSecond.ToString(CultureInfo.InvariantCulture),
                    "-start_number", "0",
                    "-i", inputPattern,
                    "-frames:v", frameCount.ToString(CultureInfo.InvariantCulture),
                    "-c:v", "libx264",
                    "-crf", "18",
                    "-preset", "medium",
                    "-movflags", "+faststart",
                    "-pix_fmt", "yuv420p",
                    "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2:color=black,format=yuv420p",
                    outputPath
                },
                Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory());

            if (result.ExitCode == 0)
            {
                return;
            }

            throw new InvalidOperationException("ffmpeg failed to encode the MP4. " + result.GetErrorSummary());
        }

        public static LoopLabMp4StreamInfo ProbeVideoStream(string ffmpegPath, string videoPath)
        {
            if (!TryResolveFfprobeExecutable(ffmpegPath, out var ffprobePath))
            {
                throw new InvalidOperationException(
                    "Unable to validate the MP4 output because ffprobe was not found next to ffmpeg or on PATH.");
            }

            var result = RunProcess(
                ffprobePath,
                new[]
                {
                    "-v", "error",
                    "-count_frames",
                    "-select_streams", "v:0",
                    "-show_entries", "stream=codec_name,pix_fmt,width,height,avg_frame_rate,nb_frames,nb_read_frames",
                    "-of", "default=noprint_wrappers=1:nokey=0",
                    videoPath
                },
                Path.GetDirectoryName(videoPath) ?? Directory.GetCurrentDirectory());

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException("ffprobe failed to inspect the MP4 output. " + result.GetErrorSummary());
            }

            var values = ParseKeyValueLines(result.StandardOutput);
            values.TryGetValue("codec_name", out var codecName);
            values.TryGetValue("pix_fmt", out var pixelFormat);
            values.TryGetValue("avg_frame_rate", out var averageFrameRate);

            return new LoopLabMp4StreamInfo(
                codecName ?? string.Empty,
                pixelFormat ?? string.Empty,
                ParseInt(values, "width"),
                ParseInt(values, "height"),
                ParseFrameCount(values),
                averageFrameRate ?? string.Empty);
        }

        public static void ExtractFrameAtTime(string ffmpegPath, string videoPath, double timeSeconds, string outputPath)
        {
            var result = RunProcess(
                ffmpegPath,
                new[]
                {
                    "-hide_banner",
                    "-loglevel", "error",
                    "-y",
                    "-i", videoPath,
                    "-ss", timeSeconds.ToString("0.######", CultureInfo.InvariantCulture),
                    "-vframes", "1",
                    outputPath
                },
                Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory());

            if (result.ExitCode == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                $"ffmpeg failed to extract a frame at {timeSeconds:0.######} seconds. {result.GetErrorSummary()}");
        }

        private static bool TryResolveExecutable(
            string toolName,
            IReadOnlyCollection<string> environmentVariableNames,
            IReadOnlyCollection<string> preferredCandidates,
            out string executablePath)
        {
            var candidates = new List<string>();

            AddCandidates(candidates, preferredCandidates);

            foreach (var environmentVariableName in environmentVariableNames)
            {
                var environmentValue = Environment.GetEnvironmentVariable(environmentVariableName);
                AddCandidates(candidates, ExpandCandidatePath(environmentValue, toolName));
            }

            var pathValue = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathValue))
            {
                foreach (var entry in pathValue.Split(Path.PathSeparator))
                {
                    AddCandidates(candidates, ExpandCandidatePath(entry, toolName));
                }
            }

            foreach (var root in SearchRoots)
            {
                AddCandidates(candidates, ExpandCandidatePath(root, toolName));
            }

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var normalizedCandidate = Path.GetFullPath(candidate);
                if (!File.Exists(normalizedCandidate))
                {
                    continue;
                }

                executablePath = normalizedCandidate;
                return true;
            }

            executablePath = string.Empty;
            return false;
        }

        private static Dictionary<string, string> ParseKeyValueLines(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0 || separatorIndex >= line.Length - 1)
                {
                    continue;
                }

                var key = line.Substring(0, separatorIndex);
                var value = line.Substring(separatorIndex + 1);
                values[key] = value;
            }

            return values;
        }

        private static int ParseInt(IReadOnlyDictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out var rawValue))
            {
                return 0;
            }

            return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue)
                ? parsedValue
                : 0;
        }

        private static int ParseFrameCount(IReadOnlyDictionary<string, string> values)
        {
            var countedFrames = ParseInt(values, "nb_read_frames");
            if (countedFrames > 0)
            {
                return countedFrames;
            }

            return ParseInt(values, "nb_frames");
        }

        private static IEnumerable<string> ExpandCandidatePath(string rawPath, string toolName)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return Array.Empty<string>();
            }

            rawPath = rawPath.Trim().Trim('"');

            if (File.Exists(rawPath))
            {
                return new[] { rawPath };
            }

            if (!Directory.Exists(rawPath))
            {
                return Array.Empty<string>();
            }

            return GetExecutableNames(toolName).Select(fileName => Path.Combine(rawPath, fileName));
        }

        private static string[] GetExecutableNames(string toolName)
        {
            return Application.platform == RuntimePlatform.WindowsEditor
                ? new[] { toolName + ".exe", toolName }
                : new[] { toolName };
        }

        private static void AddCandidates(ICollection<string> candidates, IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    candidates.Add(value);
                }
            }
        }

        private static LoopLabProcessResult RunProcess(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = BuildArguments(arguments),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                process.Start();
            }
            catch (Exception exception)
            {
                return new LoopLabProcessResult(-1, string.Empty, exception.Message);
            }

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new LoopLabProcessResult(process.ExitCode, standardOutput, standardError);
        }

        private static string BuildArguments(IReadOnlyList<string> arguments)
        {
            var builder = new StringBuilder();

            for (var index = 0; index < arguments.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(QuoteArgument(arguments[index]));
            }

            return builder.ToString();
        }

        private static string QuoteArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
            {
                return "\"\"";
            }

            if (argument.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"' }) < 0)
            {
                return argument;
            }

            var builder = new StringBuilder();
            builder.Append('"');

            var backslashCount = 0;
            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashCount * 2 + 1);
                    builder.Append(character);
                    backslashCount = 0;
                    continue;
                }

                builder.Append('\\', backslashCount);
                builder.Append(character);
                backslashCount = 0;
            }

            builder.Append('\\', backslashCount * 2);
            builder.Append('"');
            return builder.ToString();
        }

        private readonly struct LoopLabProcessResult
        {
            public LoopLabProcessResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput ?? string.Empty;
                StandardError = standardError ?? string.Empty;
            }

            public int ExitCode { get; }
            public string StandardOutput { get; }
            public string StandardError { get; }

            public string GetErrorSummary()
            {
                if (!string.IsNullOrWhiteSpace(StandardError))
                {
                    return StandardError.Trim();
                }

                if (!string.IsNullOrWhiteSpace(StandardOutput))
                {
                    return StandardOutput.Trim();
                }

                return $"Process exited with code {ExitCode}.";
            }
        }
    }
}
