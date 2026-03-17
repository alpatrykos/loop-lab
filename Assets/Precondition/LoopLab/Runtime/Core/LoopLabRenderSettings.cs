using System;
using UnityEngine;

namespace Precondition.LoopLab
{
    public enum LoopLabContrastMode
    {
        High = 0,
        Low = 1
    }

    [Serializable]
    public struct LoopLabRenderSettings
    {
        public const int DefaultSeedValue = 1337;
        public const int MaxSupportedSeedValue = 16777215;
        public const float MinimumDurationSeconds = 0.1f;
        public const int MinimumFramesPerSecond = 1;

        public LoopLabPresetKind Preset;
        public LoopLabContrastMode ContrastMode;
        public int FramesPerSecond;
        public float DurationSeconds;
        public int Resolution;
        public int Seed;

        public static LoopLabRenderSettings Default => new()
        {
            Preset = LoopLabPresetKind.Landscape,
            ContrastMode = LoopLabContrastMode.High,
            FramesPerSecond = 24,
            DurationSeconds = 3f,
            Resolution = 512,
            Seed = DefaultSeedValue
        };

        public int FrameCount => CalculateFrameCount(DurationSeconds, FramesPerSecond);

        public int ClampedResolution => Mathf.Clamp(Resolution, 128, 2048);

        public int ValidatedFramesPerSecond => ValidateFramesPerSecond(FramesPerSecond);

        public float ValidatedDurationSeconds => ValidateDurationSeconds(DurationSeconds);

        public int ValidatedSeed => ValidateSeed(Seed);

        public LoopLabRenderSettings GetValidated()
        {
            return new LoopLabRenderSettings
            {
                Preset = Preset,
                ContrastMode = ContrastMode,
                FramesPerSecond = ValidatedFramesPerSecond,
                DurationSeconds = ValidatedDurationSeconds,
                Resolution = ClampedResolution,
                Seed = ValidatedSeed
            };
        }

        public static int CalculateFrameCount(float durationSeconds, int framesPerSecond)
        {
            var safeDurationSeconds = ValidateDurationSeconds(durationSeconds);
            var safeFramesPerSecond = ValidateFramesPerSecond(framesPerSecond);
            return Mathf.Max(1, Mathf.RoundToInt(safeDurationSeconds * safeFramesPerSecond));
        }

        public static int NormalizeFrameIndex(int frameIndex, int frameCount)
        {
            var safeFrameCount = Mathf.Max(1, frameCount);
            if (safeFrameCount <= 1)
            {
                return 0;
            }

            return ((frameIndex % safeFrameCount) + safeFrameCount) % safeFrameCount;
        }

        public static int GetPreviewFrameIndex(float elapsedSeconds, float durationSeconds, int frameCount)
        {
            var safeFrameCount = Mathf.Max(1, frameCount);
            if (safeFrameCount <= 1)
            {
                return 0;
            }

            var normalizedPhase = LoopPhase.GetPhase(
                NormalizePreviewElapsedSeconds(elapsedSeconds, durationSeconds),
                ValidateDurationSeconds(durationSeconds));
            return Mathf.FloorToInt(normalizedPhase * safeFrameCount) % safeFrameCount;
        }

        public static float NormalizePreviewElapsedSeconds(float elapsedSeconds, float durationSeconds)
        {
            return Mathf.Repeat(ValidateElapsedSeconds(elapsedSeconds), ValidateDurationSeconds(durationSeconds));
        }

        public static int ValidateFramesPerSecond(int framesPerSecond)
        {
            return Mathf.Max(MinimumFramesPerSecond, framesPerSecond);
        }

        public static float ValidateDurationSeconds(float durationSeconds)
        {
            if (float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
            {
                return MinimumDurationSeconds;
            }

            return Mathf.Max(MinimumDurationSeconds, durationSeconds);
        }

        public static float ValidateElapsedSeconds(float elapsedSeconds)
        {
            if (float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds))
            {
                return 0f;
            }

            return Mathf.Max(0f, elapsedSeconds);
        }

        public static int ValidateSeed(int seed)
        {
            if (seed == 0)
            {
                return DefaultSeedValue;
            }

            if (seed > 0 && seed <= MaxSupportedSeedValue)
            {
                return seed;
            }

            unchecked
            {
                var hashedSeed = (uint)seed;
                hashedSeed ^= hashedSeed >> 16;
                hashedSeed *= 0x7feb352dU;
                hashedSeed ^= hashedSeed >> 15;
                hashedSeed *= 0x846ca68bU;
                hashedSeed ^= hashedSeed >> 16;
                return (int)(hashedSeed % MaxSupportedSeedValue) + 1;
            }
        }

        public static int RandomizeSeed(int seed)
        {
            var validatedSeed = ValidateSeed(seed);

            unchecked
            {
                var randomizedSeed = (uint)validatedSeed;
                randomizedSeed ^= 0x9e3779b9U;
                randomizedSeed *= 0x85ebca6bU;
                randomizedSeed ^= randomizedSeed >> 13;
                randomizedSeed *= 0xc2b2ae35U;
                randomizedSeed ^= randomizedSeed >> 16;

                var nextSeed = (int)(randomizedSeed % MaxSupportedSeedValue) + 1;
                if (nextSeed == validatedSeed)
                {
                    nextSeed = validatedSeed == MaxSupportedSeedValue
                        ? 1
                        : validatedSeed + 1;
                }

                return nextSeed;
            }
        }
    }
}
