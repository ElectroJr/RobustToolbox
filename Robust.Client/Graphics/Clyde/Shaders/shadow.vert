// Coordinates of the two points A & B that make up the line being drawn.
attribute vec4 aPos;

// Instanced vertex attributes:
attribute vec2 Origin; // Relative position of the current eye/light instance we want to draw the depth for.

// expands wall edges a little to prevent holes
const highp float DEPTH_LEFTRIGHT_EXPAND_BIAS = 0.001;

// Size of a single light quad (in clip space coordinates)
const highp float MAX_LIGHTS = 12.0;
const highp float SHADOW_SIZE = 2.0/MAX_LIGHTS;

varying highp vec2 UV;

void main()
{
    int lineId = gl_VertexID / 5;
    int pointId = gl_VertexID - lineId * 5;

    // Light quads are drawn to the light atlas left to right, top to bottom
    int row = gl_InstanceID/12;
    int column = gl_InstanceID - row * 12;

    // Top-left corner of the current quad in normalized device coordinates.
    highp vec2 topLeft = vec2(-1.0, 1.0) + vec2(column * SHADOW_SIZE, - row * SHADOW_SIZE);

    // For now, just return the coordinates that make up the box
    switch (pointId)
    {
        case 0:
        highp vec2 bottomLeft = topLeft - vec2(0.0, SHADOW_SIZE);
        gl_Position = vec4(bottomLeft, 0.5, 1.0);
        UV = vec2(0.0, 0.0);
        break;

        case 1:
        highp vec2 bottomRight = topLeft + vec2(SHADOW_SIZE, -SHADOW_SIZE);
        gl_Position = vec4(bottomRight, 0.5, 1.0);
        UV = vec2(1.0, 0.0);
        break;

        case 2:
        highp vec2 topRight = topLeft + vec2(SHADOW_SIZE, 0);
        gl_Position = vec4(topRight, 0.5, 1.0);
        UV = vec2(1.0, 1.0);
        break;

        default:
        gl_Position = vec4(topLeft, 0.5, 1.0);
        UV = vec2(0.0, 1.0);
    }
}
