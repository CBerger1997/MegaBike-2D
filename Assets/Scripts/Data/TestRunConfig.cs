using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a repeatable roster for testing, bypassing the setup screen.
///
/// Each bike gets one rider of each personality, plus a fifth whose
/// personality cycles from game to game. Four runs therefore cover every
/// variant of the odd rider against an otherwise identical bike.
///
/// The run index persists between play sessions, so closing Unity and coming
/// back continues the sequence rather than restarting it.
/// </summary>
public class TestRunConfig : MonoBehaviour
{
    private const string RunKey = "MegaBike.TestRunIndex";

    [Header ( "Use this instead of the setup screen" )]
    public bool useTestRoster = true;

    [Tooltip ( "The Setup Panel. Hidden at startup so the run goes straight to Play." )]
    public GameObject setupPanel;

    [Tooltip ( "The Play Menu Panel that Complete Setup would normally reveal." )]
    public GameObject playMenuPanel;

    [Header ( "Roster" )]
    [Range ( 2, 5 )] public int ridersPerBike = 5;

    [Tooltip ( "One rider per entry, in seat order. The remaining seats take the varying personality." )]
    public List<AIRider.Personality> fixedPersonalities = new List<AIRider.Personality>
    {
        AIRider.Personality.Cooperative,
        AIRider.Personality.Lazy,
        AIRider.Personality.Subversive,
        AIRider.Personality.Opposing
    };

    [Tooltip ( "The odd rider is the same personality on both bikes, so the bikes stay comparable." )]
    public bool sameVaryingOnBothBikes = true;

    [Header ( "Seat 0" )]
    [Tooltip ( "Leave on for an all-AI run with no human." )]
    public bool playerSeatIsAI = true;

    [Header ( "Personas" )]
    [Tooltip ( "Every AI presents itself as a human player." )]
    public bool allThinkHuman = false;

    [Header ( "Colours" )]
    [Tooltip ( "Off assigns colours by seat, so runs are directly comparable." )]
    public bool randomiseColours = false;

    [Header ( "Run counter" )]
    [Tooltip ( "Which run this is. Cycles the odd rider through the personalities." )]
    public int runIndex;

    [Tooltip ( "Remember the run index between play sessions." )]
    public bool persistRunIndex = true;

    [Tooltip ( "Written into every log row so runs can be told apart." )]
    public string tagPrefix = "test";

    private GameController game;

    private GameController Game
    {
        get
        {
            if ( game == null )
            {
                game = GameController.instance != null
                    ? GameController.instance
                    : FindFirstObjectByType<GameController> ();
            }

            return game;
        }
    }

    private void Awake ()
    {
        if ( persistRunIndex )
            runIndex = PlayerPrefs.GetInt ( RunKey, 0 );
    }

    private void Start ()
    {
        if ( !useTestRoster )
            return;

        // The setup screen normally gates the Play button. A test run builds
        // its own roster, so skip straight to the menu that has Play on it.
        if ( setupPanel != null )
            setupPanel.SetActive ( false );

        if ( playMenuPanel != null )
            playMenuPanel.SetActive ( true );

        Debug.Log ( $"TestRunConfig: setup screen skipped. Next run is {runIndex}, " +
                    $"odd rider {VaryingPersonality ( 0 )}." );
    }

    /// <summary>The personality the odd rider takes this run.</summary>
    public AIRider.Personality VaryingPersonality ( int bikeIndex )
    {
        if ( fixedPersonalities.Count == 0 )
            return AIRider.Personality.Cooperative;

        int offset = sameVaryingOnBothBikes ? 0 : bikeIndex;

        return fixedPersonalities[ ( runIndex + offset ) % fixedPersonalities.Count ];
    }

    /// <summary>
    /// Fills the roster and runs setup. Call this in place of the setup
    /// screen's Complete Setup.
    /// </summary>
    public void ApplyRoster ()
    {
        if ( Game == null )
        {
            Debug.LogError ( "TestRunConfig: no GameController in the scene." );
            return;
        }

        int bikeCount = Mathf.Max ( 1, Game.bikes.Count );
        int colourCount = Mathf.Min ( GameController.ColourCount, Game.playerColours.Count );

        if ( colourCount <= 0 )
        {
            Debug.LogError ( "TestRunConfig: palette is empty." );
            return;
        }

        Game.slots.Clear ();

        for ( int bike = 0; bike < bikeCount; bike++ )
        {
            for ( int i = 0; i < ridersPerBike; i++ )
            {
                int seat = bike * ridersPerBike + i;

                // The first riders take one personality each; anyone beyond
                // that list is the varying rider for this run.
                AIRider.Personality persona = i < fixedPersonalities.Count
                    ? fixedPersonalities[ i ]
                    : VaryingPersonality ( bike );

                GameController.PlayerColour colour = randomiseColours
                    ? ( GameController.PlayerColour ) Random.Range ( 0, colourCount )
                    : ( GameController.PlayerColour ) ( seat % colourCount );

                Game.slots.Add ( new GameController.RiderSlot
                {
                    isAI = seat != 0 || playerSeatIsAI,
                    colour = colour,
                    personality = persona,
                    thinksHuman = allThinkHuman && ( seat != 0 || playerSeatIsAI )
                } );
            }
        }

        TagSession ();

        Debug.Log ( $"TestRunConfig: run {runIndex}, odd rider is " +
                    $"{VaryingPersonality ( 0 )}, {Game.slots.Count} riders." );

        Game.SetupGame ();

        AdvanceRun ();
    }

    /// <summary>Puts the run and the varying personality into every log row.</summary>
    private void TagSession ()
    {
        if ( SessionLogger.instance == null )
            return;

        SessionLogger.instance.sessionTag =
            $"{tagPrefix}_run{runIndex}_odd-{VaryingPersonality ( 0 )}";

        // run_info.txt was written at startup, before this tag existed.
        SessionLogger.instance.WriteRunInfo ();
    }

    /// <summary>
    /// Moves to the next run. Done at setup rather than at the end, so a
    /// session abandoned halfway still advances and isn't silently repeated.
    /// </summary>
    private void AdvanceRun ()
    {
        runIndex++;

        if ( persistRunIndex )
        {
            PlayerPrefs.SetInt ( RunKey, runIndex );
            PlayerPrefs.Save ();
        }
    }

    [ContextMenu ( "Reset run counter" )]
    public void ResetRuns ()
    {
        runIndex = 0;

        if ( persistRunIndex )
        {
            PlayerPrefs.SetInt ( RunKey, 0 );
            PlayerPrefs.Save ();
        }

        Debug.Log ( "TestRunConfig: run counter reset to 0." );
    }
}