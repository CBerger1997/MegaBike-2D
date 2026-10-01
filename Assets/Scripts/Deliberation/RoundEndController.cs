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
/// The round-end social phase, run per bike.
///
/// SEQUENCE
///   1. Discussion      - non-binding rounds, each speaker declaring a leaning
///   2. Method vote     - the riders choose GROUP or LEADER for this decision
///   3a. Group vote     - everyone votes; highest total is removed
///   3b. Leader path    - election, plea rounds, then the leader decides alone
///
/// "Nobody" is a valid choice in both branches UNLESS the round ended because
/// the Owdi caught the bike, in which case someone must go.
///
/// A removed rider transfers to another bike if one has space, and only leaves
/// the game when every other bike is full.
/// </summary>
public partial class RoundEndController : MonoBehaviour
{
    [Serializable]
    public class LLMVoteResponse
    {
        public string dialogue;
        public string votedColor;        // binding phases
        public string currentIntention;  // discussion / plea rounds
        public string method;            // method vote: "Group" or "Leader"
        // Deliberately a string. A model that writes "bribe": "20" instead of
        // 20 would otherwise fail the whole parse, and the reply was discarded
        // along with the dialogue.
        public string bribe;             // join plea: points offered
        public string bribeTarget;       // join plea: "Bike" or a player name
    }

    [Serializable]
    public class TranscriptLine
    {
        public int seat;         // -1 for console messages
        public string speaker;
        public string text;
    }

    private enum Phase
    {
        Discussion,
        MethodVote,
        GroupVote,
        LeaderElection,
        LeaderPlea,
        LeaderDecision,
        RulesChangeVote,
        RulesTarget,
        RulesEffort,
        TermVote,
        JoinPlea,
        JoinVote,
        Succession
    }

    /// <summary>Sentinel target meaning "remove nobody".</summary>
    private const int NobodySeat = -2;
    private const string NobodyName = "Nobody";
    private const int PlayerSeat = 0;
    private const string OwnColourName = "Own";
    private const string StepDownName = "StepDown";

    [Header ( "Discussion" )]
    [Range ( 0, 6 )] public int discussionRounds = 3;
    [Range ( 0, 4 )] public int leaderPleaRounds = 2;

    [Tooltip ( "Rounds of negotiation before a bike votes on letting someone aboard." )]
    [Range ( 1, 4 )] public int joinPleaRounds = 2;

    [Header ( "Rules" )]
    [Tooltip ( "Riders may choose to remove nobody, except after an Owdi hit." )]
    public bool allowNobodyOption = true;

    [Tooltip ( "On a timer end, run the vote for every bike rather than only the human's." )]
    public bool voteAllBikesOnTimer = true;

    [Tooltip ( "On a tied removal vote, remove one of the tied riders at random." )]
    public bool randomTieBreak = true;

    [Tooltip ( "When a rider gives an invalid answer twice: off = random target, on = joins the majority." )]
    public bool fallbackFollowsMajority = false;

    [Tooltip ( "How many recent lines go into each prompt. The full transcript is kept regardless." )]
    [Range ( 4, 40 )] public int historyLinesInPrompt = 12;

    [Header ( "Pacing" )]
    [Tooltip ( "Wait for a click between phases so there is time to read what happened." )]
    public bool pauseBetweenPhases = true;

    [Tooltip ( "Any left click continues, not just the button." )]
    public bool continueOnAnyClick = true;

    [Tooltip ( "Continue automatically after this many seconds. 0 waits indefinitely." )]
    public float autoContinueSeconds = 0f;

    [Tooltip ( "Start the next round without waiting for the End Voting button. For unattended runs." )]
    public bool autoEndVoting = false;

    [Tooltip ( "Seconds to leave the result on screen before the next round begins." )]
    [Range ( 0f, 15f )] public float autoEndDelay = 3f;

    [Tooltip ( "Button shown while waiting. Optional if continueOnAnyClick is on." )]
    [SerializeField] private Button continueButton;

    [Tooltip ( "Optional label on or beside the continue button." )]
    [SerializeField] private TMP_Text continueLabel;

    [Header ( "Observer" )]
    [Tooltip ( "Shows each rider's personality beside their name ON SCREEN ONLY. " +
               "Never reaches the agents. TURN THIS OFF for participant sessions - " +
               "the player can read it too." )]
    public bool showPersonalityOnScreen = false;

    [Header ( "LLM budget" )]
    [Tooltip ( "Riders who speak per discussion round. 0 = everyone. Lower saves a lot of time." )]
    [Range ( 0, 10 )] public int speakersPerDiscussionRound = 0;

    [Tooltip ( "Answer procedural votes (method, rules, change) from personality instead of calling the model." )]
    public bool cheapProceduralVotes = true;

