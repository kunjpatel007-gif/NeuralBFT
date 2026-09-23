using UnityEngine;

/// <summary>
/// Creates a deep space starfield background using Unity's particle system.
/// Drop this on any empty GameObject in the scene.
/// Spawns thousands of tiny glowing stars in a massive sphere around the camera.
/// </summary>
public class StarfieldBackground : MonoBehaviour
{
    void Start()
    {
        CreateStarfield();
        CreateGridFloor();
    }

    void CreateStarfield()
    {
        GameObject starGO = new GameObject("Starfield");
        starGO.transform.position = Vector3.zero;

        var ps = starGO.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.maxParticles = 3000;
        main.startLifetime = Mathf.Infinity;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.6f, 0.8f, 1.0f, 0.7f),  // Cool blue-white
            new Color(1.0f, 0.95f, 0.85f, 0.5f)  // Warm white
        );
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        main.loop = false; // Emit once

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0f, 3000)
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 80f; // Huge sphere of stars around the scene

        // Disable velocity, gravity, etc.
        var velocityOverLifetime = ps.velocityOverLifetime;
        velocityOverLifetime.enabled = false;

        // Make stars glow using additive rendering
        var renderer = starGO.GetComponent<ParticleSystemRenderer>();
        renderer.material = new Material(Shader.Find("Particles/Standard Unlit"));
        renderer.material.SetColor("_Color", Color.white);
        renderer.material.SetFloat("_Mode", 1); // Additive blend
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.minParticleSize = 0.001f;
        renderer.maxParticleSize = 0.005f;
    }

    void CreateGridFloor()
    {
        // Create a subtle holographic grid floor using a LineRenderer grid
        GameObject gridGO = new GameObject("GridFloor");
        gridGO.transform.position = new Vector3(0, -10f, 0);

        // Create grid lines
        float gridSize = 50f;
        float spacing = 2.5f;
        Color gridColor = new Color(0.15f, 0.35f, 0.5f, 0.15f);

        Material lineMat = new Material(Shader.Find("Sprites/Default"));
        lineMat.color = gridColor;

        for (float x = -gridSize; x <= gridSize; x += spacing)
        {
            CreateGridLine(gridGO.transform, lineMat, 
                new Vector3(x, 0, -gridSize), new Vector3(x, 0, gridSize), gridColor);
        }
        for (float z = -gridSize; z <= gridSize; z += spacing)
        {
            CreateGridLine(gridGO.transform, lineMat,
                new Vector3(-gridSize, 0, z), new Vector3(gridSize, 0, z), gridColor);
        }
    }

    void CreateGridLine(Transform parent, Material mat, Vector3 start, Vector3 end, Color color)
    {
        GameObject lineGO = new GameObject("GridLine");
        lineGO.transform.SetParent(parent);
        var lr = lineGO.AddComponent<LineRenderer>();
        lr.material = mat;
        lr.startColor = color;
        lr.endColor = color;
        lr.startWidth = 0.03f;
        lr.endWidth = 0.03f;
        lr.positionCount = 2;
        lr.SetPositions(new Vector3[] { start, end });
        lr.useWorldSpace = false;
    }
}
