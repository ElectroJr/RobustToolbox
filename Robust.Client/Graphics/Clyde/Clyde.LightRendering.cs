using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using TKStencilOp = OpenToolkit.Graphics.OpenGL4.StencilOp;
using Robust.Shared.Physics;
using Robust.Client.ComponentTrees;
using Robust.Client.Utility;
using Robust.Shared.Graphics;
using Robust.Shared.Light;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Box2 = Robust.Shared.Maths.Box2;
using Color = Robust.Shared.Maths.Color;
using Matrix3x2 = System.Numerics.Matrix3x2;
using TextureWrapMode = Robust.Shared.Graphics.TextureWrapMode;
using Vector4 = System.Numerics.Vector4;
using RVector4 = Robust.Shared.Maths.Vector4;
using Vector2 = System.Numerics.Vector2;
using Vector2i = Robust.Shared.Maths.Vector2i;

namespace Robust.Client.Graphics.Clyde
{
    // This file handles everything about light rendering.
    // That includes shadow casting and also FOV.
    // A detailed explanation of how all this works can be found here:
    // https://docs.spacestation14.io/en/engine/lighting-fov

    internal partial class Clyde
    {
        // Various shaders used in the light rendering process.
        // We keep ClydeHandles into the _loadedShaders dict so they can be reloaded.
        // They're all .swsl now.
        private ClydeHandle _fovShaderHandle;
        private ClydeHandle _fovLightShaderHandle;
        private ClydeHandle _wallBleedBlurShaderHandle;
        private ClydeHandle _lightBlurShaderHandle;
        private ClydeHandle _mergeWallLayerShaderHandle;
        private ClydeHandle _wallMaskLayerShaderHandle;

        // Sampler used to sample the FovTexture with linear filtering, used in the lighting FOV pass
        // (it uses VSM unlike final FOV).
        private GLHandle _fovFilterSampler;

        // Shader program used to calculate depth for shadows/FOV.
        // Sadly not .swsl since it has a different vertex format and such.
        private GLShaderProgram _lightProgram = default!;


        private GLBuffer _shadowVbo = default!;
        private GLBuffer _quadIndicesEbo = default!;
        private GLHandle _shadowVao;
        private Vector4[] _shadowVertexData = default!;
        private int _shadowVertexCount;

        // For depth calculation for FOV.
        private RenderTexture _fovRenderTarget = default!;

        // Proxies to textures of the above render targets.
        private ClydeTexture FovTexture => _fovRenderTarget.Texture;

        private int _lightCount;
        private int _shadowCastingLightCount;
        private PointLight[] _nonShadowCastingLights = default!;
        private PointLight[] _shadowCastingLights = default!;
        private ClydeTexture _lightMaskTexture = default!;

        private void InitLighting()
        {
            LoadLightingShaders();

            // FOV FBO.
            _fovRenderTarget = CreateRenderTarget((FovMapSize, 2),
                new RenderTargetFormatParameters(
                    _hasGLFloatFramebuffers ? RenderTargetColorFormat.RG32F : RenderTargetColorFormat.Rgba8,
                    true),
                new TextureSampleParameters { WrapMode = TextureWrapMode.Repeat },
                nameof(_fovRenderTarget));

            if (_hasGLSamplerObjects)
            {
                _fovFilterSampler = new GLHandle(GL.GenSampler());
                GL.SamplerParameter(_fovFilterSampler.Handle, SamplerParameterName.TextureMagFilter, (int)All.Linear);
                GL.SamplerParameter(_fovFilterSampler.Handle, SamplerParameterName.TextureMinFilter, (int)All.Linear);
                GL.SamplerParameter(_fovFilterSampler.Handle, SamplerParameterName.TextureWrapS, (int)All.Repeat);
                GL.SamplerParameter(_fovFilterSampler.Handle, SamplerParameterName.TextureWrapT, (int)All.Repeat);
                CheckGlError();
            }

            _cfg.OnValueChanged(CVars.MaxShadowcastingLights, MaxShadowCastingLightsChanged, true);

            SetupLightMasks();
        }

        /// <summary>
        /// Merge all know light masks into one texture.
        /// </summary>
        private void SetupLightMasks()
        {
            List<(LightMaskPrototype, Image<Rgba32>)> textures = new();
            List<LightMaskPrototype> textureLess = new();
            foreach (var proto in _protoMan.EnumeratePrototypes<LightMaskPrototype>())
            {
                if (proto.Texture == null)
                {
                    textureLess.Add(proto);
                    continue;
                }

                try
                {
                    var path = SpriteSpecifierSerializer.TextureRoot / proto.Texture.TexturePath;
                    using var stream = _resourceCache.ContentFileRead(path);
                    var image = Image.Load<Rgba32>(stream);
                    textures.Add((proto, image));
                }
                catch (Exception)
                {
                    _clydeSawmill.Error($"Failed to load light mask. Id: {proto.ID}. Path: {proto.Texture}");
                    throw;
                }
            }

            var width = textures.Sum(x => x.Item2.Width) + 1;
            var height = Math.Max(1, textures.Max(x => x.Item2.Height));
            var maxSize = Math.Min(GL.GetInteger(GetPName.MaxTextureSize), _cfg.GetCVar(CVars.ResRSIAtlasSize));

            if (width > maxSize)
            {
                // If someone **REALLY** needs more than 8k of light masks, this can be changed to create more than one
                // row. Currently I'm lazy
                throw new NotSupportedException("Too many light masks");
            }

            var maskAtlas = new Image<Rgba32>(width + 1, height);
            float w = width;
            float h = height;

            // the 0-0 pixel is used by lights without any mask
            maskAtlas[0, 0] = new Rgba32(255, 255, 255, 255);
            Vector2i offset = (1,0);

            foreach (var (proto, mask) in textures)
            {
                var box = new UIBox2i(0, 0, mask.Width, mask.Height);
                mask.Blit(box, maskAtlas, offset);
                box = box.Translated(offset);

                // Convert texture box2i to atlas UV coordinates

                var l = box.Left / w;
                var b = (h - box.Bottom) / h;
                var r = box.Right / w;
                var t = (h - box.Top) / h;

                proto.TextureBox = new Box2(l, b, r, t);

                offset.X += mask.Width;
            }

            var whiteBox = new Box2(0, (h - 1) / h, 1 / w, 1);
            textureLess.ForEach(x => x.TextureBox = whiteBox);

            _lightMaskTexture = (ClydeTexture) LoadTextureFromImage(maskAtlas);
        }


