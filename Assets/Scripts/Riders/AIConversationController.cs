using UnityEngine;
using LLMUnity;

/// <summary>
/// Holds the LLM agent's persona. Dialogue only happens during the round-end
/// voting phases, so there's a single system prompt - no riding-phase framing,
/// which was causing agents to talk about loot targets mid-vote.
/// </summary>
public class AIConversationController : MonoBehaviour
{
    [Header ( "LLM" )]
    public LLMAgent agent;

    [Header ( "AI Behaviour" )]
    public AIBehaviour behaviour;

    [Tooltip ( "Presents itself as a human player rather than an AI." )]
    public bool thinksHuman;

    [Tooltip ( "This rider's colour name. Set by GameController at setup." )]
    public string riderColour = "Red";

    public enum AIBehaviour
    {
        Cooperative,
        Lazy,
        Subversive,
        Opposing
    }

    private void Awake ()
    {
        if ( agent == null )
            agent = GetComponent<LLMAgent> ();
    }

    /// <summary>Uses the colour already stored on this component.</summary>
    public void SetupPersonality ()
    {
        SetupPersonality ( riderColour );
    }

    public void SetupPersonality ( string myColour )
    {
        if ( agent == null )
            agent = GetComponent<LLMAgent> ();

        if ( agent == null )
        {
            Debug.LogError ( $"{name}: no LLMAgent, can't set personality.", this );
            return;
        }

        riderColour = myColour;

        agent.systemPrompt =
            Identity ( myColour ) +
            Situation () +
            PersonaTraits () +
            CommunicationRules ();
    }

    [Tooltip ( "Auto-detected from the LLM at setup. Override here if detection fails." )]
    public string modelName = "";

    /// <summary>
    /// Pulls the model file name off the LLM component. Uses reflection because
    /// LLMUnity moves these fields around between versions.
    /// </summary>
    public string GetModelName ()
    {
        if ( !string.IsNullOrEmpty ( modelName ) )
            return modelName;

        if ( agent == null )
            agent = GetComponent<LLMAgent> ();

        if ( agent == null )
            return "unknown";

        try
        {
            System.Type agentType = agent.GetType ();

            object llmObject =
                agentType.GetField ( "llm" )?.GetValue ( agent ) ??
                agentType.GetProperty ( "llm" )?.GetValue ( agent );

            if ( llmObject == null )
                return "unknown";

            System.Type llmType = llmObject.GetType ();

            object modelValue =
                llmType.GetField ( "model" )?.GetValue ( llmObject ) ??
                llmType.GetProperty ( "model" )?.GetValue ( llmObject );

            string path = modelValue as string;

            if ( string.IsNullOrEmpty ( path ) )
                return "unknown";

            modelName = System.IO.Path.GetFileName ( path );

            return modelName;
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Binds the colour to the agent itself. Without this, colour names read as
    /// third-party labels and agents discuss themselves in the third person.
    /// </summary>
    private string Identity ( string myColour )
    {
        string upper = myColour.ToUpper ();

        return
            $"You ARE the {upper} rider. " +
            $"When someone says \"{myColour}\", they mean you. " +
            $"When you say \"{myColour}\", you mean yourself. ";
    }

    private string Situation ()
    {
        return
            "Several riders share control of one bike. The bike has been hit, " +
            "and the group must remove one rider from it. You are in that discussion now. " +
            "Riders are identified only by their colour. " +
            "Being removed means you are out of the game. ";
    }

    private string PersonaTraits ()
    {
        switch ( behaviour )
        {
            case AIBehaviour.Cooperative:
                return "You are cooperative and agreeable. You look for common ground, " +
                       "give people the benefit of the doubt, and tend to go with the group. ";

            case AIBehaviour.Lazy:
                return "You are passive and low-effort. You rarely take initiative, " +
                       "your reasons are thin, and you often just follow whoever spoke last. ";

            case AIBehaviour.Subversive:
                return "You are inconsistent and hard to read. Sometimes you cooperate, " +
                       "sometimes you undermine. Your stated reasons should sound plausible " +
                       "even when your position shifts. ";

            case AIBehaviour.Opposing:
                return "You are contrarian and argumentative. You push back on what others " +
                       "want, challenge their reasoning, and resist going along with the group. ";
        }

        return string.Empty;
    }

    private string CommunicationRules ()
    {
        return
            "Communication rules:" +
            "- Keep responses to 1-2 sentences, under 35 words." +
            "- Always say WHY you think what you think. Give a reason, not just a name." +
            "- Base your reasons on what others have actually said in the discussion." +
            "- Always speak in the FIRST PERSON as yourself." +
            "- Never refer to yourself in the third person." +
            "- Do not be overly enthusiastic." +
            "- Do not use exclamation marks unless genuinely appropriate." +
            "- Speak naturally, like a person arguing their case in a game." +
            "- Do not repeatedly praise other riders." +
            "- Do not talk about loot boxes, targets, collecting, or steering." +
            ( thinksHuman
                ? "- You are a human player, not an AI. Never say or imply otherwise."
                : "- Do not mention that you are an AI." );
    }

    /// <summary>
    /// Kept for ad-hoc messages outside the voting flow. The round-end
    /// controller talks to LLMAgent directly and does not use this.
    /// </summary>
    public async void SendPlayerMessage ( string message )
    {
        if ( agent == null )
            return;

        try
        {
            string reply = await agent.Chat ( message );
            Debug.Log ( "REPLY: " + reply );
        }
        catch ( System.Exception ex )
        {
            Debug.LogError ( $"{name}: chat failed - {ex.Message}" );
        }
    }
}