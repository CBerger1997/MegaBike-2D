using UnityEngine;

/// <summary>
/// Pins itself to the edge of the camera viewport in the direction of an
/// incoming threat, so the player always sees where the car is coming from
/// even when the car itself is far off-screen.
///
/// Once the car spawns, the marker tracks it and removes itself as soon as the
/// car is actually visible - by which point the marker has done its job.
/// </summary>
public class ThreatWarningMarker : MonoBehaviour
{
    [Header ( "Placement" )]
    [Tooltip ( "How far in from the screen edge to sit, as a fraction of the viewport." )]
    [Range ( 0.02f, 0.25f )] public float edgeInset = 0.08f;

    [Tooltip ( "Rotate to point along the car's direction of travel." )]
    public bool rotateToHeading = true;

    [Tooltip ( "Tick if the marker sprite is drawn pointing up rather than right." )]
    public bool spriteFacesUp = true;

    [Header ( "Appearance" )]
    public SpriteRenderer markerRenderer;
    public bool pulse = true;
    public float pulsesPerSecond = 3f;
    public float pulseMinAlpha = 0.35f;

    [Header ( "Lifetime" )]
    [Tooltip ( "Destroy once the tracked car is comfortably on screen." )]
    public bool hideWhenCarVisible = true;
    [Tooltip ( "Safety net in case the car never becomes visible. 0 = no limit." )]
    public float maxLifetime = 12f;

    private Camera cam;
    private Vector2 sourcePoint;
    private Vector2 heading;
    private OwdiController trackedCar;
    private float spawnTime;

    private void Awake ()
    {
        cam = Camera.main;

        if ( markerRenderer == null )
            markerRenderer = GetComponentInChildren<SpriteRenderer> ();

        spawnTime = Time.time;
    }

    /// <summary>Called by the director before the car exists.</summary>
    public void Configure ( Vector2 incomingFrom, Vector2 travelDirection, Camera camera = null )
    {
        sourcePoint = incomingFrom;
        heading = travelDirection.normalized;

        if ( camera != null )
            cam = camera;
    }

    /// <summary>Called by the director once the car has launched.</summary>
    public void Track ( OwdiController car )
    {
        trackedCar = car;
    }

    private void LateUpdate ()
    {
        if ( cam == null )
        {
            cam = Camera.main;

            if ( cam == null )
                return;
        }

        if ( maxLifetime > 0f && Time.time - spawnTime > maxLifetime )
        {
            Destroy ( gameObject );
            return;
        }

        // Once the car exists, point at the car itself rather than its origin.
        Vector2 target = trackedCar != null
            ? ( Vector2 ) trackedCar.transform.position
            : sourcePoint;

        Vector3 viewport = cam.WorldToViewportPoint ( new Vector3 ( target.x, target.y, 0f ) );

        bool onScreen =
            viewport.x > edgeInset && viewport.x < 1f - edgeInset &&
            viewport.y > edgeInset && viewport.y < 1f - edgeInset;

        if ( onScreen && trackedCar != null && hideWhenCarVisible )
        {
            Destroy ( gameObject );
            return;
        }

        // Clamp to the viewport edge.
        viewport.x = Mathf.Clamp ( viewport.x, edgeInset, 1f - edgeInset );
        viewport.y = Mathf.Clamp ( viewport.y, edgeInset, 1f - edgeInset );

        // Distance from the camera to the 2D plane the game sits on.
        float depth = Mathf.Abs ( cam.transform.position.z );

        Vector3 world = cam.ViewportToWorldPoint (
            new Vector3 ( viewport.x, viewport.y, depth )
        );

        transform.position = new Vector3 ( world.x, world.y, 0f );

        if ( rotateToHeading )
        {
            Vector2 dir = trackedCar != null ? trackedCar.Heading : heading;

            float angle = Mathf.Atan2 ( dir.y, dir.x ) * Mathf.Rad2Deg;

            if ( spriteFacesUp )
                angle -= 90f;

            transform.rotation = Quaternion.Euler ( 0f, 0f, angle );
        }

        if ( pulse && markerRenderer != null )
        {
            float t = ( Mathf.Sin ( Time.time * pulsesPerSecond * Mathf.PI * 2f ) + 1f ) * 0.5f;

            Color c = markerRenderer.color;
            c.a = Mathf.Lerp ( pulseMinAlpha, 1f, t );
            markerRenderer.color = c;
        }
    }
}