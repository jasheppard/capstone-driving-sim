// The painted route line.
//
// WHY THIS EXISTS RATHER THAN Sprites/Default
//
// A line lying 12 cm above photogrammetry terrain fights with it. The depth buffer
// cannot separate two surfaces that close at distance, so the line flickers in and
// out along its length, and where a tile swaps to a coarser level of detail the
// ground rises through it and swallows it entirely.
//
// Offset pulls the line towards the camera in depth without moving it in the world,
// which fixes the flicker. It deliberately does NOT disable the depth test: a
// guidance line that draws through buildings and through the back of the bus is
// worse than one that occasionally dips. Real burial is fixed by height and by
// re-grounding, not by refusing to test depth.
Shader "NCAT/RoutePath"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.85, 0.2, 0.9)

        // Exposed so it can be set to Always from the Inspector if the line really
        // does need to be visible through terrain on some stretch.
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
    }

    SubShader
    {
        // Transparent, and late in the queue, so it draws over the road rather than
        // being sorted into the middle of the opaque campus.
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest [_ZTest]
        Offset -1, -1
        Cull Off
        Lighting Off

        // No LightMode tag, so URP draws this through its default unlit path and the
        // same shader works in both pipelines.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float4 colour   : COLOR;
                UNITY_FOG_COORDS(0)
            };

            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);

                // The LineRenderer's own start and end colours arrive as vertex
                // colours, so both it and the material have a say.
                o.colour = v.color * _Color;

                UNITY_TRANSFER_FOG(o, o.position);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 colour = i.colour;
                UNITY_APPLY_FOG(i.fogCoord, colour);
                return colour;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
