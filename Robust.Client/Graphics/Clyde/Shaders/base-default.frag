varying highp vec2 UV;
varying highp vec2 Pos;
varying highp vec4 VtxModulate;

uniform sampler2D lightMap;

// [SHADER_HEADER_CODE]

void main()
{
    highp vec4 FRAGCOORD = gl_FragCoord;

    lowp vec4 COLOR;

    // [SHADER_CODE]

    if (VtxModulate.x < -1)
    {
        // Negative modulation implies unshaded/no lighting.
        // Faster than swapping textures and easier than swapping batches in clyde.
        // 3.0 is arbitrary and matches the 
        gl_FragColor = zAdjustResult(COLOR * (3.0 +  VtxModulate));
    }
    else
    {
        lowp vec3 lightSample = texture2D(lightMap, Pos).rgb;
        gl_FragColor = zAdjustResult(COLOR * VtxModulate * vec4(lightSample, 1.0));
    }
}
