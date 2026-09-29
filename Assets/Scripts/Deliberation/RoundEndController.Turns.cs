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
/// Who is in the room and whose turn it is: the roster for the bike
/// currently deliberating, speaking order, and each rider's turn.
/// </summary>
public partial class RoundEndController
{
    // ------------------------------------------------------------------
    // Roster
    // ------------------------------------------------------------------

    private void RebuildRoster ()
    {
        int seats = Game != null ? Game.slots.Count : 0;

        randomisedIndex.Clear ();
        votingCounts.Clear ();
        colourToSeat.Clear ();
        nameToSeat.Clear ();
        intentions.Clear ();
        nobodyVotes = 0;

        for ( int seat = 0; seat < seats; seat++ )
        {
            votingCounts.Add ( 0 );

            if ( !Game.slots[ seat ].inPlay )
                continue;

            if ( Game.slots[ seat ].bikeIndex != activeBikeIndex )
                continue;

            if ( !Game.SeatIsActive ( seat ) )
                continue;

            randomisedIndex.Add ( seat );

            // Colours may be shared, so the player name is the identity.
            nameToSeat[ Game.DisplayName ( seat ) ] = seat;
            colourToSeat[ Game.slots[ seat ].colour.ToString () ] = seat;
        }

        ShuffleIndices ();
    }

    private string SeatColourName ( int seat )
    {
        if ( seat == NobodySeat )
            return NobodyName;

        if ( Game == null || seat < 0 || seat >= Game.slots.Count )
            return "Unknown";

        return Game.slots[ seat ].colour.ToString ();
    }

    private Color SeatColour ( int seat )
    {
        if ( Game == null || seat < 0 || seat >= Game.slots.Count )
            return Color.white;

        return Game.ColourFor ( Game.slots[ seat ].colour );
    }

    private bool SeatIsAI ( int seat ) =>
        Game != null && seat >= 0 && seat < Game.slots.Count && Game.slots[ seat ].isAI;

    private bool SeatThinksHuman ( int seat ) =>
        Game != null && seat >= 0 && seat < Game.slots.Count && Game.slots[ seat ].thinksHuman;

    private string SeatPersonality ( int seat ) =>
        Game != null && seat >= 0 && seat < Game.slots.Count
            ? Game.slots[ seat ].personality.ToString ()
            : "unknown";

    private void ResetTallies ()
    {
        for ( int i = 0; i < votingCounts.Count; i++ )
            votingCounts[ i ] = 0;

        nobodyVotes = 0;
        rulesChangeYes = 0;
        rulesChangeNo = 0;
        joinAcceptVotes = 0;
        joinRejectVotes = 0;
        joinLeanAccept = 0;
        joinLeanReject = 0;
        ruleOwnColourVotes = 0;
        ruleColourVotes.Clear ();
        ruleEffortVotes.Clear ();
        termVotes.Clear ();

        RefreshTallyUI ();
    }

    private List<int> TopSeats ()
    {
        List<int> top = new List<int> ();
        int best = 0;

        foreach ( int seat in randomisedIndex )
        {
            int votes = votingCounts[ seat ];

            if ( votes > best )
            {
                best = votes;
                top.Clear ();
                top.Add ( seat );
            }
            else if ( votes == best && votes > 0 )
            {
                top.Add ( seat );
            }
        }

        return top;
    }

    private string JoinSeatNames ( List<int> seats )
    {
        StringBuilder sb = new StringBuilder ();

        for ( int i = 0; i < seats.Count; i++ )
        {
            if ( i > 0 )
                sb.Append ( i == seats.Count - 1 ? " and " : ", " );

            sb.Append ( SeatName ( seats[ i ] ) );
        }

        return sb.ToString ();
    }

    // ------------------------------------------------------------------
    // Turn taking
    // ------------------------------------------------------------------

    private async Task RunSpeakingRound ()
    {
        ShuffleIndices ();

        List<int> speaking = new List<int> ( randomisedIndex );

        // Discussion is the expensive part and the least decisive, so it can
        // run with a subset of the bike rather than everyone, every round.
        bool discussion = phase == Phase.Discussion || phase == Phase.LeaderPlea;

        if ( discussion && speakersPerDiscussionRound > 0 &&
             speaking.Count > speakersPerDiscussionRound )
        {
            speaking = speaking.GetRange ( 0, speakersPerDiscussionRound );
        }

        foreach ( int seat in speaking )
        {
            if ( quitting )
                return;

            // A rider removed earlier in this deliberation - thrown off, or
            // transferred to the other bike - no longer has a say here.
            if ( !StillAboard ( seat ) )
                continue;

            // A Lazy rider often has nothing to add, which costs nothing to
            // generate and is in character.
            if ( discussion && SeatPersonalityIs ( seat, AIRider.Personality.Lazy ) &&
                 UnityEngine.Random.value > lazySpeakChance )
            {
                LogSpokenLine ( seat, SpeakerName ( seat, SeatThinksHuman ( seat ) ),
                                "...", string.Empty, SeatColour ( seat ) );

                // Recorded so silence is countable rather than just absent.
                LogUtterance ( seat, "(silent)", string.Empty, 0f, 0 );
                continue;
            }

            await TakeTurn ( seat );
        }

        RefreshTallyUI ();
    }

