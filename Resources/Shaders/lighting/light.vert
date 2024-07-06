/*layout (location = 0)*/ attribute vec2 aMaskUV;
/*layout (location = 1)*/ attribute vec4 aLightColor;
/*layout (location = 2)*/ attribute vec2 aLightPos;
/*layout (location = 3)*/ attribute vec4 aLightData;
/*layout (location = 4)*/ attribute float aLightAngle;

const int MAX_LIGHTS = 12;
const highp float SHADOW_SIZE = 1.0/12.0;

// Distance from the current fragment to the center of the light source, in world coordinates
varying highp vec2 DeltaWorldPos;
varying highp vec2 MaskUV;
varying highp vec2 ShadowUV;

flat varying highp vec4 LightColor;
flat varying highp vec4 LightData; // (Range, Power, Softness, Index)

void main()
{
    float s = sin(aLightAngle);
    float c = cos(aLightAngle);
    mat2 rotate = mat2(c, s, -s, c);

    // TODO LIGHTING
    // if the batch breaks, this needs an offset.
    int lightId = gl_VertexID/4;

    int row = lightId/MAX_LIGHTS;
    int column = lightId - row * MAX_LIGHTS;

    // UV coordiantes for the corner of this light's shadowmap in the shadowmap atlas.
    highp vec2 shadowOrigin = vec2(column * SHADOW_SIZE, row * SHADOW_SIZE);

    highp vec2 pos;
    switch (gl_VertexID - lightId * 4)
    {
        case 0:
            pos = vec2(-1.0, -1.0);
            break;
        case 1:
            pos = vec2(+1.0, -1.0);
            break;
        case 2:
            pos = vec2(+1.0, +1.0);
            break;
        default:
            pos = vec2(-1.0, +1.0);
    }

    // TODO LIGHTING
    // Fix bilinear interpolation bleed
    // probably: make shadowUV centered ON THE PIXELS
    // Requires shifting it in or out by 0.5*pixel_size;
    // in the previous switch block.
    ShadowUV = shadowOrigin + SHADOW_SIZE * (pos + 1.0)/2.0;
    pos = (rotate * pos) * aLightData.x;
    DeltaWorldPos = pos;
    MaskUV = aMaskUV;

    LightColor = zFromSrgb(aLightColor);
    LightData = aLightData;

    highp vec3 transformed = projectionMatrix * viewMatrix * vec3(pos + aLightPos, 1.0);
    gl_Position = vec4(transformed.xy, 0.0, 1.0);
}
