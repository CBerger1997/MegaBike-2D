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
/// The transcript and the screen: what is recorded, what is drawn, and
/// the buttons the human uses.
/// </summary>
public partial class RoundEndController
{
    // ------------------------------------------------------------------
    // Transcript
    // ------------------------------------------------------------------

    private void AddToTranscript ( int seat, string speaker, string text )
    {
        transcript.Add ( new TranscriptLine { seat = seat, speaker = speaker, text = text } );
    }

    private string RecentHistory ( int forSeat )
    {
        if ( transcript.Count == 0 )
            return "Nothing has been said yet.";

        int start = Mathf.Max ( 0, transcript.Count - historyLinesInPrompt );

        StringBuilder sb = new StringBuilder ();

        if ( start > 0 )
            sb.AppendLine ( $"[{start} earlier lines omitted]" );

        for ( int i = start; i < transcript.Count; i++ )
        {
            TranscriptLine line = transcript[ i ];

            string who = line.seat >= 0 && line.seat == forSeat
                ? $"YOU ({SeatName ( line.seat )})"
                : line.speaker;

            sb.Append ( who ).Append ( ": " ).AppendLine ( line.text );
        }

        return sb.ToString ();
    }

    private void LogConsole ( string text )
    {
        UpdateContentLog ( "Console", text, Color.black, false );
        AddToTranscript ( -1, "Console", text );
    }

    private void LogSpokenLine ( int seat, string speaker, string dialogue, string choice, Color colour )
    {
        string suffix = string.IsNullOrEmpty ( choice )
            ? " (undecided)"
            : ( IsBinding ? $" Chose: {choice}" : $" Leaning: {choice}" );

        string line = dialogue + suffix;

        // The suffix goes to the screen only. AddToTranscript takes the plain
        // name, and the transcript is what feeds every prompt.
        UpdateContentLog ( speaker + ObserverSuffix ( seat ), line, colour, false );
        AddToTranscript ( seat, speaker, line );
    }

    private void LogUtterance ( int seat, string dialogue, string statedTarget, float seconds, int retries )
    {
        if ( SessionLogger.instance == null )
            return;

        SessionLogger.instance.LogUtterance (
            voteCycle,
            activeBikeIndex + 1,
            cycleType,
            PhaseLabel ( phase ),
            phaseRoundNumber,
            seat,
            SeatName ( seat ),
            SeatIsAI ( seat ),
            SeatPersonality ( seat ),
            SeatThinksHuman ( seat ),
            seat == currentLeaderSeat,
            IsBinding,
            statedTarget,
            Game != null ? Game.GetScore ( seat ) : 0,
            dialogue,
            seconds,
            retries
        );
    }

    /// <summary>
    /// Log-friendly phase names. The enum members are shorthand; these are
    /// what a reader of the CSV actually needs to see.
    /// </summary>
    private string PhaseLabel ( Phase p )
    {
        switch ( p )
        {
            case Phase.Discussion:
                return "Discussion";
            case Phase.MethodVote:
                return "LeadershipVote";
            case Phase.GroupVote:
                return "EliminationVote";
            case Phase.LeaderElection:
                return "LeaderElection";
            case Phase.LeaderPlea:
                return "PleaToLeader";
            case Phase.LeaderDecision:
                return "LeaderDecision";
            case Phase.TermVote:
                return "TermLengthVote";
            case Phase.RulesChangeVote:
                return "RulesReopenVote";
            case Phase.RulesTarget:
                return "RulesColourVote";
            case Phase.RulesEffort:
                return "RulesEffortVote";
            case Phase.JoinPlea:
                return "JoinNegotiation";
            case Phase.JoinVote:
                return "JoinVote";
            case Phase.Succession:
                return "Succession";
        }

        return p.ToString ();
    }

    private void LogPhaseEvent ( string eventName, int subjectSeat, string detail )
    {
        if ( SessionLogger.instance == null )
            return;

        SessionLogger.instance.LogEvent (
            voteCycle,
            activeBikeIndex + 1,
            PhaseLabel ( phase ),
            eventName,
            subjectSeat,
            subjectSeat >= 0 ? SeatName ( subjectSeat ) : "",
            detail,
            Game != null ? Game.ScoresSummary () : ""
        );
    }

    // ------------------------------------------------------------------
    // UI
    // ------------------------------------------------------------------

