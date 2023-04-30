varying highp vec2 UV;
varying highp vec2 Pos;
varying highp vec4 VtxModulate;

uniform sampler2D lightMap;
uniform int enableLight;

// [SHADER_HEADER_CODE]

void main()
{
    highp vec4 FRAGCOORD = gl_FragCoord;

    lowp vec4 COLOR;

    // [SHADER_CODE]

    if (enableLight == 0)
    {
        gl_FragColor = zAdjustResult(COLOR * VtxModulate);
    }
    else
    {
        lowp vec3 lightSample = texture2D(lightMap, Pos).rgb;
        gl_FragColor = zAdjustResult(COLOR * VtxModulate * vec4(lightSample, 1.0));
    }
}
