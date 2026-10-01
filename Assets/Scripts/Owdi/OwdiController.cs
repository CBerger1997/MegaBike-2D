using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The Owdi. Drives in a straight line across the arena at a fixed speed and
/// never changes course to follow the bike - dodging is entirely down to the
/// riders reading its line and steering off it.
///
/// On contact with the bike it fires <see cref="onBikeHit"/>, which is where
/// you hook up your existing voting sequence.
/// </summary>
[RequireComponent ( typeof ( Rigidbody2D ) )]
[RequireComponent ( typeof ( Collider2D ) )]
public class OwdiController : MonoBehaviour
{
    public enum EdgeBehaviour { Despawn, Bounce, Wrap }

    /// <summary>Live registry so AI riders can find incoming cars cheaply.</summary>
    public static readonly List<OwdiController> All = new List<OwdiController> ();

    [Header ( "Speed" )]
    public MegaBikeController bike;
    [Tooltip ( "Take speed from the bike instead of the fixed value below." )]
    public bool deriveSpeedFromBike = true;
    [Tooltip ( "1.2 = 20% faster than the bike at full pedal." )]
    public float speedMultiplier = 1.2f;
    public float fixedSpeed = 6f;

    [Header ( "Heading" )]
    [Tooltip ( "Set once at spawn. The car never re-aims at the bike." )]
    public Vector2 heading = Vector2.right;
    [Tooltip ( "Degrees per second of curve. Leave at 0 for a perfectly straight line." )]
    public float headingDriftPerSecond = 0f;
    [Tooltip ( "Tick if the car sprite is drawn pointing up rather than right." )]
    public bool spriteFacesUp = true;
    public bool rotateToHeading = true;

    [Header ( "Arena" )]
    public float arenaBounds = 45f;
    [Tooltip ( "How far past the bounds it travels before Despawn/Wrap kicks in." )]
    public float edgeMargin = 12f;
    public EdgeBehaviour onLeaveArena = EdgeBehaviour.Despawn;
    [Tooltip ( "Safety net so a car can never live forever. 0 = no limit." )]
    public float maxLifetime = 30f;

    [Header ( "Collision" )]
    public string bikeTag = "Bike";
    [Tooltip ( "Ignore further hits for this long, so one pass can't fire two votes." )]
    public float hitCooldown = 5f;
    public bool despawnAfterHit = true;
    public bool stopBikeOnHit = true;

    [Header ( "On hit" )]
    [Tooltip ( "Hook your existing voting sequence up here." )]
    public UnityEvent onBikeHit;

    [Tooltip ( "Optional fallback: calls a method by name on this object instead of / as well as the event." )]
    public GameObject voteTriggerTarget;
    public string voteTriggerMethod = "StartVote";

    /// <summary>The bike this car struck, for the round-end handler.</summary>
    public MegaBikeController HitBike { get; private set; }

    /// <summary>Fires for any listener that wants to know a hit happened (audio, camera shake...).</summary>
    public static event System.Action<OwdiController> OnAnyBikeHit;

    private Rigidbody2D rb;
    private float lastHitTime = -999f;
    private float spawnTime;

    public Vector2 Heading => heading;

    public float Speed
    {
        get
        {
            if ( deriveSpeedFromBike && bike != null )
                return bike.movementSpeed * speedMultiplier;

            return fixedSpeed;
        }
    }

    private bool GameRunning =>
        GameController.instance == null ||
        GameController.instance.gameState == GameController.GameState.Running;

    private void Awake ()
    {
        rb = GetComponent<Rigidbody2D> ();

        // Kinematic so it ploughs through the bike rather than shoving it around.
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.useFullKinematicContacts = true;
        rb.gravityScale = 0f;

        arenaBounds = Arena.Bounds;

        if ( bike == null )
            bike = FindFirstObjectByType<MegaBikeController> ();

        if ( maxLifetime > 0f && Time.time - spawnTime > maxLifetime )
            maxLifetime = ( Arena.Bounds * 3f ) / Mathf.Max ( 1f, Speed );

        spawnTime = Time.time;

        if ( heading.sqrMagnitude < 0.0001f )
            heading = Random.insideUnitCircle.normalized;

        heading.Normalize ();

        this.onBikeHit.AddListener ( delegate
        { Debug.Log ( "HIT THE BIKE" ); } );
    }

    private void OnEnable () => All.Add ( this );

    private void OnDisable () => All.Remove ( this );

    /// <summary>Call straight after Instantiate to set the line of travel.</summary>
    public void Launch ( Vector2 startPosition, Vector2 travelDirection )
    {
        transform.position = startPosition;

        heading = travelDirection.sqrMagnitude > 0.0001f
            ? travelDirection.normalized
            : Vector2.right;

        spawnTime = Time.time;

        ApplyRotation ();
    }

