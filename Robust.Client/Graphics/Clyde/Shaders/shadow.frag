varying highp vec2 occlusion;

void main()
{
    // Blend-mode is subtraction. I.e., we always reduce visibility.
    highp float a = occlusion.x/max(0.01,occlusion.y);
    gl_FragColor = vec4(1.0, a, vec2(1.0));
}
