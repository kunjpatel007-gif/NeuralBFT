#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Keeps URP switched on and its shaders in every build:
///  - assigns the project's URP asset as the default render pipeline, and to any quality level
///    that has none (a level without one makes that platform render with no URP at all);
///  - writes Resources/URPPipelineReference.asset, which forces URP on at runtime as a last resort;
///  - keeps "dummy" materials in Resources so the URP Lit, Unlit and Particles/Unlit shaders the
///    scripts create materials from at runtime are never stripped from a build.
/// Runs when the editor loads and again before every build; the build stops with a clear error
/// if no URP asset can be found.
/// </summary>
[InitializeOnLoad]
public class EnsureShaders : IPreprocessBuildWithReport
{
    const string PcPipelinePath = "Assets/Settings/PC_RPAsset.asset";
    const string MobilePipelinePath = "Assets/Settings/Mobile_RPAsset.asset";
    const string ReferencePath = "Assets/Resources/URPPipelineReference.asset";

    public int callbackOrder => -1000;

    static EnsureShaders()
    {
        // Wait until the asset database is ready
        EditorApplication.delayCall += () => Apply();
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        if (!Apply())
            throw new BuildFailedException("[EnsureShaders] No URP asset found at " + PcPipelinePath + " or " + MobilePipelinePath +
                                           ". The build would render without URP (pink/missing materials).");
    }

    static bool Apply()
    {
        if (!Directory.Exists("Assets/Resources"))
        {
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.Refresh();
        }

        bool ok = EnsurePipeline();

        // Shaders that scripts look up by name at runtime: a material referencing each keeps it in the build
        EnsureMaterial("Assets/Resources/DummyURPParticleMat.mat", "Universal Render Pipeline/Particles/Unlit");
        EnsureMaterial("Assets/Resources/DummyURPLitMat.mat", "Universal Render Pipeline/Lit");
        EnsureMaterial("Assets/Resources/DummyURPUnlitMat.mat", "Universal Render Pipeline/Unlit");

        AssetDatabase.SaveAssets();
        return ok;
    }

    static bool EnsurePipeline()
    {
        RenderPipelineAsset pc = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PcPipelinePath);
        RenderPipelineAsset mobile = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(MobilePipelinePath);
        RenderPipelineAsset fallback = pc != null ? pc : mobile;
        if (fallback == null)
        {
            Debug.LogError("[EnsureShaders] No URP asset found at " + PcPipelinePath + " or " + MobilePipelinePath);
            return false;
        }

        if (GraphicsSettings.defaultRenderPipeline == null)
        {
            GraphicsSettings.defaultRenderPipeline = fallback;
            Debug.LogWarning("[EnsureShaders] Default render pipeline was empty; set to " + fallback.name);
        }

        // Any quality level without a pipeline gets one (Mobile levels get the mobile asset)
        Object[] qualityAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
        if (qualityAssets != null && qualityAssets.Length > 0)
        {
            var quality = new SerializedObject(qualityAssets[0]);
            SerializedProperty levels = quality.FindProperty("m_QualitySettings");
            bool changed = false;
            for (int i = 0; levels != null && i < levels.arraySize; i++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                SerializedProperty pipeline = level.FindPropertyRelative("customRenderPipeline");
                if (pipeline == null || pipeline.objectReferenceValue != null) continue;

                string levelName = level.FindPropertyRelative("name")?.stringValue ?? "";
                bool isMobile = levelName.ToLowerInvariant().Contains("mobile");
                pipeline.objectReferenceValue = isMobile && mobile != null ? mobile : fallback;
                changed = true;
                Debug.LogWarning("[EnsureShaders] Quality level '" + levelName + "' had no render pipeline; assigned " + pipeline.objectReferenceValue.name);
            }
            if (changed) quality.ApplyModifiedPropertiesWithoutUndo();
        }

        // Runtime safety net
        var reference = AssetDatabase.LoadAssetAtPath<URPPipelineReference>(ReferencePath);
        if (reference == null)
        {
            reference = ScriptableObject.CreateInstance<URPPipelineReference>();
            reference.pipeline = fallback;
            reference.mobilePipeline = mobile != null ? mobile : fallback;
            AssetDatabase.CreateAsset(reference, ReferencePath);
        }
        else if (reference.pipeline == null || reference.mobilePipeline == null)
        {
            if (reference.pipeline == null) reference.pipeline = fallback;
            if (reference.mobilePipeline == null) reference.mobilePipeline = mobile != null ? mobile : fallback;
            EditorUtility.SetDirty(reference);
        }
        return true;
    }

    static void EnsureMaterial(string path, string shaderName)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            // Must match the runtime materials, which do not use GPU instancing
            if (existing.enableInstancing)
            {
                existing.enableInstancing = false;
                EditorUtility.SetDirty(existing);
            }
            return;
        }

        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError("[EnsureShaders] Shader not found: " + shaderName + " (is the URP package installed?)");
            return;
        }
        AssetDatabase.CreateAsset(new Material(shader) { enableInstancing = false }, path);
    }
}
#endif
