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
/// Agreeing how the bike is run: the target colour, the effort level,
/// how long the arrangement stands, and whether to reopen it.
/// </summary>
public partial class RoundEndController
{
    // ------------------------------------------------------------------
    // Rules
    // ------------------------------------------------------------------

    /// <summary>
    /// Run once before the first round: every bike agrees its opening rules.
    /// </summary>
    /// <summary>
    /// The opening phase, before anyone has moved. Each bike decides how it
    /// will be governed first - because if they elect a leader, that leader
    /// writes the rules rather than the group voting on them.
    ///
    /// Nothing is preset: the bike has no rules at all until this finishes.
    /// </summary>
    public async Task RunOpeningPhase ()
    {
        forcedElimination = false;

        SetupDiscussionUI ();
        transcript.Clear ();

        for ( int b = 0; b < Game.bikes.Count; b++ )
        {
            if ( quitting )
                break;

            if ( Game.RidersOnBike ( b ) == 0 )
                continue;

            activeBikeIndex = b;
            voteCycle++;

            currentLeaderSeat = -1;
            Game.SetLeader ( b, -1 );

            RebuildRoster ();
            BuildPanels ();

            LogConsole ( $"=== Bike {b + 1}: {BuildActiveColorsString ()} ===" );
            LogConsole ( "Before you set off, decide how this bike will be run." );

            LogPhaseEvent ( "opening_started", -1, $"riders:{Game.RidersOnBike ( b )}" );

            // 1. leader or group?
            bool useLeader = await RunMethodVote ();

            cycleType = useLeader ? "leader" : "group";

            // 2. elect, if that's the choice
            if ( useLeader )
            {
                currentLeaderSeat = await RunLeaderElection ();

                if ( currentLeaderSeat < 0 )
                    LogConsole ( "No leader emerged. The group will decide the rules together." );
            }

            // 3. how long does this arrangement last?
            await RunTermVote ();

            // 4. whoever holds the power writes the rules
            if ( currentLeaderSeat >= 0 )
                await RunLeaderSetsRules ();
            else
                await RunRulesVote ();

            Game.SetLeader ( activeBikeIndex, currentLeaderSeat );

            await Pause ( $"Bike {activeBikeIndex + 1} is ready - continue" );
        }

        FinalizeDiscussionUI ();
    }

    /// <summary>Kept for anything still calling the old name.</summary>
    public async Task RunRulesVoteForAllBikes ()
    {
        await RunOpeningPhase ();
    }

    /// <summary>Two questions: which colour to chase, and how hard to pedal.</summary>
    private async Task RunRulesVote ()
    {
        BikeRules proposed = Game.RulesForBike ( activeBikeIndex ).Clone ();

        phase = Phase.RulesTarget;
        phaseRoundNumber = 1;
        cycleType = "rules";
        ResetTallies ();

        SetRoundLabel ( "Which colour do we chase?" );
        LogConsole ( "--- Rule 1: which colour of loot should this bike chase? ---" );

        await RunSpeakingRound ();

        string topColour = BestOf ( ruleColourVotes, out int topColourVotes );

        if ( ruleOwnColourVotes >= topColourVotes || string.IsNullOrEmpty ( topColour ) )
        {
            proposed.targetOwnColour = true;
        }
        else if ( BikeRules.TryParseColour ( topColour, out GameController.PlayerColour parsed ) )
        {
            proposed.targetOwnColour = false;
            proposed.targetColour = parsed;
        }

        LogConsole ( $"Agreed: chase {proposed.TargetName}." );

        LogPhaseEvent ( "rules_target_agreed", -1,
            $"target:{proposed.TargetName};own_votes:{ruleOwnColourVotes};top_votes:{topColourVotes}" );

        await Pause ( "Continue to the effort rule" );

        phase = Phase.RulesEffort;
        phaseRoundNumber = 1;
        ResetTallies ();

        SetRoundLabel ( "How hard do we pedal?" );
        LogConsole ( "--- Rule 2: how hard should everyone pedal? ---" );

        await RunSpeakingRound ();

        string topEffort = BestOf ( ruleEffortVotes, out int _ );

        if ( BikeRules.TryParseEffort ( topEffort, out BikeRules.EffortLevel level ) )
            proposed.effort = level;

        LogConsole ( $"Agreed: pedal {proposed.effort}." );

        LogPhaseEvent ( "rules_effort_agreed", -1, $"effort:{proposed.effort}" );

        proposed.agreed = true;

        Game.SetRulesForBike ( activeBikeIndex, proposed );

        LogConsole ( $"=== Bike {activeBikeIndex + 1} rules: {proposed.Describe ()} ===" );

        await Pause ( "Continue" );
    }

