// Coordinates of the two points A & B that make up the line being drawn.
attribute highp vec4 aPos;

// Instanced vertex attributes:
attribute highp vec2 Origin; // Relative position of the current eye/light instance we want to draw the depth for.
attribute highp float Range; // Range/radisus of the current eye/light. Occluders further away than this distance are ignored.

// expands wall edges a little to prevent holes
const highp float DEPTH_LEFTRIGHT_EXPAND_BIAS = 0.001;

// Size of a single light quad (in clip space coordinates)
const highp float MAX_LIGHTS = 12.0;
const highp float SHADOW_SIZE = 2.0/MAX_LIGHTS;

varying highp vec2 UV;

// This function clamps a point to lie within a box centered around the origin.
// If the point is outside of the box, this just clamp the components of the coordinates
highp vec2 SimpleBoxClamp(highp vec2 point)
{
    return clamp(point, vec2(-1.0, -1.0), vec2(1.0, 1.0));
}

void main()
{
    int lineId = gl_VertexID / 5;
    int pointId = gl_VertexID - lineId * 5;

    // Light quads are drawn to the light atlas left to right, top to bottom
    int row = gl_InstanceID/12;
    int column = gl_InstanceID - row * 12;

    // Top-left corner of the current quad in normalized device coordinates.
    highp vec2 topLeft = vec2(-1.0, 1.0) + vec2(column * SHADOW_SIZE, - row * SHADOW_SIZE);
    highp vec2 bottomRight = topLeft + vec2(SHADOW_SIZE, -SHADOW_SIZE);

    vec2 pointA = (aPos.xy - Origin)/Range;
    vec2 pointB = (aPos.zw - Origin)/Range;
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

    // TODO clamp inital line to lie within box
    pointA = aaa;
    pointB = bbb;
    angleA = aaa;
    angleB = bbb;

    // Next we cast a "shadow" by drawing lines from the origin through either pointA or pointB out to the edge of the box.
    // We do this by just extending the line out to some circle that contains the box, and then clamping it to lie on the edge of the box.

    highp vec2 pointAShadow = 2 * vec(cos(angleA), sin(angleA));
    highp vec2 pointBShadow = 2 * vec(cos(angleB), sin(angleB));
    highp float midpointAngle = (angleB + angleA) * 0.5f;
    highp vec2 midpointShadow = 2 * vec(cos(midpointAngle), sin(midpointAngle));

    // Next we clamp the points to lie within our box.
    // for points A & B, we want to ensure that they still lie on the line going from the origin to pointA/B.
    pointAShadow /= max(abs(pointAShadow.x), abs(pointAShadow.y));
    pointBShadow /= max(abs(pointBShadow.x), abs(pointBShadow.y));


    // Finally we need to include all corners of the box that lie between angleA and angleB
    // This can be zero, one, or two corners.


    // Does the line actual intersect a light's box?
    // If it never intersects, we can just clip it.
    bool clip = (pointA.x >= Range && pointB.x >= Range)
                || (pointA.x <= -Range && pointB.x <= -Range)
                || (pointA.y >= Range && pointB.y >= Range)
                || (pointA.y <= -Range && pointB.y <= -Range);

    // For now, just return the coordinates that make up the box
    switch (pointId)
    {
        case 0:
        highp vec2 bottomLeft = topLeft - vec2(0.0, SHADOW_SIZE);
        gl_Position = vec4(bottomLeft, 0.5, 1.0);
        UV = vec2(0.0, 0.0);
        break;

        case 1:
        highp vec2 bottomRight = topLeft + vec2(SHADOW_SIZE, -SHADOW_SIZE);
        gl_Position = vec4(bottomRight, 0.5, 1.0);
        UV = vec2(1.0, 0.0);
        break;

        case 2:
        highp vec2 topRight = topLeft + vec2(SHADOW_SIZE, 0);
        gl_Position = vec4(topRight, 0.5, 1.0);
        UV = vec2(1.0, 1.0);
        break;

        default:
        gl_Position = vec4(topLeft, 0.5, 1.0);
        UV = vec2(0.0, 1.0);
    }
}
