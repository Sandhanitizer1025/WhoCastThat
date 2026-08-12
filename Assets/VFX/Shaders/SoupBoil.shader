// Boiling soup for the cauldron (the draw pile).
//
// This runs on submesh 1 of Assets/VFX/Meshes/pot_soup_split.mesh - the soup
// surface separated out of the Tripo cauldron mesh so it can be shaded and
// animated independently of the pot body. The source pot.fbx is untouched.
//
// The model already has bubble domes sculpted into it, so this does NOT try to
// fake bubbles with a texture. It heaves the real geometry up and down and
// drives colour and emission from the same wave, so the sculpted bubbles read
// as rising and sinking rather than sitting there frozen.
//
// Brightness note: the project has HDR OFF on URP-Performant and no Bloom
// volume, so anything above 1.0 simply clips to flat white. Every colour path
// here is deliberately kept at or below 1.0 - this is meant to look like warm
// simmering brew lighting the inside of the pot, not a light source you cannot
// look at. _EmissionStrength is the dial if it needs toning further.
Shader "VFX/SoupBoil"
{
    Properties
    {
        [Header(Colour)]
        _DeepColour   ("Deep Colour",   Color) = (0.05, 0.22, 0.09, 1)
        _MidColour    ("Mid Colour",    Color) = (0.24, 0.62, 0.20, 1)
        _HotColour    ("Hot Colour",    Color) = (0.62, 0.92, 0.36, 1)

        [Header(Boil)]
        _BoilSpeed    ("Boil Speed", Range(0, 4)) = 0.85
        _BoilHeight   ("Boil Height (metres)", Range(0, 0.02)) = 0.0035
        _ChurnScale   ("Churn Scale", Range(1, 120)) = 34
        _ChurnContrast("Churn Contrast", Range(0.5, 4)) = 1.6

        [Header(Glow)]
        _EmissionStrength ("Emission Strength", Range(0, 1.5)) = 0.38
        _HotThreshold ("Hot Spot Threshold", Range(0, 1)) = 0.55
        _PulseDepth   ("Glow Pulse Depth", Range(0, 0.5)) = 0.12
        _PulseSpeed   ("Glow Pulse Speed", Range(0, 3)) = 0.7

        [Header(Shading)]
        _DiffuseStrength ("Diffuse Strength", Range(0, 2)) = 0.75
        _RimStrength  ("Rim Strength", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "SoupForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColour;
                half4 _MidColour;
                half4 _HotColour;
                float _BoilSpeed;
                float _BoilHeight;
                float _ChurnScale;
                float _ChurnContrast;
                float _EmissionStrength;
                float _HotThreshold;
                float _PulseDepth;
                float _PulseSpeed;
                float _DiffuseStrength;
                float _RimStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  churn      : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Cheap 2D value noise. The soup surface is essentially horizontal,
            // so sampling in world XZ is both correct and half the cost of a
            // 3D lattice - 4 corners per octave instead of 8.
            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);      // smoothstep for C1 continuity
                float a = hash21(i + float2(0, 0));
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Two octaves scrolling in opposing directions. Opposing drift is
            // what stops it reading as a texture sliding across the surface -
            // it has to look like convection, not a conveyor belt.
            float churnAt(float2 worldXZ, float t)
            {
                float n = 0.0;
                n += vnoise(worldXZ * _ChurnScale + float2( t * 0.35,  t * 0.22)) * 0.62;
                n += vnoise(worldXZ * _ChurnScale * 2.13 - float2( t * 0.27, -t * 0.41)) * 0.38;
                return saturate(pow(abs(n), _ChurnContrast));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);

                float t = _Time.y * _BoilSpeed;
                float churn = churnAt(positionWS.xz, t);

                // Heave along world up, not object up: the cauldron is rotated
                // -90 on X, so object space Y is not vertical here.
                // Centred on 0 so the surface swells and sinks around its rest
                // height rather than drifting upward out of the pot.
                positionWS.y += (churn - 0.5) * 2.0 * _BoilHeight;

                OUT.positionWS = positionWS;
                OUT.normalWS   = normalWS;
                OUT.churn      = churn;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // Re-evaluate at pixel rate. The vertex churn drives the shape;
                // this drives the detail between vertices, which matters because
                // the sculpted bubbles are much larger than the churn cells.
                float t = _Time.y * _BoilSpeed;
                float churn = churnAt(IN.positionWS.xz, t);
                churn = lerp(churn, IN.churn, 0.35);

                // Deep -> mid across the churn, then hot only in the top band so
                // the bright areas stay sparse and the soup keeps a dark base.
                half3 albedo = lerp(_DeepColour.rgb, _MidColour.rgb, churn);
                float hot = smoothstep(_HotThreshold, 1.0, churn);
                albedo = lerp(albedo, _HotColour.rgb, hot * 0.7);

                // Simple main-light lambert plus SH ambient. The sculpted bubble
                // domes need real shading or they flatten out completely.
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 lit = albedo * (ambient + mainLight.color * ndotl * _DiffuseStrength);

                // Slow breathing on the glow so the pot feels alive when nobody
                // is interacting with it. Kept shallow - this sits in peripheral
                // vision for the whole match.
                float pulse = 1.0 - _PulseDepth * (0.5 - 0.5 * cos(_Time.y * _PulseSpeed));

                // Emission rides the hot band only, so the glow comes from the
                // churn rather than washing the whole surface out.
                half3 emission = _HotColour.rgb * (hot * _EmissionStrength * pulse);
                emission += _MidColour.rgb * (churn * _EmissionStrength * 0.25 * pulse);

                // Grazing-angle lift reads as the wet meniscus against the pot wall.
                float rim = pow(1.0 - saturate(dot(normalWS, viewDirWS)), 3.0);
                emission += _HotColour.rgb * rim * _RimStrength * pulse;

                half3 col = lit + emission;

                // Hard clamp. HDR is off in this project, so anything over 1
                // would clip to flat white and destroy the churn detail - and
                // this is 40cm from the player's face in a headset.
                col = min(col, 1.0);

                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // Lets the cauldron and soup receive each other's shadows correctly.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct SAttributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct SVaryings   { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            SVaryings shadowVert(SAttributes IN)
            {
                SVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 shadowFrag(SVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    Fallback Off
}
