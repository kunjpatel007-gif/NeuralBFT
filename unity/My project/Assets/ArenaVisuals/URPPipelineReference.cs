using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Makes sure the Universal Render Pipeline is running in every build. The editor script
/// EnsureShaders keeps a copy of this asset in Resources pointing at the project's URP assets.
/// If a build ever starts without a render pipeline (so every URP material would come out pink,
/// black or missing), it is switched on here before the first scene loads. It also prints one
/// line to the log (the browser console on WebGL) saying which pipeline, quality level and
/// graphics API are active, so a broken build is easy to diagnose.
/// </summary>
public class URPPipelineReference : ScriptableObject
{
    [Tooltip("Pipeline used by desktop quality levels (PC_RPAsset).")]
    public RenderPipelineAsset pipeline;

    [Tooltip("Pipeline used by the Mobile quality level, which WebGL uses by default (Mobile_RPAsset).")]
    public RenderPipelineAsset mobilePipeline;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void EnsurePipeline()
    {
        int level = QualitySettings.GetQualityLevel();
        string[] names = QualitySettings.names;
        string levelName = level >= 0 && level < names.Length ? names[level] : "?";

        if (GraphicsSettings.currentRenderPipeline == null)
        {
            var reference = Resources.Load<URPPipelineReference>("URPPipelineReference");
            RenderPipelineAsset chosen = null;
            if (reference != null)
            {
                bool mobile = levelName.ToLowerInvariant().Contains("mobile");
                chosen = mobile && reference.mobilePipeline != null ? reference.mobilePipeline : reference.pipeline;
                if (chosen == null) chosen = reference.mobilePipeline;
            }

            if (chosen != null)
            {
                GraphicsSettings.defaultRenderPipeline = chosen;
                QualitySettings.renderPipeline = chosen;
                Debug.LogWarning($"[URP] No render pipeline was active; forced on: {chosen.name}");
            }
            else
            {
                Debug.LogError("[URP] No render pipeline is active and no URPPipelineReference was found in Resources. " +
                               "Open the project in the Unity editor once so EnsureShaders can create it, then rebuild.");
            }
        }

        RenderPipelineAsset active = GraphicsSettings.currentRenderPipeline;
        bool litFound = Shader.Find("Universal Render Pipeline/Lit") != null;
        string message = $"[URP] Pipeline: {(active != null ? active.name : "NONE")} | Quality: {levelName} | " +
                         $"Graphics: {SystemInfo.graphicsDeviceType} | URP Lit shader: {(litFound ? "found" : "MISSING")}";
        if (active == null || !litFound) Debug.LogError(message);
        else Debug.Log(message);
    }
}