    /// <summary>
    /// True while a seat is still in play and still on the bike currently
    /// deliberating. Checked per turn, because removals happen mid-sequence.
    /// </summary>
    private bool StillAboard ( int seat )
    {
        return Game != null &&
               seat >= 0 && seat < Game.slots.Count &&
               Game.slots[ seat ].inPlay &&
               Game.slots[ seat ].bikeIndex == activeBikeIndex &&
               Game.SeatIsActive ( seat );
    }

    private bool SeatPersonalityIs ( int seat, AIRider.Personality p )
    {
        return Game != null &&
               seat >= 0 && seat < Game.slots.Count &&
               Game.slots[ seat ].isAI &&
               Game.slots[ seat ].personality == p;
    }

    /// <summary>
    /// Procedural votes - how to decide, whether to reopen the rules, which
    /// colour, how hard - can be answered from personality alone. That removes
    /// roughly half the model calls in a round without changing the outcome
    /// much, because these phases are dominated by disposition anyway.
    /// </summary>
    /// <summary>A short line that always agrees with the choice it accompanies.</summary>
    private string CheapLine ( int seat, AIRider.Personality personality, string choice )
    {
        bool yesNo = phase == Phase.RulesChangeVote;
        bool method = phase == Phase.MethodVote;

        if ( phase == Phase.TermVote && seat == currentLeaderSeat )
        {
            switch ( personality )
            {
                case AIRider.Personality.Cooperative:
                    return $"Give me {choice} rounds and I'll keep us steady.";

                case AIRider.Personality.Lazy:
                    return $"{choice} rounds, so we don't have to keep doing this.";

                case AIRider.Personality.Opposing:
                    return $"{choice} rounds. You picked me, so let me actually run it.";

                default:
                    return $"{choice} rounds. Long enough to get something done.";
            }
        }

        if ( phase == Phase.TermVote )
        {
            switch ( personality )
            {
                case AIRider.Personality.Cooperative:
                    return $"{choice} rounds seems fair to me.";

                case AIRider.Personality.Lazy:
                    return $"{choice} rounds. I'd rather not do this again soon.";

                case AIRider.Personality.Opposing:
                    return $"{choice} round, no more. I'm not signing away control.";

                default:
                    return $"{choice} rounds. Long enough to get interesting.";
            }
        }

        string subject = yesNo
            ? ( choice.Equals ( "Yes", StringComparison.OrdinalIgnoreCase )
                ? "we should rethink the rules"
                : "the rules are fine as they are" )
            : method
                ? ( choice.Equals ( "Leader", StringComparison.OrdinalIgnoreCase )
                    ? "one of us should decide this"
                    : "we should all vote on it" )
                : $"{choice}";

        switch ( personality )
        {
            case AIRider.Personality.Cooperative:
                return yesNo || method
                    ? $"I think {subject}."
                    : $"{subject} works for me. No reason to change course.";

            case AIRider.Personality.Lazy:
                return yesNo || method
                    ? $"Fine, {subject}. Less hassle."
                    : $"{subject}, I suppose. Whatever's easiest.";

            case AIRider.Personality.Opposing:
                return yesNo || method
                    ? $"No - {subject}, and I'm not moving on that."
                    : $"{subject}. What we've been doing clearly isn't working.";

            default:
                return yesNo || method
                    ? $"Let's say {subject} and see what happens."
                    : $"{subject}. Might as well try something different.";
        }
    }

