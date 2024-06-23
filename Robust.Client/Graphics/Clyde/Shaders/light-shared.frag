// How far "above the groun" lights are.
const highp float LIGHTING_HEIGHT = 1.0;

// Distance from the current fragment to the center of the light source, in world coordinates
varying highp vec2 DeltaWorldPos;
varying highp vec2 UV; // Texture UV coordinates

flat varying highp vec4 LightColor;
flat varying highp vec4 LightData; // (Range, Power, Softness, Index)

uniform sampler2D shadowMap;

// Populate with hard or soft occlusion function:
// [CreateOcclusion]

void main()
{
    highp float lightIndex = LightData.w;
    highp float dist = length(DeltaWorldPos);

    // Totally not hacky PCF on top of VSM.
    highp float occlusion = lightIndex < 0.0 ? 1.0 : createOcclusion(dist);

    if (occlusion == 0.0)
    {
        discard;
    }

    highp float lightRange = LightData.x;
    highp float lightPower = LightData.y;

    highp float dist2 = dist * dist + LIGHTING_HEIGHT * LIGHTING_HEIGHT;
    highp float val = clamp((1.0 - clamp(sqrt(dist2) / lightRange, 0.0, 1.0)) * (1.0 / (sqrt(dist2 + 1.0))), 0.0, 1.0);

    val *= lightPower * zTexture(UV).r * occlusion;
    gl_FragColor = zAdjustResult(LightColor * vec4(vec3(1.0), val));
}
