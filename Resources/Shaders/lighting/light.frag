// How far "above the ground" lights are.
const highp float LIGHTING_HEIGHT = 1.0;

// Distance from the current fragment to the center of the light source, in world coordinates
varying highp vec2 vDeltaPos;
varying highp vec2 vMaskUV;

uniform highp vec4 uLightColor;
uniform highp float uLightPower;
uniform highp vec4 uLightData; // (x pos, y pos, range, angle)
uniform int uClamp;  // True if this is an alpha clamping pass.

void main()
{
    if (uClamp == 1)
    {
        gl_FragColor = vec4(1.0);
        return;
    }

    highp float range = uLightData.z;
    highp float dist2 = dot(vDeltaPos, vDeltaPos) + LIGHTING_HEIGHT * LIGHTING_HEIGHT;
    highp float falloff = clamp((1.0 - clamp(sqrt(dist2) / range, 0.0, 1.0)) * (1.0 / (sqrt(dist2 + 1.0))), 0.0, 1.0);
    highp float mask = zTextureSpec(TEXTURE, vMaskUV).r;
    gl_FragColor = zAdjustResult(uLightColor * falloff * uLightPower * mask * uLightColor.w);
}
