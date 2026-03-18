// LoopLab shaders must derive motion from normalized loop phase and closed paths.
// Do not introduce Unity time globals or frame-accumulated state here.
#ifndef LOOPLAB_LOOPING_INCLUDED
#define LOOPLAB_LOOPING_INCLUDED

static const float LoopLabFullTurn = 6.28318530718;

float LoopLabPhase01(float phase)
{
    return frac(phase);
}

float LoopLabTheta(float phase)
{
    return LoopLabPhase01(phase) * LoopLabFullTurn;
}

float LoopLabCosWave(float phase, float rate, float offset)
{
    return cos(LoopLabTheta(phase * rate + offset));
}

float LoopLabSinWave(float phase, float rate, float offset)
{
    return sin(LoopLabTheta(phase * rate + offset));
}

float LoopLabBlendWeight(float phase, float rate, float offset)
{
    return 0.5 + 0.5 * LoopLabCosWave(phase, rate, offset);
}

float2 LoopLabLoopVectorFromPhase(float phase)
{
    float theta = LoopLabTheta(phase);
    return float2(cos(theta), sin(theta));
}

float2 LoopLabOrbitOffset(float2 loopVector, float2 majorAxis, float2 minorAxis)
{
    return majorAxis * loopVector.x + minorAxis * loopVector.y;
}

float2 LoopLabApplyOrbit(float2 position, float2 loopVector, float2 majorAxis, float2 minorAxis)
{
    return position + LoopLabOrbitOffset(loopVector, majorAxis, minorAxis);
}

#endif