        private void LoadLightingShaders()
        {
            ClydeHandle LoadShaderHandle(string path)
            {
                if (_resourceCache.TryGetResource(path, out ShaderSourceResource? resource))
                {
                    return resource.ClydeHandle;
                }

                _clydeSawmill.Warning($"Can't load shader {path}\n");
                return default;
            }

            _fovShaderHandle = LoadShaderHandle("/Shaders/Internal/fov.swsl");
            _fovLightShaderHandle = LoadShaderHandle("/Shaders/Internal/fov-lighting.swsl");
            _wallBleedBlurShaderHandle = LoadShaderHandle("/Shaders/Internal/wall-bleed-blur.swsl");
            _lightBlurShaderHandle = LoadShaderHandle("/Shaders/Internal/light-blur.swsl");
            _mergeWallLayerShaderHandle = LoadShaderHandle("/Shaders/Internal/wall-merge.swsl");
            _wallMaskLayerShaderHandle = LoadShaderHandle("/Shaders/Internal/wall-mask.swsl");
            ReloadInternalShaders();
        }

        [SuppressMessage("ReSharper", "ConditionalAccessQualifierIsNonNullableAccordingToAPIContract")]
        public void ReloadInternalShaders()
        {
            var program = _compileProgram(
                _resManager.ContentFileReadAllText("/Shaders/lighting/shadow-depth.vert"),
                _resManager.ContentFileReadAllText("/Shaders/lighting/shadow-depth.frag"),
                [
                    ("aPos", 0),
                    ("Origin", 1),
                    ("Index", 2),
                    ("CullClockwise", 3)
                ],
                "Occlusion Depth Program");

            _depthProgram?.Delete();
            _depthProgram = program;

            program = _compileProgram(
                _resManager.ContentFileReadAllText("/Shaders/lighting/soft_shadow.vert"),
                _resManager.ContentFileReadAllText("/Shaders/lighting/soft_shadow.frag"),
                [ ("aOccluderSegment", 0) ],
                "Occlusion Shadow Program");

            _softShadowProgram?.Delete();
            _softShadowProgram = program;

            program = _compileProgram(
                _resManager.ContentFileReadAllText("/Shaders/lighting/hard_shadow.vert"),
                _resManager.ContentFileReadAllText("/Shaders/lighting/hard_shadow.frag"),
                [ ("aOccluderSegment", 0) ],
                "Occlusion Shadow Program");

            _hardShadowProgram?.Delete();
            _hardShadowProgram = program;


            program = _compileProgram(
                _resManager.ContentFileReadAllText("/Shaders/lighting/light.vert"),
                _resManager.ContentFileReadAllText("/Shaders/lighting/light.frag"),
                BaseShaderAttribLocations,
                "Light Program");

            _lightProgram?.Delete();
            _lightProgram = program;
        }

        private void DrawLightsAndFov(Viewport viewport, Box2Rotated worldBounds, Box2 worldAABB, IEye eye)
        {
            // TODO LIGHTING
            // need to regenerate light frame buffers if soft shadows are enabled to disabled.
            if (!_lightManager.Enabled || !eye.DrawLight)
                return;

            var mapId = eye.Position.MapId;
            if (mapId == MapId.Nullspace)
                return;

            // If this map has lighting disabled, return
            var mapUid = _mapSystem.GetMapOrInvalid(mapId);
            if (!_entityManager.TryGetComponent<MapComponent>(mapUid, out var map) || !map.LightingEnabled)
            {
                return;
            }

            // TODO LIGHTING PARALLELIZE
            // Maybe run the light & occluder queries in parallel? The expanded occluder bounds can just be fudged.
            // TBH with PVS enabled I assume the bound is often larger than PVS range anyways.
            GetLightsToRender(eye, worldBounds, worldAABB);
            var expandedAABB = worldAABB;
            {
                using var _ = _prof.Group("Expand Bounds");
                // TODO LIGHTING SIMD/parallelize
                // Or maybe just drop this altogether
                for (var i = 0; i < _shadowCastingLightCount; i++)
                {
                    expandedAABB = expandedAABB.ExtendToContain(_shadowCastingLights[i].Properties.LightPos + eye.Position.Position);
                }
            }

            // TODO LIGHTING
            // based on the f3 window, it seems like the occluder lookup is lopsided?
            UpdateOcclusionGeometry(expandedAABB, eye);

            _debugStats.TotalLights += _lightCount;
            _debugStats.ShadowLights += _shadowCastingLightCount;

            DrawFov(eye);

            if (!_lightManager.DrawLighting)
            {
                BindRenderTargetFull(viewport.RenderTarget);
                GL.Viewport(0, 0, viewport.Size.X, viewport.Size.Y);
                CheckGlError();
                return;
            }

            GL.Enable(EnableCap.StencilTest);
            CheckGlError();
            _isStencilling = true;

            var (lightW, lightH) = GetLightMapSize(viewport.Size);
            GL.Viewport(0, 0, lightW, lightH);
            CheckGlError();

            BindRenderTargetImmediate(RtToLoaded(viewport.LightRenderTarget));
            CheckGlError();

            var color = _entityManager.GetComponentOrNull<MapLightComponent>(mapUid)?.AmbientLightColor ?? MapLightComponent.DefaultColor;

            GL.ClearColor(color.R, color.G, color.B, 0);
            GL.ClearStencil(0x00);
            GL.StencilMask(0xFF);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.StencilBufferBit);
            CheckGlError();

