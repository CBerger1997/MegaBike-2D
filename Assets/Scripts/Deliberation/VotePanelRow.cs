using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One rider's tally row on the voting screen: colour swatch, name, and the
/// running count of leanings or votes against them.
///
/// Generated per vote by RoundEndController, so only the riders on the bike
/// currently deliberating are shown.
/// </summary>
public class VotePanelRow : MonoBehaviour
{
    [Header ( "Parts" )]
    public Image swatch;
    public TMP_Text nameLabel;
    public TMP_Text countLabel;

    [Tooltip ( "Optional. This rider's own points, so wealth is visible while bargaining." )]
    public TMP_Text scoreLabel;

    [Header ( "Appearance" )]
    [Tooltip ( "Marker appended for the current leader." )]
    public string leaderMarker = " *";

    private int seat = -1;

    public int Seat => seat;

    public void Bind ( int seatIndex, string colourName, Color colour )
    {
        seat = seatIndex;

        gameObject.name = $"VotePanel_{colourName}";

        if ( swatch != null )
            swatch.color = colour;

        if ( nameLabel != null )
        {
            nameLabel.text = colourName;
            nameLabel.color = colour;
        }

        SetCount ( "~", 0, false );
        SetScore ( GameController.instance != null ? GameController.instance.GetScore ( seatIndex ) : 0 );
    }

    /// <summary>Points this rider personally holds - what they have to bargain with.</summary>
    public void SetScore ( int score )
    {
        if ( scoreLabel != null )
            scoreLabel.text = score.ToString ();
    }

    public void SetCount ( string prefix, int count, bool isLeader )
    {
        if ( countLabel == null )
            return;

        countLabel.text = $"{prefix} {count}{( isLeader ? leaderMarker : string.Empty )}";
    }
}