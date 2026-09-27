using System;
using System.Collections.Generic;

// Every stage of the day. Each one gets its own ambient audio (in this order).
public enum Location
{
    Home,
    OutsideHome,
    CommuteToWork,
    OutsideWork,
    Work,
    CommuteHome
}

// Every background image. Assign a sprite for each in EventManager > Backgrounds.
// Stages pick a default; any event can override it with .Scene(...).
public enum Backdrop
{
    Home,
    OutsideHome,
    CommuteToWork,
    OutsideWork,
    Work,
    CommuteHome,
    OutsideHomeEvening,
    OutsideWorkEvening,
    BusDay,
    BusEvening,
    CarEvening,
    MeetingRoom,
    Warehouse,
    JailCell,
    HomeEvening     // added last so existing Inspector entries don't shift
}

// Everyone who can talk. None = narration, Player = you.
// Always add new characters at the END so Inspector assignments don't shift.
public enum CharacterId
{
    None,
    Player,
    Neighbor,
    Boss,
    Coworker,
    Barista,
    BusDriver,
    Vagrant,
    Petitioner,
    Thug,
    Dog
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
    // Lines that come out empty ("") are skipped automatically.
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
//       .Group("WorkMorning")                      // one random event per group per stage
//       .Once()                                    // only ever happens once per week
//       .Scene(Backdrop.MeetingRoom)               // override the stage's background
//       .With(Boss)                                // portrait shown from the start
//       .Narrate("Your boss is waiting.")
//       .Say(Boss, "Rough morning?", Annoyed)
//       .Choice("Apologize", () => { ... })
//           .Reply(Boss, "Fine. Don't make a habit of it.")
public class GameEvent
{
    public readonly string id;
    public readonly List<DialogueLine> lines = new List<DialogueLine>();
    public readonly List<EventChoice> choices = new List<EventChoice>();
    public CharacterId presentCharacter = CharacterId.None;
    public Expression presentExpression = Expression.Neutral;

    public string group;          // events sharing a group: one is picked at random
    public float weight = 1f;     // higher = more likely to be picked from its group
    public bool once;             // never shows again after the first time this week

    private readonly List<Func<bool>> conditions = new List<Func<bool>>();
    private Func<Backdrop> backdrop;

    public GameEvent(string id)
    {
        this.id = id;
    }

    // Shortcut for events that start with a line of narration.
    public GameEvent(string id, string text) : this(id) { Narrate(text); }
    public GameEvent(string id, Func<string> text) : this(id) { Narrate(text); }

    public string SeenFlag => "Seen_" + id;

    // ---------- Conditions & variety ----------

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

    // Random chance (0-1) that this event is allowed to happen when reached.
    public GameEvent Chance(float probability)
    {
        return When(() => UnityEngine.Random.value < probability);
    }

    // Put this event in a random pool. When the day reaches the first event of a group,
    // ONE eligible event from that group (in the same stage) is picked by weight.
    public GameEvent Group(string name, float weight = 1f)
    {
        group = name;
        this.weight = weight;
        return this;
    }

    // Only happens once per week.
    public GameEvent Once()
    {
        once = true;
        return this;
    }

    public bool CanShow()
    {
        if (once && GameState.Instance.HasPermanentFlag(SeenFlag)) return false;

        foreach (var condition in conditions)
        {
            if (!condition()) return false;
        }
        return true;
    }

    // ---------- Background ----------

    public GameEvent Scene(Backdrop scene)
    {
        backdrop = () => scene;
        return this;
    }

    // Background decided when the event is shown, e.g. bus vs car.
    public GameEvent Scene(Func<Backdrop> scene)
    {
        backdrop = scene;
        return this;
    }

    // Null = use the stage's background.
    public Backdrop? GetBackdrop()
    {
        return backdrop != null ? backdrop() : (Backdrop?)null;
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
    public readonly Backdrop backdrop;
    public readonly List<GameEvent> events;

    public DayStage(Location location, params GameEvent[] events)
        : this(location, DefaultBackdrop(location), events) { }

    public DayStage(Location location, Backdrop backdrop, params GameEvent[] events)
    {
        this.location = location;
        this.backdrop = backdrop;
        this.events = new List<GameEvent>(events);
    }

    private static Backdrop DefaultBackdrop(Location location)
    {
        switch (location)
        {
            case Location.Home: return Backdrop.Home;
            case Location.OutsideHome: return Backdrop.OutsideHome;
            case Location.CommuteToWork: return Backdrop.CommuteToWork;
            case Location.OutsideWork: return Backdrop.OutsideWork;
            case Location.Work: return Backdrop.Work;
            default: return Backdrop.CommuteHome;
        }
    }
}