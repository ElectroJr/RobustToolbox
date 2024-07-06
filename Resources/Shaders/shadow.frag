varying highp vec2 occlusion;

flat varying highp vec3 Color;

void main()
{
    // Blend-mode is subtraction. I.e., we always reduce visibility by some amount
    highp float occluded = 1.0 - occlusion.x/max(0.01,occlusion.y);
    gl_FragColor = vec4(Color, 1.0);
}
