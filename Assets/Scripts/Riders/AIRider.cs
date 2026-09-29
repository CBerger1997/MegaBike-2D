using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 2D AI rider for the megabike.
///
/// Writes two separate channels into RiderInput:
///     steerInput - the direction this rider wants the bike to face
///     pedalInput - how hard it is working, 0 to 1
///
/// Steering is free; only pedalling drains stamina. Pedal strength comes from
/// the bike's agreed rules, scaled by this rider's own effort and stamina.
///
/// Parameter names follow Table 1 of the ALife/Megabike paper where practical.
/// NOTE: the paper's "Laziness" is inverted here into <see cref="effort"/>
/// (baseline pedalling strength) because it reads far less confusingly in the
/// inspector. The "work multipliers" are treated as straight multipliers.
/// </summary>
[RequireComponent ( typeof ( RiderInput ) )]
public class AIRider : MonoBehaviour, IRider
{
    public enum Personality { Cooperative, Lazy, Subversive, Opposing }

    private enum Intent { OwnLoot, FollowGroup, Oppose, Sabotage, Wander, Idle }

    [Header ( "References" )]
    public MegaBikeController bike;
    public AIConversationController conversation;   // optional
    public GameObject movementArrow;                // scaled/rotated like the player's
    public SpriteRenderer arrowRenderer;            // optional, tinted to teamColour
    public SpriteRenderer bodyRenderer;             // optional, tinted to teamColour

    [Header ( "Identity" )]
    public Personality personality = Personality.Cooperative;
    public bool applyPersonalityPreset = true;
    public bool syncConversationPersonality = true;
    public int teamNum = 2;                         // player is 1
    public Color teamColour = Color.blue;
    [Tooltip ( "Presents itself as a human player during the vote." )]
    public bool thinksHuman;

    [Header ( "Stamina" )]
    public float maxStamina = 100f;
    public float stamina = 100f;

    public float Stamina => stamina;
    public float MaxStamina => maxStamina;

    public float staminaDrain = 1f;                 // matches PlayerRider's *2
    public float staminaPerLootBox = 10f;
    [Range ( 0f, 1f )] public float hungryThreshold = 0.35f;
    public bool stopWhenExhausted = true;

    [Header ( "Core parameters (paper, Table 1)" )]
    [Range ( 0.5f, 3f )] public float decisionInterval = 1f;
    [Range ( 0f, 1f )] public float determination = 0.3f;   // chance to keep the last decision
    [Range ( 0f, 1f )] public float selfishness = 0.5f;   // chance to chase own-colour loot
    [Range ( 0f, 1f )] public float agreeableness = 0.5f;   // chance to follow the group
    [Range ( 0f, 1f )] public float effort = 0.6f;   // baseline pedal strength

    [Header ( "Meta parameters" )]
    public float valueLerpSpeed = 5f;               // steering smoothing
    [Range ( 0f, 2f )] public float hungerEffortMult = 0.3f;   // effort multiplier at zero stamina
    [Range ( 0f, 3f )] public float hungerSelfishnessMult = 1.5f;   // selfishness multiplier at zero stamina
    public float selfishWorkMult = 1.5f;            // extra power when acting selfishly
    public float agreeableWorkMult = 1.2f;          // extra power when aligned with the group
    [Range ( 0f, 1f )] public float volatility = 0.15f;  // chance of a purely random decision
    [Range ( 0f, 1f )] public float sabotageChance = 0.6f;   // Disruptive only

    [Header ( "Threat avoidance" )]
    public bool avoidThreats = true;
    [Tooltip ( "How close the car's line has to pass before the rider panics." )]
    public float threatDangerRadius = 6f;
    [Tooltip ( "Only react to cars arriving within this many seconds." )]
    public float threatReactionTime = 3f;
    [Tooltip ( "Effort used while evading. Overrides laziness - panic is not lazy." )]
    [Range ( 0f, 1f )] public float evadeEffort = 1f;
    [Tooltip ( "Chance an Opposing rider steers INTO the car instead of away." )]
    [Range ( 0f, 1f )] public float threatSabotageChance = 0.5f;

