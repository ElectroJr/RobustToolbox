varying highp vec2 occlusion;

void main()
{
    // Blend-mode is subtraction. I.e., we always reduce visibility by some amount
    highp float occluded = 1.0 - occlusion.x/max(0.01,occlusion.y);
    occluded = clamp(occluded, 0.0, 1.0);
    gl_FragColor = vec4(occluded);
}
