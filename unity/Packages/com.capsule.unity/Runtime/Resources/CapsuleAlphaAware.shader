Shader "Portable UI/Alpha Aware"
{
    Properties
    {
        [PerRendererData] _MainTex ("Exact image", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Mask Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="False" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            Texture2D _MainTex;
            float4 _MainTex_TexelSize, _Color, _ClipRect, _TextureSampleAdd;
            struct vertexInput { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct fragmentInput { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 local:TEXCOORD1; };
            fragmentInput vert(vertexInput input)
            {
                fragmentInput output;
                output.local=input.vertex;output.vertex=UnityObjectToClipPos(input.vertex);
                output.color=input.color*_Color;output.uv=input.uv;
                return output;
            }
            float4 PremultipliedTexel(float2 cell)
            {
                // Integer fetches bypass bilinear filtering. sRGB images decode
                // to linear RGB before this multiplication; alpha stays linear.
                int2 at=(int2)clamp(cell,0,_MainTex_TexelSize.zw-1);
                float4 value=_MainTex.Load(int3(at,0))+_TextureSampleAdd;
                value.rgb*=value.a;
                return value;
            }
            float4 FilteredTexel(float2 uv)
            {
                float2 position=uv*_MainTex_TexelSize.zw-.5;
                float2 cell=floor(position), weight=frac(position);
                // Premultiply each of four source texels BEFORE interpolation.
                float4 top=lerp(PremultipliedTexel(cell),PremultipliedTexel(cell+float2(1,0)),weight.x);
                float4 bottom=lerp(PremultipliedTexel(cell+float2(0,1)),PremultipliedTexel(cell+float2(1,1)),weight.x);
                return lerp(top,bottom,weight.y);
            }
            float4 MinifiedTexel(float2 uv)
            {
                float2 center=uv*_MainTex_TexelSize.zw;
                // Integrate the screen pixel's source footprint. Four mip-zero
                // samples alone skip most texels when large UI art is reduced.
                float2 footprint=max(abs(ddx(center))+abs(ddy(center)),1);
                if(max(footprint.x,footprint.y)<=1.001)return FilteredTexel(uv);
                float2 lower=center-footprint*.5, upper=center+footprint*.5;
                float2 first=floor(lower), count=ceil(upper)-first;
                float4 sum=0;
                if(max(count.x,count.y)<=16)
                {
                    // Exact area weights; premultiply BEFORE accumulating so
                    // hidden RGB cannot pollute translucent edges.
                    [loop] for(int y=0;y<(int)count.y;y++)
                    {
                        float wy=max(0,min(first.y+y+1,upper.y)-max(first.y+y,lower.y));
                        [loop] for(int x=0;x<(int)count.x;x++)
                        {
                            float wx=max(0,min(first.x+x+1,upper.x)-max(first.x+x,lower.x));
                            sum+=PremultipliedTexel(first+float2(x,y))*wx*wy;
                        }
                    }
                    return sum/(footprint.x*footprint.y);
                }
                // Extreme reduction remains bounded: integrate 16x16 strata
                // without allocating a second texture or altering source art.
                [loop] for(int sy=0;sy<16;sy++)
                    [loop] for(int sx=0;sx<16;sx++)
                        sum+=FilteredTexel((lower+(float2(sx,sy)+.5)*footprint/16)*_MainTex_TexelSize.xy);
                return sum/256;
            }
            float4 frag(fragmentInput input):SV_Target
            {
                float4 color=MinifiedTexel(input.uv);
                color.rgb*=input.color.rgb*input.color.a;color.a*=input.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                color*=UnityGet2DClipping(input.local.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                // Standard stencil-mask semantics, not an image-edge repair.
                clip(color.a-.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
