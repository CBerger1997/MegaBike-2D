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
/// Entry point and the shape of a deliberation: who decides, the group
/// vote, the leader path, and what happens to whoever is removed.
/// </summary>
public partial class RoundEndController
{
    // ------------------------------------------------------------------
    // Entry point
    // ------------------------------------------------------------------

    public async void TriggerDiscussion ()
    {
        forcedElimination = uiManager != null && uiManager.LastEndReason == "owdi";

        List<int> bikesToVote = new List<int> ();

        if ( uiManager != null && uiManager.hitBikeIndex >= 0 )
        {
            // Owdi hit: only the struck bike deliberates.
            bikesToVote.Add ( uiManager.hitBikeIndex );
        }
        else if ( voteAllBikesOnTimer )
        {
            for ( int b = 0; b < Game.bikes.Count; b++ )
            {
                if ( Game.RidersOnBike ( b ) > 1 )
                    bikesToVote.Add ( b );
            }

            // Shuffled, so the same bike doesn't always deliberate first.
            for ( int i = 0; i < bikesToVote.Count - 1; i++ )
            {
                int r = UnityEngine.Random.Range ( i, bikesToVote.Count );
                int tmp = bikesToVote[ i ];
                bikesToVote[ i ] = bikesToVote[ r ];
                bikesToVote[ r ] = tmp;
            }
        }
        else
        {
            bikesToVote.Add ( HumanIsPlaying ? Game.slots[ PlayerSeat ].bikeIndex : 0 );
        }

        SetupDiscussionUI ();
        transcript.Clear ();

        foreach ( int bikeIndex in bikesToVote )
        {
            if ( quitting )
                return;

            int aboard = Game.RidersOnBike ( bikeIndex );

            if ( aboard == 0 )
                continue;

            // A lone rider has nobody to deliberate with. If the Owdi caught
            // them there is still a consequence - they are thrown off and must
            // find another bike, or they are out.
            if ( aboard == 1 )
            {
                if ( !forcedElimination )
                    continue;

                activeBikeIndex = bikeIndex;
                voteCycle++;

                RebuildRoster ();
                BuildPanels ();

                if ( randomisedIndex.Count == 1 )
                {
                    int lone = randomisedIndex[ 0 ];

                    SetRoundLabel ( $"Bike {bikeIndex + 1} is caught" );

                    LogConsole ( $"=== The Owdi caught bike {bikeIndex + 1} with only " +
                                 $"{SeatName ( lone )} aboard. There is nobody to vote. ===" );

                    LogPhaseEvent ( "lone_rider_caught", lone, "" );

                    await RemoveRider ( lone, "caught alone by the Owdi" );

                    await Pause ( "Continue" );
                }

                continue;
            }

            activeBikeIndex = bikeIndex;

            await RunVoteForBike ();

            await Pause ( $"Bike {bikeIndex + 1} finished - continue" );
        }

        FinalizeDiscussionUI ();

        // Unattended runs have nobody to press End Voting.
        if ( autoEndVoting && !quitting )
        {
            SetRoundLabel ( "Next round starting..." );

            await WaitSeconds ( autoEndDelay );

            if ( !quitting )
                EndVoting ();
        }
    }

