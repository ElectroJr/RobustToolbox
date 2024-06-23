// Polar-coordinate mapper.
// While inspired by https://www.gamasutra.com/blogs/RobWare/20180226/313491/Fast_2D_shadows_in_Unity_using_1D_shadow_mapping.php ,
//  has one major difference:
// The assumption here is that the shadow sampling must be reduced.
// The original cardinal-direction mapper as written by PJB used 4 separate views.
// As such, it's still an increase in performance to only render 2 views.
// And as such, a line can be split across the 2 views.

// The shader has been significantly modified since then
// So the above comments might be out of date.
// The URL doesn't even work anymore.

// Coordinates of the two points A & B that make up the line being drawn.
attribute vec4 aPos;

// Distance to the line being drawn.
varying float dist;

uniform vec2 origin;
uniform float index;
uniform float shadowOverlapSide;

// expands wall edges a little to prevent holes
const highp float DEPTH_LEFTRIGHT_EXPAND_BIAS = 0.001;

void main()
{
    // aPos is clockwise, but we need anticlockwise so swap it here
    vec2 pointA = aPos.xy - origin;
    vec2 pointB = aPos.zw - origin;
    float angleA = atan(pointA.y, pointA.x);
    float angleB = atan(pointB.y, pointB.x);

    float delta = angleB - angleA;
    float sign = sign(delta);

    // expand bias
    float lrSignBias = sign * DEPTH_LEFTRIGHT_EXPAND_BIAS;
    angleA -= lrSignBias;
    angleB += lrSignBias;

    // TODO LIGHTING
    // on pass 2, just discard any non-clipping lines

    // We need to reliably detect a clip, as opposed to, say, a backdrawn face.
    // So a clip is when the angular area is >= 180 degrees (which is not possible with a quad and always occurs when wrapping)
    if (abs(delta) >= PI)
    {
        // Oh no! It clipped...

        // If such that xA is on the right side and xB is on the left:
        //  Pass 1: Adjust left boundary past left edge
        //  Pass 2: Adjust right boundary past right edge

        // If such that xA is on the left side and xB is on the right!
        //  Pass 1: Adjust left boundary past right edge
        //  Pass 2: Adjust right boundary past left edge

        if (shadowOverlapSide < 0.5)
        {
            // ...and we're adjusting the left edge...
            angleA += sign * PI * 2.0;
            sign = - sign;
        }
        else
        {
            // ...and we're adjusting the right edge...
            angleB -= sign * PI * 2.0;
            sign = - sign;
        }
    }

    float angle;
    vec2 point;
    if (gl_VertexID == (gl_VertexID / 2) * 2)
    {
        // Even numbered vertex -> this is the start of the line
        angle = angleA;
        point = pointA;
    }
    else
    {
        angle = angleB;
        point = pointB;
    }

    dist = length(point);

    // We use sign here to perform back-face culling. I.e., if the angle is decreasing, this line is on the rear side
    // of the occluder. So we simply move it out beyond the clipping plane to cull it.
    float depth = 1.0 - sign / (dist + 1.0);

    gl_Position = vec4(angle / PI, mix(-1.0, 1.0, index), depth, 1.0);
}