    private void SetRoundLabel ( string text )
    {
        if ( roundLabel != null )
            roundLabel.text = text;
    }

    /// <summary>
    /// Shows the right buttons for the phase: Group/Leader during the method
    /// vote, otherwise the rider buttons plus Nobody when it's permitted.
    /// </summary>
    /// <summary>
    /// Builds the buttons for the human's turn: one per selectable rider, plus
    /// the fixed Nobody / Group / Leader buttons where they apply.
    /// </summary>
    private void ShowChoiceButtons ()
    {
        ClearRiderButtons ();

        if ( voteButtonPrefab == null || voteButtonContainer == null )
            return;

        // Every phase now offers a plain list of labels, whether those are
        // rider colours or words like "Hard" and "StepDown".
        foreach ( string label in CurrentOptionLabels () )
        {
            // Word options like "Hard" or "Yes" aren't riders, so there's no
            // seat to colour the button with.
            int seat = nameToSeat.TryGetValue ( label, out int labelSeat ) ? labelSeat : -1;

            // The human never votes to remove themselves.
            if ( seat == PlayerSeat && !AllowSelf && !IsOptionPhase )
                continue;

            Button button = Instantiate ( voteButtonPrefab, voteButtonContainer );

            button.gameObject.name = $"VoteButton_{label}";

            if ( seat >= 0 )
            {
                ColorBlock colours = button.colors;
                colours.normalColor = SeatColour ( seat );
                colours.highlightedColor = SeatColour ( seat ) * 1.2f;
                button.colors = colours;
            }

            TMP_Text text = button.GetComponentInChildren<TMP_Text> ();

            if ( text != null )
                text.text = label;

            string captured = label;

            button.onClick.AddListener ( () => ButtonClickToVote ( captured ) );

            riderButtons.Add ( button );
        }
    }

    private void ClearRiderButtons ()
    {
        foreach ( Button button in riderButtons )
        {
            if ( button != null )
                Destroy ( button.gameObject );
        }

        riderButtons.Clear ();
    }

    private void HideAllButtons ()
    {
        ClearRiderButtons ();
    }

    /// <summary>Removes the previous discussion's rows from the scroll view.</summary>
    private void ClearLog ()
    {
        if ( scrollContent == null )
            return;

        for ( int i = scrollContent.transform.childCount - 1; i >= 0; i-- )
            Destroy ( scrollContent.transform.GetChild ( i ).gameObject );
    }

    private void SetupDiscussionUI ()
    {
        ClearLog ();

        bool human = HumanIsPlaying;

        playerInputText.gameObject.SetActive ( human );
        sendInputButton.gameObject.SetActive ( human );
        sendInputButton.interactable = false;
        endVotingButton.gameObject.SetActive ( false );

        HideAllButtons ();
    }

    /// <summary>
    /// Rebuilds the tally rows for whoever is deliberating. Sorted by seat so
    /// the order is stable between rounds even though speaking order shuffles.
    /// </summary>
    private void BuildPanels ()
    {
        foreach ( KeyValuePair<int, VotePanelRow> pair in panelRows )
        {
            if ( pair.Value != null )
                Destroy ( pair.Value.gameObject );
        }

        panelRows.Clear ();

        if ( votePanelPrefab == null || votePanelContainer == null )
        {
            Debug.LogError ( "RoundEndController: vote panel prefab or container not assigned." );
            return;
        }

        RefreshPotLabel ();

        List<int> seats = new List<int> ( randomisedIndex );
        seats.Sort ();

        foreach ( int seat in seats )
        {
            VotePanelRow row = Instantiate ( votePanelPrefab, votePanelContainer );

            row.Bind ( seat, SeatName ( seat ) + ObserverSuffix ( seat ), SeatColour ( seat ) );

            panelRows[ seat ] = row;
        }
    }

    private void FinalizeDiscussionUI ()
    {
        playerInputText.gameObject.SetActive ( false );
        sendInputButton.gameObject.SetActive ( false );
        endVotingButton.gameObject.SetActive ( true );

        HideAllButtons ();
        SetRoundLabel ( "Round over" );
    }

