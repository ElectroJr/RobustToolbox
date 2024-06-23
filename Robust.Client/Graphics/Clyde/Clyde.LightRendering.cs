using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
using Color = Robust.Shared.Maths.Color;
using TextureWrapMode = Robust.Shared.Graphics.TextureWrapMode;

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

        // Sampler used to sample the FovTexture with linear filtering, used in the lighting FOV pass
        // (it uses VSM unlike final FOV).
        private GLHandle _fovFilterSampler;

        // Shader program used to calculate depth for shadows/FOV.
        // Sadly not .swsl since it has a different vertex format and such.
        private GLShaderProgram _softLightProgram = default!;
        private GLShaderProgram _hardLightProgram = default!;

        private GLBuffer _lightVbo = default!;
        private GLBuffer _lightEbo = default!;
        private GLHandle _lightVao;
        private readonly LightVertex[] _lightVertexData = new LightVertex[MaxBatchQuads * 4];
        private ushort[] _lightIndexData = default!;
        private int _lightVertexIndex;
        private int _lightIndexIndex;

        // For depth calculation for FOV.
        private RenderTexture _fovRenderTarget = default!;

        // For depth calculation of lighting shadows.
        private RenderTexture _shadowRenderTarget = default!;

        // Proxies to textures of the above render targets.
        private ClydeTexture FovTexture => _fovRenderTarget.Texture;
        private ClydeTexture ShadowTexture => _shadowRenderTarget.Texture;

        private int _shadowCastingLightCount;
        private PointLight[] _lightsToRenderList = default!;
        private ClydeTexture _lightMaskTexture = default!;

        private LightCapacityComparer _lightCap = new();
        private ShadowCapacityComparer _shadowCap = new();

        private unsafe void InitLighting()
        {
            LoadLightingShaders();

            {
                // Light VAO.
                _lightVao = new GLHandle(GenVertexArray());
                BindVertexArray(_lightVao.Handle);
                CheckGlError();
                ObjectLabelMaybe(ObjectLabelIdentifier.VertexArray, _lightVao, nameof(_lightVao));

                _lightVbo = new GLBuffer(this,
                    BufferTarget.ArrayBuffer,
                    BufferUsageHint.DynamicDraw,
                    sizeof(LightVertex) * _lightVertexData.Length,
                    nameof(_lightVbo));

                // Texture coordinates in the texture atlas.
                GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, sizeof(LightVertex), 0 * sizeof(float));
                GL.EnableVertexAttribArray(0);

                // Atlas sub-texture UV coordinates.
                GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, sizeof(LightVertex), 2 * sizeof(float));
                GL.EnableVertexAttribArray(1);

                // Light colour.
                GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, sizeof(LightVertex), 4 * sizeof(float));
                GL.EnableVertexAttribArray(2);

                // Light world position.
                GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, sizeof(LightVertex), 8 * sizeof(float));
                GL.EnableVertexAttribArray(3);

                // Light properties.
                GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, sizeof(LightVertex), 10 * sizeof(float));
                GL.EnableVertexAttribArray(4);

                // Light angle
                GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, sizeof(LightVertex), 14 * sizeof(float));
                GL.EnableVertexAttribArray(5);

                DebugTools.AssertEqual(sizeof(LightVertex), 15*sizeof(float));
                DebugTools.AssertEqual(sizeof(LightProperties), 11*sizeof(float));

                CheckGlError();

                _lightIndexData = new ushort[MaxBatchQuads * GetQuadBatchIndexCount()];
                _lightEbo = new GLBuffer(this,
                    BufferTarget.ElementArrayBuffer,
                    BufferUsageHint.DynamicDraw,
                    sizeof(ushort) * _lightIndexData.Length,
                    nameof(_lightEbo));

                CheckGlError();
            }

            // FOV FBO.
            _fovRenderTarget = CreateRenderTarget((FovMapSize, 2),
                new RenderTargetFormatParameters(
                    _hasGLFloatFramebuffers ? RenderTargetColorFormat.RG32F : RenderTargetColorFormat.Rgba8, true),
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

            _cfg.OnValueChanged(CVars.MaxShadowcastingLights, MaxShadowcastingLightsChanged, true);

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


                // I'm not sure whats wrong, but flashlights are pointing the wrong way.
                // So uhhh... we'll just flip these.
                (b, t) = (t, b);

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

            var lightVert = ReadEmbeddedShader("light.vert");
            var lightSoftFrag = ReadEmbeddedShader("light-soft.frag");
            var lightHardFrag = ReadEmbeddedShader("light-hard.frag");
            var lightSharedFrag = ReadEmbeddedShader("light-shared.frag");

            lightSoftFrag = lightSharedFrag.Replace("// [CreateOcclusion]", lightSoftFrag);
            lightHardFrag = lightSharedFrag.Replace("// [CreateOcclusion]", lightHardFrag);

            (string, uint)[] lightAttribLocations =
            {
                ("tCoord", 0),
                ("tCoord2", 1),
                ("lightColor", 2),
                ("lightPos", 3),
                ("lightData", 4),
                ("lightAngle", 5),
            };

            _softLightProgram = _compileProgram(lightVert, lightSoftFrag, lightAttribLocations, "Soft Light Program");
            _hardLightProgram = _compileProgram(lightVert, lightHardFrag, lightAttribLocations, "Hard Light Program");

            _fovShaderHandle = LoadShaderHandle("/Shaders/Internal/fov.swsl");
            _fovLightShaderHandle = LoadShaderHandle("/Shaders/Internal/fov-lighting.swsl");
            _wallBleedBlurShaderHandle = LoadShaderHandle("/Shaders/Internal/wall-bleed-blur.swsl");
            _lightBlurShaderHandle = LoadShaderHandle("/Shaders/Internal/light-blur.swsl");
            _mergeWallLayerShaderHandle = LoadShaderHandle("/Shaders/Internal/wall-merge.swsl");
        }

        private void DrawLightsAndFov(Viewport viewport, Box2Rotated worldBounds, Box2 worldAABB, IEye eye)
        {
            if (!_lightManager.Enabled || !eye.DrawLight)
                return;

            var mapId = eye.Position.MapId;
            if (mapId == MapId.Nullspace)
                return;

            // If this map has lighting disabled, return
            var mapUid = _mapManager.GetMapEntityId(mapId);
            if (!_entityManager.TryGetComponent<MapComponent>(mapUid, out var map) || !map.LightingEnabled)
            {
                return;
            }

            var (count, expandedBounds) = GetLightsToRender(eye, worldBounds, worldAABB);
            UpdateOcclusionGeometry(expandedBounds, eye);

            PrepareDepthDraw();
            DrawFov(eye);

            if (!_lightManager.DrawLighting)
            {
                FinalizeDepthDraw();
                BindRenderTargetFull(viewport.RenderTarget);
                GL.Viewport(0, 0, viewport.Size.X, viewport.Size.Y);
                CheckGlError();
                return;
            }

            DrawShadowDepths();
            FinalizeDepthDraw();

            GL.Enable(EnableCap.StencilTest);
            _isStencilling = true;

            var (lightW, lightH) = GetLightMapSize(viewport.Size);
            GL.Viewport(0, 0, lightW, lightH);
            CheckGlError();

            BindRenderTargetImmediate(RtToLoaded(viewport.LightRenderTarget));
            CheckGlError();
            GLClearColor(_entityManager.GetComponentOrNull<MapLightComponent>(mapUid)?.AmbientLightColor ?? MapLightComponent.DefaultColor);
            GL.ClearStencil(0xFF);
            GL.StencilMask(0xFF);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.StencilBufferBit);
            CheckGlError();

            ApplyLightingFovToBuffer(viewport, eye);

            var lightShader = _enableSoftShadows ? _softLightProgram : _hardLightProgram;
            lightShader.Use();

            SetupGlobalUniformsImmediate(lightShader, ShadowTexture);

            SetTexture(TextureUnit.Texture0, _lightMaskTexture);
            lightShader.SetUniformTextureMaybe(UniIMainTexture, TextureUnit.Texture0);
            SetTexture(TextureUnit.Texture1, ShadowTexture);
            lightShader.SetUniformTextureMaybe("shadowMap", TextureUnit.Texture1);

            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            CheckGlError();

            GL.StencilFunc(StencilFunction.Equal, 0xFF, 0xFF);
            CheckGlError();
            GL.StencilOp(TKStencilOp.Keep, TKStencilOp.Keep, TKStencilOp.Keep);
            CheckGlError();

            using (DebugGroup("Draw Lights"))
            using (_prof.Group("Draw Lights"))
            {
                var idxCount = GetQuadBatchIndexCount();
                for (var i = 0; i < count; i++)
                {
                    ref var light = ref _lightsToRenderList[i];
                    if (_lightVertexIndex + 4 >= _lightVertexData.Length || _lightIndexIndex + idxCount > _lightIndexData.Length)
                        FlushLights();

                    _lightVertexData[_lightVertexIndex + 0] = new LightVertex(light.Mask.BottomLeft, new Vector2(0,0), light.Properties);
                    _lightVertexData[_lightVertexIndex + 1] = new LightVertex(light.Mask.BottomRight, new Vector2(1,0), light.Properties);
                    _lightVertexData[_lightVertexIndex + 2] = new LightVertex(light.Mask.TopRight, new Vector2(1,1), light.Properties);
                    _lightVertexData[_lightVertexIndex + 3] = new LightVertex(light.Mask.TopLeft, new Vector2(0,1), light.Properties);
                    QuadBatchIndexWrite(_lightIndexData, ref _lightIndexIndex, (ushort) _lightVertexIndex);
                    _lightVertexIndex += 4;
                }

                FlushLights();
            }

            ResetBlendFunc();
            GL.Disable(EnableCap.StencilTest);
            _isStencilling = false;

            CheckGlError();

            if (_cfg.GetCVar(CVars.LightBlur))
                BlurLights(viewport, eye);

            using (_prof.Group("BlurOntoWalls"))
            {
                BlurOntoWalls(viewport, eye);
            }

            using (_prof.Group("MergeWallLayer"))
            {
                MergeWallLayer(viewport);
            }

            BindRenderTargetFull(viewport.RenderTarget);
            GL.Viewport(0, 0, viewport.Size.X, viewport.Size.Y);
            CheckGlError();

            Array.Clear(_lightsToRenderList, 0, count);

            _lightingReady = true;
        }


        private void FlushLights()
        {
            if (_lightVertexIndex == 0)
                return;

            BindVertexArray(_lightVao.Handle);
            CheckGlError();

            _lightVbo.Reallocate(new Span<LightVertex>(_lightVertexData, 0, _lightVertexIndex));
            _lightEbo.Reallocate(new Span<ushort>(_lightIndexData, 0, _lightIndexIndex));

            var type = MapPrimitiveType(GetQuadBatchPrimitiveType());
            GL.DrawElements(type, _lightIndexIndex, DrawElementsType.UnsignedShort, 0);
            _debugStats.LastGLDrawCalls += 1;
            CheckGlError();

            _lightIndexIndex = 0;
            _lightVertexIndex = 0;
        }

        // TODO LIGHTING PARALLELIZE
        // Also just clean up this mess.
        // This is a giant query state.
        private static bool LightQuery(ref (
            Clyde clyde,
            int count,
            int shadowCastingCount,
            TransformSystem xformSystem,
            EntityQuery<TransformComponent> xforms,
            Box2 worldAABB,
            int textureHeight,
            DepthDrawInstance[] instances,
            Vector2 eyePos) state,
            in ComponentTreeEntry<PointLightComponent> value)
        {
            ref var count = ref state.count;
            ref var shadowCount = ref state.shadowCastingCount;

            // If there are too many lights, exit the query
            if (count >= state.clyde._maxLights)
                return false;

            var (light, transform) = value;
            var (lightPos, rot) = state.xformSystem.GetWorldPositionRotation(transform, state.xforms);
            lightPos += rot.RotateVec(light.Offset);
            var circle = new Circle(lightPos, light.Radius);

            // If the light doesn't touch anywhere the camera can see, it doesn't matter.
            // The tree query is not fully accurate because the viewport may be rotated relative to a grid.
            if (!circle.Intersects(state.worldAABB))
                return true;

            var distanceSquared = (state.worldAABB.Center - lightPos).LengthSquared();

            var angle = light.MaskAutoRotate
                ? (float) (light.Rotation + rot)
                : (float) light.Rotation;

            float index = -1;
            if (light.CastShadows)
            {
                index = ImageIndexToV(shadowCount, state.textureHeight);
                if (shadowCount < state.instances.Length)
                    state.instances[shadowCount] = new(lightPos - state.eyePos, index, 1f);

                shadowCount++;
            }

            var props = new LightProperties(
                light.Color,
                lightPos,
                light.Radius,
                light.Energy,
                light.Softness,
                index,
                angle
            );

            state.clyde._lightsToRenderList[count++] = new PointLight(props, distanceSquared, light.CastShadows, light.MaskPrototype.TextureBox);

            return true;
        }

        private sealed class LightCapacityComparer : IComparer<PointLight>
        {
            public int Compare(PointLight x, PointLight y)
            {
                if (x.CastShadows == y.CastShadows)
                    return 0;

                return x.CastShadows ? 1 : -1;
            }
        }

        private sealed class ShadowCapacityComparer : IComparer<PointLight>
        {
            public int Compare(PointLight x, PointLight y)
            {
                return x.DistFromCentreSq.CompareTo(y.DistFromCentreSq);
            }
        }

        private (int count, Box2 expandedBounds) GetLightsToRender(
            IEye eye,
            in Box2Rotated worldBounds,
            in Box2 worldAABB)
        {
            using var _ = _prof.Group(nameof(GetLightsToRender));
            var lightTreeSys = _entitySystemManager.GetEntitySystem<LightTreeSystem>();
            var xformSystem = _entitySystemManager.GetEntitySystem<TransformSystem>();

            // Use worldbounds for this one as we only care if the light intersects our actual bounds
            var xforms = _entityManager.GetEntityQuery<TransformComponent>();
            var state = (this, count: 0, shadowCastingCount: 0, xformSystem, xforms, worldAABB, ShadowTexture.Height, instances: _lightInstancesBuffer, eye.Position.Position);

            foreach (var (uid, comp) in lightTreeSys.GetIntersectingTrees(eye.Position.MapId, worldAABB))
            {
                var bounds = xformSystem.GetInvWorldMatrix(uid, xforms).TransformBox(worldBounds);
                comp.Tree.QueryAabb(ref state, LightQuery, bounds);
            }

            _shadowCastingLightCount = state.shadowCastingCount;
            if (_shadowCastingLightCount > _maxShadowcastingLights)
            {
                // There are too many lights casting shadows to fit in the scene.
                // This check must occur before occluder expansion, or else bad things happen.

                // First, partition the array based on whether the lights are shadow casting or not
                // (non shadow casting lights should be the first partition, shadow casting lights the second)
                Array.Sort(_lightsToRenderList, 0, state.count, _lightCap);

                // Next, sort just the shadow casting lights by distance.
                Array.Sort(_lightsToRenderList, state.count - state.shadowCastingCount, state.shadowCastingCount, _shadowCap);

                // Then effectively delete the furthest lights, by setting the end of the array to exclude N
                // number of shadow casting lights (where N is the number above the max number per scene.)
                state.count -= state.shadowCastingCount - _maxShadowcastingLights;

                // Reassign shadow map indices
                _shadowCastingLightCount = 0;
                foreach (ref var light in _lightsToRenderList.AsSpan()[(state.count - _maxShadowcastingLights)..])
                {
                    DebugTools.Assert(light.CastShadows);
                    var index = ImageIndexToV(_shadowCastingLightCount, ShadowTexture.Height);
                    light.Properties.Index = index;
                    _lightInstancesBuffer[_shadowCastingLightCount] = new(light.Properties.LightPos - eye.Position.Position, index, 1f);
                    _shadowCastingLightCount++;
                }

                DebugTools.AssertEqual(_shadowCastingLightCount, _maxShadowcastingLights);
            }

            // When culling occluders later, we can't just remove any occluders outside the worldBounds.
            // As they could still affect the shadows of (large) light sources.
            // We expand the world bounds so that it encompasses the center of every light source.
            // This should make it so no culled occluder can make a difference.
            // (if the occluder is in the current lights at all, it's still not between the light and the world bounds).
            var expandedBounds = worldAABB;

            // TODO SIMD
            for (var i = 0; i < state.count; i++)
            {
                expandedBounds = expandedBounds.ExtendToContain(_lightsToRenderList[i].Properties.LightPos);
            }

            _debugStats.TotalLights += state.count;
            _debugStats.ShadowLights += _shadowCastingLightCount;

            return (state.count, expandedBounds);
        }

        private void BlurLights(Viewport viewport, IEye eye)
        {
            using var _ = DebugGroup(nameof(BlurLights));

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
            using var _ = DebugGroup(nameof(BlurOntoWalls));

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

            BindRenderTargetFull(viewport.LightRenderTarget);

            GL.Viewport(0, 0, viewport.LightRenderTarget.Size.X, viewport.LightRenderTarget.Size.Y);
            CheckGlError();
            GL.Disable(EnableCap.Blend);
            CheckGlError();

            var shader = _loadedShaders[_mergeWallLayerShaderHandle].Program;
            shader.Use();

            var tex = viewport.WallBleedIntermediateRenderTarget2.Texture;

            SetupGlobalUniformsImmediate(shader, tex);

            SetTexture(TextureUnit.Texture0, tex);

            shader.SetUniformTextureMaybe(UniIMainTexture, TextureUnit.Texture0);

            BindVertexArray(_occlusionMaskVao.Handle);
            CheckGlError();

            GL.DrawElements(GetQuadGLPrimitiveType(),
                _occluderCount * GetQuadBatchIndexCount(),
                DrawElementsType.UnsignedShort,
                IntPtr.Zero);
            CheckGlError();

            GL.Enable(EnableCap.Blend);
            CheckGlError();
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

            GL.StencilMask(0xFF);
            CheckGlError();
            GL.StencilFunc(StencilFunction.Always, 0, 0);
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
            var lightMapSampleParameters = new TextureSampleParameters { Filter = true };

            viewport.LightRenderTarget?.Dispose();
            viewport.WallMaskRenderTarget?.Dispose();
            viewport.WallBleedIntermediateRenderTarget1?.Dispose();
            viewport.WallBleedIntermediateRenderTarget2?.Dispose();

            viewport.WallMaskRenderTarget = CreateRenderTarget(viewport.Size, RenderTargetColorFormat.R8,
                name: $"{viewport.Name}-{nameof(viewport.WallMaskRenderTarget)}");

            viewport.LightRenderTarget = CreateRenderTarget(lightMapSize,
                new RenderTargetFormatParameters(lightMapColorFormat, hasDepthStencil: true),
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

        private void MaxShadowcastingLightsChanged(int newValue)
        {
            _maxShadowcastingLights = newValue;
            DebugTools.Assert(_maxLights >= _maxShadowcastingLights);

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_shadowRenderTarget != null)
                DeleteRenderTexture(_shadowRenderTarget.Handle);

            // Shadow FBO.
            _shadowRenderTarget = CreateRenderTarget((ShadowMapSize, _maxShadowcastingLights),
                new RenderTargetFormatParameters(
                    _hasGLFloatFramebuffers ? RenderTargetColorFormat.RG32F : RenderTargetColorFormat.Rgba8, true),
                new TextureSampleParameters { WrapMode = TextureWrapMode.Repeat, Filter = true },
                nameof(_shadowRenderTarget));

            Array.Resize(ref _lightInstancesBuffer, _maxShadowcastingLights);
        }

        private void SoftShadowsChanged(bool newValue)
        {
            _enableSoftShadows = newValue;
        }

        private void MaxLightsChanged(int value)
        {
            _maxLights = value;
            _lightsToRenderList = new PointLight[value];
            DebugTools.Assert(_maxLights >= _maxShadowcastingLights);
        }
    }
}
