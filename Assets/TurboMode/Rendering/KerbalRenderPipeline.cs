using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

namespace TurboMode.Rendering
{
    public class KrpRenderPipeline : RenderPipeline
    {
        private readonly KrpPipelineAsset renderPipelineAsset;

        RenderGraph m_RenderGraph;
        private RenderTexture sharedHdrDisplay0Target;

        static readonly ProfilerMarker s_KrpOpaqueMarker = new ProfilerMarker("KrpRenderPipeline opaque");
        static readonly ProfilerMarker s_KrpTransparentMarker = new ProfilerMarker("KrpRenderPipeline transparent");
        static readonly ProfilerMarker s_KrpSubmitMarker = new ProfilerMarker("KrpRenderPipeline context submit");
        static readonly ProfilingSampler cameraSampler = new ProfilingSampler("KRP camera");

        static Material deferredLighting;
        static Material deferredScreenSpaceShadows;
        static Material deferredReflections;
        static Mesh fullScreenTriangle;

        // https://docs.unity3d.com/Manual/shader-predefined-pass-tags-built-in.html
        private static readonly ShaderTagId deferredPassTag = new ShaderTagId("DEFERRED");
        private static readonly ShaderTagId transparentPassTag = new ShaderTagId("SRPDefaultUnlit");
        private static readonly ShaderTagId[] forwardOpaqueTags = {
            new ShaderTagId("FORWARDBASE"),
        };
        private static readonly ShaderTagId[] transparentPassTags = {
            new ShaderTagId("FORWARDBASE"), // "KSP2/Scenery/Standard (Transparent)"
            new ShaderTagId("ForwardAdd"),
            new ShaderTagId("SRPDefaultUnlit"),
        };


        CommandBuffer commandBuffer = new CommandBuffer();
        int frameIndex = 0;
        bool intitialized;

        public KrpRenderPipeline(KrpPipelineAsset asset)
        {
            renderPipelineAsset = asset;

            // shader names differ depending if running this in KSP2 or testing in unity project
            Shader shader;
            shader = Shader.Find("Hidden/Internal-DeferredShading");
            if (shader is null)
            {
                shader = Shader.Find("Hidden/Graphics-DeferredShading");
            }
            deferredLighting = CoreUtils.CreateEngineMaterial(shader);

            //shader = Shader.Find("Hidden/Internal-ScreenSpaceShadows");
            //if (shader is null)
            //{
            //    shader = Shader.Find("Hidden/Graphics-ScreenSpaceShadows");
            //}
            //deferredScreenSpaceShadows = CoreUtils.CreateEngineMaterial(shader);

            //shader = Shader.Find("Hidden/Internal-DeferredReflections");
            //deferredReflections = CoreUtils.CreateEngineMaterial(shader);

            m_RenderGraph = new RenderGraph("KRP Render Graph");
            commandBuffer.name = "KRP reusable";

            InitResources();
            RTHandles.Initialize(Screen.width, Screen.height);
        }

        private void InitResources()
        {
            if (!fullScreenTriangle)
            {
                fullScreenTriangle = new Mesh
                {
                    name = "My Post-Processing Stack Full-Screen Triangle",
                    vertices = new Vector3[] {
                        new Vector3(-1f, -1f, 0f),
                        new Vector3(-1f,  3f, 0f),
                        new Vector3( 3f, -1f, 0f)
                    },
                    triangles = new int[] { 0, 1, 2 },
                };
                fullScreenTriangle.UploadMeshData(true);
            }
        }

        protected override void Dispose(bool disposing)
        {
            m_RenderGraph.Cleanup();
            m_RenderGraph = null;
            fullScreenTriangle = null;
        }

        class KrpCameraData
        {
            public Camera camera;
            // null until the graph's begin for the camera is run.
            public CullingResults cullingResults;
        }

        struct GBuffers
        {
            public TextureHandle gBuffer0;
            public TextureHandle gBuffer1;
            public TextureHandle normals;
            public TextureHandle emissive;
            public TextureHandle depth;
        }

        class DeferredOpaqueGBufferData
        {
            public KrpCameraData cameraData;
            public GBuffers gbuffers;
        }

        class DeferredDefaultReflections
        {
            public GBuffers gbuffers;
            public TextureHandle temp;
            public TextureHandle lightBuffer;
            public Material reflectionMaterial;
            internal CommandBuffer[] before;
            internal CommandBuffer[] after;
        }