    private async Task RunVoteForBike ()
    {
        voteCycle++;

        RebuildRoster ();
        BuildPanels ();

        // A leader elected earlier stays in post until their term expires.
        currentLeaderSeat = Game.LeaderOfBike ( activeBikeIndex );
        leaderChoiceSeat = -1;

        if ( currentLeaderSeat >= 0 && !StillAboard ( currentLeaderSeat ) )
        {
            currentLeaderSeat = -1;
            Game.SetLeader ( activeBikeIndex, -1 );
        }

        bool termExpired = Game.GovernanceExpired ( activeBikeIndex );

        LogConsole ( $"=== Bike {activeBikeIndex + 1}: {BuildActiveColorsString ()} ===" );

        LogPhaseEvent ( "deliberation_started", -1,
            $"forced:{forcedElimination};riders:{Game.RidersOnBike ( activeBikeIndex )}" );

        LogConsole ( forcedElimination
            ? "The Owdi caught this bike. Someone MUST be removed."
            : "The round is over. You may remove someone, or no one." );

        // 1. discussion
        phase = Phase.Discussion;
        cycleType = "undecided";

        for ( int round = 1; round <= discussionRounds; round++ )
        {
            phaseRoundNumber = round;

            SetRoundLabel ( $"Discussion {round} of {discussionRounds}" );
            LogConsole ( $"--- Discussion round {round} of {discussionRounds} ---" );

            await RunSpeakingRound ();

            if ( quitting )
                return;
        }

        await Pause ( "Discussion over - continue to the decision" );

        // 2. who decides this time?
        if ( currentLeaderSeat >= 0 && !termExpired )
        {
            // The sitting leader still holds office, so there is nothing to
            // vote on - they simply decide.
            cycleType = "leader";

            LogConsole ( $"{SeatName ( currentLeaderSeat )} is still leader " +
                         $"({Game.RoundsLeftOfTerm ( activeBikeIndex )} round(s) of their term left)." );

            await RunSittingLeaderDecision ();
        }
        else
        {
            if ( currentLeaderSeat >= 0 && termExpired )
            {
                LogConsole ( $"{SeatName ( currentLeaderSeat )}'s term has ended. The bike must decide again." );
                LogPhaseEvent ( "term_expired", currentLeaderSeat, "" );

                currentLeaderSeat = -1;
                Game.SetLeader ( activeBikeIndex, -1 );
            }

            bool useLeader = await RunMethodVote ();

            cycleType = useLeader ? "leader" : "group";

            if ( useLeader )
                await RunLeaderPath ();
            else
                await RunGroupVote ();
        }

        // Someone may have just left the bike, so rebuild before the rules
        // phases rather than carrying the pre-vote roster into them.
        RebuildRoster ();
        BuildPanels ();

        // A leader who was thrown off or transferred no longer leads.
        if ( currentLeaderSeat >= 0 && !StillAboard ( currentLeaderSeat ) )
        {
            LogConsole ( $"{SeatName ( currentLeaderSeat )} is no longer on this bike. Leadership ends." );
            LogPhaseEvent ( "leader_lost", currentLeaderSeat, "left the bike" );

            currentLeaderSeat = -1;
            Game.SetLeader ( activeBikeIndex, -1 );
        }

        if ( randomisedIndex.Count == 0 )
        {
            LogConsole ( "Nobody is left on this bike to set rules." );
            return;
        }

        // 4. rules for the round ahead
        if ( currentLeaderSeat >= 0 && !leaderSteppedDown )
            await RunLeaderSetsRules ();
        else
            await RunRulesChangeVote ();

        Game.SetLeader ( activeBikeIndex, currentLeaderSeat );
    }

    // ------------------------------------------------------------------
    // Method vote
    // ------------------------------------------------------------------

    private async Task<bool> RunMethodVote ()
    {
        phase = Phase.MethodVote;
        phaseRoundNumber = 1;
        methodGroupVotes = 0;
        methodLeaderVotes = 0;

        SetRoundLabel ( "How should this be decided?" );
        LogConsole ( "--- Vote: should the group decide together, or should one rider lead? ---" );

        await RunSpeakingRound ();

        bool useLeader;

        if ( methodLeaderVotes == methodGroupVotes )
        {
            useLeader = UnityEngine.Random.value < 0.5f;

            LogConsole ( $"Tied {methodGroupVotes}-{methodLeaderVotes}. " +
                         $"Decided at random: {( useLeader ? "a leader will choose" : "the group will vote" )}." );
        }
        else
        {
            useLeader = methodLeaderVotes > methodGroupVotes;

            LogConsole ( useLeader
                ? $"A leader will choose ({methodLeaderVotes} to {methodGroupVotes})."
                : $"The group will vote ({methodGroupVotes} to {methodLeaderVotes})." );
        }

        LogPhaseEvent ( "method_chosen", -1,
            $"{( useLeader ? "leader" : "group" )};group:{methodGroupVotes};leader:{methodLeaderVotes}" );

        await Pause ( "Continue" );

        return useLeader;
    }

    // ------------------------------------------------------------------
    // Group path
    // ------------------------------------------------------------------

