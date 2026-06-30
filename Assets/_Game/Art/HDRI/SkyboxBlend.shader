// 役割: 2つのEquirectangularパノラマをブレンドするスカイボックスシェーダー。
//       _Blend を 0→1 にアニメーションさせることでフェードインアウトを実現する。

Shader "Custom/SkyboxBlend"
{
    Properties
    {
        _MainTex   ("Panorama A (Equirectangular)", 2D) = "grey" {}
        _SecondTex ("Panorama B (Equirectangular)", 2D) = "grey" {}
        _Blend     ("Blend", Range(0, 1)) = 0
        _Exposure  ("Exposure", Float) = 1.0
        _Rotation  ("Rotation", Range(0, 360)) = 0
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

            sampler2D _MainTex;
            sampler2D _SecondTex;
            float     _Blend;
            float     _Exposure;
            float     _Rotation;

            struct appdata_t
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float3 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Unity 標準と同じ Y 軸回転
            inline float3 RotateAroundYInDegrees(float3 vertex, float degrees)
            {
                float alpha = degrees * UNITY_PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float3(mul(m, vertex.xz), vertex.y).xzy;
            }

            // Unity Skybox/Panoramic と同じ Latitude-Longitude マッピング
            inline float2 ToRadialCoords(float3 coords)
            {
                float3 normalizedCoords = normalize(coords);
                float latitude  = acos(normalizedCoords.y);
                float longitude = atan2(normalizedCoords.z, normalizedCoords.x);
                float2 sphereCoords = float2(longitude, latitude)
                                    * float2(0.5 / UNITY_PI, 1.0 / UNITY_PI);
                return float2(0.5, 1.0) - sphereCoords;
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.texcoord = RotateAroundYInDegrees(v.vertex.xyz, _Rotation);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 uv  = ToRadialCoords(i.texcoord);
                half4 colA = tex2D(_MainTex,   uv);
                half4 colB = tex2D(_SecondTex, uv);
                half4 col  = lerp(colA, colB, _Blend);
                col.rgb   *= _Exposure;
                return half4(col.rgb, 1.0);
            }
            ENDCG
        }
    }
}