        class DefferedCollectShadows
        {
            public TextureHandle deferredDepth;
            public TextureHandle shadowMap;
            public TextureHandle screenSpaceShadowMap;
        }

        class DeferredOpaqueLighting
        {
            public KrpCameraData cameraData;
            public TextureHandle result;
            public TextureHandle gbuffer0;
            public TextureHandle gbuffer1;
            public TextureHandle gbuffer2;
            public TextureHandle gbufferDepth;
            public Material lightMaterial;
        }

        class BlitCopy
        {
            public TextureHandle source;
            public TextureHandle dest;
        }

        class DrawSkybox
        {
            public TextureHandle target;
            public Camera camera;
            public TextureHandle targetDepth;
        }

        class ForwardOpaque
        {
            public Camera camera;
            public RendererListHandle forwardOpaqueRenderers;
            public TextureHandle output;
            public TextureHandle depth;
        }

        class ForwardTransparent
        {
            public KrpCameraData cameraData;
            public RendererListHandle forwardTransparentRenderers;
            public TextureHandle output;
            public TextureHandle depth;
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {

            BeginFrameRendering(context, cameras);

            bool usedDisplayTarget = false;

            var mainDisplay = Display.main;
            RTHandles.SetReferenceSize(mainDisplay.renderingWidth, mainDisplay.renderingHeight);

            InitResources();
            if (!sharedHdrDisplay0Target || sharedHdrDisplay0Target.width != mainDisplay.renderingWidth || sharedHdrDisplay0Target.height != mainDisplay.renderingHeight)
            {
                var hdrDisplayDesc = new RenderTextureDescriptor(mainDisplay.renderingWidth, mainDisplay.renderingHeight, RenderTextureFormat.DefaultHDR);
                sharedHdrDisplay0Target = new RenderTexture(hdrDisplayDesc);
                sharedHdrDisplay0Target.name = "KRP HDR shared buffer";
            }

            CommandBuffer cmdRG = CommandBufferPool.Get("KRP main");
            RenderGraphParameters rgParams = new RenderGraphParameters()
            {
                commandBuffer = cmdRG,
                scriptableRenderContext = context,
                currentFrameIndex = Time.frameCount
            };
            //m_RenderGraph.Begin(rgParams);

            using (m_RenderGraph.RecordAndExecute(rgParams))
            {
                var sharedHdrDisplay0TargetHandle = m_RenderGraph.ImportBackbuffer(sharedHdrDisplay0Target);

                // Iterate over all Cameras
                foreach (Camera camera in cameras)
                {
                    if (camera.targetTexture is null)
                    {
                        CreateCameraGraph(context, camera, m_RenderGraph, sharedHdrDisplay0TargetHandle);
                        usedDisplayTarget = true;
                    }
                    else
                    {
                        var rttHandle = m_RenderGraph.ImportBackbuffer(camera.targetTexture);
                        var hdrBufferDesc = new TextureDesc(camera.targetTexture.width, camera.targetTexture.height)
                        {
                            colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                            clearBuffer = true,
                            clearColor = Color.black,
                            //width = camera.targetTexture.width,
                            //height = camera.targetTexture.height,
                            dimension = TextureDimension.Tex2D,
                            slices = 1,
                            msaaSamples = MSAASamples.None,
                        };
                        var hdrBufferHandle = m_RenderGraph.CreateTexture(hdrBufferDesc);
                        var cameraOut = CreateCameraGraph(context, camera, m_RenderGraph, hdrBufferHandle);
                        CreateBlit(m_RenderGraph, cameraOut, ref rttHandle);
                    }
                }

                //m_RenderGraph.Execute();

            }
            context.ExecuteCommandBuffer(cmdRG);
            CommandBufferPool.Release(cmdRG);

            if (usedDisplayTarget)
            {
                var cmd = CommandBufferPool.Get("KRP HDR blit");
                cmd.Blit(sharedHdrDisplay0Target, BuiltinRenderTextureType.CameraTarget);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                cmd.Release();
            }

            context.Submit();

            EndFrameRendering(context, cameras);

            m_RenderGraph.EndFrame();
            frameIndex++;
        }

