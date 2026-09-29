using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Building a game: scores and pots, assigning seats to bikes, placing
/// the bikes, configuring each rider, and spawning the loot.
/// </summary>
public partial class GameController
{
    // ------------------------------------------------------------------
    // Setup
    // ------------------------------------------------------------------

    public void SetupGame ()
    {
        ValidateRoster ();
        ResetScores ();

        AssignSeatsToBikes ();
        ResetRulesAndLeaders ();
        SpawnBikes ();

        int riderCount = slots.Count;

        SetupPlayerSeat ( slots[ 0 ] );

        for ( int aiIndex = 0; aiIndex < aiPlayers.Count; aiIndex++ )
        {
            int seat = aiIndex + 1;
            bool inUse = seat < riderCount;

            GameObject go = aiPlayers[ aiIndex ];

            if ( go == null )
                continue;

            go.SetActive ( inUse );

            if ( inUse )
                SetupAISeat ( go, slots[ seat ] );
        }

        RebuildBikeRiders ();

        SpawnLoot ();

        LogRoster ();
        OnRosterChanged?.Invoke ();

        if ( bikeVisuals != null )
            bikeVisuals.SetRiderCount ( riderCount );
    }


    /// <summary>Fresh rules and no leaders at the start of a game.</summary>
    public void ResetRulesAndLeaders ()
    {
        bikeRules.Clear ();

        for ( int b = 0; b < bikes.Count; b++ )
        {
            bikeRules.Add ( new BikeRules () );

            if ( bikes[ b ] != null )
            {
                bikes[ b ].bikeIndex = b;
                bikes[ b ].leaderSeat = -1;
                bikes[ b ].governanceExpiresRound = -1;
                bikes[ b ].termRounds = 0;
            }
        }
    }

    public int GetBikeScore ( int bikeIndex )
    {
        while ( bikeScores.Count < bikes.Count )
            bikeScores.Add ( 0 );

        return bikeIndex >= 0 && bikeIndex < bikeScores.Count ? bikeScores[ bikeIndex ] : 0;
    }

    public void AddToBikePot ( int bikeIndex, int amount )
    {
        while ( bikeScores.Count < bikes.Count )
            bikeScores.Add ( 0 );

        if ( bikeIndex < 0 || bikeIndex >= bikeScores.Count )
            return;

        bikeScores[ bikeIndex ] += amount;

        OnScoresChanged?.Invoke ();
    }

    /// <summary>
    /// Moves points out of a rider's own score. Destination is another rider,
    /// or a bike's pot when toSeat is -1. Returns what actually moved.
    /// </summary>
    public int PayPoints ( int fromSeat, int toSeat, int toBike, int amount )
    {
        if ( fromSeat < 0 || fromSeat >= scores.Count || amount <= 0 )
            return 0;

        int paid = Mathf.Min ( amount, scores[ fromSeat ] );

        if ( paid <= 0 )
            return 0;

        scores[ fromSeat ] -= paid;

        if ( toSeat >= 0 && toSeat < scores.Count )
            scores[ toSeat ] += paid;
        else
            AddToBikePot ( toBike, paid );

        OnScoresChanged?.Invoke ();

        if ( SessionLogger.instance != null )
        {
            string to = toSeat >= 0 ? DisplayName ( toSeat ) : $"bike {toBike + 1} pot";

            SessionLogger.instance.LogEvent (
                -1, "Wealth", "points_paid", fromSeat,
                DisplayName ( fromSeat ),
                $"to:{to};amount:{paid}",
                ScoresSummary ()
            );
        }

        return paid;
    }

    /// <summary>Moves a rider onto a specific bike. No capacity check - the caller decides.</summary>
    public void MoveSeatToBike ( int seat, int toBike )
    {
        if ( seat < 0 || seat >= slots.Count || toBike < 0 || toBike >= bikes.Count )
            return;

        int fromBike = slots[ seat ].bikeIndex;

        slots[ seat ].bikeIndex = toBike;

        GameObject go = RiderObject ( seat );

        if ( go != null && bikes[ toBike ] != null )
            go.transform.SetParent ( bikes[ toBike ].transform, false );

        RebuildBikeRiders ();

        if ( SessionLogger.instance != null )
        {
            SessionLogger.instance.LogEvent (
                -1, "Transfer", "transferred", seat,
                DisplayName ( seat ),
                $"from:{fromBike + 1};to:{toBike + 1}",
                ScoresSummary ()
            );
        }
    }

    public void ResetScores ()
    {
        scores.Clear ();

        for ( int i = 0; i < slots.Count; i++ )
            scores.Add ( 0 );

        bikeScores.Clear ();

        for ( int b = 0; b < bikes.Count; b++ )
            bikeScores.Add ( 0 );

        OnScoresChanged?.Invoke ();
    }

    public int GetScore ( int seat )
    {
        return seat >= 0 && seat < scores.Count ? scores[ seat ] : 0;
    }

