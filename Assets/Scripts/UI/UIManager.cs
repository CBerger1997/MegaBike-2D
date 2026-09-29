using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class UIManager : MonoBehaviour
{
    [Header ( "Round" )]
    public float roundDuration = 120f;

    [Header ( "UI Text" )]
    public TMP_Text scoreText;
    public TMP_Text timerText;
    public TMP_Text endText;

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

    public async void OnPlayPressed ()
    {
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
        mainMenu.SetActive ( false );

        GameController.instance.gameState = GameController.GameState.Running;

        score = 0;
        timeRemaining = roundDuration;

        if ( SessionLogger.instance != null )
        {
            SessionLogger.instance.LogEvent (
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

        bike.enabled = true;

        UpdateUI ();

        GameController.instance.roundNumber++;
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
                "Gameplay",
                "round_end",
                -1,
                "",
                $"reason:{LastEndReason};score:{score}",
                GameController.instance.ScoresSummary ()
            );
        }

        LastEndReason = "timer";

        if ( bike != null )
        {
            bike.StopBike ();
            bike.enabled = false;
        }

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

            timerText.text =
                $"Time: {minutes:00}:{seconds:00}";
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
    }

    private void OnDisable ()
    {
        OwdiController.OnAnyBikeHit -= HandleOwdiHit;
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

        if ( SessionLogger.instance != null )
        {
            Vector2 where = car != null ? ( Vector2 ) car.transform.position : Vector2.zero;

            SessionLogger.instance.LogEvent (
                -1,
                "Gameplay",
                "owdi_hit",
                -1,
                "",
                $"time:{roundDuration - timeRemaining:F1};pos:{where.x:F1}/{where.y:F1}",
                GameController.instance.ScoresSummary ()
            );
        }

        LastEndReason = "owdi";

        int hitIndex = 0;

        if ( car != null && car.HitBike != null )
            hitIndex = GameController.instance.bikes.IndexOf ( car.HitBike );

        hitBikeIndex = Mathf.Max ( 0, hitIndex );

        EndRound ();
    }
}