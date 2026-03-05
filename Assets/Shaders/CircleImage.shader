Shader "UI/RoundedImage"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Radius ("Corner Radius", Range(0, 0.5)) = 0.1
        _Width ("Width", Float) = 1
        _Height ("Height", Float) = 1

        // UI 기본 속성 (Stencil 등)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Radius;
            float _Width;
            float _Height;

            // Rounded Rectangle SDF (Signed Distance Function)
            float roundedRectSDF(float2 uv, float2 size, float radius)
            {
                float2 d = abs(uv) - size + radius;
                return length(max(d, 0.0)) - radius;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;

                // UV를 중심 기준 좌표로 변환 (-0.5 ~ 0.5)
                float2 centeredUV = i.uv - 0.5;

                // 가로세로 비율을 고려한 크기
                float2 size = float2(0.5, 0.5);

                // 비율 보정: 가로세로 비율이 다를 때 모서리 반지름이 균일하게
                float aspectRatio = _Width / max(_Height, 0.001);
                centeredUV.x *= aspectRatio;
                size.x *= aspectRatio;

                // 실제 반지름 (비율 보정 적용)
                float radius = _Radius * min(aspectRatio, 1.0);

                // SDF로 모서리 둥글기 계산
                float dist = roundedRectSDF(centeredUV, size, radius);

                // smoothstep으로 가장자리 부드럽게 (1픽셀 안티앨리어싱)
                // fwidth로 화면 해상도에 맞는 최소 블렌딩 폭 계산
                float smoothing = fwidth(dist);
                col.a *= 1.0 - smoothstep(-smoothing, 0.0, dist);

                return col;
            }
            ENDCG
        }
    }
}
