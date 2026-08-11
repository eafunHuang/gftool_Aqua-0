#version 420 core

layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoord;
layout (location = 8) in vec2 aTexCoord2;
layout (location = 3) in vec4 aColor;
layout (location = 6) in vec4 aBlendIndices;
layout (location = 7) in vec4 aBlendWeights;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
uniform bool EnableSkinning;
uniform bool SwapBlendOrder;
uniform mat4 Bones[192];
uniform int BoneCount;
uniform bool EnableDisplacementMap;
uniform sampler2D DisplacementMap;
uniform float DisplacementHeight;
uniform vec4 UVScaleOffset3;
uniform int UVIndexLayer3;
uniform vec4 ScrollUVSpeed;
uniform vec4 time_params;

out vec3 FragPos;
out vec3 Normal;
out vec4 UV01;
out vec4 Color;

vec2 TransformUv(vec2 uv, vec4 scaleOffset)
{
    vec2 stuv = scaleOffset.xy * (uv - scaleOffset.zw);
    return vec2(stuv.x, 1.0 - stuv.y);
}

vec2 SelectUv(vec2 uv0, vec2 uv1, int index)
{
    return (index == 0) ? uv0 : uv1;
}

void main()
{
    vec4 localPos = vec4(aPos, 1.0);
    vec3 localNormal = aNormal;
    if (EnableSkinning && BoneCount > 0)
    {
        vec4 weights = aBlendWeights;
        ivec4 boneIds = ivec4(aBlendIndices + 0.5);
        if (SwapBlendOrder)
        {
            weights = weights.wxyz;
            boneIds = ivec4(boneIds.w, boneIds.x, boneIds.y, boneIds.z);
        }
        float total = weights.x + weights.y + weights.z + weights.w;
        if (total > 0.0)
        {
            weights /= total;
        }
        boneIds = clamp(boneIds, ivec4(0), ivec4(BoneCount - 1));
        mat4 skinMat = weights.x * Bones[boneIds.x]
                     + weights.y * Bones[boneIds.y]
                     + weights.z * Bones[boneIds.z]
                     + weights.w * Bones[boneIds.w];
        localPos = skinMat * vec4(aPos, 1.0);
        localNormal = normalize(mat3(skinMat) * aNormal);
    }

    UV01 = vec4(aTexCoord, aTexCoord2);
    Color = aColor;

    if (EnableDisplacementMap)
    {
        vec2 dispUv = TransformUv(SelectUv(UV01.xy, UV01.zw, UVIndexLayer3), UVScaleOffset3);
        dispUv += ScrollUVSpeed.zw * time_params.x;
        float phase = textureLod(DisplacementMap, dispUv, 0.0).r;
        float disp = sin(phase) * (aColor.r * DisplacementHeight);
        localPos.xyz += disp * localNormal;
    }

    FragPos = vec3(model * localPos);
    Normal = normalize(mat3(transpose(inverse(model))) * localNormal);
    gl_Position = projection * view * vec4(FragPos, 1.0);
}
