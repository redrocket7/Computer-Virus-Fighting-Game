using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using static UnityEngine.Rendering.RenderGraphModule.Util.RenderGraphUtils;

/// <summary>
/// Fullscreen digital glitch driven by <see cref="LowHealthEffect"/>.
/// </summary>
public class LowHealthGlitchFeature : ScriptableRendererFeature
{
    class GlitchPass : ScriptableRenderPass
    {
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int TimeSeedId = Shader.PropertyToID("_TimeSeed");
        static readonly int BurstId = Shader.PropertyToID("_Burst");
        static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

        static readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();

        readonly Material material;

        public GlitchPass(Material mat)
        {
            material = mat;
            profilingSampler = new ProfilingSampler("Low Health Glitch");
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
                return;

            float intensity = LowHealthEffect.GlitchIntensity;
            if (intensity <= 0.001f)
                return;

            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (!resources.cameraColor.IsValid())
                return;

            TextureDesc desc = renderGraph.GetTextureDesc(resources.cameraColor);
            desc.name = "_LowHealthGlitchTemp";
            desc.clearBuffer = false;
            TextureHandle temp = renderGraph.CreateTexture(desc);

            // Copy camera color so the glitch pass can sample it.
            renderGraph.AddBlitPass(resources.cameraColor, temp, Vector2.one, Vector2.zero, passName: "LowHealthGlitch Copy");

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Low Health Glitch", out var passData, profilingSampler))
            {
                passData.material = material;
                passData.source = temp;
                passData.intensity = intensity;
                passData.burst = LowHealthEffect.GlitchBurst;
                passData.timeSeed = Time.unscaledTime;

                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    PropertyBlock.Clear();
                    PropertyBlock.SetTexture(BlitTextureId, data.source);
                    PropertyBlock.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    PropertyBlock.SetFloat(IntensityId, data.intensity);
                    PropertyBlock.SetFloat(BurstId, data.burst);
                    PropertyBlock.SetFloat(TimeSeedId, data.timeSeed);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, PropertyBlock);
                });
            }
        }

        class PassData
        {
            public Material material;
            public TextureHandle source;
            public float intensity;
            public float burst;
            public float timeSeed;
        }
    }

    [SerializeField] Shader shader;

    Material material;
    GlitchPass pass;

    public override void Create()
    {
        if (shader == null)
            shader = Shader.Find("Hidden/LowHealthGlitch");

        if (shader != null)
            material = CoreUtils.CreateEngineMaterial(shader);

        pass = new GlitchPass(material);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null || pass == null)
            return;

        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;

        if (LowHealthEffect.GlitchIntensity <= 0.001f)
            return;

        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
    }
}