    private async Task RunGroupVote ()
    {
        phase = Phase.GroupVote;
        phaseRoundNumber = 1;
        ResetTallies ();

        SetRoundLabel ( "Group vote" );
        LogConsole ( NobodyAllowed
            ? "--- Final vote. You may also vote for Nobody. ---"
            : "--- Final vote. Someone must be removed. ---" );

        await RunSpeakingRound ();

        await EvaluateGroupVote ();
    }

    private async Task EvaluateGroupVote ()
    {
        List<int> top = TopSeats ();
        int best = top.Count > 0 ? votingCounts[ top[ 0 ] ] : 0;

        if ( NobodyAllowed && nobodyVotes >= best && nobodyVotes > 0 )
        {
            LogConsole ( $"Nobody is removed ({nobodyVotes} votes). Everyone stays on the bike." );
            LogPhaseEvent ( "vote_nobody", -1, $"votes:{nobodyVotes}" );
            return;
        }

        if ( top.Count == 0 )
        {
            LogConsole ( "Nobody received a vote. No one is removed." );
            LogPhaseEvent ( "no_votes", -1, "" );
            return;
        }

        int loser;

        if ( top.Count > 1 )
        {
            string names = JoinSeatNames ( top );

            if ( !randomTieBreak )
            {
                LogConsole ( $"There is a tie between {names}. No one is removed." );
                LogPhaseEvent ( "vote_tie", -1, names );
                return;
            }

            loser = top[ UnityEngine.Random.Range ( 0, top.Count ) ];

            LogConsole ( $"There is a tie between {names}." );
            LogConsole ( $"A coin is flipped. {SeatName ( loser )} loses." );
            LogPhaseEvent ( "vote_tie_broken", loser, $"tied:{names}" );
        }
        else
        {
            loser = top[ 0 ];
            LogPhaseEvent ( "vote_result", loser, $"votes:{votingCounts[ loser ]}" );
        }

        await Pause ( "Continue" );

        await RemoveRider ( loser, "group vote" );
    }

    // ------------------------------------------------------------------
    // Leader path
    // ------------------------------------------------------------------

    /// <summary>
    /// Elects a leader and nothing more. Returns the seat, or -1 if the vote
    /// produced nobody. Used both at the opening and before an elimination.
    /// </summary>
    private async Task<int> RunLeaderElection ()
    {
        leaderSteppedDown = false;

        phase = Phase.LeaderElection;
        phaseRoundNumber = 1;
        ResetTallies ();

        SetRoundLabel ( "Leader election" );
        LogConsole ( "--- Choose a leader. You may nominate yourself. ---" );

        await RunSpeakingRound ();

        int elected = ResolveLeader ();

        if ( elected < 0 )
            return -1;

        Game.SetLeader ( activeBikeIndex, elected );

        LogConsole ( $"=== {SeatName ( elected )} is LEADER. They decide alone and steer the bike. ===" );

        return elected;
    }

    /// <summary>
    /// A leader already in office: pleas then their decision, with no election
    /// and no method vote. That is what holding a term actually buys them.
    /// </summary>
    private async Task RunSittingLeaderDecision ()
    {
        leaderSteppedDown = false;

        await RunPleasAndDecision ();
    }

    private async Task RunLeaderPath ()
    {
        currentLeaderSeat = await RunLeaderElection ();

        if ( currentLeaderSeat < 0 )
        {
            LogConsole ( "No leader could be chosen. Falling back to a group vote." );
            await RunGroupVote ();
            return;
        }

        // A newly elected leader serves a term the bike agrees to.
        await RunTermVote ();

        await RunPleasAndDecision ();
    }