    [Header ( "Arena containment" )]
    public bool stayInArena = true;
    [Tooltip ( "Fraction of the arena radius before the rider starts steering back. 0.75 = last quarter." )]
    [Range ( 0.3f, 0.95f )] public float softBoundaryFraction = 0.75f;
    [Tooltip ( "Effort used turning back. Kept high so a lazy rider still returns." )]
    [Range ( 0f, 1f )] public float returnEffort = 0.7f;

    [Header ( "Rule compliance" )]
    [Tooltip ( "Chance a Subversive rider decides to follow the rules, re-rolled each decision." )]
    [Range ( 0f, 1f )] public float subversiveComplyChance = 0.5f;

    [Tooltip ( "Effort a defying rider puts in, as a fraction of what the rules demand." )]
    [Range ( 0f, 1f )] public float defianceEffortMult = 0.25f;

    [Tooltip ( "Extra effort a rider puts in when they agree with the rules." )]
    [Range ( 1f, 2f )] public float complianceEffortBonus = 1f;

    [Tooltip ( "Lazy riders under-pedal even when they agree with the rules." )]
    [Range ( 0f, 1f )] public float lazyEffortMult = 0.4f;

    /// <summary>
    /// Whether this rider is currently going along with the bike's agreed
    /// rules. Re-rolled on the decision interval, so a Subversive rider drifts
    /// in and out rather than flickering frame to frame.
    /// </summary>
    public bool Complying { get; private set; } = true;

    [Header ( "Targeting" )]
    public GameObject groupTarget;                  // set by TargetManager / player selection
    public CollectibleBox ownTarget;                // nearest own-colour box
    public float retargetInterval = 0.25f;
    public bool autoRegisterWithBike = true;

    private RiderInput riderInput;
    private Intent intent = Intent.FollowGroup;
    private Vector2 smoothedInput;
    private Vector2 wanderDirection = Vector2.up;
    private float decisionTimer;
    private float retargetTimer;

    // ---------- IRider ----------
    public int TeamNum => teamNum;

    public Color TeamColour => teamColour;

    public bool IsExhausted => stamina <= 0f;

    private Vector2 BikePosition =>
        bike != null ? ( Vector2 ) bike.transform.position : ( Vector2 ) transform.position;

    private bool GameRunning =>
        GameController.instance == null ||
        GameController.instance.gameState == GameController.GameState.Running;

    public GameController.PlayerColour playerColour
    {
        get; set;
    }

    private void Awake ()
    {
        riderInput = GetComponent<RiderInput> ();

        if ( bike == null )
            bike = GetComponentInParent<MegaBikeController> ();

        if ( conversation == null )
            conversation = GetComponent<AIConversationController> ();

        // Do this in Awake so AIConversationController.Start() picks up the
        // right persona when it builds its system prompt.
        if ( conversation != null && syncConversationPersonality )
            conversation.behaviour = ( AIConversationController.AIBehaviour ) ( int ) personality;
    }

    public void SetupAIRider ()
    {
        if ( applyPersonalityPreset )
            ApplyPersonalityPreset ( personality );

        ApplyColour ();

        if ( autoRegisterWithBike && bike != null && !bike.riders.Contains ( riderInput ) )
            bike.riders.Add ( riderInput );

        decisionTimer = Random.Range ( 0f, decisionInterval );
        wanderDirection = Random.insideUnitCircle.normalized;
    }

