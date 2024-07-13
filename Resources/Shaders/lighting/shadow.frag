// See comments in the vertex shader.
varying highp vec4 vPenumbraCoords;

void main()
{
    vec2 penumbras = smoothstep(-1.0, 1.0, vPenumbraCoords.xz/vPenumbraCoords.yw);
    float penumbra = dot(penumbras, step(vPenumbraCoords.yw, vec2(0.0)));
    //penumbra -= 1e-3;
    gl_FragColor = vec4(1.0 - penumbra);
}
