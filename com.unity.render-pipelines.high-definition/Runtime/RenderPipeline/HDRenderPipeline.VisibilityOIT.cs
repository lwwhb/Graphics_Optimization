using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using Unity.Collections;

namespace UnityEngine.Rendering.HighDefinition
{
    public partial class HDRenderPipeline
    {
        struct VBufferOITOutput
        {
            public bool valid;
            public TextureHandle stencilBuffer;
            public ComputeBufferHandle histogramBuffer;
            public RenderBRGBindingData BRGBindingData;

            public static VBufferOITOutput NewDefault()
            {
                return new VBufferOITOutput()
                {
                    valid = false,
                    stencilBuffer = TextureHandle.nullHandle,
                    BRGBindingData = RenderBRGBindingData.NewDefault()
                };
            }

            public VBufferOITOutput Read(RenderGraphBuilder builder)
            {
                VBufferOITOutput readVBuffer = VBufferOITOutput.NewDefault();
                readVBuffer.valid = valid;
                readVBuffer.stencilBuffer = builder.ReadTexture(stencilBuffer);
                if (!valid)
                    return readVBuffer;

                readVBuffer.histogramBuffer = builder.ReadComputeBuffer(histogramBuffer);
                readVBuffer.BRGBindingData = BRGBindingData;
                return readVBuffer;
            }
        }

        internal bool IsVisibilityOITPassEnabled()
        {
            return currentAsset != null && currentAsset.VisibilityOITMaterial != null;
        }

        class VBufferOITCountPassData
        {
            public FrameSettings frameSettings;
            public RendererListHandle rendererList;
            public RenderBRGBindingData BRGBindingData;
        }

        void RenderVBufferOITCount(RenderGraph renderGraph, TextureHandle colorBuffer, HDCamera hdCamera, CullingResults cull, ref PrepassOutput output)
        {
            output.vbufferOIT = VBufferOITOutput.NewDefault();

            var BRGBindingData = RenderBRG.GetRenderBRGMaterialBindingData();
            if (!IsVisibilityOITPassEnabled() || !BRGBindingData.valid)
            {
                output.vbufferOIT.stencilBuffer = renderGraph.defaultResources.blackTextureXR;
                return;
            }

            output.vbufferOIT.valid = true;

            using (var builder = renderGraph.AddRenderPass<VBufferOITCountPassData>("VBufferOITCount", out var passData, ProfilingSampler.Get(HDProfileId.VBufferOITCount)))
            {
                builder.AllowRendererListCulling(false);

                FrameSettings frameSettings = hdCamera.frameSettings;

                passData.frameSettings = frameSettings;

                output.vbufferOIT.stencilBuffer = builder.UseDepthBuffer(renderGraph.CreateTexture(new TextureDesc(Vector2.one, true, true)
                {
                    depthBufferBits = DepthBits.Depth24,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    name = "VisOITStencilCount"
                }), DepthAccess.ReadWrite);

                passData.BRGBindingData = BRGBindingData;
                passData.rendererList = builder.UseRendererList(
                   renderGraph.CreateRendererList(CreateOpaqueRendererListDesc(
                        cull, hdCamera.camera,
                        HDShaderPassNames.s_VBufferOITCountName,
                        m_CurrentRendererConfigurationBakedLighting,
                        new RenderQueueRange() { lowerBound = (int)HDRenderQueue.Priority.OrderIndependentTransparent, upperBound = (int)(int)HDRenderQueue.Priority.OrderIndependentTransparent })));

                builder.SetRenderFunc(
                    (VBufferOITCountPassData data, RenderGraphContext context) =>
                    {
                        data.BRGBindingData.globalGeometryPool.BindResourcesGlobal(context.cmd);
                        DrawTransparentRendererList(context, data.frameSettings, data.rendererList);
                    });
            }

            output.vbufferOIT.histogramBuffer = ComputeOITTiledHistogram(renderGraph, new Vector2Int((int)hdCamera.screenSize.x, (int)hdCamera.screenSize.y), hdCamera.viewCount, output.vbufferOIT.stencilBuffer);

            output.vbufferOIT.BRGBindingData = BRGBindingData;
        }

        class OITTileHistogramPassData
        {
            public int tileSize;
            public Vector2Int screenSize;
            public ComputeShader cs;
            public Texture2D ditherTexture;
            public TextureHandle stencilBuffer;
            public ComputeBufferHandle histogramBuffer;
        }

        ComputeBufferHandle ComputeOITTiledHistogram(RenderGraph renderGraph, Vector2Int screenSize, int viewCount, TextureHandle stencilBuffer)
        {
            ComputeBufferHandle histogramBuffer = ComputeBufferHandle.nullHandle;
            using (var builder = renderGraph.AddRenderPass<OITTileHistogramPassData>("OITTileHistogramPassData", out var passData, ProfilingSampler.Get(HDProfileId.OITHistogram)))
            {
                builder.AllowRendererListCulling(false);
                passData.cs = defaultResources.shaders.oitTileHistogramCS;
                passData.screenSize = screenSize;
                passData.tileSize = 128;
                passData.ditherTexture = defaultResources.textures.blueNoise128RTex;
                passData.stencilBuffer = builder.ReadTexture(stencilBuffer);                
                passData.histogramBuffer = builder.WriteComputeBuffer(renderGraph.CreateComputeBuffer(new ComputeBufferDesc(passData.tileSize * passData.tileSize, sizeof(uint), ComputeBufferType.Raw) { name = "OITHistogram" }));
                histogramBuffer = passData.histogramBuffer;

                builder.SetRenderFunc(
                    (OITTileHistogramPassData data, RenderGraphContext context) =>
                    {
                        int clearKernel = data.cs.FindKernel("MainClearHistogram");
                        context.cmd.SetComputeBufferParam(data.cs, clearKernel, HDShaderIDs._VisOITHistogramOutput, data.histogramBuffer);
                        context.cmd.DispatchCompute(data.cs, clearKernel, HDUtils.DivRoundUp(data.tileSize * data.tileSize, 64), 1, 1);

                        int histogramKernel = data.cs.FindKernel("MainCreateStencilHistogram");
                        context.cmd.SetComputeTextureParam(data.cs, histogramKernel, HDShaderIDs._OITDitherTexture, data.ditherTexture);
                        context.cmd.SetComputeTextureParam(data.cs, histogramKernel, HDShaderIDs._VisOITCount, (RenderTexture)data.stencilBuffer, 0, RenderTextureSubElement.Stencil);
                        context.cmd.SetComputeBufferParam(data.cs, histogramKernel, HDShaderIDs._VisOITHistogramOutput, data.histogramBuffer);
                        context.cmd.DispatchCompute(data.cs, histogramKernel, HDUtils.DivRoundUp(data.screenSize.x, 8), HDUtils.DivRoundUp(data.screenSize.y, 8), viewCount);
                    });
            }

            return histogramBuffer;
        }
    }
}
