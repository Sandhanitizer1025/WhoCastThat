// Inverted-hull outline for the potion tubes.
//
// Why geometry and not a post-process edge detect: this ships to a headset. A
// screen-space outline needs a full-screen pass PER EYE plus a depth/normals
// prepass, which is exactly the kind of bandwidth cost standalone GPUs cannot
// absorb. This draws the same mesh a second time with the faces flipped, which
// is ~1000 extra triangles per tube and nothing else. It is also genuinely 3D,
// so it survives stereo - a flat screen-space rim would betray itself the
// moment each eye sees it from a slightly different angle.
//
// The hull is expanded along the vertex normal, so the source mesh MUST have
// smoothed (averaged) normals or the outline splits open at every hard edge.
// testtube_v2.fbx does not - see PotionOutlineHullBaker, which bakes a smoothed
// copy into Assets/VFX/Meshes/ rather than modifying the shared FBX.
//
// Colour is expected to arrive via MaterialPropertyBlock from PotionAura so all
// nine tubes share one material. Never set it with .material or .sharedMaterial.
Shader "VFX/PotionOutline"
{
    Properties
    {
        [HDR] _OutlineColour ("Outline Colour", Color) = (1, 1, 1, 1)

        // In METRES, expanded in world space. The tube mesh is authored at a
        // freakish scale (3.9 object-space units tall, then multiplied by a
        // ~9.45 lossy scale to land at 3.8cm), so an object-space width would
        // be an untunable 0.00006-ish number and would also change meaning the
        // moment anyone rescales a tube. World space keeps this honest: 0.0006
        // is 0.6mm of actual outline, on a tube that is 1cm across.
        _OutlineWidth ("Outline Width (metres)", Float) = 0.0006

        // Stops the outline vanishing to sub-pixel across a VR table. 0 disables
        // the compensation entirely and gives a pure world-space outline.
        _DistanceCompensation ("Distance Compensation", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            // Geometry-1: draw the hull BEFORE the tube so the tube's own depth
            // writes cover the hull's interior, leaving only the rim visible.
            "Queue" = "Geometry-1"
        }

        Pass
        {
            Name "PotionOutline"
            Tags { "LightMode" = "UniversalForward" }

            // The whole trick: keep the back faces, discard the front ones.
            Cull Front
            ZWrite On
            ZTest LEqual

            // Draw only OUTSIDE the tube's silhouette. VFX/PotionOutlineMask
            // stamps bit 6 over the tube first; without this the hull's interior
            // shows straight through the transparent glass and the tube reads as
            // a solid block of colour rather than an outline.
            Stencil
            {
                Ref 64
                ReadMask 64
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColour;
                float _OutlineWidth;
                float _DistanceCompensation;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = normalize(TransformObjectToWorldNormal(IN.normalOS));

                // A pure world-space width is honest in stereo but shrinks to
                // nothing at rack distance; a pure screen-space width holds up
                // far away but reads as a flat sticker up close. Blend between
                // the two so it stays legible across the table without losing
                // its solidity in the hand. viewDistance is normalised against
                // 1m so the authored width means what it says at arm's length.
                float viewDistance = length(GetCameraPositionWS() - positionWS);
                float widthScale = lerp(1.0, viewDistance, _DistanceCompensation);

                positionWS += normalWS * _OutlineWidth * widthScale;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                return _OutlineColour;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
