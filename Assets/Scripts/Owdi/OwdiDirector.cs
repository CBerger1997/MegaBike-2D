using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns ThreatCars from outside the arena edges at a ramping rate.
///
/// Each car is aimed at where the bike is *at the moment of spawn*, with a
/// random offset, and then commits to that straight line forever. So the car
/// never chases - the riders dodge by not being where they were a few seconds
/// ago. Set aimAtBikeChance to 0 for fully random traffic.
/// </summary>
public class OwdiDirector : MonoBehaviour
{
    [Header ( "References" )]
    public MegaBikeController bike;
    public GameObject threatCarPrefab;

    [Tooltip ( "Optional. Shown at the entry point before the car appears." )]
    public GameObject warningMarkerPrefab;

    [Header ( "Arena" )]
    public float arenaBounds = 45f;
    [Tooltip ( "How far outside the bounds cars are spawned." )]
    public float spawnMargin = 10f;

    [Header ( "Timing" )]
    public float firstSpawnDelay = 15f;
    public float spawnInterval = 10f;
    public float spawnIntervalMin = 4f;
    [Tooltip ( "Interval is reduced by this much after every spawn." )]
    public float intervalRampPerSpawn = 0.5f;
    [Tooltip ( "Seconds the warning marker shows before the car launches." )]
    public float warningSeconds = 1.5f;

    [Header ( "Aim" )]
    [Range ( 0f, 1f )]
    [Tooltip ( "Chance the car's line is aimed near the bike rather than at random." )]
    public float aimAtBikeChance = 0.75f;
    [Tooltip ( "Random offset from the bike's position, in world units. Bigger = easier to dodge." )]
    public float aimScatter = 10f;

    [Header ( "Pressure" )]
    public int maxSimultaneousCars = 3;
    public bool spawnOnlyWhileRunning = true;

    private readonly List<GameObject> activeWarnings = new List<GameObject> ();
    private float currentInterval;

    private bool GameRunning =>
        GameController.instance == null ||
        GameController.instance.gameState == GameController.GameState.Running;

    private void Start ()
    {
        arenaBounds = Arena.Bounds;

        if ( bike == null )
            bike = FindFirstObjectByType<MegaBikeController> ();

        currentInterval = spawnInterval;

        StartCoroutine ( SpawnLoop () );
    }

    private IEnumerator SpawnLoop ()
    {
        // Don't burn the delay while the player is still in the menus.
        while ( !GameRunning )
            yield return null;

        currentInterval = spawnInterval;

        yield return new WaitForSeconds ( firstSpawnDelay );

        while ( true )
        {
            if ( spawnOnlyWhileRunning && !GameRunning )
            {
                yield return null;
                continue;
            }

            if ( OwdiController.All.Count < maxSimultaneousCars )
            {
                yield return StartCoroutine ( SpawnOne () );

                currentInterval = Mathf.Max (
                    spawnIntervalMin,
                    currentInterval - intervalRampPerSpawn
                );
            }

            yield return new WaitForSeconds ( currentInterval );
        }
    }

    private IEnumerator SpawnOne ()
    {
        if ( threatCarPrefab == null )
        {
            Debug.LogError ( "ThreatCarDirector: no threatCarPrefab assigned." );
            yield break;
        }

        GetSpawnLine ( out Vector2 spawnPos, out Vector2 direction );

        ThreatWarningMarker warning = null;

        if ( warningMarkerPrefab != null && warningSeconds > 0f )
        {
            GameObject warningGo = Instantiate ( warningMarkerPrefab );

            warning = warningGo.GetComponent<ThreatWarningMarker> ();

            if ( warning != null )
            {
                // The marker pins itself to the screen edge each frame, so the
                // spawn position here is only a direction hint.
                warning.Configure ( spawnPos, direction );
            }
            else
            {
                Debug.LogWarning ( "ThreatCarDirector: warning prefab has no ThreatWarningMarker." );
            }

            activeWarnings.Add ( warningGo );

            yield return new WaitForSeconds ( warningSeconds );

            activeWarnings.Remove ( warningGo );
        }

        GameObject go = Instantiate ( threatCarPrefab );

        OwdiController car = go.GetComponent<OwdiController> ();

        if ( car == null )
        {
            Debug.LogError ( "ThreatCarDirector: prefab has no ThreatCar component." );
            Destroy ( go );
            yield break;
        }

        car.bike = bike;
        car.arenaBounds = arenaBounds;
        car.Launch ( spawnPos, direction );

        // Hand the marker over - it now tracks the car and removes itself once
        // the car is on screen.
        if ( warning != null )
            warning.Track ( car );
        else if ( warningMarkerPrefab != null && warningSeconds <= 0f )
        { /* warnings disabled */ }
    }