    [Tooltip ( "Chance a Lazy rider bothers to speak at all in a discussion round." )]
    [Range ( 0f, 1f )] public float lazySpeakChance = 0.35f;

    [Header ( "Runtime (read-only)" )]
    public int currentLeaderSeat = -1;
    public int activeBikeIndex = 0;
    public bool forcedElimination;

    [Header ( "UI Controls" )]
    [SerializeField] private TMP_Text roundLabel;
    [SerializeField] private TMP_InputField playerInputText;
    [SerializeField] private Button sendInputButton;
    [SerializeField] private Button endVotingButton;
    [SerializeField] private GameObject inputTextPrefab;
    [SerializeField] private GameObject scrollContent;
    [SerializeField] private ScrollRect scrollRect;

    [Header ( "Voting UI (generated)" )]
    [Tooltip ( "Prefab with a VotePanelRow on it." )]
    [SerializeField] private VotePanelRow votePanelPrefab;

    [Tooltip ( "Parent for the tally rows. Vertical Layout Group + Content Size Fitter." )]
    [SerializeField] private Transform votePanelContainer;

    [Tooltip ( "Optional. Shows the deliberating bike's pot above the tally rows." )]
    [SerializeField] private TMP_Text bikePotLabel;

    [Tooltip ( "Prefab for one rider vote button. Needs a TMP_Text child." )]
    [SerializeField] private Button voteButtonPrefab;

    [Tooltip ( "Parent for the rider buttons. Horizontal or Grid Layout Group." )]
    [SerializeField] private Transform voteButtonContainer;

    [Header ( "Managers" )]
    [SerializeField] private UIManager uiManager;

    [Header ( "Voting State" )]
    public List<int> randomisedIndex = new List<int> ();
    public List<int> votingCounts = new List<int> ();

    public readonly List<TranscriptLine> transcript = new List<TranscriptLine> ();

    private readonly Dictionary<int, VotePanelRow> panelRows = new Dictionary<int, VotePanelRow> ();
    private readonly List<Button> riderButtons = new List<Button> ();

    private readonly Dictionary<int, int> intentions = new Dictionary<int, int> ();
    private readonly Dictionary<string, int> colourToSeat =
        new Dictionary<string, int> ( StringComparer.OrdinalIgnoreCase );

    /// <summary>"Player 3" -> seat. The real identity now that colours repeat.</summary>
    private readonly Dictionary<string, int> nameToSeat =
        new Dictionary<string, int> ( StringComparer.OrdinalIgnoreCase );

    private int nobodyVotes;
    private int methodGroupVotes;
    private int methodLeaderVotes;

    private int joinAcceptVotes;
    private int joinRejectVotes;
    private int joinLeanAccept;
    private int joinLeanReject;
    private int namedSuccessorSeat = -1;
    private int pendingJoinerSeat = -1;
    private int pendingBribe;
    private int pendingBribeTargetSeat = -1;   // -1 means the bike's pot

    private int rulesChangeYes;
    private int rulesChangeNo;
    private int ruleOwnColourVotes;
    private readonly Dictionary<string, int> ruleColourVotes = new Dictionary<string, int> ();
    private readonly Dictionary<string, int> ruleEffortVotes = new Dictionary<string, int> ();
    private readonly Dictionary<string, int> termVotes = new Dictionary<string, int> ();
    private bool leaderSteppedDown;

    private Phase phase = Phase.Discussion;
    private int voteCycle;
    private int phaseRoundNumber;
    private string cycleType = "group";
    private int leaderChoiceSeat = -1;
    private bool isThinking;
    private int currentSpeaker = -1;
    private TaskCompletionSource<bool> inputTaskSource;
    private string playerChoice = string.Empty;

    private GameController Game => GameController.instance;

    private bool HumanIsPlaying =>
        Game != null && !Game.PlayerSeatIsAI && Game.slots[ PlayerSeat ].inPlay;

    private bool HumanOnActiveBike =>
        HumanIsPlaying && Game.slots[ PlayerSeat ].bikeIndex == activeBikeIndex;

    private bool IsBinding =>
        phase == Phase.GroupVote ||
        phase == Phase.MethodVote ||
        phase == Phase.LeaderElection ||
        phase == Phase.LeaderDecision ||
        phase == Phase.RulesChangeVote ||
        phase == Phase.RulesTarget ||
        phase == Phase.RulesEffort ||
        phase == Phase.TermVote ||
        phase == Phase.JoinVote;

    /// <summary>Phases where the answer is a word rather than a rider's colour.</summary>
    private bool IsOptionPhase =>
        phase == Phase.MethodVote ||
        phase == Phase.RulesChangeVote ||
        phase == Phase.RulesTarget ||
        phase == Phase.RulesEffort ||
        phase == Phase.TermVote ||
        phase == Phase.JoinVote ||
        phase == Phase.JoinPlea ||
        phase == Phase.Succession;

