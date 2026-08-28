// 打击纹样统一材质:采样精灵图,只用它的 alpha 当形状,RGB 恒为纯白。
// SpriteRenderer 会把当前精灵的图自动喂给 _MainTex,顶点色(=renderer.color)的 alpha 控制淡入淡出。
// 这样任何彩色纹样(鱼/蛇/蛙/鸟)都渲染成纯白剪影,而不改动原图资源。
Shader "LijiangEcho/WhiteSilhouette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Toggle(PIXELSNAP_ON)] PixelSnap ("Pixel snap", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ PIXELSNAP_ON
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            fixed4 _Color;
            sampler2D _MainTex;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 只取原图 alpha 当形状;颜色输出该渲染器的 tint(音符设白→纯白剪影,
                // 光晕设金→纯金剪影),不受原图彩色影响。alpha 再乘 tint.a 做淡入淡出。
                fixed a = tex2D(_MainTex, IN.texcoord).a * IN.color.a;
                return fixed4(IN.color.rgb, a);
            }
            ENDCG
        }
    }
}