    /// <summary>Picks an entry point on the spawn ring and a one-off heading.</summary>
    private void GetSpawnLine ( out Vector2 spawnPos, out Vector2 direction )
    {
        float radius = arenaBounds + spawnMargin;
        float angle = Random.Range ( 0f, Mathf.PI * 2f );

        spawnPos = new Vector2 ( Mathf.Cos ( angle ), Mathf.Sin ( angle ) ) * radius;

        // Aim at a bike that still has riders, not necessarily bike 0.
        MegaBikeController aimAt = PickOccupiedBike ();

        if ( aimAt != null && Random.value < aimAtBikeChance )
        {
            // Aim at where the bike is NOW, plus scatter. It will have moved by
            // the time the car arrives - that gap is the dodge window.
            Vector2 aimPoint =
                ( Vector2 ) aimAt.transform.position +
                Random.insideUnitCircle * aimScatter;

            direction = ( aimPoint - spawnPos ).normalized;
        }
        else
        {
            // Random traffic: head roughly across the arena rather than
            // clipping the corner and leaving immediately.
            Vector2 acrossPoint = Random.insideUnitCircle * arenaBounds * 0.6f;

            direction = ( acrossPoint - spawnPos ).normalized;
        }
    }

    /// <summary>A bike at random from those that still have riders, or null.</summary>
    private MegaBikeController PickOccupiedBike ()
    {
        GameController game = GameController.instance;

        if ( game == null )
            return bike;

        List<MegaBikeController> occupied = new List<MegaBikeController> ();

        for ( int b = 0; b < game.bikes.Count; b++ )
        {
            if ( game.bikes[ b ] != null && game.RidersOnBike ( b ) > 0 )
                occupied.Add ( game.bikes[ b ] );
        }

        if ( occupied.Count == 0 )
            return null;

        return occupied[ Random.Range ( 0, occupied.Count ) ];
    }

    /// <summary>Manual trigger, e.g. for a scripted attack at the two-minute mark.</summary>
    public void SpawnNow ()
    {
        StartCoroutine ( SpawnOne () );
    }

    public void ClearAll ()
    {
        for ( int i = OwdiController.All.Count - 1; i >= 0; i-- )
        {
            if ( OwdiController.All[ i ] != null )
                Destroy ( OwdiController.All[ i ].gameObject );
        }

        foreach ( GameObject w in activeWarnings )
        {
            if ( w != null )
                Destroy ( w );
        }

        activeWarnings.Clear ();
    }

    private void OnDrawGizmosSelected ()
    {
        Gizmos.color = new Color ( 1f, 0.4f, 0.2f, 0.5f );

        // Spawn ring.
        Vector3 prev = new Vector3 ( arenaBounds + spawnMargin, 0f, 0f );

        for ( int i = 1; i <= 48; i++ )
        {
            float a = ( i / 48f ) * Mathf.PI * 2f;

            Vector3 next = new Vector3 (
                Mathf.Cos ( a ),
                Mathf.Sin ( a ),
                0f
            ) * ( arenaBounds + spawnMargin );

            Gizmos.DrawLine ( prev, next );
            prev = next;
        }
    }
}