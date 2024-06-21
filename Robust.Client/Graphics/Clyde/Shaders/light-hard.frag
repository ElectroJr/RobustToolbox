// Inserted into light-shared.frag
highp float createOcclusion(highp float ourDist)
{
    highp float lightIndex = LightData.w;
    highp vec2 occlDist = occludeDepth(DeltaWorldPos, shadowMap, lightIndex);
    return smoothstep(0.1, 1.0, ChebyshevUpperBound(occlDist, ourDist));
}
