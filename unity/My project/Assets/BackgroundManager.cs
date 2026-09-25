using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedurally generates a hyper-premium "Apple Presentation" style gradient background,
/// combined with a sparse, slow-moving "Dyson Void" particle system of data motes.
/// </summary>
public class BackgroundManager : MonoBehaviour
{
    void Start()
    {
        // Ensure the main camera clears to solid black behind our canvas
        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = Color.black;
        }

        CreateGradientBackground();
        CreateDataMotes();
    }

    void CreateGradientBackground()
    {
        // 1. Create a dynamic Canvas
        GameObject canvasObj = new GameObject("PremiumBackgroundCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = Camera.main;
        canvas.planeDistance = 100f; // Push it way back so it sits behind the 3D nodes
        
        canvasObj.AddComponent<CanvasScaler>();

        // 2. Create the RawImage to hold our procedural gradient
        GameObject bgObj = new GameObject("GradientImage");
        bgObj.transform.SetParent(canvasObj.transform, false);
        RawImage ri = bgObj.AddComponent<RawImage>();
        
        // Stretch to fill screen perfectly
        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // 3. Generate a silky smooth 1x256 Gradient Texture in memory
        int height = 256;
        Texture2D tex = new Texture2D(1, height);
        tex.wrapMode = TextureWrapMode.Clamp;
        
        Color topColor = new Color(0.0f, 0.0f, 0.0f, 1f); // Absolute Vantablack
        Color bottomColor = new Color(0.03f, 0.04f, 0.06f, 1f); // Deep Space Obsidian/Blue
        
        for (int y = 0; y < height; y++)
        {
            float t = (float)y / (height - 1);
            // Smoothstep curve makes the gradient falloff look extremely luxurious and soft
            t = t * t * (3f - 2f * t);
            tex.SetPixel(0, y, Color.Lerp(bottomColor, topColor, t));
        }
        tex.Apply();
        ri.texture = tex;
    }

    void CreateDataMotes()
    {
        // Generate the Deep Space ambient particles
        GameObject psObj = new GameObject("AmbientDataMotes");
        // Center them exactly in the middle of the nodes so they surround the whole network
        psObj.transform.position = Vector3.zero; 
        
        ParticleSystem ps = psObj.AddComponent<ParticleSystem>();
        
        var main = ps.main;
        main.duration = 10f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(15f, 25f); // Very slow fading
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f); // Extremely slow drifting
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.12f); // Tiny, subtle motes
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;
        
        // Pure green floaty dots as requested
        ParticleSystem.MinMaxGradient gradient = new ParticleSystem.MinMaxGradient();
        gradient.mode = ParticleSystemGradientMode.TwoColors;
        gradient.colorMin = new Color(0f, 1f, 0.2f, 0.6f); // Bright Green
        gradient.colorMax = new Color(0.2f, 1f, 0.5f, 0.6f); // Soft Green
        main.startColor = gradient;

        var emission = ps.emission;
        emission.rateOverTime = 15f; 

        var shape = ps.shape;
        // Use a massive hollow sphere to strictly keep them out of the node network's vicinity
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 45f; // Nodes are at 18, so this pushes them very far out
        shape.radiusThickness = 0.05f; // Forces them to spawn only on the extreme outer shell

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        // Zero out linear drift so they don't accidentally float inwards
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        
        // Make them move around using Orbital velocity. 
        // This makes them actively swirl around the perimeter shell, guaranteeing they never cross the nodes.
        vel.orbitalX = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);

        // Smoothly fade in and out so they don't pop abruptly
        var colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient alphaGrad = new Gradient();
        alphaGrad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { 
                new GradientAlphaKey(0f, 0f), 
                new GradientAlphaKey(1f, 0.2f), 
                new GradientAlphaKey(1f, 0.8f), 
                new GradientAlphaKey(0f, 1f) 
            }
        );
        colorOverLife.color = new ParticleSystem.MinMaxGradient(alphaGrad); // Fix: Wrap in MinMaxGradient

        // Apply a glowing particle material
        ParticleSystemRenderer psr = psObj.GetComponent<ParticleSystemRenderer>();
        // If URP shader is stripped from the build, fallback safely to Sprites/Default
        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particleShader == null) particleShader = Shader.Find("Sprites/Default");
        
        if (particleShader != null)
        {
            Material pMat = new Material(particleShader);
            // Ensure additive blending so they look like pure light
            pMat.SetFloat("_Blend", 1); // Additive
            psr.material = pMat;
        }
        
        // Fast-forward the particle simulation by 25 seconds so the screen is already full
        ps.Simulate(25f, true, true, false);
        ps.Play();
    }
}