    private void Update ()
    {
        float dt = Time.deltaTime;

        if ( !GameRunning || ( stopWhenExhausted && IsExhausted ) )
        {
            smoothedInput = Vector2.MoveTowards ( smoothedInput, Vector2.zero, dt * 2f );

            Vector2 coasting = Vector2.ClampMagnitude ( smoothedInput, 1f );

            // Direction and effort are separate channels.
            riderInput.steerInput = coasting.sqrMagnitude > 0.0001f ? coasting.normalized : Vector2.zero;
            riderInput.pedalInput = RuleEffort () * coasting.magnitude;

            UpdateArrow ();
            return;
        }

        decisionTimer -= dt;

        if ( decisionTimer <= 0f )
        {
            ChooseIntent ();
            decisionTimer = decisionInterval * Random.Range ( 0.75f, 1.25f );
        }

        retargetTimer -= dt;

        if ( retargetTimer <= 0f || ownTarget == null )
        {
            // The bike may have agreed to chase one colour rather than each
            // rider chasing their own.
            GameController.PlayerColour wanted = playerColour;

            // Only a rider going along with the rules chases the agreed
            // colour. Everyone else looks after themselves.
            if ( Complying && bike != null && !bike.Rules.targetOwnColour )
                wanted = bike.Rules.targetColour;

            ownTarget = CollectibleBox.FindNearestForTeam ( wanted, BikePosition );

            // Ignore boxes sitting outside where the bike can actually go.
            if ( ownTarget != null &&
                 !Arena.Contains ( ownTarget.transform.position, 2f ) )
            {
                ownTarget = null;
            }

            retargetTimer = retargetInterval;
        }

        Vector2 desired = BuildDesiredInput ();

        // Frame-rate independent smoothing.
        smoothedInput = Vector2.Lerp (
            smoothedInput,
            desired,
            1f - Mathf.Exp ( -valueLerpSpeed * dt )
        );

        Vector2 clamped = Vector2.ClampMagnitude ( smoothedInput, 1f );

        // Direction and effort are now separate channels.
        riderInput.steerInput = clamped.sqrMagnitude > 0.0001f ? clamped.normalized : Vector2.zero;
        riderInput.pedalInput = RuleEffort () * clamped.magnitude;

        DrainStamina ( dt );
        UpdateArrow ();
    }

    // ------------------------------------------------------------------
    // Decision making
    // ------------------------------------------------------------------

    private void ChooseIntent ()
    {
        // Determination: often just stick with whatever we were already doing.
        if ( Random.value < determination )
            return;

        // Volatility: replace the decision with a true random one.
        if ( Random.value < volatility )
        {
            Vector2 random = Random.insideUnitCircle.normalized;

            // Bias away from the edge so wandering doesn't walk us out.
            Vector2 pressure = Arena.OutwardPressure ( BikePosition, softBoundaryFraction );

            if ( pressure.sqrMagnitude > 0.0001f )
                random = ( random - pressure.normalized * pressure.magnitude * 2f ).normalized;

            wanderDirection = random;
            intent = Intent.Wander;
            return;
        }

        RollCompliance ();

        float s = EffectiveSelfishness ();
        float roll = Random.value;

        switch ( personality )
        {
            case Personality.Cooperative:

                // Chases own loot only when hungry or when nothing else is going on.
                intent = roll < agreeableness ? Intent.FollowGroup
                       : roll < agreeableness + s * ( 1f - agreeableness ) ? Intent.OwnLoot
                       : Intent.FollowGroup;
                break;

            case Personality.Lazy:

                intent = roll < 0.45f ? Intent.Idle
                       : roll < 0.45f + agreeableness * 0.5f ? Intent.FollowGroup
                       : Intent.OwnLoot;
                break;

            case Personality.Subversive:

                if ( Random.value <= sabotageChance )
                {
                    intent = Intent.Sabotage;
                    break;
                }

                intent = roll < s ? Intent.OwnLoot
                       : roll < s + agreeableness * ( 1f - s ) ? Intent.FollowGroup
                       : Intent.Wander;
                break;

            case Personality.Opposing:

                intent = roll < s ? Intent.OwnLoot
                       : roll < s + agreeableness * ( 1f - s ) ? Intent.FollowGroup
                       : Intent.Oppose;
                break;
        }

        // Nothing of our colour left to chase - fall back rather than stalling.
        if ( intent == Intent.OwnLoot && ownTarget == null )
            intent = Intent.FollowGroup;
    }