        private TextureHandle CreateCameraGraph(ScriptableRenderContext context, Camera camera, RenderGraph graph, TextureHandle cameraTarget)
        {
            TextureHandle result;

            var cameraData = CameraBegin(context, graph, camera);

            var gBufferPass = CreateGBufferPass(graph, cameraData, cameraTarget);
            result = gBufferPass.gbuffers.emissive;

            //var beforeReflectionCmd = camera.GetCommandBuffers(CameraEvent.BeforeReflections);
            //var aftereReflectionCmd = camera.GetCommandBuffers(CameraEvent.AfterReflections);
            //if (beforeReflectionCmd.Length > 0 || aftereReflectionCmd.Length > 0)
            //{
            //    CreateDefferedDefaultReflectionsPass(m_RenderGraph, gBufferPass, cameraTarget, beforeReflectionCmd, aftereReflectionCmd);
            //}

            // need an actual shadow map
            //CollectScreenSpaceShadowsPass(renderGraph, gbuffers.depth, gbuffers.depth, camera);
            //var resolvedDepth = ResolveDepth(m_RenderGraph, gbuffers.depth, passAggregate, false);

            var lightPass = CreateDeferredOpaqueLightingPass(m_RenderGraph, cameraData, gBufferPass, result);
            result = lightPass.result;

            if (camera.clearFlags != CameraClearFlags.Nothing)
            {
                var skybox = CreateSkybox(camera, m_RenderGraph, result, gBufferPass.gbuffers.depth);
                result = skybox.target;
            }

            // forward opaque
            // I can't get this to work without also rendering deferred objects (with shaders that support forward) as forward
            //var forwardOpaque = CreateForwardOpaquePass(m_RenderGraph, cullingResults, camera, result, gBufferPass.gbuffers.depth);
            //result = forwardOpaque.output;

            // transparent "SRPDefaultUnlit"
            var forwardTransparent = CreateForwardTransparentPass(m_RenderGraph, cameraData, result, gBufferPass.gbuffers.depth);
            result = forwardTransparent.output;

            EndCameraRendering(context, camera);
            //Camera.SetupCurrent(camera);
            //context.InvokeOnRenderObjectCallback();

            //renderGraph.EndProfilingSampler(cameraSampler);

            return result;
        }

        private DeferredOpaqueGBufferData CreateGBufferPass(RenderGraph graph, KrpCameraData cameraData, TextureHandle lightingIn)
        {
            // TODO: check formats match KSP2
            TextureHandle diffuse = CreateColorTexture(graph, cameraData.camera, "GBUFFER diffuse", Color.black, RenderTextureFormat.ARGB32, true);
            TextureHandle specular = CreateColorTexture(graph, cameraData.camera, "GBUFFER specular", Color.black, RenderTextureFormat.ARGB32, true);
            TextureHandle normals = CreateColorTexture(graph, cameraData.camera, "GBUFFER normals", Color.black, RenderTextureFormat.ARGB2101010, false);
            TextureHandle depth = CreateDepthTexture(graph, cameraData.camera, "GBUFFER depth");

            using (var builder = graph.AddRenderPass<DeferredOpaqueGBufferData>("KRP Opaque GBUFFER pass " + cameraData.camera.name, out var passData))
            {
                passData.cameraData = cameraData;
                passData.gbuffers.gBuffer0 = builder.WriteTexture(diffuse);
                passData.gbuffers.gBuffer1 = builder.WriteTexture(specular);
                passData.gbuffers.normals = builder.WriteTexture(normals);
                passData.gbuffers.emissive = builder.ReadWriteTexture(lightingIn);
                passData.gbuffers.depth = builder.UseDepthBuffer(depth, DepthAccess.Write);

                builder.SetRenderFunc<DeferredOpaqueGBufferData>(RenderDeferredGBuffer);
                return passData;
            }
        }

        DefferedCollectShadows CollectScreenSpaceShadowsPass(RenderGraph graph, TextureHandle deferredDepth, TextureHandle shadowMap, Camera camera)
        {
            var screenSpaceShadowMap = CreateColorTexture(graph, camera, "ScreenSpaceShadowMap", Color.white);
            using (var builder = graph.AddRenderPass<DefferedCollectShadows>("KRP Shadows.CollectShadows", out var passData))
            {
                passData.deferredDepth = builder.ReadTexture(deferredDepth);
                passData.shadowMap = builder.ReadTexture(shadowMap);
                passData.screenSpaceShadowMap = builder.UseColorBuffer(screenSpaceShadowMap, 0);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((DefferedCollectShadows data, RenderGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture("_CameraDepthTexture", data.deferredDepth);
                    context.cmd.SetGlobalTexture("_ShadowMapTexture", data.shadowMap);
                    context.cmd.SetRenderTarget(data.screenSpaceShadowMap);
                    CoreUtils.DrawFullScreen(context.cmd, deferredScreenSpaceShadows);
                });

                return passData;
            }
        }