    private void FixedUpdate ()
    {
        if ( !GameRunning )
            return;

        if ( maxLifetime > 0f && Time.time - spawnTime > maxLifetime )
        {
            Destroy ( gameObject );
            return;
        }

        if ( !Mathf.Approximately ( headingDriftPerSecond, 0f ) )
        {
            float drift = headingDriftPerSecond * Time.fixedDeltaTime;
            heading = Quaternion.Euler ( 0f, 0f, drift ) * heading;
        }

        rb.MovePosition ( rb.position + heading * Speed * Time.fixedDeltaTime );

        HandleBounds ();
        ApplyRotation ();
    }

    private void HandleBounds ()
    {
        float limit = arenaBounds + edgeMargin;
        Vector2 p = rb.position;

        bool outside = Mathf.Abs ( p.x ) > limit || Mathf.Abs ( p.y ) > limit;

        if ( !outside )
            return;

        switch ( onLeaveArena )
        {
            case EdgeBehaviour.Despawn:

                Destroy ( gameObject );
                break;

            case EdgeBehaviour.Bounce:

                if ( Mathf.Abs ( p.x ) > limit )
                    heading.x = -heading.x;
                if ( Mathf.Abs ( p.y ) > limit )
                    heading.y = -heading.y;

                rb.position = new Vector2 (
                    Mathf.Clamp ( p.x, -limit, limit ),
                    Mathf.Clamp ( p.y, -limit, limit )
                );
                break;

            case EdgeBehaviour.Wrap:

                if ( Mathf.Abs ( p.x ) > limit )
                    p.x = -Mathf.Sign ( p.x ) * limit;
                if ( Mathf.Abs ( p.y ) > limit )
                    p.y = -Mathf.Sign ( p.y ) * limit;

                rb.position = p;
                break;
        }
    }

    private void ApplyRotation ()
    {
        if ( !rotateToHeading )
            return;

        float angle = Mathf.Atan2 ( heading.y, heading.x ) * Mathf.Rad2Deg;

        if ( spriteFacesUp )
            angle -= 90f;

        transform.rotation = Quaternion.Euler ( 0f, 0f, angle );
    }

    private void OnTriggerEnter2D ( Collider2D other ) => TryHit ( other );

    private void OnCollisionEnter2D ( Collision2D collision ) => TryHit ( collision.collider );

    private void TryHit ( Collider2D other )
    {
        if ( !GameRunning )
            return;

        if ( Time.time - lastHitTime < hitCooldown )
            return;

        if ( !other.CompareTag ( bikeTag ) )
            return;

        MegaBikeController struck = other.GetComponentInParent<MegaBikeController> ();

        // An abandoned bike is scenery. Hitting it produces a vote with nobody
        // in it and fills the log with meaningless events.
        if ( struck != null && GameController.instance != null &&
             GameController.instance.RidersOnBike ( struck.bikeIndex ) == 0 )
        {
            return;
        }

        lastHitTime = Time.time;

        MegaBikeController hitBike = other.GetComponentInParent<MegaBikeController> ();

        if ( stopBikeOnHit && hitBike != null )
            hitBike.StopBike ();

        HitBike = hitBike;

        Debug.Log ( $"Owdi hit {( hitBike != null ? hitBike.name : "a bike" )} - triggering vote." );

        onBikeHit?.Invoke ();

        if ( voteTriggerTarget != null && !string.IsNullOrEmpty ( voteTriggerMethod ) )
        {
            voteTriggerTarget.SendMessage (
                voteTriggerMethod,
                SendMessageOptions.DontRequireReceiver
            );
        }

        OnAnyBikeHit?.Invoke ( this );

        if ( despawnAfterHit )
            Destroy ( gameObject );
    }

    /// <summary>
    /// Shortest distance from a point to this car's forward line of travel.
    /// Used by AI riders to work out whether they're standing in the road.
    /// </summary>
    public float DistanceFromPath ( Vector2 point )
    {
        Vector2 toPoint = point - ( Vector2 ) transform.position;
        float along = Vector2.Dot ( toPoint, heading );

        // Already passed us - not a threat any more.
        if ( along < 0f )
            return float.MaxValue;

        Vector2 closest = ( Vector2 ) transform.position + heading * along;

        return Vector2.Distance ( point, closest );
    }

    /// <summary>Seconds until the car reaches the closest point to the given position.</summary>
    public float TimeToReach ( Vector2 point )
    {
        Vector2 toPoint = point - ( Vector2 ) transform.position;
        float along = Vector2.Dot ( toPoint, heading );

        if ( along <= 0f || Speed <= 0.01f )
            return float.MaxValue;

        return along / Speed;
    }

    private void OnDrawGizmosSelected ()
    {
        Gizmos.color = Color.red;

        Vector3 h = new Vector3 ( heading.x, heading.y, 0f ).normalized;

        Gizmos.DrawLine ( transform.position, transform.position + h * 40f );
    }
}