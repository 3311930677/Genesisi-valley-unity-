#ifndef GV_WIND_INCLUDED
#define GV_WIND_INCLUDED

// Global wind, written every frame by GenesisValley.Presentation.WindDriver:
//   _GV_WindDir    xz = normalised horizontal direction
//   _GV_WindParams x = strength (0..1.5), y = speed, z = gust strength, w = time
float4 _GV_WindDir;
float4 _GV_WindParams;

// Bends a vertex in world space. heightOS = object-space height above the pivot (metres before scale),
// windHeight = height at which the bend saturates, flutter = small per-vertex leaf motion.
float3 GV_ApplyWind(float3 positionWS, float heightOS, float windHeight, float strength, float flutter)
{
    float t = _GV_WindParams.w * max(_GV_WindParams.y, 0.01);
    float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    float phase = dot(origin.xz, float2(0.071, 0.053));
    float h = saturate(heightOS / max(windHeight, 0.01));
    float bend = h * h;
    float gust = (sin(t * 0.37 + phase * 0.31) * 0.5 + 0.5) * _GV_WindParams.z;
    float sway = sin(t + phase) * 0.65 + sin(t * 2.27 + phase * 1.7) * 0.22;
    float amount = (sway * 0.55 + gust) * bend * strength * _GV_WindParams.x;
    float2 dir = _GV_WindDir.xz;
    positionWS.xz += dir * amount * 0.35;
    positionWS.y -= abs(amount) * 0.04 * h;
    float f = sin(t * 6.3 + dot(positionWS, float3(1.7, 2.3, 1.3))) * flutter * 0.04 * h * (0.5 + _GV_WindParams.x);
    positionWS += float3(f, f * 0.6, -f);
    return positionWS;
}
#endif
