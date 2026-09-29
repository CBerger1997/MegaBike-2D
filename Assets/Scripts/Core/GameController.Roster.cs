using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Who is in the game and what they are called: seats, identity, and
/// the colour lookups that replaced colour-as-identity.
/// </summary>
public partial class GameController
{
    // ------------------------------------------------------------------
    // Roster helpers
    // ------------------------------------------------------------------

    public Color ColourFor ( PlayerColour colour )
    {
        int index = ( int ) colour;

        if ( index >= 0 && index < playerColours.Count )
            return playerColours[ index ];

        Debug.LogWarning ( $"GameController: no palette entry for {colour}." );
        return Color.white;
    }

    /// <summary>The GameObject sitting in a given seat. Seat 0 is the player.</summary>
    public GameObject RiderObject ( int seat )
    {
        if ( seat == 0 )
            return player;

        int aiIndex = seat - 1;

        return aiIndex >= 0 && aiIndex < aiPlayers.Count ? aiPlayers[ aiIndex ] : null;
    }

    /// <summary>GameObject is on AND the rider hasn't been eliminated.</summary>
    public bool SeatInPlay ( int seat )
    {
        return seat >= 0 &&
               seat < slots.Count &&
               slots[ seat ].inPlay &&
               SeatIsActive ( seat );
    }

    public bool SeatIsActive ( int seat )
    {
        GameObject go = RiderObject ( seat );

        return go != null && go.activeSelf;
    }

    /// <summary>
    /// The active driver of a seat. Seat 0 may hold both a PlayerRider and an
    /// AIRider, only one of which is enabled, so this returns whichever is live.
    /// </summary>
    public IRider GetRider ( int seat )
    {
        GameObject go = RiderObject ( seat );

        if ( go == null )
            return null;

        foreach ( IRider r in go.GetComponents<IRider> () )
        {
            if ( r is MonoBehaviour mb && mb.enabled )
                return r;
        }

        return null;
    }

    /// <summary>Seat index of a colour, or -1. Used by the voting screen.</summary>
    public int SeatForColour ( PlayerColour colour )
    {
        for ( int i = 0; i < slots.Count; i++ )
        {
            if ( slots[ i ].colour == colour )
                return i;
        }

        return -1;
    }

    public int ActiveRiderCount ()
    {
        int count = 0;

        for ( int i = 0; i < slots.Count; i++ )
        {
            if ( SeatIsActive ( i ) && slots[ i ].inPlay )
                count++;
        }

        return count;
    }

    /// <summary>True if seat 0 is being driven by an AI rather than the human.</summary>
    public bool PlayerSeatIsAI => slots.Count > 0 && slots[ 0 ].isAI;

    // ------------------------------------------------------------------
    // Identity
    // ------------------------------------------------------------------

    /// <summary>
    /// Stable, anonymous name for a seat. Deliberately says nothing about
    /// whether the rider is human or AI, and stays unique when colours repeat.
    /// </summary>
    public string DisplayName ( int seat )
    {
        return $"Player {seat + 1}";
    }

    /// <summary>"Player 3 (Blue)" - for prompts and any UI with room for it.</summary>
    public string DisplayNameWithColour ( int seat )
    {
        if ( seat < 0 || seat >= slots.Count )
            return "Unknown";

        return $"{DisplayName ( seat )} ({slots[ seat ].colour})";
    }

    /// <summary>Seat for a name like "Player 3", or -1. Tolerates a bare number.</summary>
    public int SeatForDisplayName ( string name )
    {
        if ( string.IsNullOrEmpty ( name ) )
            return -1;

        string cleaned = name.Trim ();

        int space = cleaned.LastIndexOf ( ' ' );

        if ( space >= 0 )
            cleaned = cleaned.Substring ( space + 1 );

        if ( int.TryParse ( cleaned, out int number ) )
        {
            int seat = number - 1;

            if ( seat >= 0 && seat < slots.Count )
                return seat;
        }

        return -1;
    }

    /// <summary>Everyone sharing a colour - the basis for informal alliances.</summary>
    public List<int> SeatsWithColour ( PlayerColour colour, int bikeIndex = -1 )
    {
        List<int> result = new List<int> ();

        for ( int seat = 0; seat < slots.Count; seat++ )
        {
            if ( !slots[ seat ].inPlay || slots[ seat ].colour != colour )
                continue;

            if ( bikeIndex >= 0 && slots[ seat ].bikeIndex != bikeIndex )
                continue;

            result.Add ( seat );
        }

        return result;
    }

    /// <summary>Randomises every rider's colour, allowing repeats.</summary>
    public void RandomiseColours ( int distinctColours = 0 )
    {
        int available = System.Enum.GetValues ( typeof ( PlayerColour ) ).Length;

        if ( distinctColours > 0 )
            available = Mathf.Clamp ( distinctColours, 1, available );

        foreach ( RiderSlot slot in slots )
            slot.colour = ( PlayerColour ) Random.Range ( 0, available );
    }
}
