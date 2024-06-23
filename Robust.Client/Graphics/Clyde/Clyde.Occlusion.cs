using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Graphics;
using Robust.Shared.Utility;
using SysVec4 = System.Numerics.Vector4;
using static Robust.Shared.GameObjects.OccluderComponent;

namespace Robust.Client.Graphics.Clyde;
// This file handles everything about light rendering.
// That includes shadow casting and also FOV.
// A detailed explanation of how all this works can be found here:
// https://docs.spacestation14.io/en/engine/lighting-fov

internal partial class Clyde
{
    // Horizontal width, in pixels, of the shadow maps used to render regular lights.
    private const int ShadowMapSize = 512;

    // Horizontal width, in pixels, of the shadow maps used to render FOV.
    // I figured this was more accuracy sensitive than lights so resolution is significantly higher.
    private const int FovMapSize = 2048;

    private ClydeShaderInstance _fovDebugShaderInstance = default!;

    // Shader program used to calculate depth for shadows/FOV.
    // Sadly not .swsl since it has a different vertex format and such.
    private GLShaderProgram _depthProgram = default!;

    // Occlusion geometry used to render shadows and FOV.
    // Each Vector2 vertex is the position of the start or end of a line occluder. The position is in world
    // coordinates, but shifted such that the current eye is at (0,0) to avoid floating point errors.
    // Currently the occlusion and depth draw code assumes that there are always 4 lines per occluder.
    private int _occluderCount;
    private ComponentTreeEntry<OccluderComponent>[] _occluders = default!;

    // Rendering data for drawing the light depth map (i.e., distance to nearest occluder).
    private int _lightOcclusionVertexCount;
    private SysVec4[] _lightOcclusionBuffer = default!;
    private DepthDrawInstance[] _lightInstancesBuffer = default!;
    private GLBuffer _lightOcclusionVbo = default!;
    private GLBuffer _lightInstanceVbo = default!;
    private GLHandle _lightOcclusionVao;

    // Rendering data for drawing the fov depth map. This is a variant of the light data that allows the FOV
    // to see into the first layer of walls.
    private int _fovOcclusionVertexCount;
    private SysVec4[] _fovOcclusionBuffer = default!;
    private GLBuffer _fovOcclusionVbo = default!;
    private GLBuffer _fovInstanceVbo = default!;
    private GLHandle _fovOcclusionVao;

    // Rendering data for drawing the FOV mask when bleeding lights onto walls.
    private int _occlusionMaskIndex;
    private ushort[] _occlusionMaskIndices = default!;
    private Vector2[] _occlusionMaskBuffer = default!;
    private GLBuffer _occlusionMaskVbo = default!;
    private GLBuffer _occlusionMaskEbo = default!;
    private GLHandle _occlusionMaskVao;

    private unsafe void InitOcclusion()
    {
        var depthVert = ReadEmbeddedShader("shadow-depth.vert");
        var depthFrag = ReadEmbeddedShader("shadow-depth.frag");

        (string, uint)[] attribLocations =
        {
            ("aPos", 0),
            ("Origin", 1),
            ("Index", 2),
            ("CullClockwise", 3)
        };

        _depthProgram = _compileProgram(depthVert, depthFrag, attribLocations, "Occlusion Depth Program");

        var debugShader = _resourceCache.GetResource<ShaderSourceResource>("/Shaders/Internal/depth-debug.swsl");
        _fovDebugShaderInstance = (ClydeShaderInstance)InstanceShader(debugShader);

        // Set up VAO for drawing occluder depths for lights.
        {
            _lightOcclusionVao = new GLHandle(GenVertexArray());
            BindVertexArray(_lightOcclusionVao.Handle);
            CheckGlError();
            ObjectLabelMaybe(ObjectLabelIdentifier.VertexArray, _lightOcclusionVao, nameof(_lightOcclusionVao));

            _lightOcclusionVbo = new GLBuffer(this,
                BufferTarget.ArrayBuffer,
                BufferUsageHint.DynamicDraw,
                nameof(_lightOcclusionVbo));

            // Line positions
            GL.VertexAttribPointer(0, 4, VertexAttribPointerType.Float, false, sizeof(SysVec4), IntPtr.Zero);
            GL.EnableVertexAttribArray(0);

            _lightInstanceVbo = new GLBuffer(this,
                BufferTarget.ArrayBuffer,
                BufferUsageHint.DynamicDraw,
                nameof(_lightInstanceVbo));

            // Light instance position
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, sizeof(DepthDrawInstance), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribDivisor(1, 1);

            // Light instance index
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, sizeof(DepthDrawInstance), 2 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribDivisor(2, 1);

            // Instance Culling orientation
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, sizeof(DepthDrawInstance), 3 * sizeof(float));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribDivisor(3, 1);

            CheckGlError();
        }

