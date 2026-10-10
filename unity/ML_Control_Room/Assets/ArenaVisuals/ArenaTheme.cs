using System;
using UnityEngine;

/// <summary>
/// The arena's colour language, in one place: graphite surfaces, cool off-white lines and text,
/// and a small set of muted status colours. Everything is matte and desaturated; colour is used
/// to carry meaning, never to decorate, and nothing relies on glow.
/// </summary>
public static class ArenaTheme
{
    public enum NodeState { Verified, Trusted, Watched, Quarantined, Blacklisted }

    // Surfaces, lines and text
    public static readonly Color Ink         = new Color(0.028f, 0.030f, 0.034f); // graphite
    public static readonly Color Line        = new Color(0.72f, 0.76f, 0.80f);    // neutral linework
    public static readonly Color Accent      = new Color(0.45f, 0.62f, 0.78f);    // steel blue
    public static readonly Color TextPrimary = new Color(0.86f, 0.88f, 0.90f);
    public static readonly Color TextMuted   = new Color(0.48f, 0.52f, 0.56f);

    // Node trust states
    public static readonly Color Trusted     = new Color(0.44f, 0.68f, 0.57f); // muted green
    public static readonly Color Verified    = new Color(0.45f, 0.62f, 0.78f); // steel blue
    public static readonly Color Watched     = new Color(0.80f, 0.63f, 0.33f); // muted amber
    public static readonly Color Quarantined = new Color(0.77f, 0.35f, 0.31f); // muted red
    public static readonly Color Blacklisted = new Color(0.20f, 0.21f, 0.22f); // dark grey

    public static NodeState Classify(string status)
    {
        if (Is(status, "TRUSTED")) return NodeState.Trusted;
        if (Is(status, "WATCHED") || Is(status, "HIGH RISK")) return NodeState.Watched;
        if (Is(status, "QUARANTINED")) return NodeState.Quarantined;
        if (Is(status, "BLACKLISTED")) return NodeState.Blacklisted;
        return NodeState.Verified; // "VERIFIED" and anything unrecognised
    }

    public static Color StatusColor(string status) => StatusColor(Classify(status));

    public static Color StatusColor(NodeState state)
    {
        switch (state)
        {
            case NodeState.Trusted:     return Trusted;
            case NodeState.Watched:     return Watched;
            case NodeState.Quarantined: return Quarantined;
            case NodeState.Blacklisted: return Blacklisted;
            default:                    return Verified;
        }
    }

    /// <summary>How much of its own light a node's body gives off.</summary>
    public static float StatusEnergy(NodeState state)
    {
        switch (state)
        {
            case NodeState.Trusted:     return 1.00f;
            case NodeState.Watched:     return 0.90f;
            case NodeState.Quarantined: return 1.05f;
            case NodeState.Blacklisted: return 0.05f;
            default:                    return 0.85f;
        }
    }

    /// <summary>Only quarantined nodes are marked with a containment ring.</summary>
    public static float ShieldStrength(NodeState state)
    {
        switch (state)
        {
            case NodeState.Quarantined: return 1f;
            default:                    return 0f;
        }
    }

    /// <summary>Cool neutrals for the main partition, amber tones for a severed one.</summary>
    public static Color MessageColor(string messageType, int partitionId)
    {
        if (partitionId == 1)
        {
            switch (messageType)
            {
                case "PRE_PREPARE": return new Color(0.86f, 0.74f, 0.52f); // pale amber
                case "PREPARE":     return Quarantined;                    // muted red
                default:            return Watched;                        // amber (COMMIT and others)
            }
        }

        switch (messageType)
        {
            case "PRE_PREPARE": return TextPrimary; // off-white
            case "PREPARE":     return Verified;    // steel blue
            default:            return Line;        // neutral (COMMIT and others)
        }
    }

    public static Color ConsensusColor(string consensus)
    {
        if (Is(consensus, "POW"))  return Watched;                        // amber
        if (Is(consensus, "POS"))  return Trusted;                        // green
        if (Is(consensus, "DPOS")) return new Color(0.60f, 0.58f, 0.76f); // muted violet
        return Verified; // PBFT and anything unrecognised
    }

    public static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

    public static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);

    static bool Is(string value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
