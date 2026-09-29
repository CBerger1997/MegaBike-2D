using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Shows every rider's score on one line, so the human can see the same
/// standings the AI riders are given in their prompts.
///
/// Put this on a TMP text object somewhere on the HUD and assign it, or leave
/// scoreText empty and it'll use a TMP_Text on the same GameObject.
/// </summary>
public class ScoreboardUI : MonoBehaviour
{
    public TMP_Text scoreText;

    [Tooltip ( "Colour each rider's name using their team colour." )]
    public bool useRichTextColours = true;

    [Tooltip ( "Separator between entries." )]
    public string separator = "   ";

    [Tooltip ( "Strike through riders who have been eliminated." )]
    public bool markEliminated = true;

    private void Awake ()
    {
        if ( scoreText == null )
            scoreText = GetComponent<TMP_Text> ();
    }

    private void OnEnable ()
    {
        GameController.OnScoresChanged += Refresh;
        Refresh ();
    }

    private void OnDisable ()
    {
        GameController.OnScoresChanged -= Refresh;
    }

    public void Refresh ()
    {
        if ( scoreText == null )
            return;

        GameController game = GameController.instance;

        if ( game == null )
            return;

        StringBuilder sb = new StringBuilder ();

        for ( int seat = 0; seat < game.slots.Count; seat++ )
        {
            if ( sb.Length > 0 )
                sb.Append ( separator );

            string name = game.slots[ seat ].colour.ToString ();
            int score = game.GetScore ( seat );

            bool out_ = markEliminated && !game.slots[ seat ].inPlay;

            string entry = out_ ? $"<s>{name} {score}</s>" : $"{name} {score}";

            if ( useRichTextColours )
            {
                string hex = ColorUtility.ToHtmlStringRGB ( game.ColourFor ( game.slots[ seat ].colour ) );
                entry = $"<color=#{hex}>{entry}</color>";
            }

            sb.Append ( entry );
        }

        scoreText.text = sb.ToString ();
    }
}