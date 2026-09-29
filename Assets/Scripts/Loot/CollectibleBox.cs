using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A lootbox. Any box collected feeds the whole bike - everyone aboard gains
/// stamina - but it is worth more points to riders whose colour matches, so a
/// shared colour is something to cooperate over.
/// </summary>
[RequireComponent ( typeof ( Collider2D ) )]
public class CollectibleBox : MonoBehaviour
{
    /// <summary>
    /// Live registry of every box in the scene, so AI riders can find their
    /// nearest target without a scene-wide search every frame.
    /// </summary>
    public static readonly List<CollectibleBox> All = new List<CollectibleBox> ();

    [Header ( "Identity" )]
    public GameController.PlayerColour boxColour;

    [Header ( "Value" )]
    [Tooltip ( "Points for a rider whose colour matches this box." )]
    public int matchingColourPoints = 2;

    [Tooltip ( "Points for every other rider on the bike." )]
    public int otherColourPoints = 1;

    [Tooltip ( "Stamina restored to every rider on the bike." )]
    public float staminaForAll = 20f;

    private SpriteRenderer spriteRenderer;
    private TargetManager targetManager;
    private UIManager gameManager;

    private bool hasBeenCollected;

    private void Awake ()
    {
        spriteRenderer = GetComponent<SpriteRenderer> ();
        targetManager = FindFirstObjectByType<TargetManager> ();
        gameManager = FindFirstObjectByType<UIManager> ();
    }

    private void OnEnable () => All.Add ( this );

    private void OnDisable () => All.Remove ( this );

    private void OnTriggerEnter2D ( Collider2D other )
    {
        if ( hasBeenCollected )
            return;

        if ( !other.CompareTag ( "Bike" ) )
            return;

        MegaBikeController bike = other.GetComponentInParent<MegaBikeController> ();

        if ( bike == null )
            return;

        hasBeenCollected = true;

        if ( targetManager != null )
            targetManager.BoxCollected ( this );

        // Stamina goes to everyone on the bike, regardless of colour.
        foreach ( IRider rider in bike.GetRiders () )
        {
            if ( rider != null )
                rider.ReceiveLoot ( this );
        }

        // Points are split by colour. Several riders may share one, so this
        // awards all of them rather than the first match.
        if ( GameController.instance != null )
        {
            GameController.instance.AwardBox (
                bike.bikeIndex,
                boxColour,
                matchingColourPoints,
                otherColourPoints
            );
        }

        // The round UI still tracks the human's own score.
        if ( gameManager != null && GameController.instance != null )
        {
            int humanSeat = 0;

            bool humanAboard =
                !GameController.instance.PlayerSeatIsAI &&
                GameController.instance.slots.Count > humanSeat &&
                GameController.instance.slots[ humanSeat ].bikeIndex == bike.bikeIndex;

            if ( humanAboard )
            {
                bool matches = GameController.instance.slots[ humanSeat ].colour == boxColour;

                gameManager.AddScore ( matches ? matchingColourPoints : otherColourPoints );
            }
        }

        Destroy ( gameObject );
    }

    /// <summary>Nearest uncollected box of a given colour. Null if none left.</summary>
    public static CollectibleBox FindNearestForTeam ( GameController.PlayerColour colour, Vector2 from )
    {
        CollectibleBox best = null;
        float bestSqr = float.MaxValue;

        for ( int i = 0; i < All.Count; i++ )
        {
            CollectibleBox box = All[ i ];

            if ( box == null || box.hasBeenCollected || box.boxColour != colour )
                continue;

            float sqr = ( ( Vector2 ) box.transform.position - from ).sqrMagnitude;

            if ( sqr < bestSqr )
            {
                bestSqr = sqr;
                best = box;
            }
        }

        return best;
    }
}