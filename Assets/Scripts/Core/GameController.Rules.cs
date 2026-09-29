using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Per-bike rules, leadership and the term that limits it.
/// </summary>
public partial class GameController
{
    // ------------------------------------------------------------------
    // Rules and leadership
    // ------------------------------------------------------------------

    /// <summary>Never returns null - a bike with no agreed rules uses defaults.</summary>
    public BikeRules RulesForBike ( int bikeIndex )
    {
        while ( bikeRules.Count < bikes.Count )
            bikeRules.Add ( new BikeRules () );

        if ( bikeIndex < 0 || bikeIndex >= bikeRules.Count )
            return new BikeRules ();

        return bikeRules[ bikeIndex ];
    }

    public void SetRulesForBike ( int bikeIndex, BikeRules rules )
    {
        while ( bikeRules.Count < bikes.Count )
            bikeRules.Add ( new BikeRules () );

        if ( bikeIndex < 0 || bikeIndex >= bikeRules.Count || rules == null )
            return;

        bikeRules[ bikeIndex ] = rules;

        if ( SessionLogger.instance != null )
        {
            SessionLogger.instance.LogEvent (
                -1, "Rules", "rules_set", -1, "",
                $"bike:{bikeIndex + 1};{rules.Describe ()}",
                ScoresSummary ()
            );
        }

        OnRosterChanged?.Invoke ();
    }

    /// <summary>Tells a bike which seat, if any, is holding the handlebars.</summary>
    public void SetLeader ( int bikeIndex, int seat )
    {
        if ( bikeIndex < 0 || bikeIndex >= bikes.Count || bikes[ bikeIndex ] == null )
            return;

        bikes[ bikeIndex ].leaderSeat = seat;

        if ( SessionLogger.instance != null && seat >= 0 )
        {
            SessionLogger.instance.LogEvent (
                -1, "Leadership", "leader_set", seat,
                seat < slots.Count ? slots[ seat ].colour.ToString () : "",
                $"bike:{bikeIndex + 1}",
                ScoresSummary ()
            );
        }
    }

    /// <summary>
    /// Starts a term: the leader and rules agreed now stand until this many
    /// rounds have passed, after which the bike must decide again.
    /// </summary>
    public void SetGovernanceTerm ( int bikeIndex, int rounds )
    {
        if ( bikeIndex < 0 || bikeIndex >= bikes.Count || bikes[ bikeIndex ] == null )
            return;

        bikes[ bikeIndex ].termRounds = rounds;
        bikes[ bikeIndex ].governanceExpiresRound = roundNumber + Mathf.Max ( 1, rounds );

        if ( SessionLogger.instance != null )
        {
            SessionLogger.instance.LogEvent (
                -1, "Governance", "term_set", -1, "",
                $"bike:{bikeIndex + 1};rounds:{rounds};expires_after:{bikes[ bikeIndex ].governanceExpiresRound}",
                ScoresSummary ()
            );
        }
    }

    /// <summary>True once the agreed term has run out.</summary>
    public bool GovernanceExpired ( int bikeIndex )
    {
        if ( bikeIndex < 0 || bikeIndex >= bikes.Count || bikes[ bikeIndex ] == null )
            return true;

        int expires = bikes[ bikeIndex ].governanceExpiresRound;

        return expires < 0 || roundNumber >= expires;
    }

    public int RoundsLeftOfTerm ( int bikeIndex )
    {
        if ( bikeIndex < 0 || bikeIndex >= bikes.Count || bikes[ bikeIndex ] == null )
            return 0;

        return Mathf.Max ( 0, bikes[ bikeIndex ].governanceExpiresRound - roundNumber );
    }

    public int LeaderOfBike ( int bikeIndex )
    {
        if ( bikeIndex < 0 || bikeIndex >= bikes.Count || bikes[ bikeIndex ] == null )
            return -1;

        return bikes[ bikeIndex ].leaderSeat;
    }
}
