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

// Instanced vertex attributes:
attribute vec2 Origin; // Relative position of the current eye/light instance we want to draw the depth for.
attribute float Index; // y-index of the current eye/light instance in the render target
attribute float CullClockwise; // Whether to clip out clockwise or counter-clockwise traveling lines.

// Set of three parameters describing the line currently being drawn via: a*x + b*y = c
// where Line=(a,b,c)
flat varying highp vec3 Line;

// The angle could be inferred from gl_FragCoord, but I am lazy.
varying highp float Angle;


// How to handle lines overlap across -Pi/Pi
uniform float OverlapSide;


// expands wall edges a little to prevent holes
const highp float DEPTH_LEFTRIGHT_EXPAND_BIAS = 0.001;

void main()
{
    // aPos is clockwise, but we need anticlockwise so swap it here
    vec2 pointA = aPos.xy - Origin;
    vec2 pointB = aPos.zw - Origin;
    float angleA = atan(pointA.y, pointA.x);
    float angleB = atan(pointB.y, pointB.x);

    float delta = angleB - angleA;
    float sign = sign(delta);

    // expand bias
    float lrSignBias = sign * DEPTH_LEFTRIGHT_EXPAND_BIAS;
    angleA -= lrSignBias;
    angleB += lrSignBias;

    // If we are running the second pass to handle overlaps, we will cull any lines that don't actually overlap.
    // We do this by just nudging them past the clipping plane
    float clip = OverlapSide;

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

        if (OverlapSide < 0.5)
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
            clip = 0.0;
        }
    }

    vec2 point;
    if (gl_VertexID == (gl_VertexID / 2) * 2)
    {
        // Even numbered vertex -> this is the start of the line
        Angle = angleA;
        point = pointA;
    }
    else
    {
        Angle = angleB;
        point = pointB;
    }

    // In order to get the distance to the occluder in the fragment shader, we cannot simply linearly interpolate the,
    // the distance is not a linear function of the angle.
    //
    // However, we can just re-write the line in polar coordinates.
    // Specifically, for a line given by: a*x + b*y = c
    // The definition in polar coordiantes is just r = c / (a * cos(angle) + b * sin(angle)).
    // Hence we just pass the definition of a,b,c to the fragment shader.

    // Line = (a,b,c)
    Line = vec3(
        pointB.y - pointA.y,
        pointA.x - pointB.x,
        pointA.x * pointB.y - pointA.y * pointB.x
    );

    // Next we use the z/depth coordinate to perform a kind of face culling.
    // If the angle is decreasing, then this line is on the rear side of the occluder. So we simply move it out beyond
    // the clipping plane to cull it. This behaviour can be controlled with the cullClockwise uniform, which should be
    // either -1 or +1;
    highp float depth = 1.0 - CullClockwise * sign / (length(point) + 1.0);

    if (clip > 0.5)
        depth = 2.0;

    // Note that for exactly the same reason that we cannot linearly interpolate distances, linearly interpolating the
    // depth value will also give incorrect results. However, I am about 70% sure that linear interpolating the distance
    // (or depth) as a function of the angle will never result in a shorter distance. So the depth testing shouldn't
    // ever accidentally be occluding lines that it shouldn't be. But it is still useful for getting rid of many lines.

    gl_Position = vec4(Angle / PI, mix(-1.0, 1.0, Index), depth, 1.0);
}
