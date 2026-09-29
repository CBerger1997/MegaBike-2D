using UnityEngine;

/// <summary>
/// Anything that sits on the megabike and owns a colour / stamina pool.
/// Implemented by PlayerRider and AIRider so CollectibleBox can stay generic.
/// </summary>
public interface IRider
{
    GameController.PlayerColour playerColour { get; set; }

    bool IsExhausted { get; }

    /// <summary>Called when a lootbox matching this rider's colour is driven over.</summary>
    void ReceiveLoot ( CollectibleBox box );

    float Stamina { get; }
    float MaxStamina { get; }
}