    /// <summary>
    /// Everyone makes their case to the leader, then the leader alone decides.
    /// Shared by a freshly elected leader and one already serving a term.
    /// </summary>
    private async Task RunPleasAndDecision ()
    {
        phase = Phase.LeaderPlea;
        intentions.Clear ();

        for ( int round = 1; round <= leaderPleaRounds; round++ )
        {
            phaseRoundNumber = round;

            SetRoundLabel ( $"Pleading to {SeatName ( currentLeaderSeat )} - {round} of {leaderPleaRounds}" );
            LogConsole ( $"--- Plea round {round} of {leaderPleaRounds} ---" );

            await RunSpeakingRound ();
        }

        phase = Phase.LeaderDecision;
        phaseRoundNumber = 1;
        ResetTallies ();

        SetRoundLabel ( $"{SeatName ( currentLeaderSeat )} decides" );
        LogConsole ( $"--- {SeatName ( currentLeaderSeat )} decides. ---" );

        // A leader may stand down instead of deciding, in which case their
        // successor - or the group - resolves the round instead.
        for ( int attempt = 0; attempt < 3; attempt++ )
        {
            phase = Phase.LeaderDecision;
            phaseRoundNumber = 1;
            leaderSteppedDown = false;
            leaderChoiceSeat = -1;
            ResetTallies ();

            await TakeTurn ( currentLeaderSeat );

            if ( quitting )
                return;

            if ( !leaderSteppedDown )
                break;

            bool handedOn = await RunSuccession ();

            if ( !handedOn )
            {
                LogConsole ( "Leadership returns to the group, who must now vote." );

                currentLeaderSeat = -1;
                Game.SetLeader ( activeBikeIndex, -1 );

                await RunGroupVote ();
                return;
            }

            SetRoundLabel ( $"{SeatName ( currentLeaderSeat )} decides" );
            LogConsole ( $"--- {SeatName ( currentLeaderSeat )} now decides. ---" );
        }

        if ( leaderSteppedDown )
        {
            // Three in a row refused the job. Hand it back.
            currentLeaderSeat = -1;
            Game.SetLeader ( activeBikeIndex, -1 );

            await RunGroupVote ();
            return;
        }

        if ( leaderChoiceSeat == NobodySeat )
        {
            LogConsole ( $"{SeatName ( currentLeaderSeat )} spares everyone." );
            LogPhaseEvent ( "leader_spared", -1, $"by:{SeatName ( currentLeaderSeat )}" );
        }
        else if ( leaderChoiceSeat >= 0 )
        {
            LogPhaseEvent ( "leader_decision", leaderChoiceSeat, $"by:{SeatName ( currentLeaderSeat )}" );
            await RemoveRider ( leaderChoiceSeat, $"decision of {SeatName ( currentLeaderSeat )}" );
        }
        else
        {
            LogConsole ( $"{SeatName ( currentLeaderSeat )} failed to choose. Nobody is removed." );
        }

        // The leader keeps the job - their term decides when it ends, not
        // the end of this round.
        Game.SetLeader ( activeBikeIndex, currentLeaderSeat );
    }

    /// <summary>
    /// A leader standing down names a successor or hands back to the group.
    /// A named successor inherits whatever is left of the term.
    /// </summary>
    private async Task<bool> RunSuccession ()
    {
        int outgoing = currentLeaderSeat;

        LogConsole ( $"{SeatName ( outgoing )} steps down." );
        LogPhaseEvent ( "leader_stepped_down", outgoing, "" );

        phase = Phase.Succession;
        phaseRoundNumber = 1;
        namedSuccessorSeat = -1;
        ResetTallies ();

        SetRoundLabel ( $"{SeatName ( outgoing )} names a successor" );
        LogConsole ( $"--- {SeatName ( outgoing )} may name a successor, or hand back to the group. ---" );

        await TakeTurn ( outgoing );

        if ( namedSuccessorSeat < 0 || !StillAboard ( namedSuccessorSeat ) ||
             namedSuccessorSeat == outgoing )
        {
            LogPhaseEvent ( "succession_to_group", outgoing, "" );
            return false;
        }

        currentLeaderSeat = namedSuccessorSeat;
        Game.SetLeader ( activeBikeIndex, currentLeaderSeat );

        LogConsole ( $"=== {SeatName ( currentLeaderSeat )} takes over as LEADER, " +
                     $"with {Game.RoundsLeftOfTerm ( activeBikeIndex )} round(s) of the term left. ===" );

        LogPhaseEvent ( "succession_named", currentLeaderSeat, $"from:{SeatName ( outgoing )}" );

        await Pause ( "Continue" );

        return true;
    }