            ApplyLightingFovToBuffer(viewport, eye);
            ApplyOccluderMask();
            DrawLights(viewport, eye);
            BlurLights(viewport, eye);
            BlurOntoWalls(viewport, eye);
            MergeWallLayer(viewport);

            BindRenderTargetFull(viewport.RenderTarget);
            GL.Viewport(0, 0, viewport.Size.X, viewport.Size.Y);
            CheckGlError();

            _lightingReady = true;
        }

        private void ApplyOccluderMask()
        {
            GL.StencilFunc(StencilFunction.Gequal, 0xFE, 0xFF);
            GL.StencilOp(TKStencilOp.Keep, TKStencilOp.Keep, TKStencilOp.Replace);
            GL.ColorMask(false, false, false, false);

            var shader = _loadedShaders[_wallMaskLayerShaderHandle].Program;
            shader.Use();
            SetupGlobalUniformsImmediate(shader, null);
            BindVertexArray(_occlusionMaskVao.Handle);
            GL.DrawElements(GetQuadGLPrimitiveType(),
                _occluderCount * GetQuadBatchIndexCount(),
                DrawElementsType.UnsignedShort,
                IntPtr.Zero);
            CheckGlError();
        }

        private void DrawLights(Viewport viewport, IEye eye)
        {
            using var _ = DebugGroup("Draw Lights");
            using var __ = _prof.Group("Draw Lights");

            SetTexture(TextureUnit.Texture0, _lightMaskTexture);
            _lightProgram.Use();
            _lightProgram.SetUniformTextureMaybe(UniIMainTexture, TextureUnit.Texture0);
            _lightProgram.SetUniformMaybe("uClamp", 0);
            SetupGlobalUniformsImmediate(_lightProgram, _lightMaskTexture);

            var shadowProgram = _enableSoftShadows ? _softShadowProgram : _hardShadowProgram;
            shadowProgram.Use();
            SetupGlobalUniformsImmediate(shadowProgram, null);

            _isScissoring = true;
            GL.Enable(EnableCap.ScissorTest);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.ClearColor(0, 0, 0, 0);
            GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
            GL.StencilFunc(StencilFunction.Equal, 0x00, 0xFF);
            GL.StencilOp(TKStencilOp.Keep, TKStencilOp.Keep, TKStencilOp.Keep);
            GL.ColorMask(true, true, true, false);
            CheckGlError();

            var (lightW, lightH) = GetLightMapSize(viewport.Size);
            var lightViewport = new Box2(0, 0, lightW, lightH);

            var transform = _currentMatrixView;

            // Occluder and light positions are already relative to the eye.
            transform.M31 = 0;
            transform.M32 = 0;
            transform *= _currentMatrixProj;

            // Transform from clip space to screen space.
            transform *= Matrix3Helpers.CreateTranslation(Vector2.One);
            transform *= Matrix3Helpers.CreateScale((float)lightW/2, (float)lightH/2);

            // When transforming light bounding boxes for GL.Scissor, we want to avoid rotations that lead to enlarged
            // bounds. Hence we just directly compute the scaling factor that goes into the view & projection matrices.
            var scale = EyeManager.PixelsPerMeter * 2 * eye.Scale * viewport.RenderScale / viewport.RenderTarget.Size * new Vector2(lightW, lightH);

            for (var i = 0; i < _lightCount; i++)
            {
                DrawLight(_nonShadowCastingLights[i], lightViewport, transform, scale);
            }

            if (!_lightManager.DrawShadows)
            {
                for (var i = 0; i < _shadowCastingLightCount; i++)
                {
                    DrawLight(_shadowCastingLights[i], lightViewport, transform, scale);
                }
            }
            else if (_enableSoftShadows)
            {
                DrawSoftLights(transform, scale, lightViewport);
            }
            else
            {
                DrawHardLights(transform, scale, lightViewport);
            }

            GL.ColorMask(true, true, true, true);
            ResetBlendFunc();
            GL.Disable(EnableCap.StencilTest);
            GL.Disable(EnableCap.ScissorTest);
            _isStencilling = false;
            _isScissoring = false;
            CheckGlError();
        }

