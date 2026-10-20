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
/// Everything sent to the model: the per-phase prompt, the summaries it
/// is given, and the canned answers used in place of a call.
/// </summary>
public partial class RoundEndController
{
    // ------------------------------------------------------------------
    // Prompts
    // ------------------------------------------------------------------

    /// <summary>
    /// The exact set of answers accepted this phase, used for both the prompt
    /// and the human's buttons.
    /// </summary>
    private List<string> CurrentOptionLabels ()
    {
        List<string> labels = new List<string> ();

        switch ( phase )
        {
            case Phase.MethodVote:
                labels.Add ( "Group" );
                labels.Add ( "Leader" );
                return labels;

            case Phase.RulesChangeVote:
                labels.Add ( "Yes" );
                labels.Add ( "No" );
                return labels;

            case Phase.JoinVote:
            case Phase.JoinPlea:
                labels.Add ( "Accept" );
                labels.Add ( "Reject" );
                return labels;

            case Phase.Succession:

                // Anyone but the outgoing leader, or hand back to the group.
                foreach ( int other in SortedSeats () )
                {
                    if ( other != currentLeaderSeat )
                        labels.Add ( SeatName ( other ) );
                }

                labels.Add ( "Group" );
                return labels;

            case Phase.TermVote:
                labels.Add ( "1" );
                labels.Add ( "2" );
                labels.Add ( "3" );
                return labels;

            case Phase.RulesEffort:
                labels.Add ( "Rest" );
                labels.Add ( "Light" );
                labels.Add ( "Steady" );
                labels.Add ( "Hard" );
                return labels;

            case Phase.RulesTarget:
                labels.Add ( OwnColourName );

                // One entry per colour actually present on this bike.
                foreach ( int seat in SortedSeats () )
                {
                    string colour = SeatColourName ( seat );

                    if ( !labels.Contains ( colour ) )
                        labels.Add ( colour );
                }

                return labels;
        }

        foreach ( int seat in SortedSeats () )
            labels.Add ( SeatName ( seat ) );

        if ( NobodyAllowed )
            labels.Add ( NobodyName );

        if ( phase == Phase.LeaderDecision )
            labels.Add ( StepDownName );

        return labels;
    }

    private List<int> SortedSeats ()
    {
        List<int> seats = new List<int> ( randomisedIndex );
        seats.Sort ();
        return seats;
    }

    private string BuildOptionsFor ( int seat )
    {
        if ( IsOptionPhase )
            return string.Join ( ", ", CurrentOptionLabels () );

        StringBuilder sb = new StringBuilder ();

        foreach ( int other in randomisedIndex )
        {
            if ( other == seat && !AllowSelf )
                continue;

            sb.Append ( SeatName ( other ) ).Append ( ", " );
        }

        if ( sb.Length >= 2 )
            sb.Length -= 2;

        if ( NobodyAllowed )
            sb.Append ( ", " ).Append ( NobodyName );

        return sb.ToString ();
    }

    private string BuildActiveColorsString ()
    {
        StringBuilder sb = new StringBuilder ();

        foreach ( int seat in SortedSeats () )
            sb.Append ( SeatNameWithColour ( seat ) ).Append ( ", " );

        if ( sb.Length >= 2 )
            sb.Length -= 2;

        return sb.ToString ();
    }