    /// <summary>
    /// How many rounds the arrangement just agreed should stand for. Voted by
    /// the bike, not the leader - so a leader cannot grant themselves an
    /// indefinite term.
    /// </summary>
    private async Task RunTermVote ()
    {
        phase = Phase.TermVote;
        phaseRoundNumber = 1;
        ResetTallies ();

        SetRoundLabel ( "How long should this stand?" );

        LogConsole ( currentLeaderSeat >= 0
            ? $"--- For how many rounds does {SeatName ( currentLeaderSeat )} lead? ---"
            : "--- For how many rounds do these rules stand before we revisit them? ---" );

        // The leader may argue their case, but cannot vote on the length of
        // their own term - otherwise they are setting it themselves.
        bool leaderExcluded = false;

        if ( currentLeaderSeat >= 0 && randomisedIndex.Contains ( currentLeaderSeat ) )
        {
            await TakeTurn ( currentLeaderSeat );

            randomisedIndex.Remove ( currentLeaderSeat );
            leaderExcluded = true;
        }

        await RunSpeakingRound ();

        if ( leaderExcluded )
            randomisedIndex.Add ( currentLeaderSeat );

        string top = BestOf ( termVotes, out int _ );

        int rounds = 2;

        if ( !string.IsNullOrEmpty ( top ) && int.TryParse ( top, out int parsed ) )
            rounds = Mathf.Clamp ( parsed, 1, 3 );

        Game.SetGovernanceTerm ( activeBikeIndex, rounds );

        LogConsole ( currentLeaderSeat >= 0
            ? $"{SeatName ( currentLeaderSeat )} leads for {rounds} round(s)."
            : $"These rules stand for {rounds} round(s)." );

        LogPhaseEvent ( "term_agreed", currentLeaderSeat, $"rounds:{rounds}" );

        await Pause ( "Continue" );
    }

    /// <summary>A leader sets the rules alone, answering both questions.</summary>
    private async Task RunLeaderSetsRules ()
    {
        BikeRules proposed = Game.RulesForBike ( activeBikeIndex ).Clone ();

        phase = Phase.RulesTarget;
        phaseRoundNumber = 1;
        ResetTallies ();

        SetRoundLabel ( $"{SeatName ( currentLeaderSeat )} sets the rules" );
        LogConsole ( $"--- {SeatName ( currentLeaderSeat )} sets the rules alone. ---" );

        await TakeTurn ( currentLeaderSeat );

        string topColour = BestOf ( ruleColourVotes, out int topColourVotes );

        if ( ruleOwnColourVotes >= topColourVotes || string.IsNullOrEmpty ( topColour ) )
        {
            proposed.targetOwnColour = true;
        }
        else if ( BikeRules.TryParseColour ( topColour, out GameController.PlayerColour parsed ) )
        {
            proposed.targetOwnColour = false;
            proposed.targetColour = parsed;
        }

        phase = Phase.RulesEffort;
        ResetTallies ();

        await TakeTurn ( currentLeaderSeat );

        string topEffort = BestOf ( ruleEffortVotes, out int _ );

        if ( BikeRules.TryParseEffort ( topEffort, out BikeRules.EffortLevel level ) )
            proposed.effort = level;

        proposed.agreed = true;

        Game.SetRulesForBike ( activeBikeIndex, proposed );

        LogConsole ( $"=== {SeatName ( currentLeaderSeat )} sets: {proposed.Describe ()} ===" );
    }

    /// <summary>Asks whether to revisit the rules at all.</summary>
    private async Task RunRulesChangeVote ()
    {
        phase = Phase.RulesChangeVote;
        phaseRoundNumber = 1;
        ResetTallies ();

        BikeRules current = Game.RulesForBike ( activeBikeIndex );

        SetRoundLabel ( "Change the rules?" );
        LogConsole ( $"--- Current rules: {current.Describe ()}. Change them? ---" );

        await RunSpeakingRound ();

        bool reopening = rulesChangeYes > rulesChangeNo;

        LogPhaseEvent ( "rules_change_vote", -1,
            $"reopen:{reopening};yes:{rulesChangeYes};no:{rulesChangeNo};was:{current.Describe ()}" );

        await Pause ( "Continue" );

        if ( reopening )
        {
            LogConsole ( $"The riders will set new rules ({rulesChangeYes} to {rulesChangeNo})." );
            await RunRulesVote ();
        }
        else
        {
            LogConsole ( $"The rules stand ({rulesChangeNo} to {rulesChangeYes})." );
        }
    }

    private string BestOf ( Dictionary<string, int> votes, out int count )
    {
        string best = null;
        count = 0;

        foreach ( KeyValuePair<string, int> pair in votes )
        {
            if ( pair.Value > count )
            {
                count = pair.Value;
                best = pair.Key;
            }
        }

        return best;
    }
}
