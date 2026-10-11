using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Lattice.World
{
    /// <summary>
    /// The clouds' shadows, on everything the sun lights: a cookie for the sun,
    /// redrawn each frame by CloudShadow.compute from the sky's own cloud layer
    /// (its cover, scale, height and wind, read off the sky's material), so each
    /// shadow lies where its cloud is, carried by the same wind. The cookie
    /// covers a square around the camera in the light's frame (past it, it
    /// repeats, too far off to tell). It also hands the sky the wind's drift, so
    /// both draw the same clouds.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class CloudShadows : MonoBehaviour
    {
        public ComputeShader Compute;
        /// <summary>Texels per side, and the square they cover (m, in the light's frame).</summary>
        public int Resolution = 1024;
        public float Size = 4096f;
        /// <summary>How much of the sun a cloud's shade takes.</summary>
        [Range(0f, 1f)] public float Strength = 0.7f;

        static readonly int CloudTexId = Shader.PropertyToID("_CloudTex"), CookieId = Shader.PropertyToID("_Cookie"),
            CookieSizeId = Shader.PropertyToID("_CookieSize"), CookieOffsetId = Shader.PropertyToID("_CookieOffset"),
            LightToWorldId = Shader.PropertyToID("_LightToWorld"), CloudParamsId = Shader.PropertyToID("_CloudParams"),
            CloudDriftId = Shader.PropertyToID("_CloudDrift"), CoverId = Shader.PropertyToID("_CloudCover"),
            ScaleId = Shader.PropertyToID("_CloudScale"), HeightId = Shader.PropertyToID("_CloudHeight"),
            WindId = Shader.PropertyToID("_CloudWind");

        Light sun;
        UniversalAdditionalLightData data;
        RenderTexture cookie;
        int kernel, groups;
        Vector2 drift;

        void OnEnable()
        {
            sun = GetComponent<Light>();
            if (Compute == null || !SystemInfo.supportsComputeShaders)
            {
                enabled = false;
                return;
            }
            if (!TryGetComponent(out data))
                data = gameObject.AddComponent<UniversalAdditionalLightData>();
            cookie = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                name = "Cloud shadows",
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            cookie.Create();
            kernel = Compute.FindKernel("Shadow");
            groups = (Resolution + 7) / 8;
            sun.cookie = cookie;
            data.lightCookieSize = new Vector2(Size, Size);
        }

        void OnDisable()
        {
            if (sun != null)
                sun.cookie = null;
            if (cookie != null)
            {
                cookie.Release();
                Destroy(cookie);
                cookie = null;
            }
            Shader.SetGlobalVector(CloudDriftId, Vector4.zero);
        }

        void LateUpdate()
        {
            var sky = RenderSettings.skybox;
            var cam = Camera.main;
            if (sky == null || cam == null || !sky.HasProperty(ScaleId))
                return;
            var wind = sky.GetVector(WindId);
            drift += new Vector2(wind.x, wind.y) * Time.deltaTime;
            Shader.SetGlobalVector(CloudDriftId, drift);

            // Around the camera, in the light's frame, a whole texel at a time (no crawl).
            var eye = transform.worldToLocalMatrix.MultiplyPoint3x4(cam.transform.position);
            float texel = Size / Resolution;
            var offset = new Vector2(Mathf.Round(eye.x / texel) * texel, Mathf.Round(eye.y / texel) * texel);
            data.lightCookieOffset = offset;

            Compute.SetTexture(kernel, CloudTexId, sky.GetTexture(CloudTexId));
            Compute.SetTexture(kernel, CookieId, cookie);
            Compute.SetVector(CookieSizeId, new Vector4(Resolution, Resolution, Size, Strength));
            Compute.SetVector(CookieOffsetId, offset);
            Compute.SetMatrix(LightToWorldId, transform.localToWorldMatrix);
            Compute.SetVector(CloudParamsId, new Vector4(sky.GetFloat(CoverId), sky.GetFloat(ScaleId), sky.GetFloat(HeightId), 0f));
            Compute.SetVector(CloudDriftId, drift);
            Compute.Dispatch(kernel, groups, groups, 1);
        }
    }
}
