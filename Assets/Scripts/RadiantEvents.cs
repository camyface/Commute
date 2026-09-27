using System.Collections.Generic;
using static CharacterId;
using static Expression;

// Rare random events that can pop up when the player arrives at a stage.
// EventUI rolls "Radiant Event Chance" (default 10%) once per stage, then picks
// one eligible event from the list for that location.
//
// To add one: add a GameEvent to the right case below. Conditions (.When),
// .Once(), .Scene(), characters and replies all work the same as in DayEvents.
public static class RadiantEvents
{
    // Built fresh each time so events always see the current GameState.
    public static List<GameEvent> For(Location location)
    {
        var gs = GameState.Instance;
        var events = new List<GameEvent>();

        switch (location)
        {
            case Location.CommuteToWork:
                events.Add(
                    new GameEvent("Homeless", "You see a homeless man approach. He yells \"Hey! Can you spare some change?\" What do you do?")
                        .When(() => !gs.HasPermanentFlag("VagrantHoused"))
                        .With(Vagrant, Sad)
                        .Choice("Give him money ($2)", () =>
                        {
                            gs.ChangeMoney(-2);
                            gs.AddCount("VagrantKindness");   // feeds into Joe's storyline
                            gs.AddHistory("You gave the homeless man some money");
                            gs.AddFlag("PaidHomeless");
                        }, () => gs.CanAfford(2))
                            .Reply(Vagrant, "Thank you, friend. Stay safe out there.", Happy)
                        .Choice("Ignore him and keep walking", () =>
                        {
                            gs.AddHistory("You ignored the homeless man and kept walking");
                            gs.AddFlag("IgnoredHomeless");
                            gs.AddTime(15);
                        })
                            .Reply(None, "He follows you for a block, still asking. You lose a few minutes shaking him off.")
                );
                break;
        }

        return events;
    }
}