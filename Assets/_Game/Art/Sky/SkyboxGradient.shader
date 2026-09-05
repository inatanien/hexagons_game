// 役割: 画像を使わず、色の補間だけで空を描くスカイボックス。
//
//       ★背景に「見るもの」を置かないための空。
//         パノラマ写真の背景は、そこに描かれた木や草花が盤面のタイルと視線を取り合う。
//         育てた森を眺めるゲームなので、背景は遠景として引き下がっていてほしい。
//
//       天頂・地平・地面側の3色を上下方向で補間するだけ。
//       時間帯ごとの変化はテクスチャの差し替えではなく、この3色を動かして作る
//       （TimeOfDaySystem が環境光・太陽と一緒に補間する）。

Shader "Custom/SkyboxGradient"
{
    Properties
    {
        _ZenithColor  ("天頂の色",   Color) = (0.42, 0.66, 0.92, 1)
        _HorizonColor ("地平の色",   Color) = (0.80, 0.90, 0.97, 1)
        _GroundColor  ("地面側の色", Color) = (0.58, 0.66, 0.68, 1)

        // 地平線からどれだけの高さで天頂の色へ移りきるか。小さいほど境目がはっきりする
        _HorizonWidth ("地平のぼかし幅", Range(0.05, 1.5)) = 0.55
        _GroundWidth  ("地面側のぼかし幅", Range(0.05, 1.5)) = 0.25

        _Exposure     ("明るさ", Range(0, 4)) = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"

            half4 _ZenithColor;
            half4 _HorizonColor;
            half4 _GroundColor;
            half  _HorizonWidth;
            half  _GroundWidth;
            half  _Exposure;

            struct appdata_t
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex    : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex    = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;   // スカイボックスの頂点位置がそのまま視線方向
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.direction);

                // 地平線（dir.y = 0）を境に、上は天頂色へ、下は地面側の色へ寄せる
                half up   = saturate(smoothstep(0.0, _HorizonWidth, dir.y));
                half down = saturate(smoothstep(0.0, _GroundWidth, -dir.y));

                half3 color = lerp(_HorizonColor.rgb, _ZenithColor.rgb, up);
                color       = lerp(color, _GroundColor.rgb, down);

                return half4(color * _Exposure, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