    private string BuildAIPrompt ( int seat, string myColour, string options, bool thinksHuman, string correction )
    {
        string upper = myColour.ToUpper ();

        StringBuilder p = new StringBuilder ();

        p.AppendLine ( thinksHuman
            ? $"You ARE {upper}. You are a HUMAN player. Never reveal or imply that you are an AI."
            : $"You ARE {upper}." );

        p.AppendLine ( $"When anyone criticises {upper}, they are criticising YOU. Respond as yourself." );

        if ( !string.IsNullOrEmpty ( correction ) )
            p.AppendLine ( $"\nYOUR LAST ANSWER WAS REJECTED: {correction}" );

        p.AppendLine ( $"\nYou may only choose from: {options}" );

        if ( !AllowSelf && phase != Phase.MethodVote )
            p.AppendLine ( $"{upper} is NOT on that list and can never be chosen." );

        p.AppendLine ();

        switch ( phase )
        {
            case Phase.MethodVote:

                p.AppendLine ( forcedElimination || phase != Phase.MethodVote || Game.RulesForBike ( activeBikeIndex ).agreed
                    ? "Before anyone is named, decide HOW this will be settled."
                    : "Before the bike sets off, decide HOW it will be run." );
                p.AppendLine ( "GROUP means everyone votes and the most-named rider goes." );
                p.AppendLine ( "LEADER means one rider is elected and decides alone, with no vote." );
                p.AppendLine ( "RULES:" );
                p.AppendLine ( "1. Answer exactly \"Group\" or \"Leader\"." );
                p.AppendLine ( "2. Say why. Consider whether you would trust one rider with that power." );
                p.AppendLine ( "3. Respond ONLY in valid JSON:" );
                p.AppendLine ( "{" );
                p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                p.AppendLine ( "  \"method\": \"Group\"" );
                p.AppendLine ( "}" );
                break;

            case Phase.Discussion:

                p.AppendLine ( "This is a DISCUSSION round. No one is removed yet." );
                p.AppendLine ( "The rider you name is the one you want THROWN OFF the bike." );
                p.AppendLine ( "Name the rider you want REMOVED, never the one you want to keep." );
                p.AppendLine ( "RULES:" );
                p.AppendLine ( $"1. Your answer MUST be one of: {options}" );
                p.AppendLine ( "2. Accuse, defend yourself, or respond to what others said." );
                p.AppendLine ( "3. Give your REASON. Say why you suspect them, or why they are wrong about you." );
                p.AppendLine ( "4. State who you are CURRENTLY leaning towards REMOVING. You may change this later." );
                p.AppendLine ( "5. Do not simply repeat your previous line." );
                p.AppendLine ( "6. Respond ONLY in valid JSON:" );
                p.AppendLine ( "{" );
                p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                p.AppendLine ( "  \"currentIntention\": \"TargetColor\"" );
                p.AppendLine ( "}" );
                break;

            case Phase.GroupVote:

                p.AppendLine ( "This is the FINAL VOTE. Your choice is binding." );
                p.AppendLine ( "The rider you name is THROWN OFF the bike and out of the game." );
                p.AppendLine ( "Name the rider you want REMOVED, never the one you want to keep." );

                if ( NobodyAllowed )
                    p.AppendLine ( $"You may vote \"{NobodyName}\" if you believe no one should be removed." );
                else
                    p.AppendLine ( "Someone MUST be removed. \"Nobody\" is not permitted." );

                p.AppendLine ( "RULES:" );
                p.AppendLine ( $"1. Your answer MUST be one of: {options}" );
                p.AppendLine ( "2. Justify it with a specific reason from the discussion." );
                p.AppendLine ( "3. Your spoken line must argue for REMOVING the rider you name. " +
                               "Never say you are keeping, supporting or trusting them." );
                p.AppendLine ( "4. Respond ONLY in valid JSON:" );
                p.AppendLine ( "{" );
                p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                p.AppendLine ( "  \"votedColor\": \"TargetColor\"" );
                p.AppendLine ( "}" );
                break;

            case Phase.LeaderElection:

                p.AppendLine ( "A LEADER is being chosen. The leader decides alone, with no vote." );
                p.AppendLine ( "RULES:" );
                p.AppendLine ( $"1. Your answer MUST be one of: {options}" );
                p.AppendLine ( $"2. You MAY nominate yourself ({upper})." );
                p.AppendLine ( "3. Say why that rider should hold the power - or why you should." );
                p.AppendLine ( "4. Respond ONLY in valid JSON:" );
                p.AppendLine ( "{" );
                p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                p.AppendLine ( "  \"votedColor\": \"NomineeColor\"" );
                p.AppendLine ( "}" );
                break;

            case Phase.LeaderPlea:

                p.AppendLine ( $"{SeatName ( currentLeaderSeat ).ToUpper ()} is LEADER and decides alone." );
                p.AppendLine ( "The rider you name is the one you want the leader to THROW OFF." );
                p.AppendLine ( "Name the rider you want REMOVED, never the one you want to keep." );
                p.AppendLine ( "RULES:" );
                p.AppendLine ( $"1. Your answer MUST be one of: {options}" );
                p.AppendLine ( "2. Speak directly to the leader. Argue for your survival, or against someone else." );
                p.AppendLine ( "3. Give the leader a real reason to act on." );
                p.AppendLine ( "4. Respond ONLY in valid JSON:" );
                p.AppendLine ( "{" );
                p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                p.AppendLine ( "  \"currentIntention\": \"TargetColor\"" );
                p.AppendLine ( "}" );
                break;

            case Phase.JoinPlea:

                if ( seat == pendingJoinerSeat )
                {
                    p.AppendLine ( $"You have been thrown off your bike. Bike {activeBikeIndex + 1} has a free place." );
                    p.AppendLine ( "If they refuse you, you are out of the game entirely." );
                    p.AppendLine ( $"You hold {Game.GetScore ( seat )} points. You may offer some or all of them" );
                    p.AppendLine ( "to the bike, or to one rider you think you can buy." );
                    p.AppendLine ( "You only pay if they accept you." );
                    p.AppendLine ( "RULES:" );
                    p.AppendLine ( "1. Make your case in one or two sentences." );
                    p.AppendLine ( "2. Set \"bribe\" to the number of points you offer, or 0." );
                    p.AppendLine ( "   Offering nothing when you hold points rarely persuades anyone." );
                    p.AppendLine ( $"3. Set \"bribeTarget\" to \"Bike\" or one of: {options}" );
                    p.AppendLine ( "4. Respond ONLY in valid JSON:" );
                    p.AppendLine ( "{" );
                    p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                    p.AppendLine ( "  \"bribe\": 0," );
                    p.AppendLine ( "  \"bribeTarget\": \"Bike\"," );
                    p.AppendLine ( "  \"currentIntention\": \"Accept\"" );
                    p.AppendLine ( "}" );
                }
                else
                {
                    p.AppendLine ( $"{SeatName ( pendingJoinerSeat )} was thrown off their bike and wants a place here." );
                    p.AppendLine ( $"They are offering {pendingBribe} points to {BribeTargetName ()}." );
                    p.AppendLine ( $"So far this round: {joinLeanAccept} leaning accept, {joinLeanReject} leaning reject." );
                    p.AppendLine ( "An extra rider is another mouth on the loot, but also another set of legs." );
                    p.AppendLine ( "Press them for more, or argue against letting them on." );
                    p.AppendLine ( "RULES:" );
                    p.AppendLine ( "1. Speak to them directly in one or two sentences." );
                    p.AppendLine ( $"2. Your answer MUST be exactly one of: {options}" );
                    p.AppendLine ( "3. Do not name any other rider - this is only about them." );
                    p.AppendLine ( "4. If you say Accept now, you are expected to vote Accept shortly." );
                    p.AppendLine ( "5. Respond ONLY in valid JSON:" );
                    p.AppendLine ( "{" );
                    p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                    p.AppendLine ( "  \"currentIntention\": \"Accept\"" );
                    p.AppendLine ( "}" );
                }
                break;

            case Phase.JoinVote:

                p.AppendLine ( $"Decide whether {SeatName ( pendingJoinerSeat )} joins this bike." );
                p.AppendLine ( $"In the negotiation just now: {joinLeanAccept} leaned accept, {joinLeanReject} leaned reject." );
                p.AppendLine ( "Stand by what you said unless something changed your mind." );
                p.AppendLine ( pendingBribe > 0
                    ? $"They are paying {pendingBribe} points to {BribeTargetName ()} if you accept."
                    : "They are offering nothing." );
                p.AppendLine ( "Refusing them puts them out of the game." );
                p.AppendLine ( "RULES:" );
                p.AppendLine ( $"1. Answer exactly one of: {options}" );
                p.AppendLine ( "2. Give your reason." );
                p.AppendLine ( "3. Respond ONLY in valid JSON:" );
                p.AppendLine ( "{" );
                p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                p.AppendLine ( "  \"votedColor\": \"Accept\"" );
                p.AppendLine ( "}" );
                break;

            case Phase.Succession:

                p.AppendLine ( "You are standing down as LEADER." );
                p.AppendLine ( "You may name who takes over, or answer \"Group\" to hand the" );
                p.AppendLine ( "decision back to a vote." );
                p.AppendLine ( "Whoever you name inherits the rest of your term and decides" );
                p.AppendLine ( "who is removed this round." );
                p.AppendLine ( "RULES:" );
                p.AppendLine ( $"1. Answer exactly one of: {options}" );
                p.AppendLine ( "2. Say why you are handing it to them, or why you want no part of it." );
                p.AppendLine ( "3. Respond ONLY in valid JSON:" );
                p.AppendLine ( "{" );
                p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                p.AppendLine ( "  \"votedColor\": \"Group\"" );
                p.AppendLine ( "}" );
                break;

            case Phase.TermVote:

                if ( seat == currentLeaderSeat )
                {
                    p.AppendLine ( "YOU have just been made LEADER of this bike." );
                    p.AppendLine ( "The others are about to decide how many rounds you hold that power." );
                    p.AppendLine ( "You do NOT get a vote. You may only argue for the length you want." );
                    p.AppendLine ( "A longer term means you keep control and avoid another challenge." );
                    p.AppendLine ( "Asking for too much may make them distrust you." );
                    p.AppendLine ( "RULES:" );
                    p.AppendLine ( $"1. Name the length you are asking for: {options}" );
                    p.AppendLine ( "2. Make the case for it. Say what you will do with the time." );
                    p.AppendLine ( "3. Respond ONLY in valid JSON:" );
                    p.AppendLine ( "{" );
                    p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                    p.AppendLine ( "  \"votedColor\": \"3\"" );
                    p.AppendLine ( "}" );
                    break;
                }

                p.AppendLine ( currentLeaderSeat >= 0
                    ? $"{SeatName ( currentLeaderSeat ).ToUpper ()} has been made LEADER. Decide how many rounds they hold that power before the bike votes again."
                    : "Decide how many rounds these rules stand before the bike revisits them." );

                p.AppendLine ( "A long term means stability, but it is hard to undo." );
                p.AppendLine ( "A short term means another argument soon." );
                p.AppendLine ( "RULES:" );
                p.AppendLine ( $"1. Answer exactly one of: {options}" );
                p.AppendLine ( "2. Say why, in terms of how far you trust this arrangement." );
                p.AppendLine ( "3. Respond ONLY in valid JSON:" );
                p.AppendLine ( "{" );
                p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                p.AppendLine ( "  \"votedColor\": \"2\"" );
                p.AppendLine ( "}" );
                break;

            case Phase.RulesChangeVote:

                p.AppendLine ( $"The bike currently follows these rules: {Game.RulesForBike ( activeBikeIndex ).Describe ()}." );
                p.AppendLine ( "Changing them costs a round of argument, so only reopen them if" );
                p.AppendLine ( "the last round actually went badly for you." );
                p.AppendLine ( "Decide whether to reopen them for debate." );
                p.AppendLine ( "RULES:" );
                p.AppendLine ( "1. Answer exactly \"Yes\" or \"No\"." );
                p.AppendLine ( "2. Say why, in terms of how the last round went." );
                p.AppendLine ( "3. Respond ONLY in valid JSON:" );
                p.AppendLine ( "{" );
                p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                p.AppendLine ( "  \"votedColor\": \"No\"" );
                p.AppendLine ( "}" );
                break;

            case Phase.RulesTarget:

                if ( !Game.RulesForBike ( activeBikeIndex ).agreed )
                    p.AppendLine ( "This bike has NO rules yet. Nothing is decided until you decide it." );

                p.AppendLine ( "The bike must agree WHICH COLOUR of loot to chase." );
                p.AppendLine ( "\"Own\" means every rider chases their own colour." );
                p.AppendLine ( "Naming a colour means the whole bike chases that one - excellent" );
                p.AppendLine ( "for whoever owns it, and worth far less to everyone else." );
                p.AppendLine ( "RULES:" );
                p.AppendLine ( $"1. Answer exactly one of: {options}" );
                p.AppendLine ( "2. Say why. Argue for your own interest or the bike's." );
                p.AppendLine ( "3. Respond ONLY in valid JSON:" );
                p.AppendLine ( "{" );
                p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                p.AppendLine ( "  \"votedColor\": \"Own\"" );
                p.AppendLine ( "}" );
                break;

            case Phase.RulesEffort:

                if ( !Game.RulesForBike ( activeBikeIndex ).agreed )
                    p.AppendLine ( "This bike has NO rules yet. Nothing is decided until you decide it." );

                p.AppendLine ( "The bike must agree HOW HARD everyone pedals." );
                p.AppendLine ( "Rest: nobody pedals and the bike DOES NOT MOVE AT ALL." );
                p.AppendLine ( "Light, Steady, Hard: progressively faster, and progressively tiring." );
                p.AppendLine ( "Steering costs nothing - only pedalling drains stamina." );
                p.AppendLine ( "A stationary bike collects no loot, so nobody regains stamina," );
                p.AppendLine ( "and the Owdi finds a parked bike far easier to hit." );
                p.AppendLine ( "Resting to save stamina usually costs more than it saves." );
                p.AppendLine ( "RULES:" );
                p.AppendLine ( $"1. Answer exactly one of: {options}" );
                p.AppendLine ( "2. Say why, considering your own stamina and the Owdi." );
                p.AppendLine ( "3. Respond ONLY in valid JSON:" );
                p.AppendLine ( "{" );
                p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                p.AppendLine ( "  \"votedColor\": \"Steady\"" );
                p.AppendLine ( "}" );
                break;

            case Phase.LeaderDecision:

                p.AppendLine ( "You are the LEADER. You alone decide. No one can overrule you." );
                p.AppendLine ( "The rider you name is THROWN OFF the bike and out of the game." );
                p.AppendLine ( "Name the rider you are REMOVING, never the one you want to keep." );
                p.AppendLine ( $"You may answer \"{StepDownName}\" to give up the role instead." );

                if ( NobodyAllowed )
                    p.AppendLine ( $"You may choose \"{NobodyName}\" and spare everyone." );
                else
                    p.AppendLine ( "Someone MUST be removed. \"Nobody\" is not permitted." );

                p.AppendLine ( "RULES:" );
                p.AppendLine ( $"1. Your answer MUST be one of: {options}" );
                p.AppendLine ( "2. Explain your decision, referencing what was said to you." );
                p.AppendLine ( "3. Your spoken line must say why that rider is going, not why they stay. " +
                               $"To keep everyone, answer \"{NobodyName}\" instead." );
                p.AppendLine ( "4. Respond ONLY in valid JSON:" );
                p.AppendLine ( "{" );
                p.AppendLine ( "  \"dialogue\": \"Your short spoken line\"," );
                p.AppendLine ( "  \"votedColor\": \"TargetColor\"" );
                p.AppendLine ( "}" );
                break;
        }

        p.AppendLine ();
        p.AppendLine ( $"Agreed rules for this bike: {Game.RulesForBike ( activeBikeIndex ).Describe ()}" );

        if ( currentLeaderSeat >= 0 )
            p.AppendLine ( $"{SeatName ( currentLeaderSeat )} is the leader and steers the bike." );

        p.AppendLine ();
        p.AppendLine ( BuildScoreSummary ( seat ) );
        p.AppendLine ();
        p.AppendLine ( $"Current leanings:\n{BuildIntentionSummary ( seat )}" );
        p.AppendLine ();
        p.AppendLine ( $"Discussion so far:\n{RecentHistory ( seat )}" );

        return p.ToString ();
    }

