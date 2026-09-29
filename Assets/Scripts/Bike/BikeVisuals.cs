using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Swaps the megabike sprite and re-seats the riders when the rider count
/// changes - at setup, and again whenever someone is voted off.
/// </summary>
public class BikeVisuals : MonoBehaviour
{
    [System.Serializable]
    public class BikeSize
    {
        public int riderCount = 3;
        public Sprite sprite;

        [Tooltip ( "Distance from the bike centre to each rider." )]
        public float seatRadius = 0.8f;

        [Tooltip ( "Distance from the bike centre to each rider's direction arrow." )]
        public float arrowRadius = 1.4f;

        [Tooltip ( "Collider radius for lootbox pickup at this size." )]
        public float colliderRadius = 1.2f;
    }

    [Header ( "References" )]
    public SpriteRenderer bikeRenderer;
    public CircleCollider2D bikeCollider;

    [Tooltip ( "Which bike this is. Must match the index in GameController.bikes." )]
    public int bikeIndex = 0;

    [Header ( "Sizes" )]
    [Tooltip ( "One entry per supported rider count. Nearest match is used if a count is missing." )]
    public List<BikeSize> sizes = new List<BikeSize> ();

    [Header ( "Seating" )]
    [Tooltip ( "Angle of the first seat, degrees. 90 puts the player at the top." )]
    public float startAngleDegrees = 90f;

    [Tooltip ( "Rotate rider sprites to face outward from the centre." )]
    public bool faceRidersOutward = false;

    [Tooltip ( "Reposition riders when the count changes. Untick if you seat them by hand." )]
    public bool autoSeatRiders = true;

    private int currentCount = -1;

    private void Awake ()
    {
        if ( bikeRenderer == null )
            bikeRenderer = GetComponent<SpriteRenderer> ();

        if ( bikeCollider == null )
            bikeCollider = GetComponent<CircleCollider2D> ();
    }

    public void SetRiderCount ( int count )
    {
        if ( count == currentCount )
            return;

        currentCount = count;

        BikeSize size = GetSizeFor ( count );

        if ( size == null )
        {
            Debug.LogWarning ( "BikeVisuals: no sizes configured." );
            return;
        }

        if ( bikeRenderer != null && size.sprite != null )
            bikeRenderer.sprite = size.sprite;

        if ( bikeCollider != null )
            bikeCollider.radius = size.colliderRadius;

        if ( autoSeatRiders )
            SeatRiders ( count, size );
    }

    private BikeSize GetSizeFor ( int count )
    {
        BikeSize best = null;
        int bestDistance = int.MaxValue;

        foreach ( BikeSize size in sizes )
        {
            if ( size.riderCount == count )
                return size;

            int distance = Mathf.Abs ( size.riderCount - count );

            if ( distance < bestDistance )
            {
                bestDistance = distance;
                best = size;
            }
        }

        return best;
    }

    /// <summary>Spaces the active riders evenly around the bike.</summary>
    private void SeatRiders ( int count, BikeSize size )
    {
        GameController game = GameController.instance;

        if ( game == null )
            return;

        int placed = 0;

        foreach ( int seat in game.SeatsOnBike ( bikeIndex ) )
        {
            GameObject go = game.RiderObject ( seat );

            if ( go == null || !go.activeSelf )
                continue;

            float angle = ( startAngleDegrees + ( 360f / Mathf.Max ( 1, count ) ) * placed ) * Mathf.Deg2Rad;
            Vector2 offset = new Vector2 ( Mathf.Cos ( angle ), Mathf.Sin ( angle ) );

            go.transform.localPosition = offset * size.seatRadius;

            if ( faceRidersOutward )
                go.transform.localRotation = Quaternion.Euler ( 0f, 0f, angle * Mathf.Rad2Deg - 90f );

            GameObject arrow = GetArrowFor ( go );

            if ( arrow != null )
            {
                // The arrow lives on the rider, which is already seated, so it
                // sits at the rider's own origin rather than offset again.
                arrow.transform.localPosition = Vector3.zero;
            }

            placed++;
        }
    }

    private GameObject GetArrowFor ( GameObject riderObject )
    {
        AIRider ai = riderObject.GetComponent<AIRider> ();

        if ( ai != null && ai.enabled && ai.movementArrow != null )
            return ai.movementArrow;

        PlayerRider human = riderObject.GetComponent<PlayerRider> ();

        if ( human != null && human.playerMovementArrow != null )
            return human.playerMovementArrow;

        // Player seat driven by AI: the AIRider component may be disabled but
        // the arrow reference is still the right one.
        if ( ai != null && ai.movementArrow != null )
            return ai.movementArrow;

        return null;
    }
}