        DeferredDefaultReflections CreateDefferedDefaultReflectionsPass(RenderGraph graph, DeferredOpaqueGBufferData gBufferPass, TextureHandle target, CommandBuffer[] before, CommandBuffer[] after)
        {
            // KSP2 doesn't actually use unity's environmental lightmapping.  It has a cubemap attached to the deferred shader.
            // However, the decal and terrain blending command buffers run here.  So if they are present we have to at least set render targets etc.

            using (var builder = graph.AddRenderPass<DeferredDefaultReflections>("KRP reflection pass", out var passData))
            {

                passData.gbuffers.gBuffer0 = builder.ReadTexture(gBufferPass.gbuffers.gBuffer0);
                passData.gbuffers.gBuffer1 = builder.ReadTexture(gBufferPass.gbuffers.gBuffer1);
                passData.gbuffers.normals = builder.ReadTexture(gBufferPass.gbuffers.normals);
                passData.gbuffers.depth = builder.ReadTexture(gBufferPass.gbuffers.depth);
                //passData.temp = builder.CreateTransientTexture(target);
                passData.lightBuffer = builder.WriteTexture(target);
                passData.reflectionMaterial = deferredReflections;

                passData.before = before;
                passData.after = after;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((DeferredDefaultReflections data, RenderGraphContext context) =>
                {
                    // I worked out most of this before I realized I didn't need it :-\

                    //MaterialPropertyBlock properties = new MaterialPropertyBlock();
                    //properties.SetFloat("_LightAsQuad", 1f);
                    //properties.SetVector("unity_SpecCube0_BoxMax", new Vector4(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, 1));
                    //properties.SetVector("unity_SpecCube0_BoxMin", new Vector4(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, 1));
                    //properties.SetVector("unity_SpecCube0_ProbePosition", Vector4.zero);
                    //properties.SetVector("unity_SpecCube0_HDR", new Vector4(1f, 1f, 0f, 0f));
                    //properties.SetVector("unity_SpecCube1_ProbePosition", new Vector4(0f, 0f, 0f, 1f));

                    context.cmd.SetGlobalTexture("_CameraGBufferTexture0", data.gbuffers.gBuffer0);
                    context.cmd.SetGlobalTexture("_CameraGBufferTexture1", data.gbuffers.gBuffer1);
                    context.cmd.SetGlobalTexture("_CameraGBufferTexture2", data.gbuffers.normals);
                    context.cmd.SetGlobalTexture("_CameraDepthTexture", data.gbuffers.depth);

                    context.renderContext.ExecuteCommandBuffer(context.cmd);
                    context.cmd.Clear();
                    foreach (var cmd in data.before)
                    {
                        context.renderContext.ExecuteCommandBuffer(cmd);
                    }

                    //context.cmd.SetRenderTarget(data.temp);
                    //context.cmd.EnableShaderKeyword("UNITY_HDR_ON");
                    //DrawFullScreenTriangle(context.cmd, data.reflectionMaterial, properties, 0);

                    context.cmd.SetRenderTarget(data.lightBuffer);
                    //properties.SetTexture("_CameraReflectionsTexture", data.temp);
                    //DrawFullScreenTriangle(context.cmd, data.reflectionMaterial, properties, 1);

                    foreach (var cmd in data.after)
                    {
                        context.renderContext.ExecuteCommandBuffer(cmd);
                    }
                });

                return passData;
            }
        }

