using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Lattice.Combat
{
    /// <summary>
    /// Draws the first-person rifle and arms (<see cref="Gun.ViewLayer"/>) in
    /// the world camera's own render, after its transparents and before post:
    /// depth cleared, with the rifle's own field of view, so the rifle never
    /// sinks into a wall and never stretches with the player's FOV.
    ///
    /// One pass instead of an overlay camera, which culled and occlusion-tested
    /// the whole world a second time (1.6 ms of main thread in a 1,000-player
    /// fight). The world camera culls the view layer with everything else;
    /// the renderer's own passes leave it out (Lattice > Setup sets their layer
    /// masks). The near and far planes stay the camera's, so fog and everything
    /// else reading them stay right.
    ///
    /// The rifle samples no shadow map (our own body, drawn for its shadow
    /// alone, would shade it in shapes that match nothing on screen) and no
    /// screen-space AO (the world's, behind it). The world's shade reaches it
    /// whole instead: the sun's light on it is scaled by <see cref="Sun"/>,
    /// down to what a shadow leaves (the light's shadow strength).
    /// </summary>
    public sealed class ViewModelFeature : ScriptableRendererFeature
    {
        /// <summary>The camera to draw in (the world camera; the gun sets it).</summary>
        public static Camera Camera;
        /// <summary>The view's vertical field of view, degrees; 0: nothing to draw.</summary>
        public static float Fov;
        /// <summary>How much of the sun reaches the view: 1 in the sun, 0 in the world's shade.</summary>
        public static float Sun = 1f;
        /// <summary>Measuring (--off fpview): don't draw it.</summary>
        public static bool Off;

        Pass pass;

        public override void Create()
        {
            pass = new Pass { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (Off || Fov <= 0f || Camera == null || renderingData.cameraData.camera != Camera)
                return;
            renderer.EnqueuePass(pass);
        }

        sealed class Pass : ScriptableRenderPass
        {
            static readonly List<ShaderTagId> Tags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit"),
            };
            static readonly int ShadowParams = Shader.PropertyToID("_MainLightShadowParams");
            static readonly int AoParams = Shader.PropertyToID("_AmbientOcclusionParam");
            static readonly int MainLightColor = Shader.PropertyToID("_MainLightColor");

            sealed class Data
            {
                public TextureHandle Color;
                public RendererListHandle Opaque, Transparent;
                public Matrix4x4 View, Projection, CameraProjection;
                public bool HasSun;
                public Vector4 SunColor, ViewSunColor;
            }

            public Pass()
            {
                profilingSampler = new ProfilingSampler("First-person view");
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var renderingData = frameData.Get<UniversalRenderingData>();
                var lightData = frameData.Get<UniversalLightData>();
                var resources = frameData.Get<UniversalResourceData>();

                using var builder = graph.AddRasterRenderPass<Data>("First-person view", out var data, profilingSampler);
                data.Color = resources.activeColorTexture;
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                // The forward pass's inputs (bound globally): the graph must keep them alive until here.
                if (resources.mainShadowsTexture.IsValid())
                    builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                if (resources.additionalShadowsTexture.IsValid())
                    builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
                if (resources.ssaoTexture.IsValid())
                    builder.UseTexture(resources.ssaoTexture, AccessFlags.Read);

                var layer = 1 << Gun.ViewLayer;
                var opaque = RenderingUtils.CreateDrawingSettings(Tags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                data.Opaque = graph.CreateRendererList(new RendererListParams(renderingData.cullResults, opaque, new FilteringSettings(RenderQueueRange.opaque, layer)));
                var transparent = RenderingUtils.CreateDrawingSettings(Tags, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
                data.Transparent = graph.CreateRendererList(new RendererListParams(renderingData.cullResults, transparent, new FilteringSettings(RenderQueueRange.transparent, layer)));
                builder.UseRendererList(data.Opaque);
                builder.UseRendererList(data.Transparent);

                var camera = cameraData.camera;
                float aspect = (float)cameraData.scaledWidth / cameraData.scaledHeight;
                data.View = cameraData.GetViewMatrix();
                data.CameraProjection = cameraData.GetProjectionMatrix();
                data.Projection = Matrix4x4.Perspective(Fov, aspect, camera.nearClipPlane, camera.farClipPlane);

                // The sun's color as the forward pass had it (w 1: no baked
                // subtractive lighting here), and as much of it as reaches the view.
                int main = lightData.mainLightIndex;
                data.HasSun = main >= 0;
                if (data.HasSun)
                {
                    UniversalRenderPipeline.InitializeLightConstants_Common(lightData.visibleLights, main, out _, out var color, out _, out _, out _);
                    color.w = 1f;
                    var light = lightData.visibleLights[main].light;
                    float shade = light != null && light.shadows != LightShadows.None ? 1f - light.shadowStrength : 1f;
                    float k = Mathf.Lerp(shade, 1f, Mathf.Clamp01(Sun));
                    data.SunColor = color;
                    data.ViewSunColor = new Vector4(color.x * k, color.y * k, color.z * k, 1f);
                }
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) =>
                {
                    var cmd = ctx.cmd;
                    bool flipped = ctx.GetTextureUVOrigin(d.Color) == TextureUVOrigin.BottomLeft;
                    cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1f, 0);
                    // No shadow map or AO on the rifle (strength 0): nothing lit draws
                    // after this in the camera, and each camera sets both again.
                    cmd.SetGlobalVector(ShadowParams, Vector4.zero);
                    cmd.SetGlobalVector(AoParams, Vector4.zero);
                    if (d.HasSun)
                        cmd.SetGlobalVector(MainLightColor, d.ViewSunColor);
                    RenderingUtils.SetViewAndProjectionMatrices(cmd, d.View, GL.GetGPUProjectionMatrix(d.Projection, flipped), false);
                    cmd.DrawRendererList(d.Opaque);
                    cmd.DrawRendererList(d.Transparent);
                    RenderingUtils.SetViewAndProjectionMatrices(cmd, d.View, GL.GetGPUProjectionMatrix(d.CameraProjection, flipped), false);
                    if (d.HasSun)
                        cmd.SetGlobalVector(MainLightColor, d.SunColor);
                });
            }
        }
    }
}