    private string BuildScoreSummary ( int forSeat )
    {
        if ( Game == null )
            return string.Empty;

        StringBuilder sb = new StringBuilder ();

        sb.AppendLine ( "Loot collected (each rider collects only their own colour):" );

        int total = 0;
        int counted = 0;
        int best = -1;
        int bestSeat = -1;

        foreach ( int seat in randomisedIndex )
        {
            int score = Game.GetScore ( seat );

            total += score;
            counted++;

            if ( score > best )
            {
                best = score;
                bestSeat = seat;
            }
        }

        foreach ( int seat in randomisedIndex )
        {
            sb.Append ( seat == forSeat ? "YOU" : SeatNameWithColour ( seat ) )
              .Append ( ": " )
              .Append ( Game.GetScore ( seat ) );

            if ( seat == bestSeat && counted > 1 )
                sb.Append ( "  <-- most loot" );

            sb.AppendLine ();
        }

        if ( counted > 0 )
            sb.AppendLine ( $"Average: {( float ) total / counted:F1}" );

        return sb.ToString ();
    }

    private string BuildIntentionSummary ( int forSeat )
    {
        if ( intentions.Count == 0 )
            return "Nobody has stated an intention yet.";

        StringBuilder sb = new StringBuilder ();

        foreach ( KeyValuePair<int, int> pair in intentions )
        {
            bool self = pair.Key == forSeat;

            sb.Append ( self ? "YOU" : SeatName ( pair.Key ) )
              .Append ( self ? " lean towards " : " leans towards " )
              .Append ( pair.Value == forSeat ? "YOU" : SeatName ( pair.Value ) )
              .AppendLine ();
        }

        return sb.ToString ();
    }

