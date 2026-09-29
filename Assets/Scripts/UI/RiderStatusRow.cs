using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One rider's HUD row: colour name, stamina bar, and loot score.
///
/// Put this on a prefab containing a TMP label, a Slider and a score TMP text.
/// RiderStatusPanel instantiates one per rider, so the HUD always matches the
/// roster however many riders are in play.
/// </summary>
public class RiderStatusRow : MonoBehaviour
{
    [Header ( "Parts" )]
    public TMP_Text nameLabel;
    public Slider staminaSlider;
    public Image staminaFill;
    public TMP_Text scoreLabel;
    public CanvasGroup canvasGroup;

    [Header ( "Appearance" )]
    [Tooltip ( "Transparency once this rider has been eliminated." )]
    [Range ( 0f, 1f )] public float eliminatedAlpha = 0.35f;

    [Tooltip ( "Show the bike number after the colour name." )]
    public bool showBikeNumber = true;

    private int seat = -1;

    public int Seat => seat;

    public void Bind ( int seatIndex )
    {
        seat = seatIndex;

        GameController game = GameController.instance;

        if ( game == null || seat < 0 || seat >= game.slots.Count )
            return;

        Color colour = game.ColourFor ( game.slots[ seat ].colour );

        if ( nameLabel != null )
        {
            nameLabel.text = game.slots[ seat ].colour.ToString ();
            nameLabel.color = colour;
        }

        if ( staminaFill != null )
            staminaFill.color = colour;

        if ( staminaSlider != null )
        {
            staminaSlider.minValue = 0f;
            staminaSlider.maxValue = 100f;
            staminaSlider.value = 100f;
        }

        Refresh ();
    }

    public void Refresh ()
    {
        GameController game = GameController.instance;

        if ( game == null || seat < 0 || seat >= game.slots.Count )
            return;

        bool inPlay = game.slots[ seat ].inPlay;

        IRider rider = game.GetRider ( seat );

        if ( staminaSlider != null )
        {
            if ( rider != null )
            {
                staminaSlider.maxValue = Mathf.Max ( 1f, rider.MaxStamina );
                staminaSlider.value = inPlay ? rider.Stamina : 0f;
            }
            else if ( !inPlay )
            {
                staminaSlider.value = 0f;
            }
        }

        if ( scoreLabel != null )
            scoreLabel.text = game.GetScore ( seat ).ToString ();

        if ( nameLabel != null && showBikeNumber )
        {
            string name = game.slots[ seat ].colour.ToString ();

            nameLabel.text = inPlay
                ? $"{name}  (B{game.slots[ seat ].bikeIndex + 1})"
                : $"{name}  (out)";
        }

        if ( canvasGroup != null )
            canvasGroup.alpha = inPlay ? 1f : eliminatedAlpha;
    }
}