        // Set up VAO for drawing occluder depths for FOV.
        {
            _fovOcclusionVao = new GLHandle(GenVertexArray());
            BindVertexArray(_fovOcclusionVao.Handle);
            CheckGlError();
            ObjectLabelMaybe(ObjectLabelIdentifier.VertexArray, _fovOcclusionVao, nameof(_fovOcclusionVao));

            _fovOcclusionVbo = new GLBuffer(this,
                BufferTarget.ArrayBuffer,
                BufferUsageHint.DynamicDraw,
                nameof(_fovOcclusionVbo));

            GL.VertexAttribPointer(0, 4, VertexAttribPointerType.Float, false, sizeof(SysVec4), IntPtr.Zero);
            GL.EnableVertexAttribArray(0);
            CheckGlError();

            _fovInstanceVbo = new GLBuffer(this,
                BufferTarget.ArrayBuffer,
                BufferUsageHint.StaticDraw,
                nameof(_fovInstanceVbo));

            // FOV rendering always has two instances, one of which culls back faces while the other culls front faces.
            // For details about the front-face culling, see comments in UpdateOcclusionGeometry()
            Span<DepthDrawInstance> instances =
                [
                    new(default, ImageIndexToV(0,2), 1),
                    new(default, ImageIndexToV(1,2), -1)
                ];
            _fovInstanceVbo.Reallocate(instances);

            // Light instance position
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, sizeof(DepthDrawInstance), IntPtr.Zero);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribDivisor(1, 1);

            // Light instance index
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, sizeof(DepthDrawInstance), 2 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribDivisor(2, 1);

