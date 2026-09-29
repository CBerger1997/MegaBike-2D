using UnityEngine;

/// <summary>
/// One place that defines how big the map is. Everything else (bike clamp, AI
/// containment, lootbox spawning, threat car despawn) reads from here so the
/// numbers can't drift apart.
///
/// Drop this on an empty GameObject at the world origin.
/// </summary>
public class Arena : MonoBehaviour
{
    private static Arena instance;

    [Tooltip ( "Half-width of the playable square, in world units." )]
    public float bounds = 45f;

    [Tooltip ( "Drawn in the editor so you can see the play area." )]
    public Color gizmoColour = new Color ( 0.3f, 0.9f, 1f, 0.6f );

    public static float Bounds
    {
        get
        {
            if ( instance == null )
                instance = FindFirstObjectByType<Arena> ();

            return instance != null ? instance.bounds : 45f;
        }
    }

    private void Awake ()
    {
        instance = this;
    }

    /// <summary>Clamps a position into the arena, optionally inset by a margin.</summary>
    public static Vector2 Clamp ( Vector2 position, float margin = 0f )
    {
        float limit = Mathf.Max ( 0f, Bounds - margin );

        return new Vector2 (
            Mathf.Clamp ( position.x, -limit, limit ),
            Mathf.Clamp ( position.y, -limit, limit )
        );
    }

    public static bool Contains ( Vector2 position, float margin = 0f )
    {
        float limit = Mathf.Max ( 0f, Bounds - margin );

        return Mathf.Abs ( position.x ) <= limit &&
               Mathf.Abs ( position.y ) <= limit;
    }

    /// <summary>
    /// How far outside the soft boundary a position is, per axis, as 0-1.
    /// Zero while comfortably inside. Used by AI riders to steer back.
    /// </summary>
    public static Vector2 OutwardPressure ( Vector2 position, float softFraction )
    {
        float limit = Bounds;
        float soft = limit * Mathf.Clamp01 ( softFraction );

        Vector2 pressure = Vector2.zero;

        if ( position.x > soft )
            pressure.x = Mathf.InverseLerp ( soft, limit, position.x );
        else if ( position.x < -soft )
            pressure.x = -Mathf.InverseLerp ( -soft, -limit, position.x );

        if ( position.y > soft )
            pressure.y = Mathf.InverseLerp ( soft, limit, position.y );
        else if ( position.y < -soft )
            pressure.y = -Mathf.InverseLerp ( -soft, -limit, position.y );

        return pressure;
    }

    private void OnDrawGizmos ()
    {
        Gizmos.color = gizmoColour;
        Gizmos.DrawWireCube ( Vector3.zero, new Vector3 ( bounds * 2f, bounds * 2f, 0f ) );

        Gizmos.color = new Color ( gizmoColour.r, gizmoColour.g, gizmoColour.b, 0.25f );
        Gizmos.DrawWireCube ( Vector3.zero, new Vector3 ( bounds * 1.5f, bounds * 1.5f, 0f ) );
    }
}