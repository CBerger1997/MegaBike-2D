using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns lootboxes once the roster exists, and tops the map back up at the
/// start of every round.
///
/// Two things matter here. It must run AFTER setup, because the colours in
/// play aren't known until the riders are configured. And the map must be
/// replenished: loot is the only source of stamina, so a map stripped in
/// round one guarantees every session dies the same way regardless of how
/// the riders behave.
/// </summary>
public class LootBoxSpawner : MonoBehaviour
{
    [Header ( "Prefab" )]
    public GameObject lootBox;

    [Header ( "Amount" )]
    [Tooltip ( "Total boxes across all colours. Split evenly between the colours in play." )]
    public int totalBoxes = 200;

    [Tooltip ( "Spawn every colour in the enum, including ones no rider holds." )]
    public bool spawnAllColours = false;

    [Header ( "Round top-up" )]
    [Tooltip ( "Refill at the start of each round. Without this later rounds starve." )]
    public bool topUpEachRound = true;

    [Tooltip ( "Fraction of totalBoxes to restore to. 1 = back to full, 0.5 = half." )]
    [Range ( 0.1f, 1f )] public float topUpToFraction = 1f;

    [Tooltip ( "Log what the spawner is doing. Leave on until the top-up is confirmed working." )]
    public bool verbose = true;

    [Header ( "Placement" )]
    [Tooltip ( "Kept clear around each bike's current position." )]
    public float clearRadiusAroundBikes = 3f;

    [Tooltip ( "Inset from the arena edge." )]
    public float edgeMargin = 2f;

    [Header ( "Runtime" )]
    public List<GameObject> currentLootBoxes = new List<GameObject> ();

    private bool spawned;

    public bool HasSpawned => spawned;

    /// <summary>Live boxes, counted from the registry rather than this list.</summary>
    public int LiveBoxCount => CollectibleBox.All.Count;

    // ------------------------------------------------------------------
    // Initial spawn
    // ------------------------------------------------------------------

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
            if ( game.ColourFor ( colour ).a <= 0.01f )
                Debug.LogWarning ( $"LootBoxSpawner: palette entry for {colour} has zero alpha." );

            for ( int i = 0; i < perColour; i++ )
                SpawnOne ( game, colour );
        }

        spawned = true;

        Debug.Log ( $"LootBoxSpawner: spawned {currentLootBoxes.Count} boxes across " +
                    $"{colours.Count} colours ({string.Join ( ", ", colours )})." );
    }

    // ------------------------------------------------------------------
    // Per-round top-up
    // ------------------------------------------------------------------

    /// <summary>
    /// Brings the map back up to strength between rounds. Counts live boxes
    /// from CollectibleBox.All rather than this component's own list, because
    /// the two drift apart as boxes are destroyed.
    /// </summary>
    public void TopUp ()
    {
        GameController game = GameController.instance;

        int live = LiveBoxCount;

        if ( verbose )
        {
            Debug.Log ( $"LootBoxSpawner.TopUp: enabled={topUpEachRound} spawned={spawned} " +
                        $"prefab={( lootBox != null )} game={( game != null )} " +
                        $"live={live} tracked={currentLootBoxes.Count}" );
        }

        if ( !topUpEachRound )
            return;

        if ( lootBox == null || game == null )
        {
            Debug.LogWarning ( "LootBoxSpawner.TopUp: no prefab or no GameController." );
            return;
        }

        // Spawning on demand rather than refusing, so a missed SpawnAll does
        // not silently leave the map empty for the whole session.
        if ( !spawned )
        {
            Debug.LogWarning ( "LootBoxSpawner.TopUp: nothing spawned yet, doing a full spawn instead." );
            SpawnAll ();
            return;
        }

        currentLootBoxes.RemoveAll ( b => b == null );

        int target = Mathf.RoundToInt ( totalBoxes * topUpToFraction );
        int missing = target - live;

        if ( missing <= 0 )
        {
            if ( verbose )
                Debug.Log ( $"LootBoxSpawner.TopUp: {live} boxes already, target {target}. Nothing to do." );

            return;
        }

        List<GameController.PlayerColour> colours = ColoursToSpawn ( game );

        if ( colours.Count == 0 )
        {
            Debug.LogWarning ( "LootBoxSpawner.TopUp: no colours in play." );
            return;
        }

        for ( int i = 0; i < missing; i++ )
            SpawnOne ( game, colours[ i % colours.Count ] );

        Debug.Log ( $"LootBoxSpawner: topped up {missing} boxes ({live} -> {LiveBoxCount}) " +
                    $"across {colours.Count} colours." );
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    /// <summary>One box of the given colour, placed clear of the bikes.</summary>
    private void SpawnOne ( GameController game, GameController.PlayerColour colour )
    {
        GameObject box = Instantiate ( lootBox, transform );

        box.transform.position = RandomSpawnPosition ( game );
        box.name = $"Loot_{colour}";

        SpriteRenderer renderer = box.GetComponent<SpriteRenderer> ();

        if ( renderer != null )
            renderer.color = game.ColourFor ( colour );

        CollectibleBox collectible = box.GetComponent<CollectibleBox> ();

        if ( collectible != null )
            collectible.boxColour = colour;
        else
            Debug.LogError ( "LootBoxSpawner: prefab has no CollectibleBox component." );

        currentLootBoxes.Add ( box );
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
            if ( slot.inPlay && !colours.Contains ( slot.colour ) )
                colours.Add ( slot.colour );
        }

        // Everyone eliminated mid-setup would otherwise leave nothing to spawn.
        if ( colours.Count == 0 )
        {
            foreach ( GameController.RiderSlot slot in game.slots )
            {
                if ( !colours.Contains ( slot.colour ) )
                    colours.Add ( slot.colour );
            }
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
}