// Coordinates of the two points A & B that make up the line being drawn.
attribute highp vec4 aPos;

uniform highp vec3 LightPosition; // (x position, y position, rotation)
uniform highp vec2 LightData; // (range, softness)

// expands wall edges a little to prevent holes
const highp float DEPTH_LEFTRIGHT_EXPAND_BIAS = 0.001;

// Size of a single light quad (in clip space coordinates)
const highp float MAX_LIGHTS = 12.0;
const highp float SHADOW_SIZE = 2.0/MAX_LIGHTS;

varying highp vec2 occlusion;

void main()
{
    highp vec2 lightPos = LightPosition.xy;
    highp float lightRot = LightPosition.z;
    highp float lightRange = LightData.x;
    highp float lightSoftness = 3.0;// LightData.y;

    // Each line occluder is defined using two points, A & B.
    // We scale all distanes such that 1.0 = max light range.
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

    // If the occluder line is going clockwise, we clip it by moving it out of the view box
    float depth = delta < 0.0 ? 2.0 : 0.0;

    // expand angles
    float lrSignBias = sign(delta) * DEPTH_LEFTRIGHT_EXPAND_BIAS;
    angleA -= lrSignBias;
    angleB += lrSignBias;

    // For drawing the penumbra, we offset the origin / light position when we try to find the "shadows" of points A & B.
    // The actual shape of the penumbra is not fully accurate. instead of treating the light as a ball or some other shape,
    // each line occluder assumes that the light is a parallel line located at the origin with a length equal to the light's softness.
    // However, we limit the "length" of the light source to be at most the length of the occluder. This is mainly just
    // a simplification that leads to decent soft shadows, while avoiding ever having to deal with an antumbra.

    highp float lightLength = lightSoftness/lightRange;

    // Formula for line a*x + b*y = c
    highp vec3 line = vec3(
        pointB.y - pointA.y, // a
        pointA.x - pointB.x, // b
        pointA.x * pointB.y - pointA.y * pointB.x // c
    );
    float closest = abs(line.z)/sqrt(line.x*line.x + line.y*line.y);
    lightLength = min(lightLength, 2.0 * closest - 1e-5);
    lightLength = max(0, lightLength);

    // TODO LIGHTING fix shadow atlas borders / interpolation.
    // TODO LIGHTING try use geometry shader. Is it faster?

    // When drawing the shadow for each line occluder, which part of the shape should this vertex refer to?
    // 0: The is the "shadow" of point A that makes up the outer part of the penumbra (i.e., barely occluded).
    // 1: This is just point A
    // 2: The is the "shadow" of point A that makes up the inner part of the penumbra (i.e., touching the umbra).
    // 3: This is just point B
    // 4: The is the "shadow" of point B that makes up the inner part of the penumbra (i.e., touching the umbra).
    // 5: The is the "shadow" of point B that makes up the outer part of the penumbra (i.e., barely occluded).
    int pointId = gl_VertexID - (gl_VertexID / 6) * 6;

    // perspective divide (using 0 for infinite homogeneous coordinates)
    highp float w = 0.0;
    highp vec2 point = vec2(0.0);

    switch (pointId)
    {
        case 0:
        point = length(pointA) * vec2(cos(angleA), sin(angleA));
        point -= normalize(point).yx * 0.5 * lightLength * vec2(-1.0, 1.0);
        occlusion = vec2(1.0, 1.0);
        break;

        case 1:
        point = length(pointA) * vec2(cos(angleA), sin(angleA));
        w = 1.0;
        occlusion = vec2(0.0, 0.0);
        break;

        case 2:
        point = length(pointA) * vec2(cos(angleA), sin(angleA));
        occlusion = vec2(0.0, 1.0);
        break;

        case 3:
        point = length(pointB) * vec2(cos(angleB), sin(angleB));
        w = 1.0;
        occlusion = vec2(0.0, 0.0);
        break;

        case 4:
        point = length(pointB) * vec2(cos(angleB), sin(angleB));
        occlusion = vec2(0.0, 1.0);
        break;

        case 5:
        point = length(pointB) * vec2(cos(angleB), sin(angleB));
        point += normalize(point).yx * 0.5 * lightLength * vec2(-1.0, 1.0);
        occlusion = vec2(1.0, 1.0);
        break;
    }


    // TODO JUST COMBINE THESE
    float s = sin(lightRot);
    float c = cos(lightRot);
    point = mat2(c, -s, s, c) * point;
    gl_Position = vec4(point, depth, w);
}
