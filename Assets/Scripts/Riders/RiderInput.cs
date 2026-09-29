using UnityEngine;

/// <summary>
/// What one rider is contributing to the bike this frame.
///
/// Steering and pedalling are separate: a rider can point the bike somewhere
/// without doing any work, which costs no stamina. Only pedalling tires you.
/// </summary>
public class RiderInput : MonoBehaviour
{
    /// <summary>Desired direction of travel. Zero means "not steering".</summary>
    public Vector2 steerInput { get; set; }

    /// <summary>How hard this rider is pedalling, 0 to 1. Zero costs no stamina.</summary>
    public float pedalInput { get; set; }

    /// <summary>True when this rider is actively trying to turn the bike.</summary>
    public bool IsSteering => steerInput.sqrMagnitude > 0.0001f;

    /// <summary>True when this rider is doing work.</summary>
    public bool IsPedalling => pedalInput > 0.01f;

    /// <summary>
    /// Combined vector, kept for HUD arrows: direction of steer, length of
    /// pedal effort. Not used by the bike itself any more.
    /// </summary>
    public Vector2 DisplayVector =>
        IsSteering ? steerInput.normalized * Mathf.Clamp01 ( pedalInput ) : Vector2.zero;

    public void Clear ()
    {
        steerInput = Vector2.zero;
        pedalInput = 0f;
    }
}