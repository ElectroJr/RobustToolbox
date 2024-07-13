// While this was originally all custom code, I realised I was significantly overcomplicating getting antumbras to work
// after reading some articles by Scott Lembcke, and I've now switched to using their method:
// https://slembcke.github.io/SuperFastSoftShadows
// https://slembcke.github.io/SuperFastHardShadows
//
// It's been modified signifcantly, but still uses some of their code, which can be found on Github  under both the MIT
// and GPL 3+ licenses (https://github.com/slembcke/veridian-expanse). So to be safe:
//
// Copyright (c) 2021 Scott Lembcke and Howling Moon Software
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

// Coordinates of the two points that define the occluder line segment
attribute highp vec4 aOccluderSegment; // (pointA.x, pointA.y, pointB.x, pointB.y)

varying highp vec4 vPenumbraCoords;
// varying highp float vClipEdge;

uniform highp vec3 uLightPosition; // (x position, y position, rotation)
uniform highp vec2 uLightData; // (range, radius)

// expands wall edges a little to prevent holes
const highp float DEPTH_LEFTRIGHT_EXPAND_BIAS = 0.001;

highp mat2 Adjugate(highp mat2 m)
{
    return mat2(m[1][1], -m[0][1], -m[1][0], m[0][0]);
}

void main()
{
    // Unpack uniforms
    highp vec2 lightPos = uLightPosition.xy;
    highp float lightRot = uLightPosition.z;
    highp float lightRange = uLightData.x;
    highp float lightRadius = uLightData.y;

    // Transform into light-relative coordinates, with all distances scaled by the light's range.
    highp vec2 deltaA = (aOccluderSegment.xy - lightPos)/lightRange;
    highp vec2 deltaB = (aOccluderSegment.zw - lightPos)/lightRange;
    lightRadius /= lightRange;

    highp float s = sin(lightRot);
    highp float c = cos(lightRot);
    highp mat2 rot = mat2(c, -s, s, c);
    deltaA = rot * deltaA;
    deltaB = rot * deltaB;

    highp float angleA = atan(deltaA.y, deltaA.x);
    highp float angleB = atan(deltaB.y, deltaB.x);
    highp float angleDelta = angleB - angleA;

    // Check if the line clips over the [Pi, -Pi] range.
    if (angleDelta >= PI)
    {
        angleB -= PI * 2.0;
        angleDelta = angleB - angleA;
    }
    else if (angleDelta <= -PI)
    {
        angleB += PI * 2.0;
        angleDelta = angleB - angleA;
    }

    // Ensure the occluder line segment is always traveling clockwise around the lightsource
    if (angleDelta < 0.0)
    {
        highp vec2 tmp = deltaA;
        deltaA = deltaB;
        deltaB = tmp;
        tmp.x = angleA;
        angleA = angleB;
        angleB = tmp.x;
    }

    // expand angles
    angleA -= DEPTH_LEFTRIGHT_EXPAND_BIAS;
    angleB += DEPTH_LEFTRIGHT_EXPAND_BIAS;
    deltaA = length(deltaA) * vec2(cos(angleA), sin(angleA));
    deltaB = length(deltaB) * vec2(cos(angleB), sin(angleB));

    // Is this vertex associated with start or end of the line segment?
    highp float pointSelector = 0.0;

    // 1 or 0 depending on whether this vertex is for a point that makes up the occluder line segment, or that points
    // shadow. We also use this for perspective division, i.e., we use 0 for pointsthat lie out at infinity.
    highp float shadowSelector = 0.0;

    int pointId = gl_VertexID - (gl_VertexID / 4) * 4;
    switch (pointId)
    {
        case 0:
            shadowSelector = 1.0;
            break;

        case 2:
            shadowSelector = 1.0;
            pointSelector = 1.0;
            break;

        case 3:
            pointSelector = 1.0;
            break;
    }

    // Formula for the line segment a*x + b*y = c
    highp vec3 line = vec3(
        deltaB.y - deltaA.y, // a
        deltaA.x - deltaB.x, // b
        deltaA.x * deltaB.y - deltaA.y * deltaB.x // c
    );

    // How close does the line come to the origin / light source?
    highp float closest = abs(line.z)/sqrt(line.x*line.x + line.y*line.y);

    // We use the above distance to limit the "size" of the light that is used when generating soft shadows
    // This ensures that the occluder never "clips" the light, by effectively shrinking the light for nearby occluders.
    lightRadius = max(0, min(lightRadius, closest - 1e-5));
    // this differs fromh how slembcke handles it.
    // the upside is that IMO it leads less jarring artifacts (or did I just improperly implement their clipping?)
    // the downside is that this means that adjacent occluder segments (i.e., the segments making up an occluder quad).
    // will have different penumbras for the same light, as they may have different light radii.
    // The effect is that some penumbras won't add up to one. However, AFAICT this is not a problem as long
    // as we do not cull back-facing occluders. which I don't want to do anyways to prevent lights that are embeded
    // inside walls from emmiting light.

    // Penumbra offsets.
    // Instead of casting the shadow from the lights origin, we cast the shadow from a slightly offset position.
    highp vec2 offsetA =  vec2(-lightRadius, lightRadius) * normalize(deltaA).yx;
    highp vec2 offsetB = -vec2(-lightRadius, lightRadius) * normalize(deltaB).yx;

    highp vec2 offset = mix(offsetA, offsetB, pointSelector);
    highp vec2 delta = mix(deltaA,  deltaB,  pointSelector);
    highp vec2 point = mix(delta - offset, delta, shadowSelector);

    // Compute penumbra coordinates
    highp vec2 penumbraA = Adjugate(mat2( offsetA, -deltaA))*(delta - mix(offset, deltaA, shadowSelector));
    highp vec2 penumbraB = Adjugate(mat2(-offsetB,  deltaB))*(delta - mix(offset, deltaB, shadowSelector));

    gl_Position = vec4(point, 0.0, shadowSelector);
    vPenumbraCoords = (lightRadius > 0.0) ? vec4(penumbraA, penumbraB) : vec4(0, 1, 0, 1);

    // original clipping prevention
    // highp vec2 normal = deltaB - deltaA;
    // normal = normal.yx*vec2(-1.0, 1.0);
    // vClipEdge = dot(normal, delta - offset)*(1.0 - shadowSelector);
}
