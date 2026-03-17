using UnityEngine;

namespace Precondition.LoopLab
{
    public static class LoopPhase
    {
        private const float FullTurn = Mathf.PI * 2f;

        public static float GetPhase(int frameIndex, int totalFrames)
        {
            return GetPhaseFromFrame(frameIndex, totalFrames);
        }

        public static float GetPhase(float timeSeconds, float durationSeconds)
        {
            return GetPhaseFromTime(timeSeconds, durationSeconds);
        }

        public static float GetPhaseFromFrame(int frameIndex, int totalFrames)
        {
            if (totalFrames <= 0)
            {
                return 0f;
            }

            return Mathf.Repeat(frameIndex / (float)totalFrames, 1f);
        }

        public static float GetPhaseFromTime(float timeSeconds, float durationSeconds)
        {
            if (durationSeconds <= 0f)
            {
                return 0f;
            }

            return Mathf.Repeat(timeSeconds / durationSeconds, 1f);
        }

        public static float GetTheta(float phase)
        {
            return phase * FullTurn;
        }

        public static float GetCos(float phase)
        {
            return Mathf.Cos(GetTheta(phase));
        }

        public static float GetSin(float phase)
        {
            return Mathf.Sin(GetTheta(phase));
        }

        public static Vector2 GetLoopVector(float phase)
        {
            return new Vector2(GetCos(phase), GetSin(phase));
        }
    }
}
