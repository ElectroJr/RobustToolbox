
// Three floats that define the line via a*x + b*y = c, where Line=(a,b,c).
// Equivalent line in polar coordintes is given by r = c / (a * cos(angle) + b * sin(angle))
flat varying highp vec3 Line;
varying highp float Angle;

void main()
{
    highp float dist = Line.z/(Line.x*cos(Angle) + Line.y*sin(Angle));

    // The shadow depth stores both the distance & it's square value for an (attempt?) to variance shadow maps.
    // We also bias the variance using the derivative WRT the angle.
    //
    // TBH I'm not even sure if we should still be using this or if its important.
    // I think the soft light stuff is just an ensemble of things that were tried that ended up getting nice looking
    // results.
#ifdef HAS_DFDX
    highp float dx = dFdx(dist);
#else
    highp float dx = 1.0;
    highp float dy = 1.0;
#endif
    gl_FragColor = zClydeShadowDepthPack(vec2(dist, dist * dist + 0.25 * dx*dx));
}