    /// <summary>
    /// Decides whether this rider is going along with the rules for the next
    /// stretch. Cooperative always does, Opposing never does, Subversive is a
    /// coin flip, and Lazy agrees but barely works either way.
    /// </summary>
    private void RollCompliance ()
    {
        switch ( personality )
        {
            case Personality.Cooperative:
                Complying = true;
                break;

            case Personality.Lazy:
                Complying = true;
                break;

            case Personality.Subversive:
                Complying = Random.value < subversiveComplyChance;
                break;

            case Personality.Opposing:
                Complying = false;
                break;
        }
    }

    /// <summary>
    /// True when this rider is being steered by someone else's decision - a
    /// leader holding the handlebars. Their only way to protest is the pedals.
    /// </summary>
    private int cachedSeat = -1;

    /// <summary>
    /// This rider's seat in the roster. Found by matching the GameObject,
    /// because colours are shared and can't identify a rider any more.
    /// </summary>
    public int SeatIndex
    {
        get
        {
            if ( cachedSeat >= 0 )
                return cachedSeat;

            GameController game = GameController.instance;

            if ( game == null )
                return -1;

            for ( int i = 0; i < game.slots.Count; i++ )
            {
                if ( game.RiderObject ( i ) == gameObject )
                {
                    cachedSeat = i;
                    return cachedSeat;
                }
            }

            return -1;
        }
    }

    private bool UnderLeader =>
        bike != null && bike.leaderControlsSteering &&
        bike.leaderSeat >= 0 && bike.leaderSeat != SeatIndex;

    private Vector2 BuildDesiredInput ()
    {
        // Evasion overrides every other intent - a rider about to be hit stops
        // caring about lootboxes.
        if ( avoidThreats )
        {
            Vector2 evade = GetEvadeInput ();

            if ( evade != Vector2.zero )
                return evade;
        }

        Vector2 dir;
        float power = EffectiveEffort ();

        switch ( intent )
        {
            case Intent.OwnLoot:

                if ( ownTarget == null )
                    goto case Intent.FollowGroup;

                dir = ( ( Vector2 ) ownTarget.transform.position - BikePosition ).normalized;
                power *= selfishWorkMult;
                break;

            case Intent.FollowGroup:

                dir = GroupDirection ();
                power *= agreeableWorkMult;
                break;

            case Intent.Oppose:

                dir = -GroupDirection ();
                break;

            case Intent.Sabotage:

                // Push sideways rather than straight back - it drags the bike off
                // course without fully cancelling out in the averaging.
                Vector2 g = GroupDirection ();
                dir = new Vector2 ( -g.y, g.x );
                power *= 0.8f;
                break;

            case Intent.Wander:

                dir = wanderDirection;
                power *= 0.5f;
                break;

            default: // Idle

                return Vector2.zero;
        }
        if ( dir.sqrMagnitude < 0.0001f )
            return ApplyContainment ( Vector2.zero );

        return ApplyContainment ( dir.normalized * Mathf.Clamp01 ( power ) );
    }

    /// <summary>
    /// Blends the rider's intent toward the centre of the map as it approaches
    /// the edge. At the very boundary the intent is fully overridden, so even an
    /// Opposing rider stops pulling outward.
    /// </summary>
    private Vector2 ApplyContainment ( Vector2 desired )
    {
        if ( !stayInArena )
            return desired;

        Vector2 pressure = Arena.OutwardPressure ( BikePosition, softBoundaryFraction );

        if ( pressure.sqrMagnitude < 0.0001f )
            return desired;

        // Point back toward the middle, proportional to how far out we are.
        Vector2 inward = -pressure.normalized;
        float t = Mathf.Clamp01 ( pressure.magnitude );

        float power = Mathf.Max ( desired.magnitude, returnEffort );

        return Vector2.Lerp ( desired, inward * power, t );
    }

