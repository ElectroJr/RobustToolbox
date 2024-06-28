// Coordinates of the two points A & B that make up the line being drawn.
attribute highp vec4 aPos;

uniform highp vec3 LightData; // (lightPos.x, lightPos.y, lightRange);

// expands wall edges a little to prevent holes
const highp float DEPTH_LEFTRIGHT_EXPAND_BIAS = 0.001;

// Size of a single light quad (in clip space coordinates)
const highp float MAX_LIGHTS = 12.0;
const highp float SHADOW_SIZE = 2.0/MAX_LIGHTS;

void main()
{
    int pointId = gl_VertexID - (gl_VertexID / 4) * 4;

    highp vec2 lightPos = LightData.xy;
    highp float lightRange = LightData.z;

    vec2 pointA = (aPos.xy - lightPos)/lightRange;
    vec2 pointB = (aPos.zw - lightPos)/lightRange;
    float angleA = atan(pointA.y, pointA.x);
    float angleB = atan(pointB.y, pointB.x);

    float delta = angleB - angleA;

    // Check if the line clips over the [Pi, -Pi] range.
    if (delta >= PI)
    {
        angleB -= PI * 2.0;
        delta = angleB - angleA;
    }
    else if (delta <= -PI)
    {
        angleB += PI * 2.0;
        delta = angleB - angleA;
    }

    float sign = sign(delta);

    // expand angles
    float lrSignBias = sign * DEPTH_LEFTRIGHT_EXPAND_BIAS;
    angleA -= lrSignBias;
    angleB += lrSignBias;

    // Line defined via a*x + b*y = c
    highp vec3 line = vec3(
        pointB.y - pointA.y, // a
        pointA.x - pointB.x, // b
        pointA.x * pointB.y - pointA.y * pointB.x // c
    );

    // Convert to polar coordinates.
    // Line defined via r = r0/cos(theta-t0)
    highp float r0 = line.z/sqrt(line.x*line.x + line.y*line.y);
    highp float t0 = atan(line.y, line.x);

    // If the line is going clockwise, we clip it
    float depth = sign < 0.0 ? 2.0 : 0.0;
    highp float r;
    highp float angle;

    switch (pointId)
    {
        case 0:
        // This is just pointA, but with the DEPTH_LEFTRIGHT_EXPAND_BIAS applied to the angle
        angle = angleA;
        r = r0/cos(angle - t0);
        break;

        case 1:
        // The is the "shadow" of point A, cast out to some point beyond the box.
        angle = angleA;
        r = (r0 + 2.0)/cos(angle - t0);
        break;

        case 2:
        // The is the "shadow" of point B, cast out to some point beyond the box.
        angle = angleB;
        r = (r0 + 2.0)/cos(angle - t0);
        break;

        default:
        // This is just pointB, but with the DEPTH_LEFTRIGHT_EXPAND_BIAS applied to the angle
        angle = angleB;
        r = r0/cos(angle - t0);
    }

    highp vec2 point = r * vec2(cos(angle), sin(angle));;
    gl_Position = vec4(point, depth, 1.0);
}