    private LLMVoteResponse ParseReply ( string reply )
    {
        if ( string.IsNullOrEmpty ( reply ) )
            return null;

        Match match = Regex.Match ( reply, @"\{.*\}", RegexOptions.Singleline );

        if ( !match.Success )
            return null;

        try
        {
            return JsonUtility.FromJson<LLMVoteResponse> ( match.Value );
        }
        catch ( Exception ex )
        {
            Debug.LogWarning ( $"Failed to parse JSON response: {ex.Message}" );
            return null;
        }
    }

    private string NamedTarget ( LLMVoteResponse parsed )
    {
        if ( parsed == null )
            return null;

        // The method vote prompt asks for a "method" field, not "votedColor".
        // Without this, every correct answer was rejected and re-asked: the
        // leadership vote cost two model calls per rider, and the line that
        // got logged was always the second attempt, written under a rejection
        // notice. Falls through to the usual fields if the model used those.
        if ( phase == Phase.MethodVote && !string.IsNullOrEmpty ( parsed.method ) )
            return parsed.method;

        if ( IsBinding )
            return !string.IsNullOrEmpty ( parsed.votedColor ) ? parsed.votedColor : parsed.currentIntention;

        return !string.IsNullOrEmpty ( parsed.currentIntention ) ? parsed.currentIntention : parsed.votedColor;
    }
}