    private int ResolveLeader ()
    {
        List<int> top = TopSeats ();

        if ( top.Count == 0 )
            return -1;

        if ( top.Count == 1 )
        {
            LogPhaseEvent ( "leader_elected", top[ 0 ], $"votes:{votingCounts[ top[ 0 ] ]}" );
            return top[ 0 ];
        }

        int picked = top[ UnityEngine.Random.Range ( 0, top.Count ) ];
        string names = JoinSeatNames ( top );

        LogConsole ( $"Tied for leader between {names}. {SeatName ( picked )} is chosen at random." );
        LogPhaseEvent ( "leader_tie_broken", picked, names );

        return picked;
    }

    // ------------------------------------------------------------------
    // Removal
    // ------------------------------------------------------------------

    /// <summary>
    /// Throws a rider off. They may then try to buy their way onto another
    /// bike; if nobody will have them, they are out of the game.
    /// </summary>
    private async Task RemoveRider ( int seat, string reason )
    {
        int fromBike = Game.slots[ seat ].bikeIndex;
        bool transfersOn = Game.allowBikeTransfers;

        // With transfers off the seat-aware lookup always refuses, so don't
        // even ask - removal is final and there is nothing to negotiate.
        int target = transfersOn ? Game.FindBikeWithSpace ( fromBike, seat ) : -1;

        LogConsole ( $"{SeatName ( seat )} is thrown off bike {fromBike + 1}." );

        if ( target >= 0 )
        {
            bool accepted = await RunJoinAttempt ( seat, fromBike, target );

            if ( accepted )
            {
                Game.MoveSeatToBike ( seat, target );

                LogPhaseEvent ( "removed_transferred", seat,
                    $"to_bike:{target + 1};reason:{reason};bribe:{pendingBribe}" );

                return;
            }
        }
        else if ( !transfersOn )
        {
            // Logged so the condition is visible in the events CSV rather than
            // having to be inferred from the absence of join rows.
            LogConsole ( "There is no second seat to beg for. Removal is final." );
            LogPhaseEvent ( "join_disabled", seat, "transfers:off" );
        }
        else
        {
            // Distinguish "no room" from "barred" - they read very differently
            // in a transcript.
            bool barred = Game.FindBikeWithSpace ( fromBike ) >= 0;

            LogConsole ( barred
                ? $"{SeatName ( seat )} left that bike too recently to be allowed back."
                : "Every other bike is full. There is nowhere to go." );

            LogPhaseEvent ( barred ? "join_barred_cooldown" : "join_no_space", seat, "" );
        }

        bool isHuman = seat == PlayerSeat && HumanIsPlaying;

        if ( transfersOn )
        {
            LogConsole ( isHuman
                ? "Nobody will take you. You are out. GAME OVER."
                : $"Nobody will take {SeatName ( seat )}. They are out." );
        }
        else
        {
            LogConsole ( isHuman
                ? "You are out. GAME OVER."
                : $"{SeatName ( seat )} is out." );
        }

        Game.EliminateSeat ( seat );

        LogPhaseEvent ( "removed_eliminated", seat,
            $"reason:{reason};transfers:{( transfersOn ? "on" : "off" )}" );
    }

