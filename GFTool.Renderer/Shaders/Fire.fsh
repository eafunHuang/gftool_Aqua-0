#version 420 core

uniform sampler2D BaseColorMap;
uniform sampler2D LayerMaskMap;
uniform sampler2D DisplacementMap;

uniform vec4 UVScaleOffset;
uniform vec4 UVScaleOffset3;
uniform vec4 ScrollUVSpeed;
uniform vec4 time_params;
uniform vec4 BaseColor;
uniform vec4 BaseColorLayer1;
uniform vec4 BaseColorLayer2;
uniform vec4 BaseColorLayer3;
uniform vec4 BaseColorLayer4;

uniform float LayerMaskScale1;
uniform float LayerMaskScale2;
uniform float LayerMaskScale3;
uniform float LayerMaskScale4;

uniform bool EnableBaseColorMap;
uniform bool EnableLayerMaskMap;
uniform bool EnableDisplacementMap;
uniform bool EnableAlphaTest;
uniform bool EnableVertexColor;
uniform bool BaseColorMultiply;
uniform bool TransparentPass;
uniform bool PremultiplyAlpha;
uniform int NumMaterialLayer;
uniform int UVIndexLayerMask;
uniform int UVIndexLayer3;
uniform float AlphaTestThreshold;
uniform float DiscardValue;

layout (location = 0) out vec4 gAlbedo;
layout (location = 1) out vec4 gNormal;
layout (location = 2) out vec4 gSpecular;
layout (location = 3) out vec4 gAO;

in vec3 FragPos;
in vec3 Normal;
in vec4 UV01;
in vec4 Color;

vec2 FlipV(vec2 uv)
{
    return vec2(uv.x, 1.0 - uv.y);
}

vec2 TransformUvFd(vec2 uv, vec4 scaleOffset)
{
    return uv * scaleOffset.xy + scaleOffset.zw;
}

vec2 SelectUv(int index, vec2 uv0, vec2 uv1)
{
    return (index == 0) ? uv0 : uv1;
}

void main()
{
    vec2 rawUv0 = UV01.xy;
    vec2 rawUv1 = UV01.zw;
    float t = time_params.x;

    vec2 uv = FlipV(rawUv0);
    uv = TransformUvFd(uv, UVScaleOffset);
    uv += ScrollUVSpeed.xy * t;

    bool useLayerMask = EnableLayerMaskMap && (NumMaterialLayer > 0);
    vec4 layerMask = vec4(0.0);
    if (useLayerMask)
    {
        vec2 maskUv = FlipV(SelectUv(UVIndexLayerMask, rawUv0, rawUv1));
        maskUv = TransformUvFd(maskUv, UVScaleOffset);
        maskUv += ScrollUVSpeed.xy * t;
        layerMask = texture(LayerMaskMap, maskUv);
        layerMask *= vec4(LayerMaskScale1, LayerMaskScale2, LayerMaskScale3, LayerMaskScale4);
        if (dot(BaseColorLayer2.rgb, BaseColorLayer2.rgb) < 0.000001) layerMask.g = 0.0;
        if (dot(BaseColorLayer3.rgb, BaseColorLayer3.rgb) < 0.000001) layerMask.b = 0.0;
        if (dot(BaseColorLayer4.rgb, BaseColorLayer4.rgb) < 0.000001) layerMask.a = 0.0;
    }

    float baseLayerWeight = 1.0;
    if (useLayerMask)
    {
        float layerSum = clamp(dot(vec4(1.0), layerMask), 0.0, 1.0);
        baseLayerWeight = clamp(1.0 - layerSum, 0.0, 1.0);
    }

    vec4 baseSample = EnableBaseColorMap ? texture(BaseColorMap, uv) : vec4(1.0);
    float alphaValue = baseSample.a * BaseColor.a;
    if (EnableDisplacementMap)
    {
        vec2 rawDispUv = SelectUv(UVIndexLayer3, rawUv0, rawUv1);
        vec2 dispUv = FlipV(rawDispUv);
        dispUv = TransformUvFd(dispUv, UVScaleOffset3);
        dispUv += ScrollUVSpeed.zw * t;
        float dispMask = texture(DisplacementMap, dispUv).r;
        alphaValue *= clamp(dispMask, 0.0, 1.0);
    }

    if (EnableAlphaTest)
    {
        if (alphaValue < AlphaTestThreshold)
        {
            discard;
        }
    }
    else if (EnableDisplacementMap)
    {
        if (alphaValue < DiscardValue)
        {
            discard;
        }
    }

    vec3 texColor = baseSample.rgb;
    vec3 baseTint = BaseColorMultiply ? BaseColor.rgb : vec3(1.0);
    vec3 baseLayerColor = texColor * baseTint;
    vec3 layerColor = baseLayerColor;
    if (useLayerMask)
    {
        vec3 layer1 = texColor * BaseColorLayer1.rgb;
        vec3 layer2 = texColor * BaseColorLayer2.rgb;
        vec3 layer3 = texColor * BaseColorLayer3.rgb;
        vec3 layer4 = texColor * BaseColorLayer4.rgb;
        layerColor = baseLayerColor * baseLayerWeight +
                     layer1 * layerMask.r +
                     layer2 * layerMask.g +
                     layer3 * layerMask.b +
                     layer4 * layerMask.a;
    }

    vec3 vertexColor = EnableVertexColor ? Color.rgb : vec3(1.0);
    vec3 albedo = layerColor * vertexColor;

    if (TransparentPass)
    {
        vec3 colorOut = albedo;
        if (PremultiplyAlpha)
        {
            colorOut *= alphaValue;
        }
        gAlbedo = vec4(colorOut, alphaValue);
        gNormal = vec4(0.0);
        gSpecular = vec4(0.0);
        gAO = vec4(0.0);
        return;
    }

    gAlbedo = vec4(albedo, alphaValue);
    gNormal = vec4(normalize(Normal) * 0.5 + 0.5, 1.0);
    gSpecular = vec4(1.0, 0.0, 0.0, 0.0);
    gAO = vec4(0.0, 0.0, 0.0, 1.0); // emission=0, shadingModel=PreLit
}
