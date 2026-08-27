Shader "Custom/World Triplanar Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)

        _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1

        _MetallicGlossMap("Metallic Map", 2D) = "white" {}
        _SpecGlossMap("Specular Map", 2D) = "white" {}
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.5
        _SpecColor("Specular Color", Color) = (0.2, 0.2, 0.2, 1)

        [HDR] _EmissionColor("Emission", Color) = (0, 0, 0, 1)
        _EmissionMap("Emission Map", 2D) = "white" {}

        [Header(World Mapping)]
        _WorldTiling("World Tiling (1 / meters per tile)", Float) = 0.25
        _BlendSharpness("Triplanar Blend Sharpness", Range(1, 16)) = 4

        [Toggle(_SPECULAR_SETUP)] _SpecularSetup("Specular Workflow", Float) = 0
        [Toggle(_NORMALMAP)] _NormalMapOn("Use Normal Map", Float) = 1
        [Toggle(_METALLICSPECGLOSSMAP)] _MetallicSpecMapOn("Use Metal/Spec Map", Float) = 1
        [Toggle(_EMISSION)] _EmissionOn("Use Emission", Float) = 0

        [HideInInspector] _WorkflowMode("WorkflowMode", Float) = 1
        [HideInInspector] _Cull("__cull", Float) = 2
        [HideInInspector] _SrcBlend("__src", Float) = 1
        [HideInInspector] _DstBlend("__dst", Float) = 0
        [HideInInspector] _ZWrite("__zw", Float) = 1
        [HideInInspector] _Surface("__surface", Float) = 0
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [HideInInspector] _BaseMap_TexelSize("TexelSize", Vector) = (1, 1, 0, 0)
        [HideInInspector] _BaseMap_MipInfo("MipInfo", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Lit"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile_instancing

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _SPECULAR_SETUP
            #pragma shader_feature_local_fragment _EMISSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_SpecGlossMap);       SAMPLER(sampler_SpecGlossMap);
            TEXTURE2D(_EmissionMap);        SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseMap_TexelSize;
                float4 _BaseMap_MipInfo;
                half4 _BaseColor;
                half4 _SpecColor;
                half4 _EmissionColor;
                half _BumpScale;
                half _Metallic;
                half _Smoothness;
                half _WorldTiling;
                half _BlendSharpness;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 vertexSH : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half3 TriplanarWeights(half3 normalWS)
            {
                half3 blend = pow(abs(normalWS), _BlendSharpness);
                return blend / max(dot(blend, half3(1, 1, 1)), HALF_MIN);
            }

            half4 SampleTriplanar(TEXTURE2D_PARAM(tex, samp), float3 positionWS, half3 weights)
            {
                float scale = max(_WorldTiling, 1e-4);
                half4 x = SAMPLE_TEXTURE2D(tex, samp, positionWS.zy * scale);
                half4 y = SAMPLE_TEXTURE2D(tex, samp, positionWS.xz * scale);
                half4 z = SAMPLE_TEXTURE2D(tex, samp, positionWS.xy * scale);
                return x * weights.x + y * weights.y + z * weights.z;
            }

            // Whiteout-blend triplanar normals (Ben Golus style).
            half3 SampleTriplanarNormal(float3 positionWS, half3 normalWS, half3 weights)
            {
            #if defined(_NORMALMAP)
                float scale = max(_WorldTiling, 1e-4);
                half3 nx = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, positionWS.zy * scale), _BumpScale);
                half3 ny = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, positionWS.xz * scale), _BumpScale);
                half3 nz = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, positionWS.xy * scale), _BumpScale);

                nx = half3(nx.xy + normalWS.zy, abs(normalWS.x));
                ny = half3(ny.xy + normalWS.xz, abs(normalWS.y));
                nz = half3(nz.xy + normalWS.xy, abs(normalWS.z));

                return normalize(
                    nx.zyx * weights.x +
                    ny.xzy * weights.y +
                    nz.xyz * weights.z);
            #else
                return normalize(normalWS);
            #endif
            }

            // Matches URP Lit SampleMetallicSpecGloss behavior.
            half4 SampleMetallicSpecGlossTriplanar(float3 positionWS, half3 weights, half albedoAlpha)
            {
                half4 specGloss;

            #if defined(_METALLICSPECGLOSSMAP)
              #if defined(_SPECULAR_SETUP)
                specGloss = SampleTriplanar(TEXTURE2D_ARGS(_SpecGlossMap, sampler_SpecGlossMap), positionWS, weights);
              #else
                specGloss = SampleTriplanar(TEXTURE2D_ARGS(_MetallicGlossMap, sampler_MetallicGlossMap), positionWS, weights);
              #endif
                specGloss.a *= _Smoothness;
            #else
              #if defined(_SPECULAR_SETUP)
                specGloss.rgb = _SpecColor.rgb;
              #else
                specGloss.rgb = _Metallic.rrr;
              #endif
                specGloss.a = _Smoothness;
            #endif

                return specGloss;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = nrm.normalWS;
                output.vertexSH = SampleSHVertex(nrm.normalWS);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 positionWS = input.positionWS;
                half3 geoNormalWS = NormalizeNormalPerPixel(input.normalWS);
                half3 weights = TriplanarWeights(geoNormalWS);

                half4 albedoAlpha = SampleTriplanar(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), positionWS, weights);
                half3 albedo = albedoAlpha.rgb * _BaseColor.rgb;
                half3 normalWS = SampleTriplanarNormal(positionWS, geoNormalWS, weights);

                half4 specGloss = SampleMetallicSpecGlossTriplanar(positionWS, weights, albedoAlpha.a);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.alpha = albedoAlpha.a * _BaseColor.a;
                surfaceData.smoothness = specGloss.a;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = 1;
                surfaceData.clearCoatMask = 0;
                surfaceData.clearCoatSmoothness = 1;

            #if defined(_SPECULAR_SETUP)
                // URP Lit sets metallic to 1 in specular workflow.
                surfaceData.metallic = 1;
                surfaceData.specular = specGloss.rgb;
            #else
                surfaceData.metallic = specGloss.r;
                surfaceData.specular = half3(0, 0, 0);
            #endif

            #if defined(_EMISSION)
                surfaceData.emission = SampleTriplanar(TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap), positionWS, weights).rgb * _EmissionColor.rgb;
            #else
                surfaceData.emission = _EmissionColor.rgb;
            #endif

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(positionWS, 1), input.fogFactor);
                inputData.bakedGI = SampleSHPixel(input.vertexSH, normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input);
                inputData.tangentToWorld = half3x3(half3(1, 0, 0), half3(0, 1, 0), normalWS);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseMap_TexelSize;
                float4 _BaseMap_MipInfo;
                half4 _BaseColor;
                half4 _SpecColor;
                half4 _EmissionColor;
                half _BumpScale;
                half _Metallic;
                half _Smoothness;
                half _WorldTiling;
                half _BlendSharpness;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 GetShadowPositionHClip(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = GetShadowPositionHClip(input);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseMap_TexelSize;
                float4 _BaseMap_MipInfo;
                half4 _BaseColor;
                half4 _SpecColor;
                half4 _EmissionColor;
                half _BumpScale;
                half _Metallic;
                half _Smoothness;
                half _WorldTiling;
                half _BlendSharpness;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