    /// <summary>Awards loot and logs the collection. Returns the new total.</summary>
    /// <summary>
    /// Awards a collected box. Everyone on the bike sharing the box's colour
    /// scores, so a shared colour is worth cooperating over.
    /// </summary>
    public void AwardBox ( int bikeIndex, PlayerColour boxColour, int ownColourPoints, int otherPoints )
    {
        List<int> seats = SeatsOnBike ( bikeIndex );

        foreach ( int seat in seats )
        {
            bool matches = slots[ seat ].colour == boxColour;
            int points = matches ? ownColourPoints : otherPoints;

            if ( points > 0 )
                AddScore ( seat, points, boxColour.ToString () );
        }

        // The bike itself accumulates wealth, which is what an outsider can
        // later be asked to add to.
        AddToBikePot ( bikeIndex, ownColourPoints );
    }

    public int AddScore ( int seat, int amount, string boxColour )
    {
        if ( seat < 0 || seat >= scores.Count )
            return 0;

        scores[ seat ] += amount;

        OnScoresChanged?.Invoke ();

        if ( SessionLogger.instance != null )
        {
            SessionLogger.instance.LogCollection (
                roundNumber,
                Time.timeSinceLevelLoad,
                seat,
                slots[ seat ].colour.ToString (),
                boxColour,
                amount,
                scores[ seat ]
            );
        }

        return scores[ seat ];
    }

    /// <summary>"Red 5, Blue 2, Green 1" - used in prompts and log snapshots.</summary>
    public string BikePotsSummary ()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder ();

        for ( int b = 0; b < bikes.Count; b++ )
        {
            if ( sb.Length > 0 )
                sb.Append ( ", " );

            sb.Append ( $"Bike {b + 1} pot {GetBikeScore ( b )}" );
        }

