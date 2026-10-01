using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class UIManager : MonoBehaviour
{
    [Header ( "Round" )]
    public float roundDuration = 120f;

    [Header ( "Game end" )]
    [Tooltip ( "The game ends after this many rounds. 0 runs indefinitely." )]
    public int maxRounds = 10;

    [Tooltip ( "End early once only this many riders are left in play." )]
    public int minRidersToContinue = 2;

    [Tooltip ( "End as soon as the human is thrown off. Ignored in all-AI runs." )]
    public bool endWhenHumanEliminated = true;

    [Tooltip ( "End once any bike has been emptied of riders." )]
    public bool endWhenBikeEmpty = true;

    [Tooltip ( "Optional panel shown when the game ends." )]
    public GameObject gameOverPanel;

    [Tooltip ( "Optional text for the final standings." )]
    public TMP_Text gameOverText;

    private int roundsPlayed;
    private bool gameEnded;

    /// <summary>
    /// True while the round is paused for an Owdi vote. The round resumes
    /// afterwards with its clock and score intact, rather than restarting.
    /// </summary>
    private bool roundInterrupted;

    [Header ( "UI Text" )]
    public TMP_Text scoreText;
    public TMP_Text timerText;
    public TMP_Text endText;
    public TMP_Text roundText;

    [Header ( "UI Buttons" )]
    public Button playButton;
    public Button helpBackButton;
    public Button helpButton;
    public Button skipButton;

    [Header ( "UI Panels" )]
    public GameObject mainMenu;
    public GameObject helpPanel;
    public GameObject RounderOverUI;

    public MegaBikeController bike;

    /// <summary>Which bike the Owdi struck. -1 means a timer end, so every bike votes.</summary>
    public int hitBikeIndex = -1;

    private int score;
    private float timeRemaining;
    public string LastEndReason { get; private set; } = "timer";

    private void Awake ()
    {
        playButton.onClick.AddListener ( OnPlayPressed );
        helpBackButton.onClick.AddListener ( HelpMenuControl );
        helpButton.onClick.AddListener ( HelpMenuControl );
        skipButton.onClick.AddListener ( delegate
        { timeRemaining = 2f; } );

        mainMenu.SetActive ( true );
        helpPanel.SetActive ( false );
    }

    private void Update ()
    {
        if ( GameController.instance.gameState == GameController.GameState.Running )
        {

            timeRemaining -= Time.deltaTime;

            if ( timeRemaining <= 0f )
            {
                timeRemaining = 0f;
                EndRound ();
            }

            UpdateUI ();
        }
    }

    [Tooltip ( "Optional. When present and enabled, replaces the setup screen with a fixed test roster." )]
    public TestRunConfig testRun;

    public async void OnPlayPressed ()
    {
        // A test run builds its own roster, so the setup screen is skipped.
        if ( testRun != null && testRun.useTestRoster )
            testRun.ApplyRoster ();

        mainMenu.SetActive ( false );
        RounderOverUI.SetActive ( true );

        RoundEndController controller = RounderOverUI.GetComponent<RoundEndController> ();

        if ( controller != null )
            await controller.RunOpeningPhase ();

        RounderOverUI.SetActive ( false );

        StartRound ();
    }

    public void StartRound ()
    {
        if ( gameEnded )
            return;

        string reason = GameEndReason ();

        if ( reason != null )
        {
            EndGame ( reason );
            return;
        }

        // Counted up front so roundsPlayed always means "the round now being
        // played", and GameController.roundNumber agrees with it.
        roundsPlayed++;
        GameController.instance.roundNumber = roundsPlayed;

        mainMenu.SetActive ( false );

        GameController.instance.gameState = GameController.GameState.Running;

        score = 0;
        timeRemaining = roundDuration;

        if ( SessionLogger.instance != null )
        {
            SessionLogger.instance.LogEvent (
                -1,
                -1,
                "Gameplay",
                "round_start",
                -1,
                "",
                $"round:{GameController.instance.roundNumber}",
                GameController.instance.ScoresSummary ()
            );
        }

        if ( endText != null )
            endText.text = "";

        foreach ( MegaBikeController b in GameController.instance.bikes )
        {
            if ( b != null )
                b.enabled = true;
        }

        if ( roundText != null )
            roundText.text = GameController.instance.roundNumber.ToString ();

        UpdateUI ();

    }

    /// <summary>
    /// Why the game should stop, or null to carry on. Checked before each
    /// round rather than after, so the reason is known before anyone rides.
    /// </summary>
    /// <summary>
    /// Why the game should stop, or null to carry on.
    /// </summary>
    /// <param name="includeRoundCap">
    /// False when resuming a round that is already in progress - the cap
    /// decides whether a NEW round starts, and applying it on resume cut the
    /// final round short after an Owdi vote.
    /// </param>
    private string GameEndReason ( bool includeRoundCap = true )
    {
        GameController game = GameController.instance;

        if ( game == null )
            return null;

        if ( includeRoundCap && maxRounds > 0 && roundsPlayed >= maxRounds )
            return $"all {maxRounds} rounds played";

        int left = game.ActiveRiderCount ();

        if ( left < Mathf.Max ( 1, minRidersToContinue ) )
            return left == 1 ? "one rider left" : "nobody left";

        // A bike nobody is riding cannot come back - the remaining riders have
        // nowhere to be transferred to and the contest is over.
        if ( endWhenBikeEmpty )
        {
            for ( int b = 0; b < game.bikes.Count; b++ )
            {
                if ( game.bikes[ b ] != null && game.RidersOnBike ( b ) == 0 )
                    return $"bike {b + 1} is empty";
            }
        }

        // Loot never respawns, so an empty map makes every later round end
        // instantly - stop rather than logging a run of degenerate rounds.
        if ( CollectibleBox.All.Count <= 1 )
            return "loot exhausted";

        if ( endWhenHumanEliminated &&
             !game.PlayerSeatIsAI &&
             game.slots.Count > 0 &&
             !game.slots[ 0 ].inPlay )
        {
            return "the human was eliminated";
        }

        return null;
    }

    private void EndGame ( string reason )
    {
        gameEnded = true;

        GameController game = GameController.instance;

        game.gameState = GameController.GameState.RoundOver;

        StopAllBikes ();

        OwdiDirector director = FindFirstObjectByType<OwdiDirector> ();

        if ( director != null )
            director.ClearAll ();

        string standings = game.ScoresSummary ( false );

        if ( SessionLogger.instance != null )
        {
            SessionLogger.instance.LogEvent (
                -1,
                -1,
                "Gameplay",
                "game_end",
                -1,
                "",
                $"reason:{reason};rounds:{roundsPlayed};{game.BikePotsSummary ()}",
                standings
            );
        }

        Debug.Log ( $"Game over after {roundsPlayed} round(s): {reason}. {standings}" );

        if ( gameOverText != null )
            gameOverText.text = $"Game over\n{reason}\n{standings}";

        if ( gameOverPanel != null )
            gameOverPanel.SetActive ( true );

        if ( RounderOverUI != null )
            RounderOverUI.SetActive ( false );
    }

    /// <summary>Every bike, not just the first one.</summary>
    private void StopAllBikes ()
    {
        GameController game = GameController.instance;

        if ( game == null )
            return;

        foreach ( MegaBikeController b in game.bikes )
        {
            if ( b == null )
                continue;

            b.StopBike ();
            b.enabled = false;
        }
    }

    public void AddScore ( int amount )
    {
        score += amount;
        UpdateUI ();

        if ( CollectibleBox.All.Count <= 1 )
        {
            EndRound ();
        }
    }

    private void EndRound ()
    {
        GameController.instance.gameState = GameController.GameState.RoundOver;

        // set by HandleOwdiHit; anything else reaching EndRound is the timer
        OwdiDirector director = FindFirstObjectByType<OwdiDirector> ();

        if ( director != null )
            director.ClearAll ();

        if ( SessionLogger.instance != null )
        {
            SessionLogger.instance.LogEvent (
                -1,
                -1,
                "Gameplay",
                "round_end",
                -1,
                "",
                $"reason:{LastEndReason};score:{score}",
                GameController.instance.ScoresSummary ()
            );
        }

        LastEndReason = "timer";

        // Without this, every later timer round would keep deliberating only
        // the bike that was hit once.
        hitBikeIndex = -1;

        StopAllBikes ();

        if ( endText != null )
        {
            endText.text =
                $"Round complete\nScore: {score}";
        }

        UpdateUI ();

        RounderOverUI.SetActive ( true );
        RounderOverUI.GetComponent<RoundEndController> ().TriggerDiscussion ();
    }

    private void UpdateUI ()
    {
        if ( scoreText != null )
            scoreText.text = $"Score: {score}";

        if ( timerText != null )
        {
            int minutes =
                Mathf.FloorToInt ( timeRemaining / 60f );

            int seconds =
                Mathf.FloorToInt ( timeRemaining % 60f );

            string round = maxRounds > 0
                ? $"Round {roundsPlayed}/{maxRounds}"
                : $"Round {roundsPlayed}";

            timerText.text = $"{round}   {minutes:00}:{seconds:00}";
        }
    }

    private void HelpMenuControl ()
    {
        if ( helpPanel.activeSelf )
        {
            helpPanel.SetActive ( false );
        }
        else
        {
            helpPanel.SetActive ( true );
        }
    }

    private void OnEnable ()
    {
        OwdiController.OnAnyBikeHit += HandleOwdiHit;
        GameController.OnRosterChanged += CheckEndMidRound;
    }

    private void OnDisable ()
    {
        OwdiController.OnAnyBikeHit -= HandleOwdiHit;
        GameController.OnRosterChanged -= CheckEndMidRound;
    }

    /// <summary>
    /// The end conditions are normally tested between rounds, but a bike can
    /// empty part way through one - and riding on with an empty bike makes no
    /// sense. Fires whenever the roster changes.
    /// </summary>
    private void CheckEndMidRound ()
    {
        if ( gameEnded || GameController.instance == null )
            return;

        if ( GameController.instance.gameState != GameController.GameState.Running )
            return;

        string reason = GameEndReason ( false );

        if ( reason != null )
            EndGame ( reason );
    }

    /// <summary>
    /// The Owdi caught the bike. Ends the round immediately, which triggers the
    /// vote. Static event, so the prefab needs no scene references.
    /// </summary>
    private void HandleOwdiHit ( OwdiController car )
    {
        if ( GameController.instance == null ||
             GameController.instance.gameState != GameController.GameState.Running )
        {
            return;
        }

        LastEndReason = "owdi";

        int hitIndex = 0;

        if ( car != null && car.HitBike != null )
            hitIndex = GameController.instance.bikes.IndexOf ( car.HitBike );

        hitBikeIndex = Mathf.Max ( 0, hitIndex );

        // Logged after hitBikeIndex is known, so the row names the right bike.
        if ( SessionLogger.instance != null )
        {
            Vector2 where = car != null ? ( Vector2 ) car.transform.position : Vector2.zero;

            SessionLogger.instance.LogEvent (
                -1,
                hitBikeIndex + 1,
                "Gameplay",
                "owdi_hit",
                -1,
                "",
                $"time:{roundDuration - timeRemaining:F1};pos:{where.x:F1}/{where.y:F1}",
                GameController.instance.ScoresSummary ()
            );
        }

        InterruptForVote ();
    }

    /// <summary>
    /// Pauses the round so the struck bike can throw someone off, then picks
    /// up where it left off. The clock and the score are untouched.
    /// </summary>
    private void InterruptForVote ()
    {
        roundInterrupted = true;

        GameController.instance.gameState = GameController.GameState.RoundOver;

        StopAllBikes ();

        OwdiDirector director = FindFirstObjectByType<OwdiDirector> ();

        if ( director != null )
            director.ClearAll ();

        RounderOverUI.SetActive ( true );
        RounderOverUI.GetComponent<RoundEndController> ().TriggerDiscussion ();

        // TriggerDiscussion reads LastEndReason synchronously before its first
        // await, so it is safe to clear here - and it must be cleared, or the
        // round_end that follows is mislabelled as an Owdi hit.
        LastEndReason = "timer";
    }

    /// <summary>
    /// Called when the deliberation finishes. Resumes an interrupted round, or
    /// begins the next one if the round had actually ended.
    /// </summary>
    public void ResumeAfterVote ()
    {
        if ( !roundInterrupted )
        {
            StartRound ();
            return;
        }

        roundInterrupted = false;
        hitBikeIndex = -1;

        string reason = GameEndReason ( false );

        if ( reason != null )
        {
            EndGame ( reason );
            return;
        }

        foreach ( MegaBikeController b in GameController.instance.bikes )
        {
            if ( b != null )
                b.enabled = true;
        }

        GameController.instance.gameState = GameController.GameState.Running;

        Debug.Log ( $"Round resumed with {timeRemaining:F0}s left." );
    }
}