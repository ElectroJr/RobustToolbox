// See comments in the vertex shader.
varying highp vec4 vPenumbraCoords;
// varying highp float vClipEdge;

void main()
{
    vec2 penumbras = smoothstep(-1.0, 1.0, vPenumbraCoords.xz/vPenumbraCoords.yw);
    float penumbra = dot(penumbras, step(vPenumbraCoords.yw, vec2(0.0)));
    penumbra -= 1e-2;
    penumbra = (1.0-penumbra);// * step(vClipEdge, 0.0);
    gl_FragColor = vec4(penumbra);
}
