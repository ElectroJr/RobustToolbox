varying highp float dist;

// If float textures are supported, puts the values in the R/G fields.
// This assumes RG32F format.
// If float textures are NOT supported.
// This assumes RGBA8 format.
// Operational range is "whatever works for FOV depth"
highp vec4 zClydeShadowDepthPack(highp vec2 val) {
    #ifdef HAS_FLOAT_TEXTURES
    return vec4(val, 0.0, 1.0);
    #else
    highp vec2 valH = floor(val);
    return vec4(valH / 255.0, val - valH);
    #endif
}

void main()
{
    // Main body.
#ifdef HAS_DFDX
    highp float dx = dFdx(dist);
#else
    highp float dx = 1.0;
    highp float dy = 1.0;
#endif
    gl_FragColor = zClydeShadowDepthPack(vec2(dist, dist * dist + 0.25 * dx*dx));
}