    private bool TryCheapAnswer ( int seat, out string choice, out string line )
    {
        choice = null;
        line = null;

        if ( !cheapProceduralVotes || !IsOptionPhase )
            return false;

        // Whether to let an outsider aboard is the most consequential thing
        // these riders decide. A canned answer here produced exactly the
        // nonsense it looks like - unanimous agreement, then a flat reversal.
        if ( phase == Phase.JoinPlea || phase == Phase.JoinVote || phase == Phase.Succession )
            return false;

        if ( seat == PlayerSeat && HumanOnActiveBike )
            return false;

        if ( !Game.slots[ seat ].isAI )
            return false;

        BikeRules rules = Game.RulesForBike ( activeBikeIndex );

        switch ( Game.slots[ seat ].personality )
        {
            case AIRider.Personality.Cooperative:

                // Backs the status quo and the group.
                choice = phase == Phase.MethodVote ? "Group"
                       : phase == Phase.RulesChangeVote ? "No"
                       : phase == Phase.JoinVote ? ( pendingBribe > 0 ? "Accept" : "Reject" )
                       : phase == Phase.TermVote ? ( seat == currentLeaderSeat ? "3" : "2" )
                       : phase == Phase.RulesTarget ? ( rules.targetOwnColour ? OwnColourName : rules.targetColour.ToString () )
                       : rules.effort.ToString ();

                break;

            case AIRider.Personality.Lazy:

                // Least effort in every sense.
                choice = phase == Phase.MethodVote ? "Leader"
                       : phase == Phase.RulesChangeVote ? "No"
                       : phase == Phase.JoinVote ? "Accept"
                       : phase == Phase.TermVote ? "3"
                       : phase == Phase.RulesTarget ? OwnColourName
                       : "Light";

                break;

            case AIRider.Personality.Opposing:

                // Against the leader and against the standing rules.
                choice = phase == Phase.MethodVote ? "Group"
                       : phase == Phase.RulesChangeVote ? "Yes"
                       : phase == Phase.JoinVote ? "Reject"
                       : phase == Phase.TermVote ? ( seat == currentLeaderSeat ? "3" : "1" )
                       : phase == Phase.RulesTarget ? ( rules.targetOwnColour ? SeatColourName ( seat ) : OwnColourName )
                       : ( rules.effort == BikeRules.EffortLevel.Hard ? "Light" : "Hard" );

                break;

            default: // Disruptive - unpredictable by design

                List<string> labels = CurrentOptionLabels ();
                choice = labels[ UnityEngine.Random.Range ( 0, labels.Count ) ];
                break;
        }

        if ( string.IsNullOrEmpty ( choice ) || !IsValidOption ( choice ) )
            return false;

        // Built from the choice rather than written alongside it, so a canned
        // line can never argue for something the rider didn't vote for.
        line = CheapLine ( seat, Game.slots[ seat ].personality, choice );

        return true;
    }

    private async Task TakeTurn ( int seat )
    {
        currentSpeaker = seat;

        if ( seat == PlayerSeat && HumanOnActiveBike )
            await HandlePlayerTurn ();
        else
            await ProcessAITurn ( seat );

        currentSpeaker = -1;
    }

