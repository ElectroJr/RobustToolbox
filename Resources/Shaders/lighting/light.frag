// How far "above the ground" lights are.
const highp float LIGHTING_HEIGHT = 1.0;

// Distance from the current fragment to the center of the light source, in world coordinates
varying highp vec2 DeltaWorldPos;
varying highp vec2 MaskUV;
varying highp vec2 ShadowUV;

flat varying highp vec4 LightColor;
flat varying highp vec4 LightData; // (Range, Power, Softness, Index)

uniform sampler2D ShadowMap;
uniform float CastShadows;

void main()
{
    highp float lightIndex = LightData.w;
    highp float dist = length(DeltaWorldPos);
    highp float occlusion = CastShadows < 0.0 ? 1.0 : texture2D(ShadowMap, ShadowUV).r;

    if (occlusion == 0.0)
        discard;

    highp float lightRange = LightData.x;
    highp float lightPower = LightData.y;

    highp float dist2 = dist * dist + LIGHTING_HEIGHT * LIGHTING_HEIGHT;

    // TODO LIGHTING re-enable falloff
    highp float val = clamp((1.0 - clamp(sqrt(dist2) / lightRange, 0.0, 1.0)) * (1.0 / (sqrt(dist2 + 1.0))), 0.0, 1.0);
    highp float mask = zTextureSpec(TEXTURE, MaskUV).r;

    val *= lightPower * mask * occlusion * occlusion;
    gl_FragColor = zAdjustResult(LightColor * vec4(vec3(1.0), val));
}
