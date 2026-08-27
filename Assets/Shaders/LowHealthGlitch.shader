Shader "Hidden/LowHealthGlitch"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "LowHealthGlitch"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _TimeSeed;
            float _Burst;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float intensity = saturate(_Intensity);
                float burst = saturate(_Burst);
                float strength = intensity * (0.55 + burst * 1.45);

                if (strength < 0.001)
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float t = _TimeSeed;
                float lineNoise = Hash21(float2(floor(uv.y * lerp(18.0, 90.0, strength)), floor(t * 18.0)));
                float sliceChance = step(1.0 - strength * 0.85, lineNoise);
                float sliceOffset = (Hash21(float2(lineNoise, t)) - 0.5) * 0.18 * strength * sliceChance;

                float blockY = floor(uv.y * lerp(8.0, 40.0, strength));
                float blockNoise = Hash21(float2(blockY, floor(t * 7.0)));
                float blockShift = (blockNoise - 0.5) * 0.08 * strength * step(0.72 - strength * 0.35, blockNoise);

                float2 glitchUV = uv;
                glitchUV.x += sliceOffset + blockShift;

                float rgbSplit = (0.004 + 0.02 * strength + 0.03 * burst);
                float3 color;
                color.r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, glitchUV + float2(rgbSplit, 0)).r;
                color.g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, glitchUV).g;
                color.b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, glitchUV - float2(rgbSplit, 0)).b;

                float scan = sin((uv.y + t * 0.35) * 3.14159 * 220.0) * 0.04 * strength;
                color += scan;

                float staticNoise = (Hash21(uv * float2(1200.0, 800.0) + t) - 0.5) * 0.22 * strength;
                color += staticNoise;

                // Occasional green/cyan virus tint on hard glitches.
                float tintPulse = step(0.92 - burst * 0.2, Hash21(float2(floor(t * 12.0), 7.7)));
                color = lerp(color, color * float3(0.55, 1.25, 0.85), tintPulse * strength * 0.45);

                float4 baseColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                return float4(lerp(baseColor.rgb, color, saturate(strength * 1.35)), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
