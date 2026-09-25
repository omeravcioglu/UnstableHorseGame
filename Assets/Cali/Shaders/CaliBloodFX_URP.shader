Shader "Cali/BloodFX_URP"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        _MainTex("Texture", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Color("Color", Color) = (1,1,1,1)
        _ColorIntensity("Color Intensity", Float) = 0.55
        _AlbedoPower("Albedo Power", Float) = 1
        _AmbientColorIntensity("Ambient Color Intensity", Float) = 1.5
        _HueShift("Hue Shift", Range(-180, 180)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.8
        _UseSpecularity("Use Specularity", Float) = 1
        _BumpScale("Bump Scale", Float) = 1
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.01
        [HideInInspector] _SrcBlend("Src Blend", Float) = 5
        [HideInInspector] _DstBlend("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _MainTex_ST;
                float4 _BaseColor;
                float4 _Color;
                float _ColorIntensity;
                float _AlbedoPower;
                float _AmbientColorIntensity;
                float _HueShift;
                float _Smoothness;
                float _UseSpecularity;
                float _BumpScale;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
            };

            float3 ApplyHue(float3 color, float degrees)
            {
                float rad = radians(degrees);
                float3 k = float3(0.57735, 0.57735, 0.57735);
                return color * cos(rad) + cross(k, color) * sin(rad) + k * dot(k, color) * (1.0 - cos(rad));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = nrm.normalWS;
                output.tangentWS = float4(nrm.tangentWS, input.tangentOS.w);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                if (albedoSample.a < 0.001)
                    albedoSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                float4 tint = _BaseColor.a > 0.001 ? _BaseColor : _Color;
                float3 texCol = pow(max(albedoSample.rgb, 0.001), max(_AlbedoPower, 0.35));
                // Preset tints are muted; keep the packed blood texture dominant.
                float3 albedo = texCol * lerp(float3(1, 1, 1), tint.rgb * 2.4, saturate(_ColorIntensity));
                albedo *= input.color.rgb;
                albedo = ApplyHue(albedo, _HueShift);

                float3 n = normalize(input.normalWS);
                float3 t = input.tangentWS.xyz;
                if (dot(t, t) > 0.01)
                {
                    float4 packedN = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv);
                    float3 tS = UnpackNormalScale(packedN, _BumpScale);
                    t = normalize(t);
                    float3 b = cross(n, t) * input.tangentWS.w;
                    n = normalize(mul(tS, float3x3(t, b, n)));
                }

                Light light = GetMainLight();
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float ndotl = saturate(dot(n, light.direction));
                float3 halfDir = normalize(light.direction + viewDir);
                float specPow = exp2(8.0 * saturate(_Smoothness) + 1.0);
                float spec = pow(saturate(dot(n, halfDir)), specPow) * saturate(_UseSpecularity) * saturate(_Smoothness);

                float3 ambient = texCol * max(_AmbientColorIntensity, 0.6) * 0.35;
                float3 lit = albedo * (light.color * (0.35 + ndotl * 0.85) + ambient);
                lit += spec * light.color;

                float alpha = saturate(albedoSample.a * tint.a * input.color.a);
                return half4(lit, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
