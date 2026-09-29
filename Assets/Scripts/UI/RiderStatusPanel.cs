using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Builds one RiderStatusRow per rider and keeps them updated.
///
/// Rebuilt whenever the roster changes, so it always shows every rider rather
/// than a fixed number of hand-placed bars. The player's own row is handled
/// separately and pinned wherever you put it - normally the bottom of the
/// screen - and is shown whether the player seat is human or AI.
/// </summary>
public class RiderStatusPanel : MonoBehaviour
{
    [Header ( "Other riders" )]
    [Tooltip ( "Prefab with a RiderStatusRow on it." )]
    public RiderStatusRow rowPrefab;

    [Tooltip ( "Parent for the generated rows. A Vertical Layout Group works well." )]
    public Transform rowContainer;

    [Header ( "Player" )]
    [Tooltip ( "The always-visible row for seat 0, wherever you've placed it." )]
    public RiderStatusRow playerRow;

    [Header ( "Options" )]
    [Tooltip ( "Keep showing riders after they've been eliminated, dimmed." )]
    public bool keepEliminatedVisible = true;

    [Tooltip ( "Only show riders on the same bike as the player." )]
    public bool onlyPlayerBike = false;

    [Tooltip ( "Show only this bike's riders. -1 shows every bike. Use one panel per bike." )]
    public int onlyBikeIndex = -1;

    [Tooltip ( "Optional. Shows this bike's pooled score, e.g. \"Bike 1 - 34\"." )]
    public TMP_Text bikePotLabel;

    private readonly List<RiderStatusRow> rows = new List<RiderStatusRow> ();

    private void OnEnable ()
    {
        GameController.OnRosterChanged += Build;
        Build ();
    }

    private void OnDisable ()
    {
        GameController.OnRosterChanged -= Build;
    }

    /// <summary>Destroys and recreates every row from the current roster.</summary>
    public void Build ()
    {
        GameController game = GameController.instance;

        if ( game == null || rowPrefab == null || rowContainer == null )
            return;

        foreach ( RiderStatusRow row in rows )
        {
            if ( row != null )
                Destroy ( row.gameObject );
        }

        rows.Clear ();

        int playerBike = game.slots.Count > 0 ? game.slots[ 0 ].bikeIndex : 0;

        // Seat 0 joins the list when an AI is driving it, and only gets the
        // pinned bottom row when a human is playing.
        bool humanDriving = !game.PlayerSeatIsAI;
        int firstSeat = humanDriving ? 1 : 0;

        for ( int seat = firstSeat; seat < game.slots.Count; seat++ )
        {
            if ( !keepEliminatedVisible && !game.slots[ seat ].inPlay )
                continue;

            if ( onlyPlayerBike && game.slots[ seat ].bikeIndex != playerBike )
                continue;

            // One panel per bike: skip anyone riding elsewhere.
            if ( onlyBikeIndex >= 0 && game.slots[ seat ].bikeIndex != onlyBikeIndex )
                continue;

            RiderStatusRow row = Instantiate ( rowPrefab, rowContainer );

            row.gameObject.name = $"Row_{game.slots[ seat ].colour}";
            row.Bind ( seat );

            rows.Add ( row );
        }

        if ( playerRow != null )
        {
            playerRow.gameObject.SetActive ( humanDriving && game.slots.Count > 0 );

            if ( humanDriving && game.slots.Count > 0 )
                playerRow.Bind ( 0 );
        }
    }

    /// <summary>The bike's pooled wealth, separate from any rider's own score.</summary>
    private void RefreshPot ()
    {
        if ( bikePotLabel == null )
            return;

        GameController game = GameController.instance;

        if ( game == null )
            return;

        if ( onlyBikeIndex >= 0 )
        {
            bikePotLabel.text = $"Bike {onlyBikeIndex + 1} - {game.GetBikeScore ( onlyBikeIndex )}";
            return;
        }

        bikePotLabel.text = game.BikePotsSummary ();
    }

    private void Update ()
    {
        RefreshPot ();

        foreach ( RiderStatusRow row in rows )
        {
            if ( row != null )
                row.Refresh ();
        }

        if ( playerRow != null && playerRow.gameObject.activeSelf )
            playerRow.Refresh ();
    }
}