        private void DrawHardLights(in Matrix3x2 transform, in Vector2 scale, in Box2 lightViewport)
        {
            GL.StencilOp(TKStencilOp.Keep, TKStencilOp.Keep, TKStencilOp.Replace);
            CheckGlError();

            for (var i = 0; i < _shadowCastingLightCount; i++)
            {
                GL.StencilFunc(StencilFunction.Greater, i + 1, 0xFF);
                CheckGlError();
                DrawHardLight(_shadowCastingLights[i], lightViewport, transform, scale);
            }
        }

        private void DrawSoftLights(in Matrix3x2 transform, in Vector2 scale, in Box2 lightViewport)
        {
            GL.BlendFuncSeparate(BlendingFactorSrc.OneMinusDstAlpha, BlendingFactorDest.One, BlendingFactorSrc.One, BlendingFactorDest.One);
            GL.ColorMask(false, false, false, true);
            CheckGlError();

            for (var i = 0; i < _shadowCastingLightCount; i++)
            {
                DrawSoftLight(_shadowCastingLights[i], lightViewport, transform, scale);
            }
        }

        private void DrawLight(in PointLight light, Box2 viewBox, Matrix3x2 transform, Vector2 scale)
        {
            var props = light.Properties;

            var screenPos = Vector2.Transform(props.LightPos, transform);
            var lightBox = Box2.CenteredAround(screenPos, scale * props.Range).Intersect(viewBox);
            GL.Scissor((int)lightBox.Left, (int)lightBox.Bottom, (int)Math.Ceiling(lightBox.Width), (int)Math.Ceiling(lightBox.Height));

            _lightProgram.Use();
            // TODO LIGHTING combine into a single batch with vertex attributes
            _lightProgram.SetUniformMaybe("uLightData", new RVector4(props.LightPos.X, props.LightPos.Y, props.Range, props.Angle));
            _lightProgram.SetUniformMaybe("uLightPower", props.Power);
            _lightProgram.SetUniformMaybe("uLightMask", light.Mask.AsVector4);
            _lightProgram.SetUniformMaybe("uLightColor", props.Color);
            BindVertexArray(QuadVAO.Handle);

            // Draw light
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            _debugStats.LastGLDrawCalls += 1;
            CheckGlError();
        }

        private void DrawSoftLight(in PointLight light, in Box2 viewBox, in Matrix3x2 transform, in Vector2 scale)
        {
            // TODO LIGHTING consider using instanced rendering.
            // I.e. populate a singe light data buffer once, then just draw one instance at a time with different offsets.

            // single VBO for all light quads
            // shadows use light quads as instanced data

            // TODO LIGHTING consider using instanced rendering.
            // I.e. populate a singe light data buffer once, then just draw one instance at a time with different offsets.

            var props = light.Properties;
            var screenPos = Vector2.Transform(props.LightPos, transform);
            var lightBox = Box2.CenteredAround(screenPos, scale * props.Range).Intersect(viewBox);
            GL.Scissor((int)lightBox.Left, (int)lightBox.Bottom, (int)Math.Ceiling(lightBox.Width), (int)Math.Ceiling(lightBox.Height));

            _softShadowProgram.Use();
            _softShadowProgram.SetUniformMaybe("uLightData", new RVector4(props.LightPos.X, props.LightPos.Y, props.Range, props.Softness));
            BindVertexArray(_shadowVao.Handle);
            GL.DrawElements(
                _hasGLPrimitiveRestart
                    ? PrimitiveType.TriangleStrip
                    : PrimitiveType.Triangles,
                GetQuadBatchIndexCount() * _shadowVertexCount/VerticesPerOccluderSegment,
                DrawElementsType.UnsignedShort,
                0);
            _debugStats.LastGLDrawCalls += 1;
            CheckGlError();

            _lightProgram.Use();
            _lightProgram.SetUniformMaybe("uLightData", new RVector4(props.LightPos.X, props.LightPos.Y, props.Range, props.Angle));
            _lightProgram.SetUniformMaybe("uLightPower", props.Power);
            _lightProgram.SetUniformMaybe("uLightMask", light.Mask.AsVector4);
            _lightProgram.SetUniformMaybe("uLightColor", props.Color);
            BindVertexArray(QuadVAO.Handle);

            // TODO LIGHTING remove alpha clamping call somehow
            // is there really no way to have float colors and fixed (or at least clamped) alpha
            if (_hasGLFloatFramebuffers)
            {
                _lightProgram.SetUniformMaybe("uClamp", 1);
                GL.BlendEquation(BlendEquationMode.Min);
                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
                _debugStats.LastGLDrawCalls += 1;
                _lightProgram.SetUniformMaybe("uClamp", 0);
                GL.BlendEquation(BlendEquationMode.FuncAdd);
                CheckGlError();
            }

            // Draw light
            GL.ColorMask(true, true, true, false);
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            _debugStats.LastGLDrawCalls += 1;

            // TODO LIGHTING performance
            // try clearing alpha in the light draw call
            // then use a quad instead of Gl.Clear with a stencil.
            // does that help performance?

            // Clear alpha
            GL.ColorMask(false, false, false, true);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            CheckGlError();
        }

