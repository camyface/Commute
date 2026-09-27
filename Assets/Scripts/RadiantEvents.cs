using Mono.Cecil.Cil;
using System.Collections.Generic;

public static class RadiantEvents
{
    static GameState gs = GameState.Instance;
    public static Dictionary<Location, GameEvent> gameEvents = new Dictionary<Location,GameEvent>
    {{Location.CommuteToWork,
        new GameEvent("Homeless", () =>
        {
            string text = $"You see a homeless man approach. He yells \"Hey! Can you spare some change?\" What do you do?";
            return text;
        }).Choice("Give him money", () =>
        {
            gs.AddHistory("You gave the homeless man some money");
            gs.AddFlag("PaidHomeless");
        }).Choice("Ignore him and keep driving", () =>
        {
            gs.AddHistory("You ignored the homeless man and kept driving");
            gs.AddFlag("IgnoredHomeless");
            gs.AddTime(15);
        })
        }
    };  
}
