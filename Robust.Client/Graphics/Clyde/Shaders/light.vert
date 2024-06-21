/*layout (location = 0)*/ attribute vec2 tCoord;
/*layout (location = 1)*/ attribute vec2 tCoord2;
/*layout (location = 2)*/ attribute vec4 lightColor;
/*layout (location = 3)*/ attribute vec2 lightPos;
/*layout (location = 4)*/ attribute vec4 lightData;
/*layout (location = 5)*/ attribute float lightAngle;

// TODO LIGHTING AFAIK this can be inferred from UV & Range in the fragment shader, is doing that better?
// Distance from the current fragment to the center of the light source, in world coordinates
varying highp vec2 DeltaWorldPos;
varying highp vec2 UV;

flat varying highp vec4 LightColor;
flat varying highp vec4 LightData; // (Range, Power, Softness, Index)

void main()
{
    // Input position aPos should just be the corners of a square with side length 2 centered at 0,0.

    // TODO LIGHTING is it better to do these transformation on the CPU?
    // I'm guessing the answer is no, but should probably check.
    float s = sin(lightAngle);
    float c = cos(lightAngle);
    mat2 rotate = mat2(c, s, -s, c);

    // scale UV coordinates up to a 2*2 square centered at (0,0)
    highp vec2 pos = tCoord2 * 2 - vec2(1);

    // Rotate the square, scale it by the lights radius,
    pos = (rotate * pos) * lightData.x;

    DeltaWorldPos = pos;
    UV = tCoord;

    LightColor = zFromSrgb(lightColor);
    LightData = lightData;

    highp vec3 transformed = projectionMatrix * viewMatrix * vec3(pos + lightPos, 1.0);
    gl_Position = vec4(transformed.xy, 0.0, 1.0);
}
