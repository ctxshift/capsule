Shader "Portable UI/Soft Light"
{
    Properties
    {
        [PerRendererData] _MainTex ("Exact highlight asset", 2D) = "white" {}
        _Color ("RGB brightness", Color) = (1,1,1,1)
        _BaseRgb ("Display-space background", Vector) = (0,0,0,0)
        _OverlayRgba ("Display-space overlay", Vector) = (0,0,0,0)
        _Strength ("Highlight opacity", Range(0,1)) = 0.12
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            sampler2D _MainTex;
            float4 _Color, _BaseRgb, _OverlayRgba, _ClipRect;
            float _Strength;
            struct vertexInput { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct fragmentInput { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 local:TEXCOORD1; };
            fragmentInput vert(vertexInput input)
            {
                fragmentInput output;
                output.local=input.vertex;
                output.vertex=UnityObjectToClipPos(input.vertex);
                output.color=input.color; output.uv=input.uv;
                return output;
            }
            float4 frag(fragmentInput input):SV_Target
            {
                // UI vertex alpha gates hover. Blend in display space, matching
                // CSS source-over and soft-light; output converts once for the
                // Player's linear framebuffer. The original asset is sampled.
                clip(input.color.a-0.001);
                float4 source=tex2D(_MainTex,input.uv);
                #ifndef UNITY_COLORSPACE_GAMMA
                source.rgb=LinearToGammaSpace(source.rgb);
                #endif
                float3 base=lerp(_BaseRgb.rgb,_OverlayRgba.rgb,_OverlayRgba.a);
                float3 curve=lerp(((16*base-12)*base+4)*base,sqrt(base),step(0.25,base));
                float3 low=base-(1-2*source.rgb)*base*(1-base);
                float3 high=base+(2*source.rgb-1)*(curve-base);
                float3 blend=lerp(low,high,step(0.5,source.rgb));
                float3 rgb=saturate(lerp(base,blend,source.a*_Strength)*_Color.rgb);
                #ifndef UNITY_COLORSPACE_GAMMA
                rgb=GammaToLinearSpace(rgb);
                #endif
                float alpha=_Color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha*=UnityGet2DClipping(input.local.xy,_ClipRect);
                #endif
                return float4(rgb,alpha);
            }
            ENDCG
        }
    }
}
