using System.Collections.Generic;

// All the day's content lives here. BuildDay() is called fresh every morning,
// so events can change depending on which day it is.
//
// Each day runs through these stages in order:
// Home -> Outside Home -> Commute -> Outside Work -> Work -> Outside Work
//      -> Commute Home -> Outside Home -> Home
public static class DayEvents
{
    public static List<DayStage> BuildDay()
    {
        var gs = GameState.Instance;

        // ---------- Day info ----------
        int day = gs.currentDay;
        bool isMonday = day == 1;
        bool isWednesday = day == 3;
        bool isFriday = day == 5;
        bool rainyDay = day == 2 || day == 4;   // Tuesday and Thursday

        // Shorthand for times: T(7, 30) = 7:30 AM, T(17) = 5:00 PM
        int T(int hour, int minute = 0) => GameState.TimeOf(hour, minute);

        // Records whether you were late the moment you walk into work.
        void CheckIn()
        {
            if (gs.IsLateForWork())
            {
                int late = gs.timeMinutes - gs.workStartMinutes;
                gs.AddFlag("LateToWork");
                gs.AddCount("TimesLate");
                gs.AddHistory($"Arrived at work {late} minutes late");
            }
            else
            {
                gs.AddHistory("Arrived at work on time");
            }
        }

        bool IsRushHour() => gs.timeMinutes >= T(17) && gs.timeMinutes < T(18, 30);

        // Across the week: chatting builds friendship, ignoring hurts it.
        int friendship = gs.GetCount("NeighborFriendship");
        bool CookiesShow() => gs.GetCount("NeighborFriendship") >= 2
                              && !gs.HasPermanentFlag("GotCookies")
                              && gs.IsBefore(20);
        bool ColdShoulderShows() => gs.GetCount("NeighborFriendship") <= -2;

        return new List<DayStage>
        {
            // ==================== HOME (morning) ====================
            new DayStage(Location.Home,

                new GameEvent("Alarm", () =>
                    {
                        string text = $"{gs.DayName} morning. Your alarm goes off at {gs.GetTimeString()}.";
                        if (gs.energy <= 3) text += "\nYou feel exhausted.";
                        return text;
                    })
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

                new GameEvent("Umbrella", rainyDay
                        ? "Dark clouds outside. The forecast says heavy rain. Take an umbrella?"
                        : "The sky looks clear. Take an umbrella anyway?")
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
                new GameEvent("Neighbor", friendship >= 1
                        ? "Your neighbor spots you and grins. \"Morning again!\""
                        : "Your neighbor is watering their plants and waves you over.")
                    .When(() => gs.IsBefore(7, 20))
                    .Choice("Stop and chat", () =>
                    {
                        gs.AddHistory("Chatted with the neighbor");
                        gs.AddCount("NeighborFriendship");
                        gs.ChangeMood(1);
                        gs.AddTime(15);
                    })
                    .Choice("Wave and keep walking", () =>
                    {
                        gs.AddHistory("Brushed off the neighbor");
                        gs.AddCount("NeighborFriendship", -1);
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

                // Only on rainy days, and only if you walked without an umbrella.
                new GameEvent("Rain", "Halfway there, it starts pouring.")
                    .When(() => rainyDay && gs.HasFlag("WalkedToWork") && !gs.HasFlag("HasUmbrella"))
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

                // Across the week: the boss gets less patient each time you're late.
                new GameEvent("BossLate", () =>
                    {
                        int times = gs.GetCount("TimesLate");
                        if (times <= 1) return "Your boss is waiting at your desk. \"Rough morning?\"";
                        if (times == 2) return "Your boss is waiting at your desk. \"That's twice this week.\"";
                        return "Your boss is waiting at your desk, arms crossed. \"This is becoming a pattern.\"";
                    })
                    .When(() => gs.HasFlag("LateToWork"))
                    .Choice("Apologize", () =>
                    {
                        gs.AddHistory("Apologized to the boss");
                        gs.ChangeMood(-1);
                    })
                    .Choice("Blame the traffic", () =>
                    {
                        gs.AddHistory("Blamed the traffic");
                        gs.AddCount("LiesToBoss");
                    }),

                // Monday only.
                new GameEvent("MondayMeeting", "The Monday all-hands meeting runs long.")
                    .When(() => isMonday)
                    .Choice("Pay attention", () =>
                    {
                        gs.AddHistory("Paid attention in the Monday meeting");
                        gs.ChangeEnergy(-1);
                        gs.AddPermanentFlag("KnowsTheDeadline");
                        gs.AdvanceTo(T(10));
                    })
                    .Choice("Zone out", () =>
                    {
                        gs.AddHistory("Zoned out in the Monday meeting");
                        gs.AdvanceTo(T(10));
                    }),

                new GameEvent("MorningWork", () => gs.HasPermanentFlag("KnowsTheDeadline")
                        ? "You settle in at your desk. You remember the report is due Friday."
                        : "You settle in at your desk. There's a report due at some point.")
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

                // Wednesday only.
                new GameEvent("BirthdayCake", "It's a coworker's birthday. There's cake in the break room.")
                    .When(() => isWednesday)
                    .Choice("Have a slice", () =>
                    {
                        gs.AddHistory("Had birthday cake");
                        gs.ChangeMood(1);
                        gs.ChangeEnergy(1);
                        gs.AddTime(15);
                    })
                    .Choice("Keep working", () =>
                    {
                        gs.AddHistory("Skipped the cake to keep working");
                        gs.AddFlag("Productive");
                    }),

                // Stat-based: only shows if you're running low.
                new GameEvent("AfternoonSlump", "It's mid-afternoon and you can barely keep your eyes open.")
                    .When(() => gs.energy <= 3)
                    .Choice("Sneak a nap", () =>
                    {
                        gs.AddHistory("Napped in the break room");
                        gs.ChangeEnergy(2);
                        gs.AddTime(20);
                    })
                    .Choice("Push through", () =>
                    {
                        gs.AddHistory("Pushed through the slump");
                        gs.ChangeEnergy(-1);
                    }),

                // Friday only: the boss looks back on your week so far.
                new GameEvent("FridayReview", () =>
                    {
                        int productive = gs.GetCount("ProductiveDays") + (gs.HasFlag("Productive") ? 1 : 0);
                        int late = gs.GetCount("TimesLate");

                        if (late >= 3) return "Your boss calls you into their office. \"We need to talk about your attendance.\"";
                        if (productive >= 4) return "Your boss stops by. \"Great work this week. I've noticed.\"";
                        return "Your boss stops by. \"Solid week. Have a good weekend.\"";
                    })
                    .When(() => isFriday)
                    .Choice("Nod"),

                new GameEvent("EndOfDay", () =>
                    {
                        string text = gs.HasFlag("Productive")
                            ? "It's nearly 5:00 PM and you got a lot done today."
                            : "It's nearly 5:00 PM and you didn't get much done today.";
                        if (isFriday) text += "\nThe weekend is so close.";
                        return text;
                    })
                    .Choice("Leave on time", () =>
                    {
                        gs.AdvanceTo(gs.workEndMinutes);
                        gs.AddHistory("Left work on time");
                    })
                    .Choice("Stay late", () =>
                    {
                        gs.AdvanceTo(gs.workEndMinutes);
                        gs.AddHistory("Stayed late to catch up");
                        gs.AddFlag("StayedLate");
                        gs.AddFlag("Productive");
                        gs.ChangeMood(-1);
                        gs.AddTime(90);
                    })
            ),

            // ==================== OUTSIDE WORK (evening) ====================
            new DayStage(Location.OutsideWork,

                // Time-based: only if you left before 6 PM.
                new GameEvent("Drinks", isFriday
                        ? "It's Friday! The whole office is heading to the bar across the street."
                        : "A few coworkers are heading to the bar across the street. \"You coming?\"")
                    .When(() => gs.IsBefore(18))
                    .Choice("Join them", () =>
                    {
                        gs.AddHistory("Went for drinks with coworkers");
                        gs.AddFlag("WentForDrinks");
                        gs.ChangeMood(isFriday ? 3 : 2);
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

                // Across the week: two friendly chats earn you cookies (once).
                new GameEvent("NeighborCookies", "Your neighbor catches you at the door with a plate of cookies. \"You've been great company this week!\"")
                    .When(CookiesShow)
                    .Choice("Thank them", () =>
                    {
                        gs.AddHistory("Got cookies from the neighbor");
                        gs.AddPermanentFlag("GotCookies");
                        gs.ChangeMood(2);
                    }),

                // Across the week: brushing them off twice has consequences.
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

                // Bedtime decides how much energy you get back tomorrow.
                new GameEvent("Bedtime", () =>
                    {
                        string text = gs.energy <= 2
                            ? $"It's {gs.GetTimeString()} and you're completely wiped out."
                            : $"It's {gs.GetTimeString()}. Time to wind down.";
                        text += gs.IsLastDay ? "\nThe weekend starts now." : "\nTomorrow's another workday.";
                        return text;
                    })
                    .Choice("Go to sleep", () => gs.GoToBed())
                    .Choice("Stay up watching TV", () =>
                    {
                        gs.AddHistory("Stayed up watching TV");
                        gs.ChangeMood(1);
                        gs.AddTime(90);
                        gs.GoToBed();
                    })
            )
        };
    }
}