        DeferredOpaqueLighting CreateDeferredOpaqueLightingPass(RenderGraph graph, KrpCameraData cameraData, DeferredOpaqueGBufferData gBufferPass, TextureHandle target)
        {
            using (var builder = graph.AddRenderPass<DeferredOpaqueLighting>("KRP Deferred light pass " + cameraData.camera.name, out var passData))
            {
                passData.cameraData = cameraData;
                passData.lightMaterial = deferredLighting;

                passData.gbuffer0 = builder.ReadTexture(gBufferPass.gbuffers.gBuffer0);
                passData.gbuffer1 = builder.ReadTexture(gBufferPass.gbuffers.gBuffer1);
                passData.gbuffer2 = builder.ReadTexture(gBufferPass.gbuffers.normals);
                passData.gbufferDepth = builder.ReadTexture(gBufferPass.gbuffers.depth);
                passData.result = builder.WriteTexture(target);

                //builder.AllowPassCulling(false);

                builder.SetRenderFunc((DeferredOpaqueLighting data, RenderGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture("_CameraDepthTexture", data.gbufferDepth);
                    //context.cmd.SetGlobalTexture("_ShadowMapTexture", data.shadowMap);
                    context.cmd.SetGlobalTexture("_CameraGBufferTexture0", data.gbuffer0);
                    context.cmd.SetGlobalTexture("_CameraGBufferTexture1", data.gbuffer1);
                    context.cmd.SetGlobalTexture("_CameraGBufferTexture2", data.gbuffer2);

                    context.cmd.SetRenderTarget(data.result, data.gbufferDepth);

                    context.cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);

                    foreach (var light in data.cameraData.cullingResults.visibleLights)
                    {
                        MaterialPropertyBlock lightInfo = new MaterialPropertyBlock();

                        switch (light.lightType)
                        {
                            case LightType.Directional:
                                context.cmd.EnableShaderKeyword("DIRECTIONAL");
                                context.cmd.DisableShaderKeyword("POINT");

                                var forward = light.localToWorldMatrix * new Vector4(0, 0, 1, 0); // forward vector as a vector4
                                lightInfo.SetVector("_LightDir", forward);
                                lightInfo.SetColor("_LightColor", light.finalColor);
                                lightInfo.SetFloat("_LightAsQuad", 1);
                                break;
                            default:
                                continue;
                        }

                        context.cmd.DrawMesh(fullScreenTriangle, Matrix4x4.identity, passData.lightMaterial, 0, 0, lightInfo);
                    }

                    context.cmd.SetViewProjectionMatrices(data.cameraData.camera.worldToCameraMatrix, data.cameraData.camera.projectionMatrix);
                });

                return passData;
            }
        }

        private ForwardOpaque CreateForwardOpaquePass(RenderGraph graph, CullingResults cullingResults, Camera forCamera, TextureHandle src, TextureHandle depth)
        {
            using (var builder = graph.AddRenderPass<ForwardOpaque>("KRP Forward Opaque", out var passData))
            {
                passData.output = builder.ReadWriteTexture(src);
                passData.depth = builder.ReadTexture(depth);
                passData.camera = forCamera;

                // culling
                RendererListDesc rendererDesc_base_Opaque = new RendererListDesc(forwardOpaqueTags, cullingResults, forCamera)
                {
                    sortingCriteria = SortingCriteria.CommonOpaque,
                    renderQueueRange = RenderQueueRange.opaque,
                    rendererConfiguration = PerObjectData.None,
                };
                RendererListHandle handle = graph.CreateRendererList(rendererDesc_base_Opaque);
                passData.forwardOpaqueRenderers = builder.UseRendererList(handle);

                builder.SetRenderFunc((ForwardOpaque data, RenderGraphContext context) =>
                {
                    var camera = data.camera;

                    context.renderContext.SetupCameraProperties(camera);
                    context.cmd.SetRenderTarget(data.output, data.depth);

                    context.cmd.EnableShaderKeyword("UNITY_HDR_ON");
                    context.cmd.EnableShaderKeyword("DIRECTIONAL");

                    //ExecuteCommandBuffersForEvent(context.renderContext, camera, CameraEvent.BeforeForwardAlpha);
                    CoreUtils.DrawRendererList(context.renderContext, context.cmd, data.forwardOpaqueRenderers);
                    //ExecuteCommandBuffersForEvent(context.renderContext, camera, CameraEvent.AfterForwardAlpha);
                });

                return passData;
            }
        }