        private void DrawHardLight(in PointLight light, in Box2 viewBox, in Matrix3x2 transform, in Vector2 scale)
        {
            var props = light.Properties;
            var screenPos = Vector2.Transform(props.LightPos, transform);
            var lightBox = Box2.CenteredAround(screenPos, scale * props.Range).Intersect(viewBox);
            GL.Scissor((int)lightBox.Left, (int)lightBox.Bottom, (int)Math.Ceiling(lightBox.Width), (int)Math.Ceiling(lightBox.Height));

            // Draw shadows to the stencil buffer.
            GL.ColorMask(false, false, false, false);
            _hardShadowProgram.Use();
            _hardShadowProgram.SetUniformMaybe("uLightData", new RVector4(props.LightPos.X, props.LightPos.Y, props.Range, props.Softness));
            BindVertexArray(_shadowVao.Handle);
            GL.DrawElements(
                _hasGLPrimitiveRestart
                    ? PrimitiveType.TriangleStrip
                    : PrimitiveType.Triangles,
                GetQuadBatchIndexCount() * _shadowVertexCount/VerticesPerOccluderSegment,
                DrawElementsType.UnsignedShort,
                0);
            _debugStats.LastGLDrawCalls += 1;
            CheckGlError();

            // Draw light
            GL.ColorMask(true, true, true, false);
            _lightProgram.Use();
            _lightProgram.SetUniformMaybe("uLightData", new RVector4(props.LightPos.X, props.LightPos.Y, props.Range, props.Angle));
            _lightProgram.SetUniformMaybe("uLightPower", props.Power);
            _lightProgram.SetUniformMaybe("uLightMask", light.Mask.AsVector4);
            _lightProgram.SetUniformMaybe("uLightColor", props.Color);
            BindVertexArray(QuadVAO.Handle);
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            _debugStats.LastGLDrawCalls += 1;
            CheckGlError();
        }

        private static bool LightQuery(
            ref (
                Clyde clyde,
                int count,
                int shadowCastingCount,
                TransformSystem xformSystem,
                EntityQuery<TransformComponent> xforms,
                Box2 worldAABB,
                Vector2 eyePos) state,
            in ComponentTreeEntry<PointLightComponent> value)
        {
            ref var count = ref state.count;
            ref var shadowCount = ref state.shadowCastingCount;

            var (light, transform) = value;
            var (lightPos, rot) = state.clyde._transformSystem.GetWorldPositionRotation(transform, state.xforms);
            lightPos += rot.RotateVec(light.Offset);
            var circle = new Circle(lightPos, light.Radius);

            // If the light doesn't touch anywhere the camera can see, it doesn't matter.
            // The tree query is not fully accurate because the viewport may be rotated relative to a grid.
            if (!circle.Intersects(state.worldAABB))
                return true;

            var angle = light.MaskAutoRotate
                ? (float) (light.Rotation + rot)
                : (float) light.Rotation;

            lightPos -= state.eyePos;
            var props = new LightProperties(
                light.Color,
                lightPos,
                light.Radius,
                light.Energy,
                light.Softness,
                angle
            );

            if (!light.CastShadows)
            {
                if (count >= state.clyde._maxNonShadowCastingLights)
                    return false;

                state.clyde._nonShadowCastingLights[count++] = new PointLight(props, light.MaskPrototype.TextureBox);
                return true;
            }

            if (shadowCount >= state.clyde._maxShadowCastingLights)
                return false;

            state.clyde._shadowCastingLights[shadowCount++] = new PointLight(props, light.MaskPrototype.TextureBox);
            return true;
        }

        private void GetLightsToRender(IEye eye, Box2Rotated worldBounds, Box2 worldAABB)
        {
            using var _ = _prof.Group(nameof(GetLightsToRender));
            var lightTreeSys = _entitySystemManager.GetEntitySystem<LightTreeSystem>();
            var xformSystem = _entitySystemManager.GetEntitySystem<TransformSystem>();

            // Use worldbounds for this one as we only care if the light intersects our actual bounds
            var xforms = _entityManager.GetEntityQuery<TransformComponent>();
            var state = (this, count: 0, shadowCastingCount: 0, xformSystem, xforms, worldAABB, eye.Position.Position);

            foreach (var (uid, comp) in lightTreeSys.GetIntersectingTrees(eye.Position.MapId, worldAABB))
            {
                var bounds = _transformSystem.GetInvWorldMatrix(uid, xforms).TransformBox(worldBounds);
                comp.Tree.QueryAabb(ref state, LightQuery, bounds);
            }

            _lightCount = state.count;
            _shadowCastingLightCount = state.shadowCastingCount;
        }

