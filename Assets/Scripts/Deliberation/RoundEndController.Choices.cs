using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LLMUnity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Turning what a rider said into a recorded choice: resolving names,
/// validating them, tallying them, and the fallback when they fail.
/// </summary>
public partial class RoundEndController
{
    // ------------------------------------------------------------------
    // Choices
    // ------------------------------------------------------------------

    private bool LooksLikeStepDown ( string named )
    {
        return !string.IsNullOrEmpty ( named ) &&
               named.Trim ().Replace ( " ", string.Empty )
                    .Equals ( StepDownName, StringComparison.OrdinalIgnoreCase );
    }

    /// <summary>Seat for a named colour, NobodySeat for "Nobody", int.MinValue if invalid.</summary>
    /// <summary>
    /// Resolves a named target to a seat. Names are "Player 3"; a bare colour
    /// is rejected, because several riders may share one.
    /// </summary>
    private int ResolveTarget ( string named )
    {
        if ( string.IsNullOrEmpty ( named ) )
            return int.MinValue;

        string trimmed = named.Trim ();

        if ( NobodyAllowed && string.Equals ( trimmed, NobodyName, StringComparison.OrdinalIgnoreCase ) )
            return NobodySeat;

        if ( nameToSeat.TryGetValue ( trimmed, out int seat ) )
            return seat;

        // Tolerate "3" or "player3" from a sloppy model.
        int parsed = Game != null ? Game.SeatForDisplayName ( trimmed ) : -1;

        if ( parsed >= 0 && randomisedIndex.Contains ( parsed ) )
            return parsed;

        return int.MinValue;
    }

    private bool IsValidTarget ( int speakerSeat, int targetSeat )
    {
        if ( targetSeat == NobodySeat )
            return NobodyAllowed;

        if ( targetSeat == int.MinValue || targetSeat < 0 )
            return false;

        return AllowSelf || targetSeat != speakerSeat;
    }

    /// <summary>Records a word answer during a method or rules phase.</summary>
    private void ApplyOptionChoice ( int speakerSeat, string label )
    {
        if ( string.IsNullOrEmpty ( label ) )
            return;

        string clean = label.Trim ();

        switch ( phase )
        {
            case Phase.MethodVote:

                if ( clean.ToLower ().StartsWith ( "l" ) )
                    methodLeaderVotes++;
                else
                    methodGroupVotes++;

                break;

            case Phase.RulesChangeVote:

                if ( clean.ToLower ().StartsWith ( "y" ) )
                    rulesChangeYes++;
                else
                    rulesChangeNo++;

                break;

            case Phase.RulesTarget:

                if ( string.Equals ( clean, OwnColourName, StringComparison.OrdinalIgnoreCase ) )
                    ruleOwnColourVotes++;
                else
                    ruleColourVotes[ clean ] = ruleColourVotes.TryGetValue ( clean, out int c ) ? c + 1 : 1;

                break;

            case Phase.RulesEffort:

                ruleEffortVotes[ clean ] = ruleEffortVotes.TryGetValue ( clean, out int e ) ? e + 1 : 1;
                break;

            case Phase.TermVote:

                // Advocacy, not a vote.
                if ( speakerSeat == currentLeaderSeat )
                    break;

                termVotes[ clean ] = termVotes.TryGetValue ( clean, out int t ) ? t + 1 : 1;
                break;

            case Phase.JoinVote:

                if ( clean.ToLower ().StartsWith ( "a" ) )
                    joinAcceptVotes++;
                else
                    joinRejectVotes++;

                break;

            case Phase.Succession:

                namedSuccessorSeat = clean.Equals ( "Group", StringComparison.OrdinalIgnoreCase )
                    ? -1
                    : ResolveTarget ( clean );

                break;

            case Phase.JoinPlea:

                // The candidate is bargaining, not voting on themselves.
                if ( speakerSeat == pendingJoinerSeat )
                    break;

                if ( clean.ToLower ().StartsWith ( "a" ) )
                    joinLeanAccept++;
                else
                    joinLeanReject++;

                break;
        }
    }

    /// <summary>True if the label is one of this phase's accepted answers.</summary>
    private bool IsValidOption ( string label )
    {
        if ( string.IsNullOrEmpty ( label ) )
            return false;

        foreach ( string option in CurrentOptionLabels () )
        {
            if ( string.Equals ( option, label.Trim (), StringComparison.OrdinalIgnoreCase ) )
                return true;
        }

        return false;
    }

    private void ApplyChoice ( int speakerSeat, int targetSeat )
    {
        if ( !IsValidTarget ( speakerSeat, targetSeat ) )
            return;

        if ( phase == Phase.LeaderDecision )
            leaderChoiceSeat = targetSeat;

        if ( targetSeat == NobodySeat )
        {
            if ( IsBinding )
                nobodyVotes++;
        }
        else if ( IsBinding )
        {
            votingCounts[ targetSeat ]++;
        }
        else
        {
            intentions[ speakerSeat ] = targetSeat;
        }

        RefreshTallyUI ();
    }

    private int FallbackTarget ( int speakerSeat )
    {
        List<int> candidates = new List<int> ();

        foreach ( int seat in randomisedIndex )
        {
            if ( seat != speakerSeat || AllowSelf )
                candidates.Add ( seat );
        }

        if ( candidates.Count == 0 )
            return int.MinValue;

        if ( fallbackFollowsMajority )
        {
            int best = candidates[ 0 ];
            int bestCount = -1;

            foreach ( int seat in candidates )
            {
                int count = IsBinding ? votingCounts[ seat ] : CountLeaningsAgainst ( seat );

                if ( count > bestCount )
                {
                    bestCount = count;
                    best = seat;
                }
            }

            return best;
        }

        return candidates[ UnityEngine.Random.Range ( 0, candidates.Count ) ];
    }

    private int CountLeaningsAgainst ( int seat )
    {
        int count = 0;

        foreach ( KeyValuePair<int, int> pair in intentions )
        {
            if ( pair.Value == seat )
                count++;
        }

        return count;
    }
}