    /// <summary>
    /// Returns a move vector if a car is about to cross the bike's position,
    /// otherwise Vector2.zero. Pushes perpendicular to the car's line, which is
    /// the shortest way out of the road.
    /// </summary>
    private Vector2 GetEvadeInput ()
    {
        OwdiController threat = FindIncomingThreat ();

        if ( threat == null )
            return Vector2.zero;

        Vector2 carHeading = threat.Heading;

        // Perpendicular to the car's path, on whichever side we're already on.
        Vector2 sidestep = new Vector2 ( -carHeading.y, carHeading.x );

        Vector2 toBike = BikePosition - ( Vector2 ) threat.transform.position;

        if ( Vector2.Dot ( sidestep, toBike ) < 0f )
            sidestep = -sidestep;

        // Opposing riders sometimes steer into the car on purpose.
        if ( personality == Personality.Opposing &&
             Random.value < threatSabotageChance * Time.deltaTime * 10f )
        {
            sidestep = -sidestep;
        }

        float power = personality == Personality.Lazy
            ? evadeEffort * 0.6f
            : evadeEffort;

        return sidestep.normalized * Mathf.Clamp01 ( power );
    }

    private OwdiController FindIncomingThreat ()
    {
        OwdiController best = null;
        float bestTime = float.MaxValue;

        Vector2 here = BikePosition;

        for ( int i = 0; i < OwdiController.All.Count; i++ )
        {
            OwdiController car = OwdiController.All[ i ];

            if ( car == null )
                continue;

            // Are we standing in the road?
            if ( car.DistanceFromPath ( here ) > threatDangerRadius )
                continue;

            // Is it arriving soon enough to matter?
            float eta = car.TimeToReach ( here );

            if ( eta > threatReactionTime )
                continue;

            if ( eta < bestTime )
            {
                bestTime = eta;
                best = car;
            }
        }

        return best;
    }

    private Vector2 GroupDirection ()
    {
        if ( groupTarget != null )
            return ( ( Vector2 ) groupTarget.transform.position - BikePosition ).normalized;

        Vector2 group = bike != null ? bike.GetSteerDirection ( riderInput ) : Vector2.zero;

        if ( group.sqrMagnitude > 0.0001f )
            return group.normalized;

        return smoothedInput.sqrMagnitude > 0.0001f
            ? smoothedInput.normalized
            : wanderDirection;
    }

    // ------------------------------------------------------------------
    // Stamina
    // ------------------------------------------------------------------

    private float StaminaNormalised => Mathf.Clamp01 ( stamina / maxStamina );

    /// <summary>
    /// Pedal strength called for by the bike's agreed rules, scaled by how
    /// tired this rider is. Personality-based defiance layers on top of this.
    /// </summary>
    /// <summary>
    /// How hard this rider actually pedals. The agreed effort is the baseline;
    /// a rider who rejects the rules works far less, and under a leader that
    /// is the only protest available to them, since they cannot steer.
    /// </summary>
    private float RuleEffort ()
    {
        float agreed = bike != null ? bike.Rules.EffortValue : 0.65f;

        // EffectiveEffort is this rider's own baseline after hunger; dividing
        // by the baseline leaves just the stamina factor.
        float staminaFactor = effort > 0.01f ? EffectiveEffort () / effort : 1f;

        float willingness = Complying ? complianceEffortBonus : defianceEffortMult;

        // Refusing to pedal is a louder protest when there's a leader to
        // protest against.
        if ( !Complying && UnderLeader )
            willingness *= 0.6f;

        if ( personality == Personality.Lazy )
            willingness *= lazyEffortMult;

        return Mathf.Clamp01 ( agreed * staminaFactor * willingness );
    }

    /// <summary>Low stamina scales effort down (paper: hunger laziness).</summary>
    private float EffectiveEffort ()
    {
        float t = Mathf.InverseLerp ( 0f, Mathf.Max ( 0.01f, hungryThreshold ), StaminaNormalised );
        return effort * Mathf.Lerp ( hungerEffortMult, 1f, t );
    }

