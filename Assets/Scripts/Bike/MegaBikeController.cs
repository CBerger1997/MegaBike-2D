using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The megabike moves FORWARD along a heading. The heading is steered by the
/// average of everyone's steer input; the speed comes from the average pedal
/// effort. So a rider can turn the bike while doing no work at all.
///
/// If the bike has a leader and leaderControlsSteering is on, the leader's
/// steer replaces the average entirely - everyone else can still pedal, but
/// only the leader decides where that effort goes.
/// </summary>
public class MegaBikeController : MonoBehaviour
{
    [Header ( "Identity" )]
    [Tooltip ( "Index into GameController.bikes. Must match." )]
    public int bikeIndex = 0;

    [Header ( "Riders" )]
    public List<RiderInput> riders;
    public PlayerRider playerRider;

    [Header ( "Movement" )]
    public float movementSpeed = 5f;
    public float acceleration = 8f;

    [Tooltip ( "How quickly the bike turns towards the agreed steer direction." )]
    public float turnSpeed = 3f;

    [Tooltip ( "How far the bike's edge sits from its centre, so it stops before clipping out." )]
    public float bikeRadius = 1.5f;

    [Header ( "Leadership" )]
    [Tooltip ( "Seat of the current leader, or -1. Set by RoundEndController." )]
    public int leaderSeat = -1;

    [Tooltip ( "A leader's steer overrides everyone else's." )]
    public bool leaderControlsSteering = true;

    [Tooltip ( "Round after which this bike's leader and rules lapse. -1 = never set." )]
    public int governanceExpiresRound = -1;

    [Tooltip ( "Length of the current term in rounds, for display." )]
    public int termRounds = 0;

    [Header ( "State (read-only)" )]
    public Vector2 heading = Vector2.up;
    public Vector2 currentVelocity;
    public float currentPedalEffort;

    private Rigidbody2D rb;

    public Vector2 CurrentVelocity => currentVelocity;

    /// <summary>Rules this bike's riders have agreed to. Never null.</summary>
    public BikeRules Rules =>
        GameController.instance != null
            ? GameController.instance.RulesForBike ( bikeIndex )
            : new BikeRules ();

    private void Awake ()
    {
        rb = GetComponent<Rigidbody2D> ();

        if ( heading.sqrMagnitude < 0.0001f )
            heading = Vector2.up;

        heading.Normalize ();
    }

    private void FixedUpdate ()
    {
        float dt = Time.fixedDeltaTime;

        // --- steer ---
        Vector2 steer = GetSteerDirection ();

        if ( steer.sqrMagnitude > 0.0001f )
        {
            heading = Vector2.Lerp ( heading, steer.normalized, 1f - Mathf.Exp ( -turnSpeed * dt ) );

            if ( heading.sqrMagnitude > 0.0001f )
                heading.Normalize ();
        }

        // --- pedal ---
        currentPedalEffort = GetPedalEffort ();

        Vector2 targetVelocity = heading * currentPedalEffort * movementSpeed;

        currentVelocity = Vector2.MoveTowards (
            currentVelocity,
            targetVelocity,
            acceleration * dt
        );

        Vector2 nextPosition = rb.position + currentVelocity * dt;

        float limit = Arena.Bounds - bikeRadius;

        if ( nextPosition.x > limit && currentVelocity.x > 0f )
        {
            nextPosition.x = limit;
            currentVelocity.x = 0f;
        }
        else if ( nextPosition.x < -limit && currentVelocity.x < 0f )
        {
            nextPosition.x = -limit;
            currentVelocity.x = 0f;
        }

        if ( nextPosition.y > limit && currentVelocity.y > 0f )
        {
            nextPosition.y = limit;
            currentVelocity.y = 0f;
        }
        else if ( nextPosition.y < -limit && currentVelocity.y < 0f )
        {
            nextPosition.y = -limit;
            currentVelocity.y = 0f;
        }

        rb.MovePosition ( nextPosition );
    }

    /// <summary>
    /// Average of everyone steering, or just the leader's if they hold the
    /// handlebars. Riders contributing nothing are ignored rather than counted
    /// as a zero, so one rider steering still turns the bike.
    /// </summary>
    public Vector2 GetSteerDirection ( RiderInput exclude = null )
    {
        if ( leaderControlsSteering && leaderSeat >= 0 )
        {
            RiderInput leader = GetLeaderInput ();

            if ( leader != null && leader.IsSteering )
                return leader.steerInput.normalized;
        }

        if ( riders == null || riders.Count == 0 )
            return Vector2.zero;

        Vector2 sum = Vector2.zero;
        int count = 0;

        foreach ( RiderInput rider in riders )
        {
            if ( rider == null || rider == exclude || !rider.isActiveAndEnabled )
                continue;

            if ( !rider.IsSteering )
                continue;

            sum += rider.steerInput.normalized;
            count++;
        }

        if ( count == 0 )
            return Vector2.zero;

        return ( sum / count ).sqrMagnitude > 0.0001f ? ( sum / count ).normalized : Vector2.zero;
    }

    /// <summary>
    /// Average pedal effort across every rider on the bike, including those
    /// doing nothing - so a bike where only one of five pedals moves slowly.
    /// </summary>
    public float GetPedalEffort ()
    {
        if ( riders == null || riders.Count == 0 )
            return 0f;

        float sum = 0f;
        int count = 0;

        foreach ( RiderInput rider in riders )
        {
            if ( rider == null || !rider.isActiveAndEnabled )
                continue;

            sum += Mathf.Clamp01 ( rider.pedalInput );
            count++;
        }

        return count > 0 ? sum / count : 0f;
    }

    private RiderInput GetLeaderInput ()
    {
        if ( GameController.instance == null || leaderSeat < 0 )
            return null;

        GameObject go = GameController.instance.RiderObject ( leaderSeat );

        return go != null ? go.GetComponent<RiderInput> () : null;
    }

    /// <summary>True when this seat is currently dictating the bike's direction.</summary>
    public bool SeatControlsSteering ( int seat )
    {
        return leaderControlsSteering && leaderSeat >= 0 && leaderSeat == seat;
    }

    public IEnumerable<IRider> GetRiders ()
    {
        if ( playerRider != null && playerRider.enabled )
            yield return playerRider;

        if ( riders == null )
            yield break;

        foreach ( RiderInput rider in riders )
        {
            if ( rider == null || !rider.isActiveAndEnabled )
                continue;

            foreach ( IRider r in rider.GetComponents<IRider> () )
            {
                if ( r is MonoBehaviour mb && !mb.enabled )
                    continue;

                if ( ReferenceEquals ( r, playerRider ) )
                    continue;

                yield return r;
            }
        }
    }

    public void StopBike ()
    {
        currentVelocity = Vector2.zero;
        currentPedalEffort = 0f;
    }

    private void OnDrawGizmosSelected ()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine ( transform.position,
                          transform.position + new Vector3 ( heading.x, heading.y, 0f ) * 4f );
    }
}