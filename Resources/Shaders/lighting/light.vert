/*layout (location = 0)*/ attribute vec2 aPos;
/*layout (location = 1)*/ attribute vec2 tCoord;
/*layout (location = 2)*/ attribute vec2 tCoord2;
/*layout (location = 3)*/ attribute vec4 modulate;

uniform highp vec4 uLightData; // (x pos, y pos, range, angle)
uniform highp vec4 uLightMask; // (left, bottom, right, top)

// Distance from the current fragment to the center of the light source, in world coordinates
varying highp vec2 vDeltaPos;
varying highp vec2 vMaskUV;

void main()
{
    // Ignore eye position, light & occluder positions are all specified relative to the eye.
    highp mat3 view = viewMatrix;
    view[2].xyz = vec3(0.0);

    vec2 lightPos = uLightData.xy;
    float range = uLightData.z;
    float angle = uLightData.w;

    float s = sin(angle);
    float c = cos(angle);
    mat2 rotate = mat2(c, s, -s, c);
    vec2 pos = rotate * (aPos * 2.0 - 1.0);
    pos *= range;

    vDeltaPos = pos;

    // UV masks are, rotated 180 degrees. So we flip left/right and top/bottom here.
    // I.e., a flashlight with 0 world rotation should be facing south, but the masks asume they are facing north.
    vMaskUV = mix(uLightMask.zw, uLightMask.xy, aPos);

    highp vec3 transformed = projectionMatrix * view * vec3(pos + lightPos, 1.0);
    gl_Position = vec4(transformed.xy, 0.0, 1.0);
}
