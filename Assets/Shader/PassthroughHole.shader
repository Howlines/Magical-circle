// Dessine une surface 100 % transparente : on voit le passthrough à travers.
Shader "Custom/PassthroughHole"
{
    SubShader
    {
        // "Geometry-10" : dessiné juste AVANT le reste de la scène
        Tags { "RenderType"="Opaque" "Queue"="Geometry-10" }

        Pass
        {
            ZWrite On   // écrit sa profondeur : les murs derrière ne se dessineront pas par-dessus
            Blend Off   // remplace la couleur au lieu de la mélanger
            Cull Off    // visible des deux côtés

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO     // gestion des deux yeux en VR
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(0, 0, 0, 0);   // alpha 0 = transparent = on voit le passthrough
            }
            ENDCG
        }
    }
}