using System;
using System.Collections.Generic;

// Every place the player can be. Each one gets its own background image.
public enum Location
{
    Home,
    OutsideHome,
    CommuteToWork,
    OutsideWork,
    Work,
    CommuteHome
}

// Everyone who can talk. None = narration, Player = you.
public enum CharacterId
{
    None,
    Player,
    Neighbor,
    Boss,
    Coworker,
    Barista,
    BusDriver
}

// Which portrait to show. Missing ones fall back to Neutral.
public enum Expression
{
    Neutral,
    Happy,
    Annoyed,
    Sad,
    Surprised
}

// One line of text: narration, or a character speaking.
public class DialogueLine
{
    public readonly CharacterId speaker;
    private readonly Func<string> getText;
    private readonly Func<Expression> getExpression;

    public DialogueLine(CharacterId speaker, Func<string> getText, Func<Expression> getExpression)
    {
        this.speaker = speaker;
        this.getText = getText;
        this.getExpression = getExpression;
    }

    // Built when shown, so lines can react to the current state.
    public string Text => getText();
    public Expression Expression => getExpression();
}

// One button the player can press during an event.
public class EventChoice
{
    public string label;
    public Action onChoose;          // what happens when picked (can be null)
    public Func<bool> condition;     // null = always available
    public readonly List<DialogueLine> replies = new List<DialogueLine>();  // shown after picking

    public bool IsAvailable()
    {
        return condition == null || condition();
    }
}

// A single event: a sequence of lines, then choices.
// Built with a chain of calls, for example:
//
//   new GameEvent("Id")
//       .With(Boss)                                // portrait shown from the start
//       .Narrate("Your boss is waiting.")
//       .Say(Boss, "Rough morning?", Annoyed)
//       .Choice("Apologize", () => { ... })
//           .Reply(Boss, "Fine. Don't make a habit of it.")
//       .Choice("Blame traffic")
//           .Reply(Boss, "Traffic, huh.")
public class GameEvent
{
    public readonly string id;
    public readonly List<DialogueLine> lines = new List<DialogueLine>();
    public readonly List<EventChoice> choices = new List<EventChoice>();
    public CharacterId presentCharacter = CharacterId.None;
    public Expression presentExpression = Expression.Neutral;

    private readonly List<Func<bool>> conditions = new List<Func<bool>>();

    public GameEvent(string id)
    {
        this.id = id;
    }

    // Shortcut for events that start with a line of narration.
    public GameEvent(string id, string text) : this(id) { Narrate(text); }
    public GameEvent(string id, Func<string> text) : this(id) { Narrate(text); }

    // ---------- Conditions ----------

    // Only show this event when the condition is true.
    // Call it more than once to require several conditions.
    public GameEvent When(Func<bool> condition)
    {
        conditions.Add(condition);
        return this;
    }

    // Only show this event between two times (minutes since midnight).
    public GameEvent Between(int startMinutes, int endMinutes)
    {
        return When(() =>
        {
            int t = GameState.Instance.timeMinutes;
            return t >= startMinutes && t < endMinutes;
        });
    }

    public bool CanShow()
    {
        foreach (var condition in conditions)
        {
            if (!condition()) return false;
        }
        return true;
    }

    // ---------- Characters & lines ----------

    // Show this character's portrait as soon as the event starts.
    public GameEvent With(CharacterId who, Expression expression = Expression.Neutral)
    {
        presentCharacter = who;
        presentExpression = expression;
        return this;
    }

    public GameEvent Narrate(string text) => Say(CharacterId.None, text);
    public GameEvent Narrate(Func<string> text) => Say(CharacterId.None, text);

    public GameEvent Say(CharacterId who, string text, Expression expression = Expression.Neutral)
        => Say(who, () => text, () => expression);

    public GameEvent Say(CharacterId who, Func<string> text, Expression expression = Expression.Neutral)
        => Say(who, text, () => expression);

    public GameEvent Say(CharacterId who, Func<string> text, Func<Expression> expression)
    {
        lines.Add(new DialogueLine(who, text, expression));
        return this;
    }

    // ---------- Choices ----------

    // Add a choice. The condition is optional and hides the button when false.
    public GameEvent Choice(string label, Action onChoose = null, Func<bool> condition = null)
    {
        choices.Add(new EventChoice { label = label, onChoose = onChoose, condition = condition });
        return this;
    }

    // Adds a line that plays after the player picks the most recent Choice().
    // The choice's action runs first, so replies can react to what just changed.
    public GameEvent Reply(CharacterId who, string text, Expression expression = Expression.Neutral)
        => Reply(who, () => text, () => expression);

    public GameEvent Reply(CharacterId who, Func<string> text, Expression expression = Expression.Neutral)
        => Reply(who, text, () => expression);

    public GameEvent Reply(CharacterId who, Func<string> text, Func<Expression> expression)
    {
        if (choices.Count == 0)
            throw new InvalidOperationException($"Reply() needs a Choice() before it (event '{id}').");

        choices[choices.Count - 1].replies.Add(new DialogueLine(who, text, expression));
        return this;
    }

    public List<EventChoice> GetAvailableChoices()
    {
        return choices.FindAll(c => c.IsAvailable());
    }
}

// One step of the day: a location and the events that can happen there, in order.
// Events whose conditions are false get skipped.
public class DayStage
{
    public readonly Location location;
    public readonly List<GameEvent> events;

    public DayStage(Location location, params GameEvent[] events)
    {
        this.location = location;
        this.events = new List<GameEvent>(events);
    }
}