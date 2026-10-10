#ifndef ARENA_COMMON_INCLUDED
#define ARENA_COMMON_INCLUDED

// Shared procedural building blocks for the Consensus Arena shaders.
// Everything here is texture-free, so nothing can be stripped from a build.

float ArenaHash21(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float ArenaHash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float3 ArenaHash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

// Smooth value noise, 0..1
float ArenaNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float n000 = ArenaHash31(i);
    float n100 = ArenaHash31(i + float3(1.0, 0.0, 0.0));
    float n010 = ArenaHash31(i + float3(0.0, 1.0, 0.0));
    float n110 = ArenaHash31(i + float3(1.0, 1.0, 0.0));
    float n001 = ArenaHash31(i + float3(0.0, 0.0, 1.0));
    float n101 = ArenaHash31(i + float3(1.0, 0.0, 1.0));
    float n011 = ArenaHash31(i + float3(0.0, 1.0, 1.0));
    float n111 = ArenaHash31(i + float3(1.0, 1.0, 1.0));

    float nx00 = lerp(n000, n100, f.x);
    float nx10 = lerp(n010, n110, f.x);
    float nx01 = lerp(n001, n101, f.x);
    float nx11 = lerp(n011, n111, f.x);
    return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
}

// Four-octave fractal noise, roughly 0..0.94
float ArenaFbm3(float3 p)
{
    float sum = 0.0;
    float amp = 0.5;
    for (int k = 0; k < 4; k++)
    {
        sum += amp * ArenaNoise3(p);
        p = p * 2.02 + 17.3;
        amp *= 0.5;
    }
    return sum;
}

// Hexagonal tiling. x = distance to the nearest cell edge (0 at the edge, 0.5 at the centre),
// y = a stable random value per cell.
float2 ArenaHex(float2 p)
{
    float2 s = float2(1.0, 1.7320508);
    float2 ca = floor(p / s) + 0.5;
    float2 cb = floor((p - float2(0.5, 1.0)) / s) + 0.5;
    float2 ha = p - ca * s;
    float2 hb = p - (cb + 0.5) * s;

    float2 local = ha;
    float2 id = ca;
    if (dot(hb, hb) < dot(ha, ha))
    {
        local = hb;
        id = cb + 0.5;
    }

    float2 q = abs(local);
    float edge = 0.5 - max(q.x, q.x * 0.5 + q.y * 0.8660254);
    return float2(edge, ArenaHash21(id));
}

#endif
