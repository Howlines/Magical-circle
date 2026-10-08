// Traduction du shader Godot "whiteT" (tornade) pour Unity.
// Fonctionne en Built-in et en URP (pass sans LightMode = rendu en Unlit), compatible Quest (stéréo instanciée).
Shader "Custom/GodotTornado"
{
    Properties
    {
        _Color ("Couleur", Color) = (1,1,1,1)
        _Intensity ("Intensité couleur", Float) = 5
        _NoiseTex ("Texture de bruit", 2D) = "black" {}
        _Threshold ("Seuil du bruit", Range(0,1)) = 0.5
        _Speed ("Vitesse de défilement", Float) = -0.5
        _Scale ("Échelle UV polaire", Vector) = (2,2,0,0)
        _PolarCenter ("Centre polaire", Vector) = (0.5,5,0,0)
        _Wobble ("Ondulation (amplitude)", Float) = 0.5
        _WobbleSpeed ("Ondulation (vitesse)", Float) = 1
        _GradMid ("Dégradé : position du max", Range(0.01,1)) = 0.533
        _GradEnd ("Dégradé : valeur en bas", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha   // blend_mix de Godot
        ZWrite Off
        Cull Off                          // cull_disabled : on voit l'intérieur du cône

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity, _Threshold, _Speed, _Wobble, _WobbleSpeed, _GradMid, _GradEnd;
            float4 _Scale, _PolarCenter;
            sampler2D _NoiseTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 guv : TEXCOORD0;   // UV "façon Godot" (V inversé)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Godot et Unity n'ont pas le V des UV dans le même sens
                float2 guv = float2(v.uv.x, 1.0 - v.uv.y);
                float t = _Time.y;

                // --- Wobble (copie du bloc "Wobble" du shader Godot) ---
                float s = smoothstep(0.98, 0.8, guv.y);          // le haut du cône ne bouge pas
                float p = guv.y - _WobbleSpeed * t;              // une onde qui monte le long du cône
                float m = s * 0.1 * sin(15.0 * p);               // gonflement / rétrécissement
                float3 vtx = v.vertex.xyz;
                float3 a = vtx * float3(1.0 + m, 1.0, 1.0 + m);
                float3 b = vtx + float3(sin(5.0 * p) * _Wobble, 0, 0);   // balancement latéral
                vtx = a + b;                                     // (le shader Godot double la taille ici)

                o.pos = UnityObjectToClipPos(float4(vtx, 1.0));
                o.guv = guv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 guv = i.guv;

                // Coordonnées polaires (comme le nœud UVPolarCoord de Godot)
                float2 dir = guv - _PolarCenter.xy;
                float radius = length(dir) * 2.0;
                float2 polar = float2(radius * 0.8, 0.0);

                // UV finales : polaire * échelle + défilement dans le temps + 2 x UV
                float2 uvT = polar * _Scale.xy + frac(_Speed * _Time.y) + guv + guv;

                // Bruit seuillé : noir ou blanc (comme la color ramp "constant" de Godot)
                float n = tex2D(_NoiseTex, float2(uvT.x, 1.0 - uvT.y)).r;
                float mask = smoothstep(_Threshold - 0.02, _Threshold + 0.02, n);

                // Dégradé vertical : fait disparaître le cône en haut et/ou en bas
                float g = guv.y < _GradMid
                    ? guv.y / _GradMid
                    : lerp(1.0, _GradEnd, (guv.y - _GradMid) / max(1.0 - _GradMid, 1e-4));

                fixed4 col;
                col.rgb = saturate(_Color.rgb * _Intensity * mask);
                col.a = saturate(mask * g);
                return col;
            }
            ENDCG
        }
    }
}
