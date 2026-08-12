// Companion to VFX/PotionOutline. Stamps the tube's silhouette into the stencil
// buffer so the outline hull can punch its own interior out.
//
// Why this is needed at all: the `glass` material is Transparent with ZWrite
// OFF, so the tube writes no depth. The inflated hull therefore has nothing to
// be occluded by and its whole interior shows straight through the glass,
// turning the tube into a solid slab of colour instead of a rim.
//
// Why stencil rather than a depth prime: priming the tube's depth would also
// occlude the liquid inside it (queue 2450) and every mote orbiting behind it,
// because those are all further away than the tube's front surface. Stencil
// touches neither depth nor colour, so nothing else in the potion changes.
//
// Only bit 6 (value 64) is read/written, via explicit masks, so this cannot
// tread on the stencil bits URP uses for its own passes.
Shader "VFX/PotionOutlineMask"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            // Must land before VFX/PotionOutline (1999) so the mask exists by
            // the time the hull is tested against it.
            "Queue" = "Geometry-2"
        }

        Pass
        {
            Name "PotionOutlineMask"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back        // front faces == the tube's screen footprint
            ZWrite Off       // critical: must not disturb the liquid or motes
            ZTest LEqual
            ColorMask 0      // stencil only, never touches colour

            Stencil
            {
                Ref 64
                WriteMask 64
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
