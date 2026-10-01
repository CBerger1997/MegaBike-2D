using UnityEngine;

/// <summary>
/// Samples where each bike is and what every rider is contributing, at a
/// fixed interval while a round is running.
///
/// This is the data the other logs don't have: whether a bike is parked
/// because nobody is pedalling, because riders are steering against each
/// other, or because it is jammed against the arena edge.
///
/// Put this on any object that lives for the session - the Session Logger
/// object is a sensible home.
/// </summary>
public class MovementSampler : MonoBehaviour
{
    [Tooltip ( "Seconds between samples. 2 gives about 60 rows per rider per round." )]
    [Range ( 0.5f, 10f )] public float sampleSeconds = 2f;

    [Tooltip ( "Turn off to stop writing movement.csv without touching the other logs." )]
    public bool enabled_ = true;

    private float nextSample;

    private void Update ()
    {
        if ( !enabled_ || SessionLogger.instance == null )
            return;

        GameController game = GameController.instance;

        if ( game == null || game.gameState != GameController.GameState.Running )
            return;

        if ( Time.time < nextSample )
            return;

        nextSample = Time.time + sampleSeconds;

        Sample ( game );
    }

    private void Sample ( GameController game )
    {
        for ( int bikeIndex = 0; bikeIndex < game.bikes.Count; bikeIndex++ )
        {
            MegaBikeController bike = game.bikes[ bikeIndex ];

            if ( bike == null )
                continue;

            Vector2 pos = bike.transform.position;
            float speed = bike.CurrentVelocity.magnitude;
            float pedal = bike.currentPedalEffort;

            int aboard = game.RidersOnBike ( bikeIndex );
            string rules = game.RulesForBike ( bikeIndex ).Describe ();

            foreach ( int seat in game.SeatsOnBike ( bikeIndex ) )
            {
                GameObject go = game.RiderObject ( seat );

                if ( go == null )
                    continue;

                RiderInput input = go.GetComponent<RiderInput> ();

                if ( input == null )
                    continue;

                AIRider ai = go.GetComponent<AIRider> ();
                IRider rider = game.GetRider ( seat );

                // Only an AI has a view on whether it is following the rules.
                string complying = ai != null && ai.enabled
                    ? ( ai.Complying ? "1" : "0" )
                    : "";

                SessionLogger.instance.LogMovement (
                    game.roundNumber,
                    Time.timeSinceLevelLoad,
                    bikeIndex,
                    pos,
                    speed,
                    pedal,
                    aboard,
                    bike.leaderSeat,
                    rules,
                    seat,
                    game.DisplayName ( seat ),
                    game.slots[ seat ].personality.ToString (),
                    complying,
                    input.IsSteering,
                    input.steerInput,
                    input.pedalInput,
                    rider != null ? rider.Stamina : 0f
                );
            }
        }
    }
}