        private ForwardTransparent CreateForwardTransparentPass(RenderGraph graph, KrpCameraData cameraData, TextureHandle src, TextureHandle depth)
        {
            using (var builder = graph.AddRenderPass<ForwardTransparent>("KRP Forward Transparent", out var passData))
            {
                passData.output = builder.ReadWriteTexture(src);
                passData.depth = builder.ReadTexture(depth);
                passData.cameraData = cameraData;

                builder.SetRenderFunc((ForwardTransparent data, RenderGraphContext context) =>
                {
                    var camera = data.cameraData.camera;

                    // culling
                    RendererListDesc rendererDesc_base_Opaque = new RendererListDesc(transparentPassTags, cameraData.cullingResults, cameraData.camera)
                    {
                        sortingCriteria = SortingCriteria.CommonTransparent,
                        renderQueueRange = RenderQueueRange.transparent,
                        rendererConfiguration = (PerObjectData)0xfff, // PerObjectData.ReflectionProbes,
                    };
                    var rendererList = context.renderContext.CreateRendererList(rendererDesc_base_Opaque);

                    context.renderContext.SetupCameraProperties(camera);
                    CoreUtils.SetRenderTarget(context.cmd, passData.output, passData.depth);

                    context.cmd.EnableShaderKeyword("UNITY_HDR_ON");

                    context.cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                    //ExecuteCommandBuffersForEvent(context.renderContext, camera, CameraEvent.BeforeForwardAlpha);
                    CoreUtils.DrawRendererList(context.renderContext, context.cmd, rendererList);
                    //ExecuteCommandBuffersForEvent(context.renderContext, camera, CameraEvent.AfterForwardAlpha);
                });

                return passData;
            }
        }

        private BlitCopy CreateBlit(RenderGraph graph, TextureHandle src, ref TextureHandle dest, string name = "KRP blit")
        {
            using (var builder = graph.AddRenderPass<BlitCopy>(name, out var passData))
            {
                passData.source = builder.ReadTexture(src);
                passData.dest = dest = builder.WriteTexture(dest);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((BlitCopy data, RenderGraphContext context) =>
                {
                    context.cmd.Blit(data.source, data.dest);
                });

                return passData;
            }
        }

        private void BlitToDisplayBackBuffer(RenderGraph graph, TextureHandle source)
        {
            using (var builder = graph.AddRenderPass<BlitCopy>("KRP HDR to display", out var passData))
            {
                builder.AllowPassCulling(false);
                passData.source = builder.ReadTexture(source);
                builder.SetRenderFunc((BlitCopy data, RenderGraphContext context) =>
                {
                    // todo.. postprocess material?
                    context.cmd.Blit(null, BuiltinRenderTextureType.CameraTarget);
                });
            }
        }

        private void ClearHdrBuffer(RenderGraph graph, TextureHandle source)
        {
            using (var builder = graph.AddRenderPass<BlitCopy>("KRP clear HDR display", out var passData))
            {
                builder.AllowPassCulling(false);
                passData.source = builder.WriteTexture(source);
                builder.SetRenderFunc((BlitCopy data, RenderGraphContext context) =>
                {
                    //context.cmd.ClearRenderTarget(true, true, Color.black);
                });
            }
        }

        private BlitCopy ResolveDepth(RenderGraph graph, TextureHandle src, TextureHandle dest, bool allowPassCull = false)
        {
            using (var builder = graph.AddRenderPass<BlitCopy>("KRP depth copy", out var passData))
            {
                passData.source = src;
                passData.dest = dest;

                builder.WriteTexture(dest);
                builder.ReadTexture(src);
                builder.AllowPassCulling(allowPassCull);

                builder.SetRenderFunc((BlitCopy data, RenderGraphContext context) =>
                {
                    //var srcDepth = ((RenderTexture)data.source).depthBuffer;
                    //var srcDepth = data.source;
                    //var targetDepth = ((RenderTexture)data.dest).depthBuffer;
                    context.cmd.ResolveAntiAliasedSurface(src, dest);
                    //context.cmd.Blit(src, dest);
                    //context.cmd.Blit(src, ((RenderTexture)data.dest).depthBuffer);
                });

                return passData;
            }
        }

        private DrawSkybox CreateSkybox(Camera camera, RenderGraph graph, TextureHandle dest, TextureHandle destDepth)
        {
            using (var builder = graph.AddRenderPass<DrawSkybox>("KRP skybox", out var passData))
            {
                passData.target = builder.ReadWriteTexture(dest);
                passData.camera = camera;
                passData.targetDepth = builder.ReadTexture(destDepth);

                builder.SetRenderFunc((DrawSkybox data, RenderGraphContext context) =>
                {
                    context.cmd.SetRenderTarget(data.target, data.targetDepth);
                    context.cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                    // without flushing the buffer, the render target setting here doesn't always apply.
                    context.renderContext.ExecuteCommandBuffer(context.cmd);
                    context.cmd.Clear();

                    context.renderContext.DrawSkybox(data.camera);
                });

                return passData;
            }
        }

