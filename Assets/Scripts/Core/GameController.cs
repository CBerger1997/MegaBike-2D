using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Roster-driven game setup.
///
/// Seat 0 is the "player seat" - it may be driven by the human or by an AI.
/// Seats 1..N-1 are the AI riders, drawn from the aiPlayers list in order.
///
/// This replaces the old parallel lists (playerColourStrings / aiPersonas /
/// isPlayerAI), which couldn't express a variable roster.
/// </summary>
public partial class GameController : MonoBehaviour
{
    public enum GameState { MainMenu, Running, RoundOver };

    public enum PlayerColour
    {
        Red, Blue, Green, Yellow, Pink, Purple, Orange, Cyan
    }

    /// <summary>How many colours exist. Riders may share one.</summary>
    public static int ColourCount => System.Enum.GetValues ( typeof ( PlayerColour ) ).Length;

    public const int MinRiders = 6;
    public const int MaxRiders = 10;

    [System.Serializable]
    public class RiderSlot
    {
        [Tooltip ( "Seat 0 only: tick to hand the human's seat to an AI." )]
        public bool isAI;

        [Tooltip ( "Which bike this rider is currently on." )]
        public int bikeIndex;

        public PlayerColour colour = PlayerColour.Red;

        public AIRider.Personality personality = AIRider.Personality.Cooperative;

        [Tooltip ( "AI presents itself as a human player during the vote." )]
        public bool thinksHuman;

        public bool inPlay = true;

        [Tooltip ( "Bike this rider was last thrown off, or -1." )]
        public int lastBikeLeft = -1;

        [Tooltip ( "Round they were thrown off it." )]
        public int lastLeftRound = -1;
    }

    public static GameController instance;

    public GameState gameState;

    [Header ( "Scene objects" )]
    public GameObject player;                       // seat 0
    public List<GameObject> aiPlayers;              // seats 1..7, pre-placed and inactive
    [Tooltip ( "All bikes in play. Two is the intended setup." )]
    public List<MegaBikeController> bikes = new List<MegaBikeController> ();

    [Tooltip ( "Maximum riders per bike. An eliminated rider transfers to any bike below this." )]
    public int bikeCapacity = 4;

    [Tooltip ( "How many riders start on each bike." )]
    [Range ( 3, 5 )] public int ridersPerBike = 5;

    [Tooltip ( "Bikes are placed randomly this far inside the arena edge." )]
    public float bikeSpawnMargin = 15f;

    [Tooltip ( "Minimum distance between bike spawn points." )]
    public float minBikeSeparation = 25f;

    /// <summary>Convenience for code that still assumes one bike.</summary>
    public MegaBikeController bike => bikes.Count > 0 ? bikes[ 0 ] : null;

    [Tooltip ( "Spawns loot once the roster is known. Found automatically if left empty." )]
    public LootBoxSpawner lootSpawner; public BikeVisuals bikeVisuals;

    [Header ( "Roster" )]
    [Tooltip ( "Element 0 is the player seat. Length sets the rider count (3-8)." )]
    public List<RiderSlot> slots = new List<RiderSlot> ();

    [Header ( "Scores" )]
    [Tooltip ( "Loot collected per seat. Index matches slots." )]
    public List<int> scores = new List<int> ();

    /// <summary>Raised whenever any score changes, so UI can refresh.</summary>
    public static event System.Action OnScoresChanged;

    /// <summary>Raised when seats are created, eliminated or moved between bikes.</summary>
    public static event System.Action OnRosterChanged;

    [Header ( "Palette" )]
    [Tooltip ( "One Color per PlayerColour enum entry, in enum order." )]
    public List<Color> playerColours = new List<Color> ();

    [Header ( "Bike wealth" )]
    [Tooltip ( "Cumulative points held by each bike, separate from rider scores." )]
    public List<int> bikeScores = new List<int> ();

    [Header ( "Rules" )]
    [Tooltip ( "One per bike. Created at setup, changed by the riders' votes." )]
    public List<BikeRules> bikeRules = new List<BikeRules> ();

    /// <summary>Riders still in play, including any eliminated check callers need.</summary>
    public int TotalRiders => slots.Count;

    private void Awake ()
    {
        instance = this;

        gameState = GameState.MainMenu;
    }

    public void OnPlay () => gameState = GameState.Running;

    public void OnVote () => gameState = GameState.RoundOver;
}