            // Instance Culling orientation
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, sizeof(DepthDrawInstance), 3 * sizeof(float));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribDivisor(3, 1);
        }

        // Set up VAO for drawing occluder mask.
        {
            _occlusionMaskVao = new GLHandle(GenVertexArray());
            BindVertexArray(_occlusionMaskVao.Handle);
            CheckGlError();
            ObjectLabelMaybe(ObjectLabelIdentifier.VertexArray, _occlusionMaskVao, nameof(_occlusionMaskVao));

            _occlusionMaskVbo = new GLBuffer(this,
                BufferTarget.ArrayBuffer,
                BufferUsageHint.DynamicDraw,
                nameof(_occlusionMaskVbo));

            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, sizeof(Vector2), IntPtr.Zero);
            GL.EnableVertexAttribArray(0);
            CheckGlError();

            // TODO LIGHTING Maybe use static instead of BufferUsageHint.DynamicDraw
            // But apparently it doesnt make much of a difference and is just a hint?
            // And im scared of hidden footguns when using static indices into a dynamic draw buffer?
            _occlusionMaskEbo = new GLBuffer(this,
                BufferTarget.ElementArrayBuffer,
                BufferUsageHint.DynamicDraw,
                nameof(_occlusionMaskEbo));
        }

        _cfg.OnValueChanged(CVars.MaxOccluderCount, MaxOccludersChanged, true);
    }

    private void DrawFov(IEye eye)
    {
        using var _ = DebugGroup(nameof(DrawFov));
        using var __ = _prof.Group(nameof(DrawFov));

        // Bind & clear the FOV depth even if we do not draw with it.
        PrepareDepthTarget(RtToLoaded(_fovRenderTarget));

        if (!eye.DrawFov)
            return;

        BindVertexArray(_fovOcclusionVao.Handle);
        DrawOcclusionDepth(_fovOcclusionVertexCount, 2);
    }

    private void DrawShadowDepths()
    {
        if (!_lightManager.DrawShadows)
            return;

        using var _ = DebugGroup(nameof(DrawShadowDepths));
        using var __ = _prof.Group(nameof(DrawShadowDepths));

        PrepareDepthTarget(RtToLoaded(_shadowRenderTarget));
        BindVertexArray(_lightOcclusionVao.Handle);
        DrawOcclusionDepth(_lightOcclusionVertexCount, _shadowCastingLightCount);
    }

    /// <summary>
    /// Convert an image y-coordinate to the UV coordinates. Useful for shaders that should index a specific row in
    /// an image.
    /// </summary>
    public static float ImageIndexToV(int index, int height) => (index + 0.5f) / height;

    /// <summary>
    ///     Draws depths for lighting & FOV into the currently bound framebuffer.
    /// </summary>
    /// <param name="lineCount">Total number of line vertices</param>
    /// <param name="instanceCount">Total number of instances (eyes or lights) to draw</param>
    private void DrawOcclusionDepth(int lineCount, int instanceCount)
    {
        // Make two draw calls. This allows a faked "generation" of additional polygons.
        _depthProgram.SetUniform("OverlapSide", 0.0f);
        GL.DrawArraysInstanced(PrimitiveType.Lines, 0, lineCount, instanceCount);
        CheckGlError();
        _debugStats.LastGLDrawCalls += 1;

        // Yup, it's the other draw call.
        _depthProgram.SetUniform("OverlapSide", 1.0f);

        GL.DrawArraysInstanced(PrimitiveType.Lines, 0, lineCount, instanceCount);
        CheckGlError();
        _debugStats.LastGLDrawCalls += 1;
    }

    /// <summary>
    /// Bind and clear the target for a depth/fov draw.
    /// </summary>
    private void PrepareDepthTarget(LoadedRenderTarget target)
    {
        const float arbitraryDistanceMax = 1234;
        BindRenderTargetImmediate(target);
        CheckGlError();

        GL.Viewport(0, 0, target.Size.X, target.Size.Y);
        CheckGlError();

        GL.ClearDepth(1);
        CheckGlError();
        if (_hasGLFloatFramebuffers)
            GL.ClearColor(arbitraryDistanceMax, arbitraryDistanceMax * arbitraryDistanceMax, 0, 1);
        else
            GL.ClearColor(1, 1, 1, 1);

        CheckGlError();
        GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.ColorBufferBit);
        CheckGlError();
    }

    /// <summary>
    /// Configure <see cref="GL"/> properties and shaders for drawing occluder depths.
    /// </summary>
    private void PrepareDepthDraw()
    {
        GL.Disable(EnableCap.Blend);
        CheckGlError();
        GL.Enable(EnableCap.DepthTest);
        CheckGlError();
        GL.DepthFunc(DepthFunction.Lequal);
        CheckGlError();
        GL.DepthMask(true);
        CheckGlError();

        _depthProgram.Use();
        SetupGlobalUniformsImmediate(_depthProgram, null);
    }

    /// <summary>
    /// Reset the properties configured in <see cref="PrepareDepthDraw"/>.
    /// </summary>
    private void FinalizeDepthDraw()
    {
        GL.DepthMask(false);
        CheckGlError();
        GL.Disable(EnableCap.DepthTest);
        CheckGlError();
        GL.Enable(EnableCap.Blend);
        CheckGlError();
    }

    // TODO LIGHTING
    // Lazy occlusion updating.
    // I.e., cache occlusion geometry per occlusion tree.
    // Then draw each tree with its own transformation applied in the vertex shader
    private void UpdateOcclusionGeometry(Box2 expandedBounds, IEye eye)
    {
        using var _ = _prof.Group("UpdateOcclusionGeometry");
        using var __ = DebugGroup(nameof(UpdateOcclusionGeometry));

        _occluderCount = 0;
        _lightOcclusionVertexCount = 0;
        _fovOcclusionVertexCount = 0;
        _occlusionMaskIndex = 0;

        var eyePos = eye.Position.Position;

        var xformSystem = _entitySystemManager.GetEntitySystem<TransformSystem>();
        FindOccluders(expandedBounds, eye, xformSystem);

        // TODO LIGHTING parallelize.
        // Specifically:
        // - getting world position & rotation
        // - computing corner visibility

        foreach (var occluder in _occluders.AsSpan()[.._occluderCount])
        {
            DebugTools.Assert(occluder.Component.Enabled);

            // TODO LIGHTING
            // This can be optimized if we assume occluders are always directly parented to a grid or map
            // And AFAIK the occluder tree currently requires that (as it does not have a recursive move event subscription).
            var (pos, rot) = xformSystem.GetWorldPositionRotation(occluder.Transform);

            pos -= eyePos;
            var box = occluder.Component.BoundingBox;

            // TODO LIGHTING SIMD
            // Even though Matrix3x2 Uses some simd, it is probably faster directly SIMD these 4 transformations
            var worldTransform = Matrix3Helpers.CreateTransform(pos, rot);
            var tl = Vector2.Transform(box.TopLeft, worldTransform);
            var tr = Vector2.Transform(box.TopRight, worldTransform);
            var br = Vector2.Transform(box.BottomRight, worldTransform);
            var bl = tl + br - tr;

            // First, we just add all occluder corners to the mask buffer
            _occlusionMaskBuffer[_occlusionMaskIndex + 0] = tl;
            _occlusionMaskBuffer[_occlusionMaskIndex + 1] = tr;
            _occlusionMaskBuffer[_occlusionMaskIndex + 2] = br;
            _occlusionMaskBuffer[_occlusionMaskIndex + 3] = bl;
            _occlusionMaskIndex += 4;

            // Populating the light & fov buffers is a bit more complicated.
            // Buckle up.

            // Imagine we have a grid of walls with an eye centered at x:
            // >            x
            // > ╔═╦═╦═╦═╗
            // > ╠═╬═╬═╬═╣
            // > ╠═╬═╬═╬═╣
            // > ╚═╩═╩═╩═╝
            //
            // For lighting, we just want to stop any lights from entering a wall. This is easy enough, we can just
            // drop all "internal" lines that are touching other occluders, leaving us with this (thin occluders
            // are not drawn to the depth map):
            // >            x
            // > ╔═╤═╤═╤═╗
            // > ╟─┼─┼─┼─╢
            // > ╟─┼─┼─┼─╢
            // > ╚═╧═╧═╧═╝
            //
            // We can then also cull any "back faces" in the vertex shader, leaving us with just:
            // >            x
            // > ╒═╤═╤═╤═╗
            // > ├─┼─┼─┼─╢
            // > ├─┼─┼─┼─╢
            // > └─┴─┴─┴─╜
            //
            // Note that this culling is completely optional for lights, it just helps reduce the number of lines
            // we need to draw. For FOV, one of the draw calls will behave in the same way as lights. However, we
            // also want to have a separate FOV pass that let us see into the first layer of the walls but blockes
            // everything else behind them. For this FOV pass, the end result we want should look like this:
            // >            x
            // > ╓─┬─┬─┬─┐
            // > ╠═╪═╪═╬─┤
            // > ├─┼─┼─╫─┤
            // > └─┴─┴─╩═╛
            //
            // As we now actually need to draw the interior lines, we can't just drop them all like we did for
            // lights. We can get partway to our desired result by just culling all "front faces" in the vertex
            // shader, leaving us with:
            // >            x
            // > ╓─╥─╥─╥─┐
            // > ╠═╬═╬═╬═╡
            // > ╠═╬═╬═╬═╡
            // > ╚═╩═╩═╩═╛
            //
            // If we then also drop any internal lines that connect to front-facing external points, this leaves
            // us with:
            // >            x
            // > ╓─┬─┬─┬─┐
            // > ╠═╬═╬═╬─┤
            // > ╠═╬═╬═╬─┤
            // > ╚═╩═╩═╩═╛
            //
            // While this contains many more lines that we actually need for drawing the second FOV pass, it gets
            // the job done. I don't know if it matters much, but as we only need to draw FOV once, while light maps
            // get drawn many times, I want to minimize the number of unnecessary occlusion lines that get used by
            // the light depth drawing. Hence the FOV & light depths will use two different buffers.

            // find which directions have adjacent occluders, so that we can neglect some internal lines
            // Note that the cardinal directions here are defined relative to the occluder entity, not how
            // the occluder is drawn on the screen. Similarly, the "top left" vector is just the corner that the
            // occluder identifies as it's top left corner,
            var neighbours = occluder.Component.Occluding;
            var northBlocked = (neighbours & OccluderDir.North) != 0;
            var eastBlocked = (neighbours & OccluderDir.East) != 0;
            var southBlocked = (neighbours & OccluderDir.South) != 0;
            var westBlocked = (neighbours & OccluderDir.West) != 0;

            // When drawing a line from a to b, is the line going clockwise or counterclockwise around the origin?
            // We draw occluders in a clockwise direction from the occluders centre. I.e. tr -> tl -> bl -> br -> tr
            // So from the eye's POV, the lines going anti-clockwise are the parts facing towards the eye.
            static bool IsFrontFacing(Vector2 a, Vector2 b)
            {
                return a.X * b.Y > a.Y * b.X;
            }

            // TODO LIGHTING SIMD?
            var northFaceVisible = ((!northBlocked) && IsFrontFacing(tl, tr));
            var southFaceVisible = ((!southBlocked) && IsFrontFacing(br, bl));
            var eastFaceVisible = ((!eastBlocked) && IsFrontFacing(tr, br));
            var westFaceVisible = ((!westBlocked) && IsFrontFacing(bl, tl));

            var eastOrWestVisible = westFaceVisible || eastFaceVisible;
            var northOrSouthVisible = northFaceVisible || southFaceVisible;

            // For each unblocked direction, we draw one occluder line for both the the light depth and FOV buffer.
            // Additionally, we also draw blocked directions to the FOV buffer if it is not connected to any visible
            // corners.

            if (!northBlocked)
            {
                var vec = new SysVec4(tl, tr.X, tr.Y);
                WriteLightBuffer(vec);
                WriteFovBuffer(vec);
            }
            else if (!eastOrWestVisible)
            {
                var vec = new SysVec4(tl, tr.X, tr.Y);
                WriteFovBuffer(vec);
            }

            if (!eastBlocked)
            {
                var vec = new SysVec4(tr, br.X, br.Y);
                WriteLightBuffer(vec);
                WriteFovBuffer(vec);
            }
            else if (!northOrSouthVisible)
            {
                var vec = new SysVec4(tr, br.X, br.Y);
                WriteFovBuffer(vec);
            }

            if (!southBlocked)
            {
                var vec = new SysVec4(br, bl.X, bl.Y);
                WriteLightBuffer(vec);
                WriteFovBuffer(vec);
            }
            else if (!eastOrWestVisible)
            {
                var vec = new SysVec4(br, bl.X, bl.Y);
                WriteFovBuffer(vec);
            }

            if (!westBlocked)
            {
                var vec = new SysVec4(bl, tl.X, tl.Y);
                WriteLightBuffer(vec);
                WriteFovBuffer(vec);
            }
            else if (!northOrSouthVisible)
            {
                var vec = new SysVec4(bl, tl.X, tl.Y);
                WriteFovBuffer(vec);
            }

            // TODO LIGHTING
            // Currently the above code uses two float/position buffers. But is it maybe better to have one float
            // buffer, and two index/element buffers with indexed rendering?
            // On the cpu side, we have to populate & send an extra array, so maybe its slower?
        }

        Array.Clear(_occluders);
        DebugTools.AssertEqual(_occlusionMaskIndex, _occluderCount * 4);
        DebugTools.Assert(_lightOcclusionVertexCount <= _fovOcclusionVertexCount);

        // Upload geometry to OpenGL.
        GL.BindVertexArray(0);
        CheckGlError();

        _occlusionMaskVbo.Reallocate(_occlusionMaskBuffer.AsSpan(0, _occlusionMaskIndex));
        _lightOcclusionVbo.Reallocate(_lightOcclusionBuffer.AsSpan(0, _lightOcclusionVertexCount));
        _lightInstanceVbo.Reallocate(_lightInstancesBuffer.AsSpan(0, _shadowCastingLightCount));
        _fovOcclusionVbo.Reallocate(_fovOcclusionBuffer.AsSpan(0, _fovOcclusionVertexCount));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteLightBuffer(SysVec4 vec)
    {
        _lightOcclusionBuffer[_lightOcclusionVertexCount + 0] = vec;
        _lightOcclusionBuffer[_lightOcclusionVertexCount + 1] = vec;
        _lightOcclusionVertexCount += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteFovBuffer(SysVec4 vec)
    {
        _fovOcclusionBuffer[_fovOcclusionVertexCount + 0] = vec;
        _fovOcclusionBuffer[_fovOcclusionVertexCount + 1] = vec;
        _fovOcclusionVertexCount += 2;
    }

    /// <summary>
    /// Query the occluder tree and populate the <see cref="_occluders"/> array.
    /// </summary>
    private void FindOccluders(Box2 bounds, IEye eye, TransformSystem xformSystem)
    {
        var occluderSystem = _entitySystemManager.GetEntitySystem<OccluderSystem>();
        Array.Resize(ref _occluders, _maxOccluders);
        (ComponentTreeEntry<OccluderComponent>[] Results, int Count) state = (_occluders, 0);

        foreach (var (uid, comp) in occluderSystem.GetIntersectingTrees(eye.Position.MapId, bounds))
        {
            var treeBounds = xformSystem.GetInvWorldMatrix(uid).TransformBox(bounds);
            comp.Tree.QueryAabb(ref state,static (ref (ComponentTreeEntry<OccluderComponent>[] Results, int Count) state, in ComponentTreeEntry<OccluderComponent> entry) =>
                {
                    state.Results[state.Count++] = entry;
                    return state.Count < state.Results.Length;
                },
                treeBounds,
                true);
        }

        _occluderCount = state.Count;
        _debugStats.Occluders += _occluderCount;
    }

    private void MaxOccludersChanged(int value)
    {
        _maxOccluders = Math.Clamp(value, 1024, 8192);

        GL.BindVertexArray(0);
        CheckGlError();

        // Each occluder has four corners
        Array.Resize(ref _occlusionMaskBuffer, _maxOccluders * 4);

        // And each occluder makes up 8 lines
        Array.Resize(ref _fovOcclusionBuffer, _maxOccluders * 8);
        Array.Resize(ref _lightOcclusionBuffer, _maxOccluders * 8);
        Array.Resize(ref _occlusionMaskIndices, _maxOccluders * GetQuadBatchIndexCount());

        // Instead of updating occluder indices every frame, we just update them once. The indices are always the
        // same anyways, and _maxOccluders ensures that the buffers don't need to be ridiculously long.
        var index = 0;
        ushort vertex = 0;
        for (var i = 0; i < _maxOccluders; i++)
        {
            if (_hasGLPrimitiveRestart)
            {
                _occlusionMaskIndices[index++] = (ushort)(vertex + 0);
                _occlusionMaskIndices[index++] = (ushort)(vertex + 1);
                _occlusionMaskIndices[index++] = (ushort)(vertex + 2);
                _occlusionMaskIndices[index++] = (ushort)(vertex + 3);
                _occlusionMaskIndices[index++] = PrimitiveRestartIndex;
            }
            else
            {
                _occlusionMaskIndices[index++] = (ushort)(vertex + 0);
                _occlusionMaskIndices[index++] = (ushort)(vertex + 1);
                _occlusionMaskIndices[index++] = (ushort)(vertex + 2);
                _occlusionMaskIndices[index++] = (ushort)(vertex + 0);
                _occlusionMaskIndices[index++] = (ushort)(vertex + 2);
                _occlusionMaskIndices[index++] = (ushort)(vertex + 3);
            }
            vertex += 4;
        }
        _occlusionMaskEbo.Reallocate(_occlusionMaskIndices.AsSpan());
    }

    [StructLayout((LayoutKind.Sequential))]
    public readonly struct DepthDrawInstance(Vector2 origin, float index, float cullClockwise)
    {
        /// <summary>
        /// Location of the light (or eye) relative to the eye that was used to construct the geometry in
        /// <see cref="Clyde.UpdateOcclusionGeometry"/>
        /// </summary>
        public readonly Vector2 Origin = origin;

        /// <summary>
        /// The vertical UV coordinate of the row in the target texture where the depth will get drawn.
        /// </summary>
        public readonly float Index = index;

        /// <summary>
        /// Whether to cull clockwise or counter-clockwise traveling lines.
        /// </summary>
        /// <remarks>
        /// Note that we draw occluder boxes in a clockwise manner. I.e., the top left -> tr -> br -> bl -> tl.
        /// Hence, culling lines that appear to be traveling clockwise from the origin's POV is equivalent to culling
        /// the back faces of occluder boxes (obviously assuming the eye isn't stuck inside of an occluder).
        /// </remarks>
        public readonly float CullClockwise = cullClockwise;
    }
}
