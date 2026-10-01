using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Writes four CSV files per session, openable directly in Excel:
///
///   agents_<id>.csv       one row per rider - colour, personality, model
///   collections_<id>.csv  one row per lootbox collected, with running score
///   utterances_<id>.csv   one row per spoken turn, with the stated target
///   events_<id>.csv       eliminations, leader elections, ties
///
/// They join on session_id and seat. Rows are appended as they happen, so a
/// crash mid-session still leaves usable data.
///
/// Put this on a GameObject that lives for the whole session.
/// </summary>
public class SessionLogger : MonoBehaviour
{
    public static SessionLogger instance;

    [Header ( "Output" )]
    public bool enableLogging = true;

    [Tooltip ( "Folder under the persistent data path." )]
    public string folderName = "MegaBikeLogs";

    [Tooltip ( "Optional. Blank uses Application.persistentDataPath - see the console for the resolved path." )]
    public string customFolderPath = "";

    [Tooltip ( "Free-text tag written into every row, e.g. a participant ID or condition name." )]
    public string sessionTag = "";

    [Header ( "Test numbering" )]
    [Tooltip ( "Incremented every play session and remembered between them." )]
    public int testNumber;

    [Tooltip ( "Remember the test number after Unity closes." )]
    public bool persistTestNumber = true;

    [Tooltip ( "Each session gets its own subfolder rather than four files in one pile." )]
    public bool folderPerSession = true;

    [Header ( "Read-only" )]
    public string sessionId;
    public string sessionName;
    public string outputFolder;

    private string agentsPath;
    private string collectionsPath;
    private string utterancesPath;
    private string eventsPath;
    private string movementPath;

    private int turnCounter;

    private void Awake ()
    {
        instance = this;

        if ( !enableLogging )
            return;

        NextTestNumber ();

        sessionId = DateTime.Now.ToString ( "yyyyMMdd_HHmmss" );

        // Date before time so the folders sort chronologically in Explorer.
        sessionName = $"test{testNumber:000}_{sessionId}";

        string root = string.IsNullOrEmpty ( customFolderPath )
            ? Path.Combine ( Application.persistentDataPath, folderName )
            : customFolderPath;

        outputFolder = folderPerSession ? Path.Combine ( root, sessionName ) : root;

        try
        {
            Directory.CreateDirectory ( outputFolder );

            // Inside a per-session folder the names can be plain; in a shared
            // folder they still need the session to tell them apart.
            string stem = folderPerSession ? "" : sessionName + "_";

            agentsPath = Path.Combine ( outputFolder, $"{stem}agents.csv" );
            collectionsPath = Path.Combine ( outputFolder, $"{stem}collections.csv" );
            utterancesPath = Path.Combine ( outputFolder, $"{stem}utterances.csv" );
            eventsPath = Path.Combine ( outputFolder, $"{stem}events.csv" );
            movementPath = Path.Combine ( outputFolder, $"{stem}movement.csv" );

            File.WriteAllText ( agentsPath,
                "session_id,session_tag,seat,colour,is_ai,personality,thinks_human,model\n" );

            File.WriteAllText ( collectionsPath,
                "session_id,session_tag,round,game_time,seat,colour,box_colour,points,running_score,timestamp\n" );

            File.WriteAllText ( utterancesPath,
                "session_id,session_tag,deliberation,bike,governed_by,phase,phase_round,turn,seat,player," +
                "is_ai,personality,thinks_human,is_leader,binding,stated_target,speaker_score," +
                "dialogue,response_seconds,retries,timestamp\n" );

            File.WriteAllText ( eventsPath,
                "session_id,session_tag,deliberation,bike,phase,event,subject_seat,subject_player,detail,scores,timestamp\n" );

            File.WriteAllText ( movementPath,
                "session_id,session_tag,round,game_time,bike,bike_x,bike_y,bike_speed,bike_pedal," +
                "riders_aboard,leader_seat,rules,seat,player,personality,complying," +
                "steering,steer_x,steer_y,pedal,stamina\n" );

            WriteRunInfo ();

            Debug.Log ( $"SessionLogger: {sessionName} -> {outputFolder}" );
        }
        catch ( Exception ex )
        {
            Debug.LogError ( $"SessionLogger: could not create log files - {ex.Message}" );
            enableLogging = false;
        }
    }

