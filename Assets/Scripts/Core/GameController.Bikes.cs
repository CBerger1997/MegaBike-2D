using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeping the bikes in step with the roster: rebuilding rider lists,
/// transfers between bikes, and removing a rider from play.
/// </summary>
public partial class GameController
{
    public void RebuildBikeRiders ()
    {
        for ( int b = 0; b < bikes.Count; b++ )
        {
            if ( bikes[ b ] == null )
                continue;

            if ( bikes[ b ].riders == null )
                bikes[ b ].riders = new List<RiderInput> ();

            bikes[ b ].riders.Clear ();
            bikes[ b ].playerRider = null;
        }

        for ( int seat = 0; seat < slots.Count; seat++ )
        {
            if ( !slots[ seat ].inPlay )
                continue;

            int b = slots[ seat ].bikeIndex;

            if ( b < 0 || b >= bikes.Count || bikes[ b ] == null )
                continue;

            GameObject go = RiderObject ( seat );

            if ( go == null || !go.activeSelf )
                continue;

            RiderInput input = go.GetComponent<RiderInput> ();

            if ( input != null )
                bikes[ b ].riders.Add ( input );

            if ( seat == 0 )
            {
                PlayerRider human = go.GetComponent<PlayerRider> ();

                if ( human != null && human.enabled )
                    bikes[ b ].playerRider = human;
            }
        }

        RefreshBikeVisuals ();
        OnRosterChanged?.Invoke ();
    }

    private void RefreshBikeVisuals ()
    {
        for ( int b = 0; b < bikes.Count; b++ )
        {
            if ( bikes[ b ] == null )
                continue;

            BikeVisuals visuals = bikes[ b ].GetComponent<BikeVisuals> ();

            if ( visuals != null )
                visuals.SetRiderCount ( RidersOnBike ( b ) );
        }
    }

    public int RidersOnBike ( int bikeIndex )
    {
        int count = 0;

        for ( int seat = 0; seat < slots.Count; seat++ )
        {
            if ( slots[ seat ].inPlay && slots[ seat ].bikeIndex == bikeIndex )
                count++;
        }

        return count;
    }

    public List<int> SeatsOnBike ( int bikeIndex )
    {
        List<int> result = new List<int> ();

        for ( int seat = 0; seat < slots.Count; seat++ )
        {
            if ( slots[ seat ].inPlay && slots[ seat ].bikeIndex == bikeIndex )
                result.Add ( seat );
        }

        return result;
    }

    /// <summary>A bike other than the one given that has room, or -1.</summary>
    /// <summary>
    /// False while a rider is barred from returning to the bike that just
    /// threw them off. Without this, two under-strength bikes pass the same
    /// rider back and forth every time either of them is hit.
    /// </summary>
    public bool CanJoinBike ( int seat, int bikeIndex )
    {
        if ( transferCooldownRounds <= 0 || seat < 0 || seat >= slots.Count )
            return true;

        RiderSlot slot = slots[ seat ];

        if ( slot.lastBikeLeft != bikeIndex )
            return true;

        return roundNumber - slot.lastLeftRound >= transferCooldownRounds;
    }

    /// <summary>
    /// A bike other than the one given that has room. Pass a seat to respect
    /// that rider's cooldown.
    /// </summary>
    public int FindBikeWithSpace ( int excludingBike, int seat )
    {
        for ( int b = 0; b < bikes.Count; b++ )
        {
            if ( b == excludingBike || bikes[ b ] == null )
                continue;

            if ( RidersOnBike ( b ) >= bikeCapacity )
                continue;

            if ( !CanJoinBike ( seat, b ) )
                continue;

            return b;
        }

        return -1;
    }

    public int FindBikeWithSpace ( int excludingBike )
    {
        for ( int b = 0; b < bikes.Count; b++ )
        {
            if ( b == excludingBike || bikes[ b ] == null )
                continue;

            if ( RidersOnBike ( b ) < bikeCapacity )
                return b;
        }

        return -1;
    }

    public enum RemovalOutcome { Transferred, Eliminated, Ignored }

    /// <summary>
    /// Removes a rider from its bike. If another bike has space they move
    /// across rather than leaving the game.
    /// </summary>
    public RemovalOutcome RemoveFromBike ( int seat, out int newBikeIndex )
    {
        newBikeIndex = -1;

        if ( seat < 0 || seat >= slots.Count || !slots[ seat ].inPlay )
            return RemovalOutcome.Ignored;

        int fromBike = slots[ seat ].bikeIndex;
        int toBike = FindBikeWithSpace ( fromBike );

        if ( toBike >= 0 )
        {
            slots[ seat ].bikeIndex = toBike;
            newBikeIndex = toBike;

            GameObject go = RiderObject ( seat );

            if ( go != null && bikes[ toBike ] != null )
                go.transform.SetParent ( bikes[ toBike ].transform, false );

            RebuildBikeRiders ();

            if ( SessionLogger.instance != null )
            {
                SessionLogger.instance.LogEvent (
                    -1, toBike + 1, "Transfer", "transferred", seat,
                    DisplayName ( seat ),
                    $"from_bike:{fromBike + 1};colour:{slots[ seat ].colour}",
                    ScoresSummary ()
                );
            }

            return RemovalOutcome.Transferred;
        }

        EliminateSeat ( seat );
        return RemovalOutcome.Eliminated;
    }

    /// <summary>Called by the voting screen after someone is eliminated.</summary>
    public void EliminateSeat ( int seat )
    {
        if ( seat < 0 || seat >= slots.Count )
            return;

        slots[ seat ].inPlay = false;

        if ( SessionLogger.instance != null )
        {
            SessionLogger.instance.LogEvent (
                -1, slots[ seat ].bikeIndex + 1, "Elimination", "eliminated", seat,
                DisplayName ( seat ),
                $"colour:{slots[ seat ].colour};driver:{( slots[ seat ].isAI ? "ai" : "human" )}"
            );
        }

        GameObject go = RiderObject ( seat );

        if ( go != null )
        {
            if ( seat != 0 )
            {
                go.SetActive ( false );
            }
            else
            {
                // The player object stays alive - UI and other systems may
                // reference it - but it stops riding regardless of driver.
                RetireSeatZero ( go );
            }
        }

        RebuildBikeRiders ();

        if ( bikeVisuals != null )
            bikeVisuals.SetRiderCount ( ActiveRiderCount () );
    }

    /// <summary>
    /// Takes the player seat out of play without deactivating the GameObject:
    /// both possible drivers are switched off, input is zeroed, and the visuals
    /// are hidden.
    /// </summary>
    private void RetireSeatZero ( GameObject go )
    {
        PlayerRider human = go.GetComponent<PlayerRider> ();

        if ( human != null )
        {
            human.enabled = false;

            if ( human.playerMovementArrow != null )
                human.playerMovementArrow.SetActive ( false );

            if ( human.staminaSlider != null )
                human.staminaSlider.gameObject.SetActive ( false );
        }

        // Frozen input would otherwise keep being averaged into the bike.
        RiderInput input = go.GetComponent<RiderInput> ();

        if ( input != null )
            input.Clear ();

        SpriteRenderer body = go.GetComponent<SpriteRenderer> ();

        if ( body != null )
            body.enabled = false;
    }
}