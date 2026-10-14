// DoorFilterLeak.shader（可选）
// 贴在门洞里的一片 Quad 上：从门外往屋里看时，门洞里的画面被"染"成屋里的滤镜色。
// - Built-in 管线，支持 VRChat PC 的单通道立体渲染（Single Pass Instanced）
// - 用一次 GrabPass。脚本只在门开着、人在屋外时才打开它的 Renderer，平时零开销
// - Quad 的正面要朝向门外（Cull Back），这样从屋里往外看时不会再染一遍
// - _Strength 由 RoomWeather 按门开的程度写入，不用手动调
// 注意：这份 shader 没有在 Unity 里实际编译测试过，第一次用先在测试场景里看效果。

Shader "RainRoom/DoorFilterLeak"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.80, 0.86, 0.97, 1)
        _Saturation ("Saturation (0 = grey, 1 = original)", Range(0, 1)) = 0.55
        _Brightness ("Brightness", Range(0, 1.5)) = 0.8
        _Feather ("Edge Feather (UV)", Range(0.001, 0.5)) = 0.08
        _Strength ("Strength (set by script)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        GrabPass { "_DoorLeakGrab" }

        Pass
        {
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_DoorLeakGrab);

            fixed4 _Tint;
            half _Saturation;
            half _Brightness;
            half _Feather;
            half _Strength;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 grabPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.grabPos = ComputeGrabScreenPos(o.pos);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 screenUV = i.grabPos.xy / i.grabPos.w;
                fixed3 col = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_DoorLeakGrab, screenUV).rgb;

                // 降饱和 → 染冷色 → 压暗
                half luma = dot(col, half3(0.2126, 0.7152, 0.0722));
                fixed3 graded = lerp(luma.xxx, col, _Saturation) * _Tint.rgb * _Brightness;

                // 四边羽化，避免门框边缘出现一条硬线
                float2 edge2 = min(i.uv, 1.0 - i.uv);
                half edge = saturate(min(edge2.x, edge2.y) / _Feather);

                return fixed4(lerp(col, graded, _Strength * edge), 1);
            }
            ENDCG
        }
    }

    FallBack Off
}