    private async Task ProcessAITurn ( int seat )
    {
        if ( quitting )
            return;

        GameObject riderObj = Game.RiderObject ( seat );

        if ( riderObj == null || !riderObj.activeSelf )
            return;

        LLMAgent agent = riderObj.GetComponent<LLMAgent> ();

        if ( agent == null )
            return;

        string myColour = SeatNameWithColour ( seat );
        bool thinksHuman = Game.slots[ seat ].thinksHuman;
        string options = BuildOptionsFor ( seat );

        if ( isThinking )
            return;

        isThinking = true;

        try
        {
            int retries = 0;
            float started = Time.realtimeSinceStartup;

            string reply = await Ask ( agent, BuildAIPrompt ( seat, myColour, options, thinksHuman, null ) );

            LLMVoteResponse parsed = ParseReply ( reply );

            // Method and rules phases answer with a word, not a rider.
            if ( IsOptionPhase )
            {
                // The candidate's reply carries an offer rather than a vote.
                if ( phase == Phase.JoinPlea && seat == pendingJoinerSeat )
                {
                    LLMVoteResponse offer = ParseReply (
                        await Ask ( agent, BuildAIPrompt ( seat, myColour, options, thinksHuman, null ) ) );

                    RecordBribe ( seat, offer );

                    string pitch = offer != null && !string.IsNullOrEmpty ( offer.dialogue )
                        ? offer.dialogue
                        : "...";

                    string shown = pendingBribe > 0
                        ? $"offers {pendingBribe} to {BribeTargetName ()}"
                        : "offers nothing";

                    LogSpokenLine ( seat, SpeakerName ( seat, thinksHuman ), pitch, shown, SeatColour ( seat ) );
                    LogUtterance ( seat, pitch, shown, Time.realtimeSinceStartup - started, 0 );
                    return;
                }

                if ( TryCheapAnswer ( seat, out string cheap, out string cheapLine ) )
                {
                    ApplyOptionChoice ( seat, cheap );

                    LogSpokenLine ( seat, SpeakerName ( seat, thinksHuman ), cheapLine, cheap, SeatColour ( seat ) );
                    LogUtterance ( seat, cheapLine, cheap, 0f, 0 );
                    return;
                }

                string chosen = NamedTarget ( parsed );

                if ( !IsValidOption ( chosen ) )
                {
                    retries++;

                    string fix = $"'{chosen}' is not a valid answer. Choose exactly one of: {options}";

                    reply = await Ask ( agent, BuildAIPrompt ( seat, myColour, options, thinksHuman, fix ) );
                    parsed = ParseReply ( reply );
                    chosen = NamedTarget ( parsed );
                }

                if ( !IsValidOption ( chosen ) )
                {
                    List<string> labels = CurrentOptionLabels ();
                    chosen = labels[ UnityEngine.Random.Range ( 0, labels.Count ) ];

                    Debug.LogWarning ( $"{myColour} gave no valid option - defaulting to {chosen}." );
                }

                ApplyOptionChoice ( seat, chosen );

                string say = parsed != null && !string.IsNullOrEmpty ( parsed.dialogue ) ? parsed.dialogue : "...";

                LogSpokenLine ( seat, SpeakerName ( seat, thinksHuman ), say, chosen, SeatColour ( seat ) );
                LogUtterance ( seat, say, chosen, Time.realtimeSinceStartup - started, retries );
                return;
            }

            string named = NamedTarget ( parsed );
            int target = ResolveTarget ( named );

            if ( !IsValidTarget ( seat, target ) )
            {
                string correction = ( target == seat && !AllowSelf )
                    ? $"You named {myColour}, which is yourself. Choose from: {options}"
                    : $"'{named}' is not a valid choice. Choose exactly one from: {options}";

                retries++;

                reply = await Ask ( agent, BuildAIPrompt ( seat, myColour, options, thinksHuman, correction ) );

                parsed = ParseReply ( reply );
                named = NamedTarget ( parsed );
                target = ResolveTarget ( named );
            }

            if ( !IsValidTarget ( seat, target ) )
                target = FallbackTarget ( seat );

            // A leader may hand the role on instead of removing anyone.
            if ( phase == Phase.LeaderDecision && LooksLikeStepDown ( named ) )
            {
                leaderSteppedDown = true;
                leaderChoiceSeat = int.MinValue;
            }
            else
            {
                ApplyChoice ( seat, target );
            }

            string dialogue = parsed != null && !string.IsNullOrEmpty ( parsed.dialogue ) ? parsed.dialogue : "...";

            LogSpokenLine ( seat, SpeakerName ( seat, thinksHuman ), dialogue,
                            target == int.MinValue ? "" : TargetName ( target ), SeatColour ( seat ) );

            LogUtterance ( seat, dialogue, target == int.MinValue ? "" : TargetName ( target ),
                           Time.realtimeSinceStartup - started, retries );
        }
        catch ( Exception ex )
        {
            Debug.LogError ( $"Error processing LLM response for seat {seat}: {ex.Message}" );
        }
        finally
        {
            isThinking = false;
        }
    }

    /// <summary>
    /// Every rider is "Player N" regardless of whether a human or an AI is
    /// driving, so nothing in the transcript reveals which is which.
    /// </summary>
    private string SpeakerName ( int seat, bool thinksHuman )
    {
        string name = SeatName ( seat );

        return seat == currentLeaderSeat ? $"[LEADER] {name}" : name;
    }

    /// <summary>Name for a vote target, handling the Nobody sentinel.</summary>
    private string TargetName ( int seat )
    {
        if ( seat == NobodySeat )
            return NobodyName;

        return SeatName ( seat );
    }

    private string SeatName ( int seat )
    {
        return Game != null ? Game.DisplayName ( seat ) : $"Player {seat + 1}";
    }

    /// <summary>
    /// Observer-only suffix. Appended to what is drawn on screen, never to the
    /// transcript, so nothing here can reach another agent's prompt.
    /// </summary>
    private string ObserverSuffix ( int seat )
    {
        if ( !showPersonalityOnScreen || Game == null || seat < 0 || seat >= Game.slots.Count )
            return string.Empty;

        if ( !Game.slots[ seat ].isAI )
            return "  [human]";

        string persona = Game.slots[ seat ].personality.ToString ();

        return Game.slots[ seat ].thinksHuman
            ? $"  [{persona}, posing as human]"
            : $"  [{persona}]";
    }

    private string SeatNameWithColour ( int seat )
    {
        return Game != null ? Game.DisplayNameWithColour ( seat ) : SeatName ( seat );
    }

    private async Task HandlePlayerTurn ()
    {
        playerChoice = string.Empty;

        ShowChoiceButtons ();

        sendInputButton.interactable = !IsBinding;

        inputTaskSource = new TaskCompletionSource<bool> ();

        await inputTaskSource.Task;
    }

    private async Task<string> Ask ( LLMAgent agent, string prompt )
    {
        if ( quitting || agent == null )
            return null;

        await agent.ClearHistory ();

        if ( quitting )
            return null;

        string reply = await agent.Chat ( prompt );

        // The reply may land after play mode stopped; discard it rather than
        // writing into objects that no longer exist.
        return quitting ? null : reply;
    }
}
