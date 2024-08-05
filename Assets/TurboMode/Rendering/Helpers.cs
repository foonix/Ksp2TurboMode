using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace TurboMode.Rendering
{
    public class Helpers
    {

        #region cribbed buffer creation //  https://github.com/cinight/CustomSRP/blob/2022.1/Assets/SRP0802_RenderGraph/SRP0802_RenderGraph.BasePass.cs
        private static TextureHandle CreateColorTexture(RenderGraph graph, Camera camera, string name)
        => CreateColorTexture(graph, camera, name, Color.black);

        internal static TextureHandle CreateColorTexture(RenderGraph graph, Camera camera, string name, Color clearColor, RenderTextureFormat format = RenderTextureFormat.ARGB32, bool sRGB = false)
        {
            TextureDesc colorRTDesc = new(camera.pixelWidth, camera.pixelHeight)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(format, sRGB),
                depthBufferBits = 0,
                msaaSamples = MSAASamples.None,
                enableRandomWrite = false,
                clearBuffer = true,
                clearColor = clearColor,
                name = name
            };

            return graph.CreateTexture(colorRTDesc);
        }

        internal static TextureHandle CreateDepthTexture(RenderGraph graph, Camera camera, string name)
        {
            TextureDesc colorRTDesc = new(camera.pixelWidth, camera.pixelHeight)
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