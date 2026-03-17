using UnityEngine;

namespace Precondition.LoopLab
{
    public static class LoopPhase
    {
        private const float FullTurn = Mathf.PI * 2f;

        public static float GetPhase(int frameIndex, int totalFrames)
        {
            if (totalFrames <= 0)
            {
                return 0f;
            }

            return Mathf.Repeat(frameIndex / (float)totalFrames, 1f);
        }

        public static float GetPhase(float timeSeconds, float durationSeconds)
        {
            if (durationSeconds <= 0f)
            {
                return 0f;
            }

            return Mathf.Repeat(timeSeconds / durationSeconds, 1f);
        }

        public static Vector2 GetLoopVector(float phase)
        {
            var radians = phase * FullTurn;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }
    }
}
