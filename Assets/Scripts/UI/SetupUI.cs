using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static GameController;

/// <summary>
/// The Setup screen. Builds GameController.slots from the dropdowns.
///
/// Row 0 is the player seat and is the only row that can be Human or AI.
/// When it's set to AI it gains the same Personality and Thinks Human controls
/// the other rows have; when it's Human those controls are hidden, since a
/// human has no persona to configure.
///
/// All lists are indexed by ROW (0 = player seat), so keep them in the same
/// order in the inspector.
/// </summary>
public class SetupUI : MonoBehaviour
{
    [Header ( "Rows" )]
    [Tooltip ( "Row root objects, in order. Element 0 is the player seat." )]
    public List<GameObject> riderRows = new List<GameObject> ();

    public List<TMP_Dropdown> colourDropdowns = new List<TMP_Dropdown> ();
    public List<Image> colourImages = new List<Image> ();

    [Tooltip ( "One per row INCLUDING row 0 - the player's is used when they pick AI." )]
    public List<TMP_Dropdown> personaDropdowns = new List<TMP_Dropdown> ();

    [Tooltip ( "One per row INCLUDING row 0." )]
    public List<TMP_Dropdown> thinksHumanDropdowns = new List<TMP_Dropdown> ();

    [Header ( "Player seat" )]
    [Tooltip ( "Human / AI. Only exists on row 0." )]
    public TMP_Dropdown playerTypeDropdown;

    [Tooltip ( "Objects shown only when the player seat is set to AI - row 0's persona and thinks-human controls, plus their labels." )]
    public List<GameObject> playerAIOnlyObjects = new List<GameObject> ();

    [Header ( "Rider count" )]
    public const int MinRiders = 3;

    [Header ( "UI" )]
    public Button completeSetupButton;
    public GameObject mainMenuPanel;
    public TMP_Text warningText;
    public Button randomiseAIButton;   // assign in the inspector

    [Tooltip ( "Optional second button. Colours are also randomised by the AI button below." )]
    public Button randomiseColoursButton;

    [Tooltip ( "Randomise colours as well when the Randomise AI button is pressed." )]
    public bool randomiseColoursWithAI = true;
    public List<TMP_Text> rowLabels = new List<TMP_Text> ();

    [Tooltip ( "Rows laid out per bike page. Should equal the maximum riders per bike." )]
    public int rowsPerPage = 5;

    public SetupTabs tabs;

    private string[] optionStrings =
        System.Enum.GetNames ( typeof ( GameController.PlayerColour ) );


    private List<Color> optionColors;

    private int[] currentColours;

    public bool PlayerIsAI =>
        playerTypeDropdown != null && playerTypeDropdown.value == 1;

    public int MaxRiders =>
        Mathf.Min ( riderRows.Count, Mathf.Min ( colourDropdowns.Count, optionStrings.Length ) );

    [Tooltip ( "3-5. Total riders = this x number of bikes." )]
    public TMP_Dropdown ridersPerBikeDropdown;

    public int SelectedRidersPerBike =>
        ridersPerBikeDropdown != null
            ? ridersPerBikeDropdown.value + MinRiders
            : rowsPerPage;

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
        if ( Game == null )
        {
            Debug.LogError ( "SetupUI: no GameController in the scene." );
            return;
        }

        optionColors = new List<Color> ( Game.playerColours );

        if ( optionColors.Count < colourDropdowns.Count )
        {
            Debug.LogError ( $"SetupUI: palette has {optionColors.Count} colours " +
                             $"but there are {colourDropdowns.Count} colour dropdowns." );
        }

        currentColours = new int[ colourDropdowns.Count ];

        for ( int i = 0; i < currentColours.Length; i++ )
            currentColours[ i ] = -1;

        SetupColourDropdowns ();
        SetupPersonaDropdowns ();
        SetupThinksHumanDropdowns ();
        SetupPlayerTypeDropdown ();
        SetupRidersPerBikeDropdown ();
        LabelRows ();

        if ( completeSetupButton != null )
            completeSetupButton.onClick.AddListener ( CompleteSetup );

        if ( randomiseAIButton != null )
            randomiseAIButton.onClick.AddListener ( RandomiseAI );

        if ( randomiseColoursButton != null )
            randomiseColoursButton.onClick.AddListener ( RandomiseColours );

