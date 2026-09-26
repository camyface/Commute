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
    CommuteHome,
    CoffeeShop     // add new locations at the end; the scene stores these as numbers
}

// One button the player can press during an event.
public class EventChoice
{
    public string label;
    public Action onChoose;          // what happens when picked (can be null)
    public Func<bool> condition;     // null = always available

    public bool IsAvailable()
    {
        return condition == null || condition();
    }
}

// A single event: some text plus choices.
// Built with a chain of calls, for example:
//   new GameEvent("Id", "Text").When(...).Choice("A", ...).Choice("B", ...)
public class GameEvent
{
    public readonly string id;
    private readonly Func<string> getText;
    private readonly List<Func<bool>> conditions = new List<Func<bool>>();
    public readonly List<EventChoice> choices = new List<EventChoice>();

    public GameEvent(string id, string text) : this(id, () => text) { }

    public GameEvent(string id, Func<string> getText)
    {
        this.id = id;
        this.getText = getText;
    }

    // Text is built when the event is shown, so it can include the current time.
    public string Text => getText();

    // Only show this event when the condition is true.
    // Call it more than once to require several conditions.
    public GameEvent When(Func<bool> condition)
    {
        conditions.Add(condition);
        return this;
    }

    // Only show this event between two times (minutes since midnight).
    // Example: .Between(GameState.TimeOf(7), GameState.TimeOf(8, 30))
    public GameEvent Between(int startMinutes, int endMinutes)
    {
        return When(() =>
        {
            int t = GameState.Instance.timeMinutes;
            return t >= startMinutes && t < endMinutes;
        });
    }

    // Add a choice. The condition is optional and hides the button when false.
    public GameEvent Choice(string label, Action onChoose = null, Func<bool> condition = null)
    {
        choices.Add(new EventChoice { label = label, onChoose = onChoose, condition = condition });
        return this;
    }

    public bool CanShow()
    {
        foreach (var condition in conditions)
        {
            if (!condition()) return false;
        }
        return true;
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