        private void BlurLights(Viewport viewport, IEye eye)
        {
            // TODO LIGHTING re-enable blur
            return;
            if (!_cfg.GetCVar(CVars.LightBlur))
                return;

            using var _ = DebugGroup(nameof(BlurLights));
            using var __ = _prof.Group(nameof(BlurLights));

            GL.Disable(EnableCap.Blend);
            CheckGlError();
            CalcScreenMatrices(viewport.Size, out var proj, out var view);
            SetProjViewBuffer(proj, view);

            var shader = _loadedShaders[_lightBlurShaderHandle].Program;
            shader.Use();

            SetupGlobalUniformsImmediate(shader, viewport.LightRenderTarget.Texture);

            var size = viewport.LightRenderTarget.Size;
            shader.SetUniformMaybe("size", (Vector2)size);
            shader.SetUniformTextureMaybe(UniIMainTexture, TextureUnit.Texture0);

            GL.Viewport(0, 0, size.X, size.Y);
            CheckGlError();

            // Initially we're pulling from the light render target.
            // So we set it out of the loop so
            // _wallBleedIntermediateRenderTarget2 gets bound at the end of the loop body.
            SetTexture(TextureUnit.Texture0, viewport.LightRenderTarget.Texture);

            // Have to scale the blurring radius based on viewport size and camera zoom.
            const float refCameraHeight = 14;
            var facBase = _cfg.GetCVar(CVars.LightBlurFactor);
            var cameraSize = eye.Zoom.Y * viewport.Size.Y * (1 / viewport.RenderScale.Y) / EyeManager.PixelsPerMeter;
            // 7e-3f is just a magic factor that makes it look ok.
            var factor = facBase * (refCameraHeight / cameraSize);

            // Multi-iteration gaussian blur.
            for (var i = 3; i > 0; i--)
            {
                var scale = (i + 1) * factor;
                // Set factor.
                shader.SetUniformMaybe("radius", scale);

                BindRenderTargetFull(viewport.LightBlurTarget);

                // Blur horizontally to _wallBleedIntermediateRenderTarget1.
                shader.SetUniformMaybe("direction", Vector2.UnitX);
                _drawQuad(Vector2.Zero, viewport.Size, Matrix3x2.Identity, shader);

                SetTexture(TextureUnit.Texture0, viewport.LightBlurTarget.Texture);

                BindRenderTargetFull(viewport.LightRenderTarget);

                // Blur vertically to _wallBleedIntermediateRenderTarget2.
                shader.SetUniformMaybe("direction", Vector2.UnitY);
                _drawQuad(Vector2.Zero, viewport.Size, Matrix3x2.Identity, shader);

                SetTexture(TextureUnit.Texture0, viewport.LightRenderTarget.Texture);
            }

            GL.Enable(EnableCap.Blend);
            CheckGlError();
            // We didn't trample over the old _currentMatrices so just roll it back.
            SetProjViewBuffer(_currentMatrixProj, _currentMatrixView);
        }

        private void BlurOntoWalls(Viewport viewport, IEye eye)
        {
            // TODO LIGHTING
            // For whatever reason, this seems much darker than before?
            // Is it just my imagination or is it different somehow?
            using var _ = DebugGroup(nameof(BlurOntoWalls));
            using var __ = _prof.Group(nameof(BlurOntoWalls));

            GL.Disable(EnableCap.Blend);
            CheckGlError();
            CalcScreenMatrices(viewport.Size, out var proj, out var view);
            SetProjViewBuffer(proj, view);

            var shader = _loadedShaders[_wallBleedBlurShaderHandle].Program;
            shader.Use();

            SetupGlobalUniformsImmediate(shader, viewport.LightRenderTarget.Texture);

            shader.SetUniformMaybe("size", (Vector2)viewport.WallBleedIntermediateRenderTarget1.Size);
            shader.SetUniformTextureMaybe(UniIMainTexture, TextureUnit.Texture0);

            var size = viewport.WallBleedIntermediateRenderTarget1.Size;
            GL.Viewport(0, 0, size.X, size.Y);
            CheckGlError();

            // Initially we're pulling from the light render target.
            // So we set it out of the loop so
            // _wallBleedIntermediateRenderTarget2 gets bound at the end of the loop body.
            SetTexture(TextureUnit.Texture0, viewport.LightRenderTarget.Texture);

            // Have to scale the blurring radius based on viewport size and camera zoom.
            const float refCameraHeight = 14;
            var cameraSize = eye.Zoom.Y * viewport.Size.Y * (1 / viewport.RenderScale.Y) / EyeManager.PixelsPerMeter;
            // 7e-3f is just a magic factor that makes it look ok.
            var factor = 7e-3f * (refCameraHeight / cameraSize);

            // Multi-iteration gaussian blur.
            for (var i = 3; i > 0; i--)
            {
                var scale = (i + 1) * factor;
                // Set factor.
                shader.SetUniformMaybe("radius", scale);

                BindRenderTargetFull(viewport.WallBleedIntermediateRenderTarget1);

                // Blur horizontally to _wallBleedIntermediateRenderTarget1.
                shader.SetUniformMaybe("direction", Vector2.UnitX);
                _drawQuad(Vector2.Zero, viewport.Size, Matrix3x2.Identity, shader);

                SetTexture(TextureUnit.Texture0, viewport.WallBleedIntermediateRenderTarget1.Texture);
                BindRenderTargetFull(viewport.WallBleedIntermediateRenderTarget2);

                // Blur vertically to _wallBleedIntermediateRenderTarget2.
                shader.SetUniformMaybe("direction", Vector2.UnitY);
                _drawQuad(Vector2.Zero, viewport.Size, Matrix3x2.Identity, shader);

                SetTexture(TextureUnit.Texture0, viewport.WallBleedIntermediateRenderTarget2.Texture);
            }

            GL.Enable(EnableCap.Blend);
            CheckGlError();
            // We didn't trample over the old _currentMatrices so just roll it back.
            SetProjViewBuffer(_currentMatrixProj, _currentMatrixView);
        }

