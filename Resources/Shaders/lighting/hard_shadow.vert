// See soft_shadow for comments on attributes/uniforms
attribute highp vec4 aOccluderSegment;
uniform highp vec4 uLightData;
const highp float EXPAND_BIAS = 0.01;

void main()
{
    // TODO LIGHTING de-duplicate with soft shadow shader

    highp mat3 view = viewMatrix;
    view[2].xyz = vec3(0.0);
    highp vec2 lightPos = uLightData.xy;
    highp float lightRange = uLightData.z;
    highp float lightRadius = uLightData.w;
    highp vec2 pointA = aOccluderSegment.xy;
    highp vec2 pointB = aOccluderSegment.zw;

    highp vec2 bias = EXPAND_BIAS * normalize(pointA - pointB);
    pointA += bias;
    pointB -= bias;

    highp vec2 deltaA = pointA - lightPos;
    highp vec2 deltaB = pointB - lightPos;

    float cross = deltaA.x * deltaB.y - deltaA.y * deltaB.x;
    if (cross < 0.0)
    {
        highp vec2 tmp = deltaA;
        deltaA = deltaB;
        deltaB = tmp;
        tmp = pointA;
        pointA = pointB;
        pointB = tmp;
    }

    highp float pointSelector = 0.0;
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

    highp vec2 delta = mix(deltaA,  deltaB,  pointSelector);
    highp vec2 point = mix(pointA,  pointB,  pointSelector);
    highp vec2 position = mix(delta, point, shadowSelector);
    highp vec3 transformedPosition = projectionMatrix * view * vec3(position, shadowSelector);
    gl_Position = vec4(transformedPosition.xy, 0.0, shadowSelector);
}