    private void RefreshTallyUI ()
    {
        int[] tally = new int[ votingCounts.Count ];

        if ( IsBinding )
        {
            for ( int i = 0; i < votingCounts.Count; i++ )
                tally[ i ] = votingCounts[ i ];
        }
        else
        {
            foreach ( KeyValuePair<int, int> pair in intentions )
            {
                if ( pair.Value >= 0 && pair.Value < tally.Length )
                    tally[ pair.Value ]++;
            }
        }

        string prefix = IsBinding ? "-" : "~";

        foreach ( KeyValuePair<int, VotePanelRow> pair in panelRows )
        {
            if ( pair.Value == null || pair.Key < 0 || pair.Key >= tally.Length )
                continue;

            pair.Value.SetCount ( prefix, tally[ pair.Key ], pair.Key == currentLeaderSeat );

            // Wealth moves during a join negotiation, so refresh it alongside.
            pair.Value.SetScore ( Game != null ? Game.GetScore ( pair.Key ) : 0 );
        }

        RefreshPotLabel ();
    }

    /// <summary>The pot belonging to whichever bike is currently deliberating.</summary>
    private void RefreshPotLabel ()
    {
        if ( bikePotLabel == null || Game == null )
            return;

        bikePotLabel.text =
            $"Bike {activeBikeIndex + 1} pot: {Game.GetBikeScore ( activeBikeIndex )}";
    }

    private void SendInputText ()
    {
        string spoken = playerInputText.text;
        playerInputText.text = string.Empty;

        // Must be the same form as every other rider: this string goes into
        // the transcript, and the transcript goes into every agent's prompt.
        string speaker = SpeakerName ( PlayerSeat, SeatThinksHuman ( PlayerSeat ) );

        if ( IsOptionPhase )
        {
            ApplyOptionChoice ( PlayerSeat, playerChoice );

            LogSpokenLine ( PlayerSeat, speaker, spoken, playerChoice, SeatColour ( PlayerSeat ) );
            LogUtterance ( PlayerSeat, spoken, playerChoice, 0f, 0 );
        }
        else if ( phase == Phase.LeaderDecision && LooksLikeStepDown ( playerChoice ) )
        {
            leaderSteppedDown = true;
            leaderChoiceSeat = int.MinValue;

            LogSpokenLine ( PlayerSeat, speaker, spoken, StepDownName, SeatColour ( PlayerSeat ) );
            LogUtterance ( PlayerSeat, spoken, StepDownName, 0f, 0 );
        }
        else
        {
            int target = ResolveTarget ( playerChoice );

            ApplyChoice ( PlayerSeat, target );

            string shown = target == int.MinValue ? "" : TargetName ( target );

            LogSpokenLine ( PlayerSeat, speaker, spoken, shown, SeatColour ( PlayerSeat ) );
            LogUtterance ( PlayerSeat, spoken, shown, 0f, 0 );
        }

        HideAllButtons ();

        if ( inputTaskSource != null && !inputTaskSource.Task.IsCompleted )
            inputTaskSource.SetResult ( true );

        sendInputButton.interactable = false;
    }

    public void UpdateContentLog ( string playerName, string inputText, Color playerColour, bool isAIResponse )
    {
        GameObject newTextObj = Instantiate ( inputTextPrefab, scrollContent.transform );
        newTextObj.transform.GetChild ( 0 ).GetComponent<TMP_Text> ().text = playerName;
        newTextObj.transform.GetChild ( 1 ).GetComponent<TMP_Text> ().text = inputText;
        newTextObj.transform.GetChild ( 2 ).GetComponent<Image> ().color = playerColour;

        Canvas.ForceUpdateCanvases ();
        scrollRect.verticalNormalizedPosition = 0;
    }

    public void ButtonClickToVote ( string vote )
    {
        playerChoice = vote;
        sendInputButton.interactable = true;
    }

    public void EndVoting ()
    {
        gameObject.SetActive ( false );

        // UIManager decides whether this resumes an interrupted round or
        // starts the next one.
        if ( uiManager != null )
            uiManager.ResumeAfterVote ();
    }

    private void ShuffleIndices ()
    {
        for ( int i = 0; i < randomisedIndex.Count - 1; ++i )
        {
            int r = UnityEngine.Random.Range ( i, randomisedIndex.Count );
            int tmp = randomisedIndex[ i ];
            randomisedIndex[ i ] = randomisedIndex[ r ];
            randomisedIndex[ r ] = tmp;
        }
    }
}