    private bool AllowSelf => phase == Phase.LeaderElection;

    private bool NobodyAllowed =>
        allowNobodyOption &&
        !forcedElimination &&
        ( phase == Phase.GroupVote || phase == Phase.LeaderDecision );

    /// <summary>
    /// Set when the object is disabled or play mode ends. Every await in this
    /// class checks it before touching a Unity object again - without that,
    /// stopping the editor mid-generation continues the continuation against
    /// destroyed objects and takes the editor down with it.
    /// </summary>
    private bool quitting;

    private TaskCompletionSource<bool> continueSource;
    private float continueDeadline;

    private void Awake ()
    {
        sendInputButton.onClick.AddListener ( SendInputText );
        endVotingButton.onClick.AddListener ( EndVoting );

        if ( continueButton != null )
        {
            continueButton.onClick.AddListener ( ContinuePressed );
            continueButton.gameObject.SetActive ( false );
        }
    }

    private void OnEnable ()
    {
        // Hiding the panel between phases fires OnDisable, which sets the
        // abort flag. Without clearing it here the next discussion would bail
        // out of every await immediately.
        quitting = false;
    }

    private void OnDisable ()
    {
        AbortEverything ();
    }

    private void OnApplicationQuit ()
    {
        AbortEverything ();
    }

    /// <summary>
    /// Stops the discussion cleanly: releases anyone awaiting the human's
    /// input, and asks every agent to abandon its request so the native
    /// library isn't torn down mid-inference.
    /// </summary>
    private void AbortEverything ()
    {
        quitting = true;

        if ( inputTaskSource != null && !inputTaskSource.Task.IsCompleted )
            inputTaskSource.TrySetResult ( false );

        continueSource?.TrySetResult ( false );

        if ( GameController.instance == null )
            return;

        for ( int seat = 0; seat < GameController.instance.slots.Count; seat++ )
        {
            GameObject go = GameController.instance.RiderObject ( seat );

            if ( go == null )
                continue;

            LLMAgent agent = go.GetComponent<LLMAgent> ();

            if ( agent != null )
                CancelAgent ( agent );
        }
    }

    /// <summary>
    /// Calls the agent's cancel method if this version of LLMUnity has one.
    /// Done reflectively because the name has moved between releases.
    /// </summary>
    private void CancelAgent ( LLMAgent agent )
    {
        try
        {
            System.Type type = agent.GetType ();

            System.Reflection.MethodInfo cancel =
                type.GetMethod ( "CancelRequests" ) ??
                type.GetMethod ( "Cancel" ) ??
                type.GetMethod ( "Stop" );

            cancel?.Invoke ( agent, null );
        }
        catch
        {
            // Nothing to cancel, or the method moved again. Not fatal.
        }
    }

    /// <summary>
    /// Holds the sequence until the watcher is ready. Used between phases so
    /// there is time to read a result before the next one starts scrolling.
    /// </summary>
    private async Task Pause ( string label )
    {
        if ( !pauseBetweenPhases || quitting )
            return;

        // Nothing to click and no timeout would hang the game.
        if ( continueButton == null && !continueOnAnyClick && autoContinueSeconds <= 0f )
            return;

        if ( continueLabel != null )
            continueLabel.text = string.IsNullOrEmpty ( label ) ? "Continue" : label;

        if ( continueButton != null )
            continueButton.gameObject.SetActive ( true );

        continueDeadline = autoContinueSeconds > 0f
            ? Time.unscaledTime + autoContinueSeconds
            : float.MaxValue;

        continueSource = new TaskCompletionSource<bool> ();

        await continueSource.Task;

        continueSource = null;

        if ( continueButton != null )
            continueButton.gameObject.SetActive ( false );
    }

    /// <summary>
    /// Frame-based wait. Task.Delay would resume off the main thread, which
    /// is not safe for anything that touches Unity objects afterwards.
    /// </summary>
    private async Task WaitSeconds ( float seconds )
    {
        float until = Time.unscaledTime + Mathf.Max ( 0f, seconds );

        while ( !quitting && Time.unscaledTime < until )
            await Task.Yield ();
    }

    public void ContinuePressed ()
    {
        continueSource?.TrySetResult ( true );
    }

    private void Update ()
    {
        if ( continueSource == null )
            return;

        bool clicked = continueOnAnyClick && Input.GetMouseButtonDown ( 0 );
        bool timedOut = Time.unscaledTime >= continueDeadline;

        if ( clicked || timedOut )
            ContinuePressed ();
    }
}