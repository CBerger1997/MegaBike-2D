using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns lootboxes once the roster exists.
///
/// Two things matter here. It must run AFTER setup, because the colours in
/// play aren't known until the riders are configured - spawning in Awake gave
/// every box the fallback tint. And by default it only spawns colours someone
/// actually holds, since a colour nobody owns can never be worth full points
/// and only clutters the map.
/// </summary>
public class LootBoxSpawner : MonoBehaviour
{
    [Header ( "Prefab" )]
    public GameObject lootBox;

    [Header ( "Amount" )]
    [Tooltip ( "Total boxes across all colours. Split evenly between the colours in play." )]
    public int totalBoxes = 300;

    [Tooltip ( "Spawn every colour in the enum, including ones no rider holds." )]
    public bool spawnAllColours = false;

    [Header ( "Placement" )]
    [Tooltip ( "Kept clear around each bike's starting position." )]
    public float clearRadiusAroundBikes = 6f;

    [Tooltip ( "Inset from the arena edge." )]
    public float edgeMargin = 2f;

    [Header ( "Runtime" )]
    public List<GameObject> currentLootBoxes = new List<GameObject> ();

    private bool spawned;

    /// <summary>
    /// Called by GameController once the roster is built. Safe to call again -
    /// it clears whatever is already out there first.
    /// </summary>
    public void SpawnAll ()
    {
        if ( lootBox == null )
        {
            Debug.LogError ( "LootBoxSpawner: no lootBox prefab assigned." );
            return;
        }

        GameController game = GameController.instance;

        if ( game == null )
        {
            Debug.LogError ( "LootBoxSpawner: no GameController, cannot resolve colours." );
            return;
        }

        ClearAll ();

        List<GameController.PlayerColour> colours = ColoursToSpawn ( game );

        if ( colours.Count == 0 )
        {
            Debug.LogWarning ( "LootBoxSpawner: no colours to spawn." );
            return;
        }

        int perColour = Mathf.Max ( 1, totalBoxes / colours.Count );

        foreach ( GameController.PlayerColour colour in colours )
        {
            Color tint = game.ColourFor ( colour );

            if ( tint.a <= 0.01f )
                Debug.LogWarning ( $"LootBoxSpawner: palette entry for {colour} has zero alpha." );

            for ( int i = 0; i < perColour; i++ )
            {
                GameObject box = Instantiate ( lootBox, transform );

                box.transform.position = RandomSpawnPosition ( game );
                box.name = $"Loot_{colour}";

                SpriteRenderer renderer = box.GetComponent<SpriteRenderer> ();

                if ( renderer != null )
                    renderer.color = tint;

                CollectibleBox collectible = box.GetComponent<CollectibleBox> ();

                if ( collectible != null )
                    collectible.boxColour = colour;

                currentLootBoxes.Add ( box );
            }
        }

        spawned = true;

        Debug.Log ( $"LootBoxSpawner: {currentLootBoxes.Count} boxes across {colours.Count} colours " +
                    $"({string.Join ( ", ", colours )})." );
    }

    /// <summary>Distinct colours held by riders still in the game.</summary>
    private List<GameController.PlayerColour> ColoursToSpawn ( GameController game )
    {
        List<GameController.PlayerColour> colours = new List<GameController.PlayerColour> ();

        if ( spawnAllColours )
        {
            foreach ( GameController.PlayerColour c in System.Enum.GetValues ( typeof ( GameController.PlayerColour ) ) )
                colours.Add ( c );

            return colours;
        }

        foreach ( GameController.RiderSlot slot in game.slots )
        {
            if ( !colours.Contains ( slot.colour ) )
                colours.Add ( slot.colour );
        }

        return colours;
    }

    private Vector2 RandomSpawnPosition ( GameController game )
    {
        float limit = Arena.Bounds - edgeMargin;

        // 20 attempts to miss the bikes, then accept a slightly awkward spot
        // rather than loop forever.
        for ( int attempt = 0; attempt < 20; attempt++ )
        {
            Vector2 candidate = new Vector2 (
                Random.Range ( -limit, limit ),
                Random.Range ( -limit, limit )
            );

            bool tooClose = false;

            foreach ( MegaBikeController bike in game.bikes )
            {
                if ( bike == null )
                    continue;

                if ( Vector2.Distance ( candidate, bike.transform.position ) < clearRadiusAroundBikes )
                {
                    tooClose = true;
                    break;
                }
            }

            if ( !tooClose )
                return candidate;
        }

        return new Vector2 ( Random.Range ( -limit, limit ), Random.Range ( -limit, limit ) );
    }

    public void ClearAll ()
    {
        foreach ( GameObject box in currentLootBoxes )
        {
            if ( box != null )
                Destroy ( box );
        }

        currentLootBoxes.Clear ();
        spawned = false;
    }

    public bool HasSpawned => spawned;
}