    /// <summary>
    /// The other bike deliberates on an outsider. The candidate pleads and may
    /// offer points from their own score - to the bike's pot, or to one rider.
    /// A refused offer is never paid.
    /// </summary>
    private async Task<bool> RunJoinAttempt ( int seat, int fromBike, int targetBike )
    {
        // Remember where we were: this deliberation belongs to the other bike.
        int savedBike = activeBikeIndex;
        int savedLeader = currentLeaderSeat;
        Phase savedPhase = phase;

        pendingJoinerSeat = seat;
        pendingBribe = 0;
        pendingBribeTargetSeat = -1;

        activeBikeIndex = targetBike;
        currentLeaderSeat = Game.LeaderOfBike ( targetBike );

        RebuildRoster ();

        // The candidate is not one of them, but must be able to speak.
        if ( !randomisedIndex.Contains ( seat ) )
        {
            randomisedIndex.Add ( seat );
            nameToSeat[ Game.DisplayName ( seat ) ] = seat;
        }

        BuildPanels ();

        LogConsole ( $"=== {SeatName ( seat )} asks to join bike {targetBike + 1} ===" );
        LogConsole ( $"They hold {Game.GetScore ( seat )} points. Bike {targetBike + 1} pot: {Game.GetBikeScore ( targetBike )}." );

        LogPhaseEvent ( "join_requested", seat, $"from_bike:{fromBike + 1};to_bike:{targetBike + 1}" );

        await Pause ( $"Continue - bike {targetBike + 1} considers them" );

        // negotiation
        phase = Phase.JoinPlea;
        intentions.Clear ();

        for ( int round = 1; round <= joinPleaRounds; round++ )
        {
            phaseRoundNumber = round;

            joinLeanAccept = 0;
            joinLeanReject = 0;

            SetRoundLabel ( $"{SeatName ( seat )} bargains - {round} of {joinPleaRounds}" );
            LogConsole ( $"--- Negotiation round {round} of {joinPleaRounds} ---" );

            await RunSpeakingRound ();

            if ( quitting )
                break;
        }

        // the bike decides
        phase = Phase.JoinVote;
        phaseRoundNumber = 1;
        ResetTallies ();

        SetRoundLabel ( $"Let {SeatName ( seat )} aboard?" );

        LogConsole ( pendingBribe > 0
            ? $"--- {SeatName ( seat )} offers {pendingBribe} points to {BribeTargetName ()}. Accept them? ---"
            : $"--- {SeatName ( seat )} offers nothing. Accept them? ---" );

        // Only the bike's existing riders vote, not the candidate.
        randomisedIndex.Remove ( seat );

        await RunSpeakingRound ();

        bool accepted = joinAcceptVotes > joinRejectVotes;

        if ( accepted )
        {
            int paid = pendingBribe > 0
                ? Game.PayPoints ( seat, pendingBribeTargetSeat, targetBike, pendingBribe )
                : 0;

            LogConsole ( paid > 0
                ? $"Accepted ({joinAcceptVotes} to {joinRejectVotes}). {paid} points go to {BribeTargetName ()}."
                : $"Accepted ({joinAcceptVotes} to {joinRejectVotes})." );

            LogPhaseEvent ( "join_accepted", seat,
                $"bike:{targetBike + 1};accept:{joinAcceptVotes};reject:{joinRejectVotes};paid:{paid}" );
        }
        else
        {
            // Nothing changes hands on a refusal.
            LogConsole ( $"Refused ({joinRejectVotes} to {joinAcceptVotes}). No points change hands." );

            LogPhaseEvent ( "join_refused", seat,
                $"bike:{targetBike + 1};accept:{joinAcceptVotes};reject:{joinRejectVotes};offered:{pendingBribe}" );
        }

        await Pause ( "Continue" );

        pendingJoinerSeat = -1;

        activeBikeIndex = savedBike;
        currentLeaderSeat = savedLeader;
        phase = savedPhase;

        RebuildRoster ();
        BuildPanels ();

        return accepted;
    }

    private string BribeTargetName ()
    {
        return pendingBribeTargetSeat >= 0
            ? SeatName ( pendingBribeTargetSeat )
            : $"bike {activeBikeIndex + 1}";
    }

    /// <summary>
    /// Pulls a number out of whatever the model wrote. "20" and "20 points"
    /// both work; anything with no digits reads as nothing offered.
    /// </summary>
    private int ParseAmount ( string text )
    {
        if ( string.IsNullOrEmpty ( text ) )
            return 0;

        string digits = string.Empty;

        foreach ( char c in text )
        {
            if ( char.IsDigit ( c ) )
                digits += c;
            else if ( digits.Length > 0 )
                break;
        }

        return int.TryParse ( digits, out int value ) ? value : 0;
    }

    /// <summary>Reads a bribe offer from the candidate's reply.</summary>
    private void RecordBribe ( int seat, LLMVoteResponse parsed )
    {
        if ( parsed == null || seat != pendingJoinerSeat )
            return;

        int offered = Mathf.Clamp ( ParseAmount ( parsed.bribe ), 0, Game.GetScore ( seat ) );

        pendingBribe = offered;
        pendingBribeTargetSeat = -1;

        if ( offered > 0 && !string.IsNullOrEmpty ( parsed.bribeTarget ) )
        {
            int to = ResolveTarget ( parsed.bribeTarget );

            if ( to >= 0 && to != seat )
                pendingBribeTargetSeat = to;
        }
    }
}