        private void ExecuteCommandBuffersForEvent(ScriptableRenderContext context, Camera camera, CameraEvent cameraEvent)
        {
            foreach (var buffer in camera.GetCommandBuffers(cameraEvent))
            {
                context.ExecuteCommandBuffer(buffer);
            }
        }

        private void ExecuteCommandBuffersForLightEvent(ScriptableRenderContext context, Light light, LightEvent lightEvent)
        {
            foreach (var buffer in light.GetCommandBuffers(lightEvent))
            {
                context.ExecuteCommandBuffer(buffer);
            }
        }

        private void RenderDeferredGBuffer(DeferredOpaqueGBufferData data, RenderGraphContext ctx)
        {
            RendererListDesc rendererDesc_base_Opaque = new RendererListDesc(deferredPassTag, data.cameraData.cullingResults, data.cameraData.camera)
            {
                sortingCriteria = SortingCriteria.CommonOpaque,
                renderQueueRange = RenderQueueRange.opaque,
                rendererConfiguration = PerObjectData.None,
            };
            var opaqueRenderers = ctx.renderContext.CreateRendererList(rendererDesc_base_Opaque);

            var camera = data.cameraData.camera;
            var gbuffer = ctx.renderGraphPool.GetTempArray<RenderTargetIdentifier>(4);
            gbuffer[0] = data.gbuffers.gBuffer0;
            gbuffer[1] = data.gbuffers.gBuffer1;
            gbuffer[2] = data.gbuffers.normals;
            gbuffer[3] = data.gbuffers.emissive;

            CoreUtils.SetRenderTarget(ctx.cmd, gbuffer, data.gbuffers.depth);
            ctx.renderContext.SetupCameraProperties(camera);

            ctx.cmd.EnableShaderKeyword("UNITY_HDR_ON");
            ctx.cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

            ExecuteCommandBuffersForEvent(ctx.renderContext, camera, CameraEvent.BeforeGBuffer);
            CoreUtils.DrawRendererList(ctx.renderContext, ctx.cmd, opaqueRenderers);
            ExecuteCommandBuffersForEvent(ctx.renderContext, camera, CameraEvent.AfterGBuffer);
        }

        private KrpCameraData CameraBegin(ScriptableRenderContext context, RenderGraph graph, Camera camera)
        {
            using (var builder = graph.AddRenderPass<KrpCameraData>("KRP begin camera", out var passData))
            {
                passData.camera = camera;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((KrpCameraData data, RenderGraphContext context) =>
                {
                    BeginCameraRendering(context.renderContext, data.camera);

                    // todo: Camera.onPreCull
                    data.camera.TryGetCullingParameters(out var cullingParameters);
                    data.cullingResults = context.renderContext.Cull(ref cullingParameters);

                    // todo: Camera.onPreRender

                    // todo: run legacy camera command buffers?
                });

                return passData;
            }
        }

        #region cribbed buffer creation //  https://github.com/cinight/CustomSRP/blob/2022.1/Assets/SRP0802_RenderGraph/SRP0802_RenderGraph.BasePass.cs


        private static TextureHandle CreateColorTexture(RenderGraph graph, Camera camera, string name)
        => CreateColorTexture(graph, camera, name, Color.black);

        private static TextureHandle CreateColorTexture(RenderGraph graph, Camera camera, string name, Color clearColor, RenderTextureFormat format = RenderTextureFormat.ARGB32, bool sRGB = false)
        {
            TextureDesc colorRTDesc = new TextureDesc(camera.pixelWidth, camera.pixelHeight)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(format, sRGB),
                depthBufferBits = 0,
                msaaSamples = MSAASamples.None,
                enableRandomWrite = false,
                clearBuffer = true,
                clearColor = Color.black,
                name = name
            };

            return graph.CreateTexture(colorRTDesc);
        }

        private static TextureHandle CreateDepthTexture(RenderGraph graph, Camera camera, string name)
        {
            TextureDesc colorRTDesc = new TextureDesc(camera.pixelWidth, camera.pixelHeight)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.Depth, false),
                depthBufferBits = DepthBits.Depth32,
                msaaSamples = MSAASamples.None,
                enableRandomWrite = false,
                clearBuffer = true,
                name = name,
            };

            return graph.CreateTexture(colorRTDesc);
        }
        #endregion
    }
}