        private void MergeWallLayer(Viewport viewport)
        {
            using var _ = DebugGroup(nameof(MergeWallLayer));
            using var __ = _prof.Group(nameof(MergeWallLayer));

            BindRenderTargetFull(viewport.LightRenderTarget);

            GL.Viewport(0, 0, viewport.LightRenderTarget.Size.X, viewport.LightRenderTarget.Size.Y);
            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.StencilTest);
            GL.StencilFunc(StencilFunction.Equal, 0xFE, 0xFF);
            CheckGlError();

            var shader = _loadedShaders[_mergeWallLayerShaderHandle].Program;
            shader.Use();
            var tex = viewport.WallBleedIntermediateRenderTarget2.Texture;
            SetupGlobalUniformsImmediate(shader, tex);
            SetTexture(TextureUnit.Texture0, tex);
            shader.SetUniformTextureMaybe(UniIMainTexture, TextureUnit.Texture0);

            BindVertexArray(QuadVAO.Handle);
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            _debugStats.LastGLDrawCalls += 1;

            GL.Disable(EnableCap.StencilTest);
            GL.Enable(EnableCap.Blend);
            CheckGlError();
            // TODO LIGHTING
            // Because of the shadow shaders DEPTH_LEFTRIGHT_EXPAND_BIAS
            // The lines that make up the walls don't quite match the lines of the mask for this draw.
            // So the edges of occluders can have a pixel of black around them
            // Need to figure out some way to stop this
        }

        private void ApplyFovToBuffer(Viewport viewport, IEye eye)
        {
            GL.Clear(ClearBufferMask.StencilBufferBit);
            GL.Enable(EnableCap.StencilTest);
            GL.StencilOp(OpenToolkit.Graphics.OpenGL4.StencilOp.Keep, OpenToolkit.Graphics.OpenGL4.StencilOp.Keep,
                OpenToolkit.Graphics.OpenGL4.StencilOp.Replace);
            GL.StencilFunc(StencilFunction.Always, 1, 0xFF);
            GL.StencilMask(0xFF);

            // Applies FOV to the final framebuffer.

            var fovShader = _loadedShaders[_fovShaderHandle].Program;
            fovShader.Use();

            SetupGlobalUniformsImmediate(fovShader, FovTexture);

            SetTexture(TextureUnit.Texture0, FovTexture);

            fovShader.SetUniformTextureMaybe(UniIMainTexture, TextureUnit.Texture0);

            if (!Color.TryParse(_cfg.GetCVar(CVars.RenderFOVColor), out var color))
                color = Color.Black;

            fovShader.SetUniformMaybe("occludeColor", color);
            FovSetTransformAndBlit(viewport, eye.Position.Position, fovShader);

            GL.StencilMask(0x00);
            GL.Disable(EnableCap.StencilTest);
            _isStencilling = false;
        }

        private void ApplyLightingFovToBuffer(Viewport viewport, IEye eye)
        {
            // Applies FOV to the lighting framebuffer.

            var fovShader = _loadedShaders[_fovLightShaderHandle].Program;
            fovShader.Use();

            SetupGlobalUniformsImmediate(fovShader, FovTexture);

            SetTexture(TextureUnit.Texture0, FovTexture);

            // Have to swap to linear filtering on the shadow map here.
            // VSM wants it.
            if (_hasGLSamplerObjects)
            {
                GL.BindSampler(0, _fovFilterSampler.Handle);
                CheckGlError();
            }
            else
            {
                // OpenGL why do you torture me so.
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)All.Linear);
                CheckGlError();
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)All.Linear);
                CheckGlError();
            }

            fovShader.SetUniformTextureMaybe(UniIMainTexture, TextureUnit.Texture0);

            CheckGlError();
            GL.StencilFunc(StencilFunction.Always, 0xFF, 0xFF);
            CheckGlError();
            GL.StencilOp(TKStencilOp.Keep, TKStencilOp.Keep, TKStencilOp.Replace);
            CheckGlError();

            fovShader.SetUniformMaybe("occludeColor", Color.Black);
            FovSetTransformAndBlit(viewport, eye.Position.Position, fovShader);

            if (_hasGLSamplerObjects)
            {
                GL.BindSampler(0, 0);
                CheckGlError();
            }
            else
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)All.Nearest);
                CheckGlError();
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)All.Nearest);
                CheckGlError();
            }
        }

        private void FovSetTransformAndBlit(Viewport vp, Vector2 fovCentre, GLShaderProgram fovShader)
        {
            // It might be an idea if there was a proper way to get the LocalToWorld matrix.
            // But actually constructing the matrix tends to be more trouble than it's worth in most cases.
            // (Maybe if there was some way to observe Eye matrix changes that wouldn't be the case, as viewport could dynamically update.)
            // This is expected to run a grand total of twice per frame for 6 LocalToWorld calls.
            // Something else to note is that modifications must be made anyway.

            // Something ELSE to note is that it's absolutely critical that this be calculated in the "right way" due to precision issues!

            // Bit of an interesting little trick here - need to set things up correctly.
            // 0, 0 in clip-space is the centre of the screen, and 1, 1 is the top-right corner.
            var halfSize = vp.Size / 2.0f;
            var uZero = vp.LocalToWorld(halfSize).Position;
            var uX = vp.LocalToWorld(halfSize + (Vector2.UnitX * halfSize.X)).Position - uZero;
            var uY = vp.LocalToWorld(halfSize - (Vector2.UnitY * halfSize.Y)).Position - uZero;

            // Second modification is that output must be fov-centred (difference-space)
            uZero -= fovCentre;

            var clipToDiff = new Matrix3x2(uX.X, uX.Y, uY.X, uY.Y, uZero.X, uZero.Y);

            fovShader.SetUniformMaybe("clipToDiff", clipToDiff);
            _drawQuad(Vector2.Zero, Vector2.One, Matrix3x2.Identity, fovShader);
        }

        private void RegenLightRts(Viewport viewport)
        {
            // All of these depend on screen size so they have to be re-created if it changes.

            var lightMapSize = GetLightMapSize(viewport.Size);
            var lightMapSizeQuart = GetLightMapSize(viewport.Size, true);
            var lightMapColorFormat = _hasGLFloatFramebuffers
                ? RenderTargetColorFormat.R11FG11FB10F
                : RenderTargetColorFormat.Rgba8;

            var mainLightColorFormat = lightMapColorFormat;
            if (_enableSoftShadows && _hasGLFloatFramebuffers)
            {
                // Soft shadows use alpha channel for occlusion information.
                // RGBA8 has banding, but R11FG11FB10F has no alpha channel.
                // TODO LIGHTING do we really need a 64bit texture?
                // Maybe just use a separate texture for occlusion data?
                // This would also prevent the need for alpha clamping

                // TODO LIGHTING how much does this affect performance?
                mainLightColorFormat = RenderTargetColorFormat.Rgba16F;
            }

            var lightMapSampleParameters = new TextureSampleParameters { Filter = true };

            viewport.LightRenderTarget?.Dispose();
            viewport.WallMaskRenderTarget?.Dispose();
            viewport.WallBleedIntermediateRenderTarget1?.Dispose();
            viewport.WallBleedIntermediateRenderTarget2?.Dispose();

            viewport.WallMaskRenderTarget = CreateRenderTarget(viewport.Size, RenderTargetColorFormat.R8,
                name: $"{viewport.Name}-{nameof(viewport.WallMaskRenderTarget)}");

            viewport.LightRenderTarget = CreateRenderTarget(lightMapSize,
                new RenderTargetFormatParameters(mainLightColorFormat, hasDepthStencil: true),
                lightMapSampleParameters,
                $"{viewport.Name}-{nameof(viewport.LightRenderTarget)}");

            viewport.LightBlurTarget = CreateRenderTarget(lightMapSize,
                new RenderTargetFormatParameters(lightMapColorFormat),
                lightMapSampleParameters,
                $"{viewport.Name}-{nameof(viewport.LightBlurTarget)}");

            viewport.WallBleedIntermediateRenderTarget1 = CreateRenderTarget(lightMapSizeQuart, lightMapColorFormat,
                lightMapSampleParameters,
                $"{viewport.Name}-{nameof(viewport.WallBleedIntermediateRenderTarget1)}");

            viewport.WallBleedIntermediateRenderTarget2 = CreateRenderTarget(lightMapSizeQuart, lightMapColorFormat,
                lightMapSampleParameters,
                $"{viewport.Name}-{nameof(viewport.WallBleedIntermediateRenderTarget2)}");
        }

        private void RegenAllLightRts()
        {
            foreach (var viewportRef in _viewports.Values)
            {
                if (viewportRef.TryGetTarget(out var viewport))
                {
                    RegenLightRts(viewport);
                }
            }
        }

        private Vector2i GetLightMapSize(Vector2i screenSize, bool furtherDivide = false)
        {
            var scale = _lightResolutionScale;
            if (furtherDivide)
            {
                scale /= 2;
            }

            var w = (int)Math.Ceiling(screenSize.X * scale);
            var h = (int)Math.Ceiling(screenSize.Y * scale);

            return (w, h);
        }

        private void LightResolutionScaleChanged(float newValue)
        {
            _lightResolutionScale = newValue > 0.05f ? newValue : 0.05f;
            RegenAllLightRts();
        }

        private void SoftShadowsChanged(bool newValue)
        {
            _enableSoftShadows = newValue;
            RegenAllLightRts();
        }

        private void MaxLightsChanged(int value)
        {
            MaxLightsChanged();
        }

        private void MaxShadowCastingLightsChanged(int newValue)
        {
            MaxLightsChanged();
        }

        private void MaxLightsChanged()
        {
            var maxLights = _cfg.GetCVar(CVars.MaxLightCount);
            var maxShadowLights = _cfg.GetCVar(CVars.MaxShadowcastingLights);

            _maxShadowCastingLights = Math.Clamp(maxShadowLights, 0, maxLights);
            if (_maxShadowCastingLights > 253)
            {
                // Hard shadows use the stencil masks, with 0XFF and 0xFE reserved, which implicitly limits the max
                // number of lights. This can pretty easily be fixed by just resetting all values below 0xFE back to
                // 0x00 and but I CBF doing that atm.
                // TODO LIGHTING fix this
                _maxShadowCastingLights = 253;
                _clydeSawmill.Warning($"Clamping MaxShadowCastingLights to 253");
            }

            _maxNonShadowCastingLights = Math.Max(0, maxLights - _maxShadowCastingLights);

            _nonShadowCastingLights = new PointLight[_maxNonShadowCastingLights];
            _shadowCastingLights = new PointLight[_maxShadowCastingLights];
        }
    }
}
