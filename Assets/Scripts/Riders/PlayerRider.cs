using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The human rider. Steering and pedalling are separate: the movement keys
/// point the bike, the pedal key makes it move. Steering alone costs nothing.
/// </summary>
public class PlayerRider : MonoBehaviour, IRider
{
    [Header ( "Controls" )]
    [Tooltip ( "Hold to pedal. Steering alone costs no stamina." )]
    public KeyCode pedalKey = KeyCode.Space;

    [Tooltip ( "Also pedal automatically whenever a direction is held." )]
    public bool pedalWhileSteering = false;

    [Header ( "Stamina" )]
    public float stamina = 100f;
    public float maxStamina = 100f;

    [Tooltip ( "Stamina lost per second while pedalling at full effort." )]
    public float staminaDrain = 1f;

    [Tooltip ( "Gained per lootbox collected." )]
    public float staminaPerLootBox = 20f;

    [Header ( "References" )]
    public Slider staminaSlider;
    public GameObject playerMovementArrow;

    [Header ( "Identity" )]
    public Color teamColour = Color.red;

    /// <summary>Legacy list of AI rider objects. Unused by the current flow.</summary>
    public List<GameObject> AIPlayers = new List<GameObject> ();

    private RiderInput riderInput;

    // ---------- IRider ----------
    public GameController.PlayerColour playerColour { get; set; }
    public Color TeamColour => teamColour;
    public bool IsExhausted => stamina <= 0f;
    public float Stamina => stamina;
    public float MaxStamina => maxStamina;

    private bool GameRunning =>
        GameController.instance != null &&
        GameController.instance.gameState == GameController.GameState.Running;

    private void Awake ()
    {
        riderInput = GetComponent<RiderInput> ();

        if ( staminaSlider != null )
        {
            staminaSlider.maxValue = maxStamina;
            staminaSlider.value = stamina;
        }
    }

    public void IncreaseStamina ()
    {
        stamina = Mathf.Clamp ( stamina + staminaPerLootBox, 0f, maxStamina );

        if ( staminaSlider != null )
            staminaSlider.value = stamina;
    }

    /// <summary>
    /// Called by CollectibleBox. The amount depends on whether the box matched
    /// this rider's colour - otherwise a busy bike keeps everyone topped up
    /// and stamina stops meaning anything.
    /// </summary>
    public void ReceiveLoot ( CollectibleBox box )
    {
        float amount = box != null ? box.StaminaFor ( playerColour ) : staminaPerLootBox;

        stamina = Mathf.Clamp ( stamina + amount, 0f, maxStamina );

        if ( staminaSlider != null )
            staminaSlider.value = stamina;
    }

    private void Update ()
    {
        if ( riderInput == null )
            return;

        if ( !GameRunning )
        {
            riderInput.Clear ();
            UpdateArrow ( Vector2.zero, 0f );
            return;
        }

        Vector2 steer = new Vector2 (
            Input.GetAxisRaw ( "Horizontal" ),
            Input.GetAxisRaw ( "Vertical" )
        );

        steer = Vector2.ClampMagnitude ( steer, 1f );

        // Pointing the bike is free; pedalling is a deliberate, separate act.
        bool wantsToPedal = Input.GetKey ( pedalKey ) ||
                            ( pedalWhileSteering && steer.sqrMagnitude > 0.0001f );

        bool pedalling = wantsToPedal && stamina > 0f;

        riderInput.steerInput = steer;
        riderInput.pedalInput = pedalling ? 1f : 0f;

        if ( pedalling )
        {
            stamina = Mathf.Clamp (
                stamina - Time.deltaTime * staminaDrain,
                0f,
                maxStamina
            );
        }

        if ( staminaSlider != null )
            staminaSlider.value = stamina;

        UpdateArrow ( steer, riderInput.pedalInput );
    }

    /// <summary>
    /// The arrow points where the rider is steering and grows with pedal
    /// effort, so a rider steering without pedalling shows direction only.
    /// </summary>
    private void UpdateArrow ( Vector2 steer, float pedal )
    {
        if ( playerMovementArrow == null )
            return;

        float length = Mathf.Clamp ( pedal * 2f, 0f, 2f );

        // A short stub while steering without pedalling, so it stays visible.
        if ( steer.sqrMagnitude > 0.0001f && length < 0.35f )
            length = 0.35f;

        playerMovementArrow.transform.localScale = new Vector3 ( 1f, length, 1f );

        if ( steer.sqrMagnitude > 0.0001f )
        {
            float angle = ( Mathf.Atan2 ( steer.y, steer.x ) * Mathf.Rad2Deg ) - 90f;
            playerMovementArrow.transform.rotation = Quaternion.Euler ( 0f, 0f, angle );
        }
    }
}