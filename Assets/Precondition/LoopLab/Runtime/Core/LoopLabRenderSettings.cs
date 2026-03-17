using System;
using UnityEngine;

namespace Precondition.LoopLab
{
    [Serializable]
    public struct LoopLabRenderSettings
    {
        public LoopLabPresetKind Preset;
        public int FramesPerSecond;
        public float DurationSeconds;
        public int Resolution;
        public int Seed;

        public static LoopLabRenderSettings Default => new()
        {
            Preset = LoopLabPresetKind.Landscape,
            FramesPerSecond = 24,
            DurationSeconds = 3f,
            Resolution = 512,
            Seed = 1337
        };

        public int FrameCount => Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0.1f, DurationSeconds) * Mathf.Max(1, FramesPerSecond)));

        public int ClampedResolution => Mathf.Clamp(Resolution, 128, 2048);
    }
}