        RefreshRows ();
        RefreshPlayerControls ();
    }

    public void RandomiseAI ()
    {
        // Row 0 only gets a persona when the player has chosen to be an AI.
        int firstRow = PlayerIsAI ? 0 : 1;
        int perBike = SelectedRidersPerBike;

        for ( int row = firstRow; row < personaDropdowns.Count; row++ )
        {
            // Rows past the current rider count aren't in play.
            if ( row % rowsPerPage >= perBike )
                continue;

            if ( personaDropdowns[ row ] != null && personaDropdowns[ row ].options.Count > 0 )
            {
                personaDropdowns[ row ].value =
                    UnityEngine.Random.Range ( 0, personaDropdowns[ row ].options.Count );
            }

            if ( row < thinksHumanDropdowns.Count && thinksHumanDropdowns[ row ] != null )
                thinksHumanDropdowns[ row ].value = UnityEngine.Random.Range ( 0, 2 );
        }

        if ( randomiseColoursWithAI )
            RandomiseColours ();
    }

    // ------------------------------------------------------------------
    // Dropdown population
    // ------------------------------------------------------------------

    private void SetupColourDropdowns ()
    {
        for ( int row = 0; row < colourDropdowns.Count; row++ )
        {
            TMP_Dropdown dropdown = colourDropdowns[ row ];

            if ( dropdown == null )
                continue;

            dropdown.ClearOptions ();

            List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData> ();

            for ( int i = 0; i < optionStrings.Length; i++ )
            {
                string coloured =
                    "<color=#" + ColorUtility.ToHtmlStringRGB ( optionColors[ i ] ) + $">{optionStrings[ i ]}</color>";

                options.Add ( new TMP_Dropdown.OptionData ( coloured ) );
            }

            dropdown.AddOptions ( options );

            int index = row;
            // Random rather than by row, so every rider always lands on a real
            // colour even when there are more rows than colours.
            int available = Mathf.Min ( optionStrings.Length, optionColors.Count );
            int startingColour = UnityEngine.Random.Range ( 0, Mathf.Max ( 1, available ) );

            dropdown.SetValueWithoutNotify ( startingColour );
            dropdown.RefreshShownValue ();

            currentColours[ row ] = startingColour;

            if ( row < colourImages.Count && colourImages[ row ] != null )
                colourImages[ row ].color = optionColors[ startingColour ];

            dropdown.onValueChanged.AddListener ( value => UpdateImageColour ( value, index ) );
        }
    }

    private void SetupPersonaDropdowns ()
    {
        List<string> names = new List<string> ( Enum.GetNames ( typeof ( AIRider.Personality ) ) );

        foreach ( TMP_Dropdown dropdown in personaDropdowns )
        {
            if ( dropdown == null )
                continue;

            dropdown.ClearOptions ();
            dropdown.AddOptions ( names );
            dropdown.SetValueWithoutNotify ( 0 );
            dropdown.RefreshShownValue ();
        }
    }
    /// <summary>
    /// Rows are strided by page but seats are contiguous, so the label has to
    /// be worked out rather than taken from the row index.
    /// </summary>
    private void LabelRows ()
    {
        int perBike = SelectedRidersPerBike;

        for ( int row = 0; row < rowLabels.Count && row < riderRows.Count; row++ )
        {
            if ( rowLabels[ row ] == null )
                continue;

            int page = row / rowsPerPage;
            int indexOnPage = row % rowsPerPage;
            int seat = page * perBike + indexOnPage;

            rowLabels[ row ].text = $"Player {seat + 1}";
        }
    }

    private void SetupThinksHumanDropdowns ()
    {
        List<string> options = new List<string> { "No", "Yes" };

        foreach ( TMP_Dropdown dropdown in thinksHumanDropdowns )
        {
            if ( dropdown == null )
                continue;

            dropdown.ClearOptions ();
            dropdown.AddOptions ( options );
            dropdown.SetValueWithoutNotify ( 0 );
            dropdown.RefreshShownValue ();
        }
    }

    private void SetupRidersPerBikeDropdown ()
    {
        if ( ridersPerBikeDropdown == null )
            return;

        ridersPerBikeDropdown.ClearOptions ();

        List<string> counts = new List<string> ();

        int maxPerBike = Mathf.Min ( rowsPerPage, optionStrings.Length );

        for ( int c = MinRiders; c <= maxPerBike; c++ )
            counts.Add ( c.ToString () );

        ridersPerBikeDropdown.AddOptions ( counts );

        // Default to a full bike.
        ridersPerBikeDropdown.SetValueWithoutNotify ( counts.Count - 1 );
        ridersPerBikeDropdown.RefreshShownValue ();

        ridersPerBikeDropdown.onValueChanged.RemoveAllListeners ();
        ridersPerBikeDropdown.onValueChanged.AddListener ( delegate { RefreshRows (); } );
    }

    private void SetupPlayerTypeDropdown ()
    {
        if ( playerTypeDropdown == null )
            return;

        playerTypeDropdown.ClearOptions ();
        playerTypeDropdown.AddOptions ( new List<string> { "Human", "AI" } );
        playerTypeDropdown.SetValueWithoutNotify ( 0 );
        playerTypeDropdown.RefreshShownValue ();

        playerTypeDropdown.onValueChanged.AddListener ( delegate
        { RefreshPlayerControls (); } );
    }

    // ------------------------------------------------------------------
    // Reactive UI
    // ------------------------------------------------------------------

    /// <summary>
    /// Shows the first ridersPerBike rows on each page and hides the rest.
    /// Rows are strided by page, so hidden rows sit between used ones.
    /// </summary>
    public void RefreshRows ()
    {
        int perBike = SelectedRidersPerBike;
        // Game is the lazy lookup - GameController.instance may still be null
        // during Awake, which silently collapsed this to one bike.
        int bikeCount = Game != null ? Mathf.Max ( 1, Game.bikes.Count ) : 1;

        for ( int row = 0; row < riderRows.Count; row++ )
        {
            int page = row / rowsPerPage;
            int indexOnPage = row % rowsPerPage;

            bool inUse = page < bikeCount && indexOnPage < perBike;

            if ( riderRows[ row ] != null )
                riderRows[ row ].SetActive ( inUse );
        }

        LabelRows ();

        if ( tabs != null )
            tabs.Refresh ();
    }

    /// <summary>
    /// Row 0's persona controls only make sense when an AI is driving that seat.
    /// </summary>
    public void RefreshPlayerControls ()
    {
        bool show = PlayerIsAI;

        foreach ( GameObject go in playerAIOnlyObjects )
        {
            if ( go != null )
                go.SetActive ( show );
        }
    }

    /// <summary>
    /// Keeps colours unique by swapping with whoever already had the picked one.
    /// </summary>
    /// <summary>
    /// Updates the swatch. Colours may be shared between riders now - two
    /// riders on the same colour have a common interest, which is the point.
    /// </summary>
    public void UpdateImageColour ( int value, int row )
    {
        currentColours[ row ] = value;

        if ( row < colourImages.Count && colourImages[ row ] != null )
            colourImages[ row ].color = optionColors[ value ];
    }

    /// <summary>Random colours for every row, repeats allowed.</summary>
    public void RandomiseColours ()
    {
        int available = Mathf.Min ( optionStrings.Length, optionColors.Count );

        int perBike = SelectedRidersPerBike;

        for ( int row = 0; row < colourDropdowns.Count; row++ )
        {
            if ( row % rowsPerPage >= perBike )
                continue;

            int pick = UnityEngine.Random.Range ( 0, available );

            if ( colourDropdowns[ row ] != null )
                colourDropdowns[ row ].SetValueWithoutNotify ( pick );

            UpdateImageColour ( pick, row );
        }
    }

    // ------------------------------------------------------------------
    // Building the roster
    // ------------------------------------------------------------------

    public void CompleteSetup ()
    {
        if ( Game == null )
        {
            Debug.LogError ( "SetupUI: no GameController in the scene." );
            return;
        }

        int perBike = SelectedRidersPerBike;
        int bikeCount = Mathf.Max ( 1, Game.bikes.Count );

        Game.slots.Clear ();

        for ( int page = 0; page < bikeCount; page++ )
        {
            for ( int i = 0; i < perBike; i++ )
            {
                int row = page * rowsPerPage + i;

                if ( row >= riderRows.Count )
                    continue;

                if ( currentColours[ row ] < 0 )
                {
                    Warn ( $"Rider on bike {page + 1}, row {i + 1} has no colour." );
                    return;
                }

                // Only the very first row is the human's seat.
                bool isAI = row != 0 || PlayerIsAI;

                Game.slots.Add ( new GameController.RiderSlot
                {
                    isAI = isAI,
                    colour = ( GameController.PlayerColour ) currentColours[ row ],
                    personality = PersonalityFor ( row, isAI ),
                    thinksHuman = ThinksHumanFor ( row, isAI )
                } );
            }
        }

        Warn ( string.Empty );

        Game.SetupGame ();

        if ( mainMenuPanel != null )
            mainMenuPanel.SetActive ( true );

        gameObject.SetActive ( false );
    }

    private AIRider.Personality PersonalityFor ( int row, bool isAI )
    {
        if ( !isAI )
            return AIRider.Personality.Cooperative;   // unused for a human

        if ( row < personaDropdowns.Count && personaDropdowns[ row ] != null )
            return ( AIRider.Personality ) personaDropdowns[ row ].value;

        return AIRider.Personality.Cooperative;
    }

    private bool ThinksHumanFor ( int row, bool isAI )
    {
        if ( !isAI )
            return false;

        if ( row < thinksHumanDropdowns.Count && thinksHumanDropdowns[ row ] != null )
            return thinksHumanDropdowns[ row ].value == 1;

        return false;
    }

    private void Warn ( string message )
    {
        if ( warningText == null )
        {
            if ( !string.IsNullOrEmpty ( message ) )
                Debug.LogError ( "SetupUI: " + message );

            return;
        }

        warningText.text = message;
        warningText.gameObject.SetActive ( !string.IsNullOrEmpty ( message ) );
    }
}