        return sb.ToString ();
    }

    public string ScoresSummary ( bool inPlayOnly = true )
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder ();

        for ( int seat = 0; seat < slots.Count; seat++ )
        {
            if ( inPlayOnly && !slots[ seat ].inPlay )
                continue;

            if ( sb.Length > 0 )
                sb.Append ( ", " );

            sb.Append ( slots[ seat ].colour ).Append ( " " ).Append ( GetScore ( seat ) );
        }

        return sb.ToString ();
    }

    /// <summary>Counts how many rounds have been played, for the collection log.</summary>
    public int roundNumber = 1;

    /// <summary>Writes one row per rider so utterances can be joined to personalities.</summary>
    /// <summary>
    /// Loot can only be spawned once the roster exists, because which colours
    /// are worth spawning depends on which colours the riders hold.
    /// </summary>
    private void SpawnLoot ()
    {
        if ( lootSpawner == null )
            lootSpawner = FindFirstObjectByType<LootBoxSpawner> ();

        if ( lootSpawner != null )
            lootSpawner.SpawnAll ();
        else
            Debug.LogWarning ( "GameController: no LootBoxSpawner in the scene." );
    }

    private void LogRoster ()
    {
        if ( SessionLogger.instance == null )
            return;

        for ( int seat = 0; seat < slots.Count; seat++ )
        {
            RiderSlot slot = slots[ seat ];
            GameObject go = RiderObject ( seat );

            string model = "human";

            if ( slot.isAI && go != null )
            {
                AIConversationController convo = go.GetComponent<AIConversationController> ();

                if ( convo != null )
                    model = convo.GetModelName ();
            }

            SessionLogger.instance.LogAgent (
                seat,
                slot.colour.ToString (),
                slot.isAI,
                slot.personality.ToString (),
                slot.thinksHuman,
                model
            );
        }
    }

    /// <summary>
    /// Fills each bike to ridersPerBike in order. Seat 0 (the player) is on
    /// bike 0 so the camera and HUD have something stable to follow.
    /// </summary>
    private void AssignSeatsToBikes ()
    {
        if ( bikes.Count == 0 )
        {
            Debug.LogError ( "GameController: no bikes assigned." );
            return;
        }

        for ( int seat = 0; seat < slots.Count; seat++ )
        {
            int bikeIndex = Mathf.Min ( seat / ridersPerBike, bikes.Count - 1 );

            slots[ seat ].bikeIndex = bikeIndex;

            GameObject go = RiderObject ( seat );

            if ( go != null && bikes[ bikeIndex ] != null )
                go.transform.SetParent ( bikes[ bikeIndex ].transform, false );
        }
    }

    /// <summary>Random, separated starting positions inside the arena.</summary>
    public void SpawnBikes ()
    {
        float limit = Arena.Bounds - bikeSpawnMargin;
        List<Vector2> placed = new List<Vector2> ();

        foreach ( MegaBikeController b in bikes )
        {
            if ( b == null )
                continue;

            Vector2 pos = Vector2.zero;
            bool ok = false;

            // 30 attempts, then accept whatever we have rather than loop forever.
            for ( int attempt = 0; attempt < 30 && !ok; attempt++ )
            {
                pos = new Vector2 ( Random.Range ( -limit, limit ), Random.Range ( -limit, limit ) );

                ok = true;

                foreach ( Vector2 other in placed )
                {
                    if ( Vector2.Distance ( pos, other ) < minBikeSeparation )
                    {
                        ok = false;
                        break;
                    }
                }
            }

            placed.Add ( pos );

            b.bikeIndex = bikes.IndexOf ( b );
            b.transform.position = pos;

            Rigidbody2D rb = b.GetComponent<Rigidbody2D> ();

            if ( rb != null )
                rb.position = pos;

            b.StopBike ();
        }
    }

    private void ValidateRoster ()
    {
        if ( slots.Count < MinRiders )
        {
            Debug.LogWarning ( $"GameController: roster has {slots.Count} seats, padding to {MinRiders}." );

            while ( slots.Count < MinRiders )
                slots.Add ( new RiderSlot () );
        }

        int maxSeats = Mathf.Min ( MaxRiders, aiPlayers.Count + 1 );

        int expected = ridersPerBike * bikes.Count;

        if ( slots.Count != expected )
        {
            Debug.LogWarning ( $"GameController: roster has {slots.Count} seats, " +
                               $"expected {expected} ({ridersPerBike} per bike x {bikes.Count} bikes)." );
        }

        if ( slots.Count > maxSeats )
        {
            Debug.LogWarning ( $"GameController: roster has {slots.Count} seats but only {maxSeats} rider objects exist. Trimming." );

            slots.RemoveRange ( maxSeats, slots.Count - maxSeats );
        }

        // Colours may be shared. Riders are identified by player number, so a
        // shared colour is a common interest rather than a clash.

        foreach ( RiderSlot slot in slots )
            slot.inPlay = true;
    }

    private void SetupPlayerSeat ( RiderSlot slot )
    {
        if ( player == null )
        {
            Debug.LogError ( "GameController: no player object assigned." );
            return;
        }

        player.SetActive ( true );

        Color colour = ColourFor ( slot.colour );

        PlayerRider playerRider = player.GetComponent<PlayerRider> ();
        AIRider aiOnPlayer = player.GetComponent<AIRider> ();

        if ( playerRider != null )
        {
            playerRider.playerColour = slot.colour;
            playerRider.teamColour = colour;

            if ( playerRider.playerMovementArrow != null )
            {
                SpriteRenderer arrow = playerRider.playerMovementArrow.GetComponent<SpriteRenderer> ();
                if ( arrow != null )
                    arrow.color = colour;
            }

            if ( playerRider.staminaSlider != null && playerRider.staminaSlider.fillRect != null )
            {
                Image fill = playerRider.staminaSlider.fillRect.GetComponent<Image> ();
                if ( fill != null )
                    fill.color = colour;
            }

            // Only one of the two drivers may run.
            playerRider.enabled = !slot.isAI;
        }

        SpriteRenderer body = player.GetComponent<SpriteRenderer> ();
        if ( body != null )
            body.color = colour;

        if ( aiOnPlayer != null )
        {
            aiOnPlayer.enabled = slot.isAI;

            if ( slot.isAI )
            {
                ConfigureAIRider ( aiOnPlayer, slot, colour );

                AIConversationController convo = player.GetComponent<AIConversationController> ();

                if ( convo != null )
                {
                    convo.behaviour = ( AIConversationController.AIBehaviour ) ( int ) slot.personality;
                    convo.thinksHuman = slot.thinksHuman;
                    convo.riderColour = slot.colour.ToString ();
                    convo.SetupPersonality ();
                }
            }
        }
        else if ( slot.isAI )
        {
            Debug.LogError ( "GameController: player seat set to AI but the player object has no AIRider component." );
        }
    }

    private void SetupAISeat ( GameObject go, RiderSlot slot )
    {
        Color colour = ColourFor ( slot.colour );

        AIRider rider = go.GetComponent<AIRider> ();

        if ( rider != null )
            ConfigureAIRider ( rider, slot, colour );

        AIConversationController convo = go.GetComponent<AIConversationController> ();

        if ( convo != null )
        {
            convo.behaviour = ( AIConversationController.AIBehaviour ) ( int ) slot.personality;
            convo.thinksHuman = slot.thinksHuman;
            convo.riderColour = slot.colour.ToString ();
            convo.SetupPersonality ();
        }
    }

    private void ConfigureAIRider ( AIRider rider, RiderSlot slot, Color colour )
    {
        rider.playerColour = slot.colour;
        rider.teamColour = colour;
        rider.personality = slot.personality;
        rider.thinksHuman = slot.thinksHuman;

        Debug.Log ( $"Seat colour {slot.colour}, personality {slot.personality}, thinksHuman {slot.thinksHuman}" );

        rider.SetupAIRider ();
    }

    /// <summary>
    /// Rebuilds the bike's rider list from whoever is actually active, so a
    /// three-rider game isn't averaging in eight zero vectors.
    /// </summary>
}
