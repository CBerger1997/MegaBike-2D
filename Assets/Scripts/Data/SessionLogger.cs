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

    [Header ( "Read-only" )]
    public string sessionId;
    public string outputFolder;

    private string agentsPath;
    private string collectionsPath;
    private string utterancesPath;
    private string eventsPath;

    private int turnCounter;

    private void Awake ()
    {
        instance = this;

        if ( !enableLogging )
            return;

        sessionId = DateTime.Now.ToString ( "yyyyMMdd_HHmmss" );

        outputFolder = string.IsNullOrEmpty ( customFolderPath )
            ? Path.Combine ( Application.persistentDataPath, folderName )
            : customFolderPath;

        try
        {
            Directory.CreateDirectory ( outputFolder );

            agentsPath = Path.Combine ( outputFolder, $"agents_{sessionId}.csv" );
            collectionsPath = Path.Combine ( outputFolder, $"collections_{sessionId}.csv" );
            utterancesPath = Path.Combine ( outputFolder, $"utterances_{sessionId}.csv" );
            eventsPath = Path.Combine ( outputFolder, $"events_{sessionId}.csv" );

            File.WriteAllText ( agentsPath,
                "session_id,session_tag,seat,colour,is_ai,personality,thinks_human,model\n" );

            File.WriteAllText ( collectionsPath,
                "session_id,session_tag,round,game_time,seat,colour,box_colour,points,running_score,timestamp\n" );

            File.WriteAllText ( utterancesPath,
                "session_id,session_tag,vote_cycle,bike,cycle_type,phase,phase_round,turn,seat,colour," +
                "is_ai,personality,thinks_human,is_leader,binding,stated_target,speaker_score," +
                "dialogue,response_seconds,retries,timestamp\n" );

            File.WriteAllText ( eventsPath,
                "session_id,session_tag,vote_cycle,phase,event,subject_seat,subject_colour,detail,scores,timestamp\n" );

            Debug.Log ( $"SessionLogger writing to: {outputFolder}" );
        }
        catch ( Exception ex )
        {
            Debug.LogError ( $"SessionLogger: could not create log files - {ex.Message}" );
            enableLogging = false;
        }
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
            voteCycle.ToString (),
            bikeIndex.ToString (),
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
    /// Eliminations, leader elections, ties. The scores snapshot lets you see
    /// the standings at the moment of each decision.
    /// </summary>
    public void LogEvent (
        int voteCycle,
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
            voteCycle.ToString (),
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