    /// <summary>
    /// Takes the next test number. Done at startup so a session abandoned
    /// halfway still consumes its number and cannot overwrite the last one.
    /// </summary>
    private void NextTestNumber ()
    {
        const string key = "MegaBike.TestNumber";

        if ( persistTestNumber )
            testNumber = PlayerPrefs.GetInt ( key, 0 );

        testNumber++;

        if ( persistTestNumber )
        {
            PlayerPrefs.SetInt ( key, testNumber );
            PlayerPrefs.Save ();
        }
    }

    /// <summary>
    /// A plain-text note of what this session was, so a folder found months
    /// later still means something without opening the CSVs.
    /// </summary>
    public void WriteRunInfo ()
    {
        try
        {
            string path = Path.Combine ( outputFolder, "run_info.txt" );

            string text =
                $"session   {sessionName}\n" +
                $"started   {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"tag       {sessionTag}\n" +
                $"unity     {Application.unityVersion}\n" +
                $"product   {Application.productName}\n";

            File.WriteAllText ( path, text );
        }
        catch ( Exception ex )
        {
            Debug.LogWarning ( $"SessionLogger: could not write run_info - {ex.Message}" );
        }
    }

    [ContextMenu ( "Reset test number" )]
    public void ResetTestNumber ()
    {
        PlayerPrefs.SetInt ( "MegaBike.TestNumber", 0 );
        PlayerPrefs.Save ();

        Debug.Log ( "SessionLogger: test number reset. Next session will be test001." );
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    public void LogAgent (
        int seat,
        string colour,
        bool isAI,
        string personality,
        bool thinksHuman,
        string model )
    {
        if ( !enableLogging )
            return;

        Append ( agentsPath, string.Join ( ",", new string[]
        {
            Esc ( sessionId ),
            Esc ( sessionTag ),
            seat.ToString (),
            Esc ( colour ),
            isAI ? "1" : "0",
            Esc ( personality ),
            thinksHuman ? "1" : "0",
            Esc ( model )
        } ) );
    }

    /// <summary>One row per lootbox picked up. This is the "who collects what over time" data.</summary>
    public void LogCollection (
        int round,
        float gameTime,
        int seat,
        string colour,
        string boxColour,
        int points,
        int runningScore )
    {
        if ( !enableLogging )
            return;

        Append ( collectionsPath, string.Join ( ",", new string[]
        {
            Esc ( sessionId ),
            Esc ( sessionTag ),
            round.ToString (),
            gameTime.ToString ( "F1" ),
            seat.ToString (),
            Esc ( colour ),
            Esc ( boxColour ),
            points.ToString (),
            runningScore.ToString (),
            Esc ( DateTime.Now.ToString ( "HH:mm:ss" ) )
        } ) );
    }

    /// <summary>One row per spoken turn, AI or human.</summary>
    public void LogUtterance (
        int voteCycle,
        int bikeIndex,
        string cycleType,
        string phase,
        int phaseRound,
        int seat,
        string colour,
        bool isAI,
        string personality,
        bool thinksHuman,
        bool isLeader,
        bool binding,
        string statedTarget,
        int speakerScore,
        string dialogue,
        float responseSeconds,
        int retries )
    {
        if ( !enableLogging )
            return;

        turnCounter++;

        Append ( utterancesPath, string.Join ( ",", new string[]
        {
            Esc ( sessionId ),
            Esc ( sessionTag ),
            voteCycle > 0 ? voteCycle.ToString () : "",
            bikeIndex > 0 ? bikeIndex.ToString () : "",
            Esc ( cycleType ),
            Esc ( phase ),
            phaseRound.ToString (),
            turnCounter.ToString (),
            seat.ToString (),
            Esc ( colour ),
            isAI ? "1" : "0",
            Esc ( personality ),
            thinksHuman ? "1" : "0",
            isLeader ? "1" : "0",
            binding ? "1" : "0",
            Esc ( statedTarget ),
            speakerScore.ToString (),
            Esc ( dialogue ),
            responseSeconds.ToString ( "F2" ),
            retries.ToString (),
            Esc ( DateTime.Now.ToString ( "HH:mm:ss" ) )
        } ) );
    }

    /// <summary>
    /// One row per rider per sample. Denormalised on purpose - the bike's
    /// state repeats on every rider's row so a pivot table needs no joins.
    /// </summary>
    public void LogMovement (
        int round,
        float gameTime,
        int bikeIndex,
        Vector2 bikePos,
        float bikeSpeed,
        float bikePedal,
        int ridersAboard,
        int leaderSeat,
        string rules,
        int seat,
        string player,
        string personality,
        string complying,
        bool steering,
        Vector2 steer,
        float pedal,
        float stamina )
    {
        if ( !enableLogging )
            return;

        Append ( movementPath, string.Join ( ",", new string[]
        {
            Esc ( sessionId ),
            Esc ( sessionTag ),
            round.ToString (),
            gameTime.ToString ( "F1" ),
            ( bikeIndex + 1 ).ToString (),
            bikePos.x.ToString ( "F1" ),
            bikePos.y.ToString ( "F1" ),
            bikeSpeed.ToString ( "F2" ),
            bikePedal.ToString ( "F2" ),
            ridersAboard.ToString (),
            leaderSeat >= 0 ? ( leaderSeat + 1 ).ToString () : "",
            Esc ( rules ),
            seat.ToString (),
            Esc ( player ),
            Esc ( personality ),
            Esc ( complying ),
            steering ? "1" : "0",
            steer.x.ToString ( "F2" ),
            steer.y.ToString ( "F2" ),
            pedal.ToString ( "F2" ),
            stamina.ToString ( "F1" )
        } ) );
    }

    /// <summary>
    /// Eliminations, leader elections, ties. The scores snapshot lets you see
    /// the standings at the moment of each decision.
    /// </summary>
    public void LogEvent (
        int voteCycle,
        int bikeIndex,
        string phase,
        string eventName,
        int subjectSeat,
        string subjectColour,
        string detail,
        string scoresSnapshot = "" )
    {
        if ( !enableLogging )
            return;

        Append ( eventsPath, string.Join ( ",", new string[]
        {
            Esc ( sessionId ),
            Esc ( sessionTag ),
            voteCycle > 0 ? voteCycle.ToString () : "",
            bikeIndex > 0 ? bikeIndex.ToString () : "",
            Esc ( phase ),
            Esc ( eventName ),
            subjectSeat.ToString (),
            Esc ( subjectColour ),
            Esc ( detail ),
            Esc ( scoresSnapshot ),
            Esc ( DateTime.Now.ToString ( "HH:mm:ss" ) )
        } ) );
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private void Append ( string path, string line )
    {
        if ( string.IsNullOrEmpty ( path ) )
            return;

        try
        {
            File.AppendAllText ( path, line + "\n" );
        }
        catch ( Exception ex )
        {
            Debug.LogError ( $"SessionLogger: write failed - {ex.Message}" );
        }
    }

    /// <summary>
    /// CSV-escapes a field. Dialogue routinely contains commas and quotes, and
    /// a stray newline would split the row, so newlines are flattened.
    /// </summary>
    private string Esc ( string value )
    {
        if ( string.IsNullOrEmpty ( value ) )
            return "\"\"";

        string cleaned = value
            .Replace ( "\r\n", " " )
            .Replace ( "\n", " " )
            .Replace ( "\r", " " )
            .Replace ( "\"", "\"\"" );

        return "\"" + cleaned + "\"";
    }

    [ContextMenu ( "Open log folder" )]
    public void OpenFolder ()
    {
        if ( !string.IsNullOrEmpty ( outputFolder ) )
            Application.OpenURL ( "file://" + outputFolder );
    }
}