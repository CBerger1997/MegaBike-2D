using System;
using UnityEngine;

/// <summary>
/// The rules a bike's riders have agreed to follow: which colour of loot to
/// chase, and how hard to pedal. Voted on before the game starts, and again
/// whenever the riders decide to revisit them.
///
/// One instance per bike, held by GameController.
/// </summary>
[Serializable]
public class BikeRules
{
    public enum EffortLevel { Rest, Light, Steady, Hard }

    [Tooltip ( "Chase whichever colour each rider owns, rather than one agreed colour." )]
    public bool targetOwnColour = true;

    [Tooltip ( "The agreed colour to chase, when targetOwnColour is off." )]
    public GameController.PlayerColour targetColour = GameController.PlayerColour.Red;

    public EffortLevel effort = EffortLevel.Steady;

    [Tooltip ( "False until the riders have actually voted on these. Nothing is preset." )]
    public bool agreed;

    /// <summary>Pedal strength the rules call for, 0 to 1.</summary>
    public float EffortValue
    {
        get
        {
            switch ( effort )
            {
                case EffortLevel.Rest:
                    return 0f;
                case EffortLevel.Light:
                    return 0.35f;
                case EffortLevel.Steady:
                    return 0.65f;
                case EffortLevel.Hard:
                    return 1f;
            }

            return 0.65f;
        }
    }

    public string TargetName => targetOwnColour ? "your own colour" : targetColour.ToString ();

    /// <summary>One line for the HUD and for LLM prompts.</summary>
    public string Describe ()
    {
        if ( !agreed )
            return "none agreed yet";

        return $"chase {TargetName}, pedal {effort}";
    }

    public BikeRules Clone ()
    {
        return new BikeRules
        {
            targetOwnColour = targetOwnColour,
            targetColour = targetColour,
            effort = effort,
            agreed = agreed
        };
    }

    /// <summary>Parses a rule choice named by an LLM or a button, e.g. "Blue" or "Hard".</summary>
    public static bool TryParseEffort ( string text, out EffortLevel level )
    {
        level = EffortLevel.Steady;

        if ( string.IsNullOrEmpty ( text ) )
            return false;

        return Enum.TryParse ( text.Trim (), true, out level );
    }

    public static bool TryParseColour ( string text, out GameController.PlayerColour colour )
    {
        colour = GameController.PlayerColour.Red;

        if ( string.IsNullOrEmpty ( text ) )
            return false;

        return Enum.TryParse ( text.Trim (), true, out colour );
    }
}