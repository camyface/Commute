using System.Collections.Generic;

// All the day's content lives here.
// The day runs through these stages in order:
// Home -> Outside Home -> Commute -> Outside Work -> Work -> Outside Work
//      -> Commute Home -> Outside Home -> Home
public static class DayEvents
{
    public static List<DayStage> BuildDay()
    {
        var gs = GameState.Instance;

        // Shorthand for times: T(7, 30) = 7:30 AM, T(17) = 5:00 PM
        int T(int hour, int minute = 0) => GameState.TimeOf(hour, minute);

        // Records whether you were late the moment you walk into work.
        void CheckIn()
        {
            if (gs.IsLateForWork())
            {
                int late = gs.timeMinutes - gs.workStartMinutes;
                gs.AddFlag("LateToWork");
                gs.AddHistory($"Arrived at work {late} minutes late");
            }
            else
            {
                gs.AddHistory("Arrived at work on time");
            }
        }

        bool IsRushHour() => gs.timeMinutes >= T(17) && gs.timeMinutes < T(18, 30);
        bool CookiesShow() => gs.HasFlag("ChattedWithNeighbor") && gs.IsBefore(20);
        bool ColdShoulderShows() => gs.HasFlag("IgnoredNeighbor");

        return new List<DayStage>
        {
            // ==================== HOME (morning) ====================
            new DayStage(Location.Home,

                new GameEvent("Alarm", () => $"Your alarm goes off at {gs.GetTimeString()}.")
                    .Choice("Get up", () =>
                    {
                        gs.AddHistory("Got up when the alarm went off");
                        gs.AddTime(30);
                    })
                    .Choice("Snooze", () =>
                    {
                        gs.AddHistory("Snoozed the alarm");
                        gs.AddFlag("SnoozedAlarm");
                        gs.ChangeEnergy(1);
                        gs.AddTime(40);
                    }),

                new GameEvent("Breakfast", () => gs.HasFlag("SnoozedAlarm")
                        ? "You're dressed, but running behind. Eat breakfast anyway?"
                        : "You're dressed with time to spare. Eat breakfast?")
                    .Choice("Eat breakfast", () =>
                    {
                        gs.AddHistory("Ate breakfast");
                        gs.AddFlag("AteBreakfast");
                        gs.ChangeEnergy(2);
                        gs.AddTime(15);
                    })
                    .Choice("Skip breakfast", () =>
                    {
                        gs.AddHistory("Skipped breakfast");
                        gs.AddFlag("SkippedBreakfast");
                        gs.ChangeEnergy(-1);
                    }),

                new GameEvent("Umbrella", "You grab your keys. The forecast says rain. Take an umbrella?")
                    .Choice("Take umbrella", () =>
                    {
                        gs.AddHistory("Took an umbrella");
                        gs.AddFlag("HasUmbrella");
                    })
                    .Choice("Leave it", () => gs.AddHistory("Left the umbrella at home"))
            ),

            // ==================== OUTSIDE HOME (morning) ====================
            new DayStage(Location.OutsideHome,

                // Time-based: the neighbor is only outside early.
                new GameEvent("Neighbor", "Your neighbor is watering their plants and waves you over.")
                    .When(() => gs.IsBefore(7, 20))
                    .Choice("Stop and chat", () =>
                    {
                        gs.AddHistory("Chatted with the neighbor");
                        gs.AddFlag("ChattedWithNeighbor");
                        gs.ChangeMood(1);
                        gs.AddTime(15);
                    })
                    .Choice("Wave and keep walking", () =>
                    {
                        gs.AddHistory("Brushed off the neighbor");
                        gs.AddFlag("IgnoredNeighbor");
                        gs.ChangeMood(-1);
                    }),

                new GameEvent("QuietStreet", "The street is quiet. Your neighbor has already gone inside.")
                    .When(() => !gs.IsBefore(7, 20))
                    .Choice("Keep going")
            ),

            // ==================== COMMUTE TO WORK ====================
            new DayStage(Location.CommuteToWork,

                new GameEvent("RouteToWork", () => $"It's {gs.GetTimeString()}. How do you get to work?")
                    .Choice("Take the bus", () =>
                    {
                        gs.AddHistory("Took the bus");
                        gs.AddFlag("TookBusToWork");
                        gs.AddTime(45);
                    })
                    .Choice("Walk", () =>
                    {
                        gs.AddHistory("Walked to work");
                        gs.AddFlag("WalkedToWork");
                        gs.ChangeEnergy(gs.HasFlag("SkippedBreakfast") ? -2 : -1);
                        gs.AddTime(60);
                    }),

                // Chain reaction: snoozing made you miss the usual bus.
                new GameEvent("MissedBus", "You watch your usual bus pull away just as you reach the stop.")
                    .When(() => gs.HasFlag("TookBusToWork") && gs.HasFlag("SnoozedAlarm"))
                    .Choice("Wait for the next one", () =>
                    {
                        gs.AddHistory("Missed the bus and waited for the next one");
                        gs.AddTime(15);
                    })
                    .Choice("Chase it down", () =>
                    {
                        gs.AddHistory("Sprinted to catch the bus");
                        gs.ChangeEnergy(-2);
                    }),

                // Chain reaction: no umbrella on a rainy walk.
                new GameEvent("Rain", "Halfway there, it starts pouring.")
                    .When(() => gs.HasFlag("WalkedToWork") && !gs.HasFlag("HasUmbrella"))
                    .Choice("Wait under an awning", () =>
                    {
                        gs.AddHistory("Waited out the rain");
                        gs.AddTime(15);
                    })
                    .Choice("Keep walking", () =>
                    {
                        gs.AddHistory("Got soaked in the rain");
                        gs.AddFlag("GotWet");
                        gs.ChangeMood(-2);
                    })
            ),

            // ==================== OUTSIDE WORK (morning) ====================
            new DayStage(Location.OutsideWork,

                new GameEvent("ArriveAtWork", () => gs.IsLateForWork()
                        ? $"You reach the office at {gs.GetTimeString()}. You're late."
                        : $"You reach the office at {gs.GetTimeString()}. The coffee cart out front is open.")
                    // Time-based: coffee only if you have at least 5 minutes to spare.
                    .Choice("Grab a coffee", () =>
                    {
                        gs.AddHistory("Grabbed a coffee");
                        gs.ChangeEnergy(2);
                        gs.AddTime(5);
                        CheckIn();
                    }, () => gs.timeMinutes <= gs.workStartMinutes - 5)
                    .Choice("Head inside", CheckIn)
            ),

            // ==================== WORK ====================
            new DayStage(Location.Work,

                new GameEvent("BossLate", "Your boss is waiting at your desk. \"Rough morning?\"")
                    .When(() => gs.HasFlag("LateToWork"))
                    .Choice("Apologize", () =>
                    {
                        gs.AddHistory("Apologized to the boss");
                        gs.ChangeMood(-1);
                    })
                    .Choice("Blame the traffic", () =>
                    {
                        gs.AddHistory("Blamed the traffic");
                        gs.AddFlag("LiedToBoss");
                    }),

                new GameEvent("MorningWork", "You settle in at your desk. A big report is due today.")
                    .Choice("Focus hard", () =>
                    {
                        gs.AddHistory("Powered through the morning");
                        gs.AddFlag("Productive");
                        gs.ChangeEnergy(-2);
                        gs.AdvanceTo(T(12));
                    })
                    .Choice("Take it easy", () =>
                    {
                        gs.AddHistory("Took it easy all morning");
                        gs.ChangeMood(1);
                        gs.AdvanceTo(T(12));
                    }),

                new GameEvent("Lunch", () => $"It's {gs.GetTimeString()}. Your coworkers are heading out for lunch.")
                    .When(() => gs.IsAfter(12))
                    .Choice("Join them", () =>
                    {
                        gs.AddHistory("Had lunch with coworkers");
                        gs.AddFlag("LunchWithCoworkers");
                        gs.ChangeMood(2);
                        gs.AddTime(60);
                    })
                    .Choice("Eat at your desk", () =>
                    {
                        gs.AddHistory("Ate lunch at the desk");
                        gs.AddFlag("Productive");
                        gs.ChangeEnergy(1);
                        gs.AddTime(30);
                    }),

                // Stat-based: only shows if you're running low.
                new GameEvent("AfternoonSlump", "It's mid-afternoon and you can barely keep your eyes open.")
                    .When(() => gs.energy <= 3)
                    .Choice("Sneak a nap", () =>
                    {
                        gs.AddHistory("Napped in the break room");
                        gs.AddFlag("NappedAtWork");
                        gs.ChangeEnergy(2);
                        gs.AddTime(20);
                    })
                    .Choice("Push through", () =>
                    {
                        gs.AddHistory("Pushed through the slump");
                        gs.ChangeEnergy(-1);
                    }),

                new GameEvent("EndOfDay", () => gs.HasFlag("Productive")
                        ? "It's nearly 5:00 PM and your report is done."
                        : "It's nearly 5:00 PM and your report is only half finished.")
                    .Choice("Leave on time", () =>
                    {
                        gs.AdvanceTo(gs.workEndMinutes);
                        gs.AddHistory("Left work on time");
                    })
                    .Choice("Stay late", () =>
                    {
                        gs.AdvanceTo(gs.workEndMinutes);
                        gs.AddHistory("Stayed late to finish the report");
                        gs.AddFlag("StayedLate");
                        gs.AddFlag("Productive");
                        gs.ChangeMood(-1);
                        gs.AddTime(90);
                    })
            ),

            // ==================== OUTSIDE WORK (evening) ====================
            new DayStage(Location.OutsideWork,

                // Time-based: only if you left before 6 PM.
                new GameEvent("Drinks", "A few coworkers are heading to the bar across the street. \"You coming?\"")
                    .When(() => gs.IsBefore(18))
                    .Choice("Join them", () =>
                    {
                        gs.AddHistory("Went for drinks with coworkers");
                        gs.AddFlag("WentForDrinks");
                        gs.ChangeMood(2);
                        gs.ChangeEnergy(-1);
                        gs.AddTime(90);
                    })
                    .Choice("Head home", () => gs.AddHistory("Skipped drinks")),

                new GameEvent("DarkStreet", "Everyone else has gone home. The street is dark and empty.")
                    .When(() => gs.IsAfter(18))
                    .When(() => !gs.HasFlag("WentForDrinks"))
                    .Choice("Head home")
            ),

            // ==================== COMMUTE HOME ====================
            new DayStage(Location.CommuteHome,

                // Time-based: rush hour makes the bus slower.
                new GameEvent("RouteHome", () => IsRushHour()
                        ? $"It's {gs.GetTimeString()}, peak rush hour. How do you get home?"
                        : $"It's {gs.GetTimeString()}. How do you get home?")
                    .Choice("Take the bus", () =>
                    {
                        gs.AddHistory(IsRushHour() ? "Took a packed rush-hour bus home" : "Took the bus home");
                        gs.AddTime(IsRushHour() ? 65 : 40);
                    })
                    .Choice("Walk", () =>
                    {
                        gs.AddHistory("Walked home");
                        gs.ChangeEnergy(-1);
                        gs.AddTime(60);
                    }, () => !gs.HasFlag("WentForDrinks"))
                    .Choice("Take a taxi", () =>
                    {
                        gs.AddHistory("Took a taxi home after drinks");
                        gs.ChangeMood(-1);
                        gs.AddTime(20);
                    }, () => gs.HasFlag("WentForDrinks"))
            ),

            // ==================== OUTSIDE HOME (evening) ====================
            new DayStage(Location.OutsideHome,

                // Chain reaction: the morning chat pays off.
                new GameEvent("NeighborCookies", "Your neighbor catches you at the door with a plate of cookies. \"Nice chatting this morning!\"")
                    .When(CookiesShow)
                    .Choice("Thank them", () =>
                    {
                        gs.AddHistory("Got cookies from the neighbor");
                        gs.ChangeMood(2);
                    }),

                // Chain reaction: brushing them off didn't go unnoticed.
                new GameEvent("NeighborCold", "Your neighbor sees you coming and pointedly goes inside.")
                    .When(ColdShoulderShows)
                    .Choice("Shrug it off", () =>
                    {
                        gs.AddHistory("Got the cold shoulder from the neighbor");
                        gs.ChangeMood(-1);
                    }),

                new GameEvent("Doorstep", () => $"It's {gs.GetTimeString()}. The porch light flickers as you find your keys.")
                    .When(() => !CookiesShow() && !ColdShoulderShows())
                    .Choice("Go inside")
            ),

            // ==================== HOME (evening) ====================
            new DayStage(Location.Home,

                new GameEvent("Dinner", () => gs.IsAfter(20)
                        ? "It's late and you're starving. What about dinner?"
                        : "You're finally home. What about dinner?")
                    .Choice("Cook something", () =>
                    {
                        gs.AddHistory("Cooked dinner");
                        gs.ChangeEnergy(1);
                        gs.ChangeMood(1);
                        gs.AddTime(45);
                    }, () => gs.energy >= 2)
                    .Choice("Order takeout", () =>
                    {
                        gs.AddHistory("Ordered takeout");
                        gs.ChangeMood(1);
                        gs.AddTime(30);
                    }),

                new GameEvent("Bedtime", () => gs.energy <= 2
                        ? $"It's {gs.GetTimeString()} and you're completely wiped out."
                        : $"It's {gs.GetTimeString()}. Time to wind down.")
                    .Choice("Go to sleep", () => gs.AddHistory("Went to bed"))
            )
        };
    }
}