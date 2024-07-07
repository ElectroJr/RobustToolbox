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
    highp float lightSoftness = 1.0;// LightData.y;

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

    // For drawing the penumbra, we offset the origin / light position when we try to find the "shadows" of points A & B.
    // The actual shape of the penumbra is not fully accurate. instead of treating the light as a ball or some other shape,
    // each line occluder assumes that the light is a parallel line located at the origin with a length equal to the light's softness.
    // However, we limit the "length" of the light source to be at most the length of the occluder. This is mainly just
    // a simplification that leads to decent soft shadows, while avoiding ever having to deal with an antumbra.

    highp float occluderLength = length(pointB - pointA);
    // The above implicitly assumes all sides of an occluder have the same length.
    // If they don't then the two penumbras of each line won't add up to full occlusion.
    highp float lightLength = min(lightSoftness/lightRange, occluderLength);
    highp vec2 offset = vec2(0.0);


    // TODO LIGHTING now that lines are no longer parallel, what do we do if the light length is larger than the
    // (projected) occluder length?
    //
    //
    // TODO LIGHTING penumbra does not appear to nicely go from 0->1 ???

    // When drawing the shadow for each line occluder, which part of the shape should this vertex refer to?
    // 0: The is the "shadow" of point A that makes up the outer part of the penumbra (i.e., barely occluded).
    // 1: This is just point A
    // 2: The is the "shadow" of point A that makes up the inner part of the penumbra (i.e., touching the umbra).
    // 3: This is just point B
    // 4: The is the "shadow" of point B that makes up the inner part of the penumbra (i.e., touching the umbra).
    // 5: The is the "shadow" of point B that makes up the outer part of the penumbra (i.e., barely occluded).
    int pointId = gl_VertexID - (gl_VertexID / 6) * 6;
    switch (pointId)
    {
        case 0:
        offset = pointA/length(pointA) * 0.5 * lightLength;
        offset = vec2(-offset.y, offset.x);
        break;

        case 2:
        offset = pointA/length(pointA) * 0.5 * lightLength;
        offset = vec2(offset.y, -offset.x);
        break;

        case 4:
        offset = pointB/length(pointB) * 0.5 * lightLength;
        offset = vec2(-offset.y, offset.x);
        break;

        case 5:
        offset = pointB/length(pointB) * 0.5 * lightLength;
        offset = vec2(offset.y, -offset.x);
        break;
    }

    pointA -= offset;
    pointB -= offset;
    angleA = atan(pointA.y, pointA.x);
    angleB = atan(pointB.y, pointB.x);
    delta = angleB - angleA;

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

    // expand angles
    float lrSignBias = sign(delta) * DEPTH_LEFTRIGHT_EXPAND_BIAS;
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
    highp float r0 = abs(line.z)/sqrt(line.x*line.x + line.y*line.y);
    highp float t0 = atan(line.y, line.x) + PI*(sign(line.z)-1.0)/2.0;
    highp float angle;

    // For each "shadow" of point a or B, we push the A->B line to lie entirely outside of the quad that will get drawn
    // for this light. We do this by just increasing r0, the point of closest approach in the equation for the line in polar
    // coordiantes. Given that coordiantes are normalized to the light's range, we just add 2.0 to (though sqrt(2) would
    // suffice).
    switch (pointId)
    {
        case 0:
        angle = angleA;
        r0 += 2.0 + length(offset);
        occlusion = vec2(1.0, 1.0);
        break;

        case 1:
        angle = angleA;
        occlusion = vec2(0.0, 0.0);
        break;

        case 2:
        angle = angleA;
        r0 += 2.0 + length(offset);
        occlusion = vec2(0.0, 1.0);
        break;

        case 3:
        angle = angleB;
        occlusion = vec2(0.0, 0.0);
        break;

        case 4:
        angle = angleB;
        r0 += 2.0 + length(offset);
        occlusion = vec2(0.0, 1.0);
        break;

        default:
        angle = angleB;
        r0 += 2.0 + length(offset);
        occlusion = vec2(1.0, 1.0);
        break;
    }
    // This is the fomula for a the line in polar coordinates.
    // We clamp the denominator, as it should never be negative, but due to floating point errors it sometimes is.
    highp float r = r0/clamp(cos(angle - t0),1e-5, 1.0);

    highp vec2 point = r * vec2(cos(angle), sin(angle));;
    point += offset;

    float s = sin(lightRot);
    float c = cos(lightRot);
    point = mat2(c, -s, s, c) * point;

    // finally, we clamp the points so that they lie within the unit box. This ensures that the penumbra edges of adjacent
    // occluders are at the same location, which is needed to ensure that the penumbras add up to full occlusion.
    point /= max(1.0, max(abs(point.x), abs(point.y)));

    // well fuck that fixed it, but now its back to the same problem
    // the two poins on the box make a line that is IN the box.
    // I.e., the "far part" of the shadow isn't outside of the lights range.
    // aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
    //
    //
    // Well
    // I think I just need to do manual culling
    // I wanted a geometry shader anyways.
    //
    //
    //
    // also fuck the box clamping isn't right either
    // for the penumbra interpolation to be accurate the points that make up the far part of a shadow have to be quidistant
    // i.e., form an isosceles  triangle
    // TODO LIGHTING AAAAAAAAAAAAAAAAAA
    // I guess I should just turn it into a proper geometry shader
    // and while I'm at it, handle antumbras

    // actually fuck theres another problem as well
    // we can't just cull "back facing" occluders
    // because the penumbra then allows for light leakage
    // i.e., part of the penumbra should be blocked by a back facing part of the occluder.
    //  AAAAAAAAA
    // fuck
    // but then we also can't  just not cull them because the penumras will overlap.
    // I don't think that that has a solution
    // AAAAAAAA
    // Maybe just give up on this
    // and float the idea of having a shader w/o soft shadows? fucking dammit.
    // but then you can just disable soft shadows yourself.


    gl_Position = vec4(point, depth, 1.0);
}