    /// <summary>Low stamina makes the rider greedier (paper: hunger selfishness).</summary>
    private float EffectiveSelfishness ()
    {
        float t = Mathf.InverseLerp ( 0f, Mathf.Max ( 0.01f, hungryThreshold ), StaminaNormalised );
        return Mathf.Clamp01 ( Mathf.Lerp ( selfishness * hungerSelfishnessMult, selfishness, t ) );
    }

    private void DrainStamina ( float dt )
    {
        // Steering costs nothing - only pedal effort tires a rider.
        float pedal = riderInput.pedalInput;

        if ( pedal > 0.01f )
            stamina = Mathf.Clamp ( stamina - dt * pedal * staminaDrain, 0f, maxStamina );
    }

    public void ReceiveLoot ( CollectibleBox box )
    {
        stamina = Mathf.Clamp ( stamina + staminaPerLootBox, 0f, maxStamina );

        if ( box != null && ownTarget == box )
            ownTarget = null;

        retargetTimer = 0f;
    }

    // ------------------------------------------------------------------
    // Presentation
    // ------------------------------------------------------------------

    private void UpdateArrow ()
    {
        if ( movementArrow == null )
            return;

        Vector2 steer = riderInput.steerInput;
        float pedal = riderInput.pedalInput;

        float length = Mathf.Clamp ( pedal * 2f, 0f, 2f );

        // A short stub while steering without pedalling, so it stays visible.
        if ( steer.sqrMagnitude > 0.0001f && length < 0.35f )
            length = 0.35f;

        movementArrow.transform.localScale = new Vector3 ( 1f, length, 1f );

        if ( steer.sqrMagnitude > 0.0001f )
        {
            float angle = ( Mathf.Atan2 ( steer.y, steer.x ) * Mathf.Rad2Deg ) - 90f;
            movementArrow.transform.rotation = Quaternion.Euler ( 0f, 0f, angle );
        }
    }

    private void ApplyColour ()
    {
        if ( arrowRenderer != null )
            arrowRenderer.color = teamColour;
        if ( bodyRenderer != null )
            bodyRenderer.color = teamColour;
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>Kept for compatibility with TargetManager - sets the shared/group target.</summary>
    public void UpdateTarget ( GameObject newTarget )
    {
        groupTarget = newTarget;
    }

    public void SetPersonality ( Personality newPersonality, bool applyPreset = true )
    {
        personality = newPersonality;

        if ( applyPreset )
            ApplyPersonalityPreset ( personality );

        if ( conversation != null && syncConversationPersonality )
            conversation.behaviour = ( AIConversationController.AIBehaviour ) ( int ) personality;
    }

    /// <summary>Route a line of dialogue to this rider's LLM agent.</summary>
    public void Say ( string playerMessage )
    {
        if ( conversation != null )
            conversation.SendPlayerMessage ( playerMessage );
    }

    public void ApplyPersonalityPreset ( Personality p )
    {
        switch ( p )
        {
            case Personality.Cooperative:
                decisionInterval = 1.0f;
                determination = 0.50f;
                selfishness = 0.25f;
                agreeableness = 0.85f;
                effort = 0.80f;
                volatility = 0.05f;
                sabotageChance = 0.00f;
                hungerEffortMult = 0.40f;
                break;

            case Personality.Lazy:
                decisionInterval = 2.0f;
                determination = 0.70f;
                selfishness = 0.40f;
                agreeableness = 0.30f;
                effort = 0.20f;
                volatility = 0.10f;
                sabotageChance = 0.00f;
                hungerEffortMult = 0.20f;
                break;

            case Personality.Subversive:
                decisionInterval = 0.7f;
                determination = 0.20f;
                selfishness = 0.60f;
                agreeableness = 0.40f;
                effort = 0.70f;
                volatility = 0.50f;
                sabotageChance = 0.60f;
                hungerEffortMult = 0.30f;
                break;

            case Personality.Opposing:
                decisionInterval = 1.2f;
                determination = 0.60f;
                selfishness = 0.80f;
                agreeableness = 0.10f;
                effort = 0.80f;
                volatility = 0.20f;
                sabotageChance = 0.30f;
                hungerEffortMult = 0.30f;
                break;
        }
    }
}