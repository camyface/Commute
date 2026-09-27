using System.Collections.Generic;
using static CharacterId;   // lets you write Boss instead of CharacterId.Boss
using static Expression;    // lets you write Happy instead of Expression.Happy

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
        bool rainyDay = day == 2 || day == 4;          // Tuesday and Thursday
        bool coworkerNeedsHelp = day == 2 || day == 4;

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

        // Relationship values at the start of today.
        int neighborFriendship = gs.GetCount("NeighborFriendship");
        int coworkerFriendship = gs.GetCount("CoworkerFriendship");

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
                // Their greeting depends on how you've treated them this week.
                new GameEvent("Neighbor")
                    .When(() => gs.IsBefore(7, 20))
                    .With(Neighbor, neighborFriendship <= -1 ? Sad : Neutral)
                    .Narrate("Your neighbor is out front, watering their plants.")
                    .Say(Neighbor,
                        neighborFriendship >= 1 ? "Well, if it isn't my favorite early bird! Morning again!"
                        : neighborFriendship <= -1 ? "Oh. Morning."
                        : "Good morning! Off to work already?",
                        neighborFriendship >= 1 ? Happy : neighborFriendship <= -1 ? Sad : Neutral)

                    .Choice("Stop and chat", () =>
                    {
                        gs.AddHistory("Chatted with the neighbor");
                        gs.AddCount("NeighborFriendship");
                        gs.ChangeMood(1);
                        gs.AddTime(15);
                    })
                        .Reply(Neighbor, neighborFriendship >= 1
                            ? "My tomatoes finally came in, you know. I'll have to save you some."
                            : "You know, you're the first person on this street to actually stop and talk.", Happy)
                        .Reply(Player, "I should get going, but this was nice.", Happy)
                        .Reply(Neighbor, "Go on, don't be late on my account!", Happy)

                    .Choice("Wave and keep walking", () =>
                    {
                        gs.AddHistory("Brushed off the neighbor");
                        gs.AddCount("NeighborFriendship", -1);
                        gs.ChangeMood(-1);
                    })
                        .Reply(Neighbor, "...Right. Busy, busy.", Sad),

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
                        gs.AddCount("BusChases");
                        gs.ChangeEnergy(-2);
                    })
                        .Reply(None, "The bus squeals to a stop. The doors hiss open.")
                        .Reply(BusDriver, () => gs.GetCount("BusChases") >= 2
                            ? "You again? I'm starting to think you enjoy the cardio."
                            : "Cutting it close, aren't we? Hop on.",
                            () => gs.GetCount("BusChases") >= 2 ? Happy : Surprised),

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

                // Across the week: the barista starts to remember you.
                new GameEvent("ArriveAtWork", () => gs.IsLateForWork()
                        ? $"You reach the office at {gs.GetTimeString()}. You're late."
                        : $"You reach the office at {gs.GetTimeString()}. The coffee cart out front is open.")
                    .Choice("Grab a coffee", () =>
                    {
                        gs.AddCount("CoffeeVisits");
                        gs.AddHistory("Grabbed a coffee");
                        gs.ChangeEnergy(2);
                        gs.AddTime(gs.GetCount("CoffeeVisits") >= 3 ? 2 : 5);  // regulars get served faster
                        CheckIn();
                    }, () => gs.timeMinutes <= gs.workStartMinutes - 5)
                        .Reply(Barista, () =>
                        {
                            int visits = gs.GetCount("CoffeeVisits");
                            string line;
                            if (visits == 1) line = "Morning! What can I get you?";
                            else if (visits == 2) line = "Back again! Same as yesterday?";
                            else line = "Your usual's already poured. Have a good one!";

                            // Chain reaction: walked in the rain without an umbrella.
                            if (gs.HasFlag("GotWet")) line += " ...You might want a towel with that.";
                            return line;
                        }, () => gs.HasFlag("GotWet") ? Surprised
                               : gs.GetCount("CoffeeVisits") >= 2 ? Happy : Neutral)

                    .Choice("Head inside", CheckIn)
            ),

            // ==================== WORK ====================
            new DayStage(Location.Work,

                // Across the week: the boss gets less patient each time you're late.
                new GameEvent("BossLate")
                    .When(() => gs.HasFlag("LateToWork"))
                    .With(Boss, Annoyed)
                    .Narrate("Your boss is waiting at your desk.")
                    .Say(Boss, () =>
                    {
                        int times = gs.GetCount("TimesLate");
                        if (times <= 1) return "Rough morning?";
                        if (times == 2) return "That's twice this week.";
                        return "This is becoming a pattern.";
                    }, Annoyed)

                    .Choice("Apologize", () =>
                    {
                        gs.AddHistory("Apologized to the boss");
                        gs.ChangeMood(-1);
                    })
                        .Reply(Player, "Sorry. It won't happen again.", Sad)
                        .Reply(Boss, () => gs.GetCount("TimesLate") >= 3
                            ? "You said that last time. Consider this your final warning."
                            : "Fine. Just don't make a habit of it.",
                            () => gs.GetCount("TimesLate") >= 3 ? Annoyed : Neutral)

                    .Choice("Blame the traffic", () =>
                    {
                        gs.AddHistory("Blamed the traffic");
                        gs.AddCount("LiesToBoss");
                    })
                        .Reply(Player, "Traffic was a nightmare this morning.")
                        .Reply(Boss, () => gs.GetCount("LiesToBoss") >= 2
                            ? "Traffic. Again. Funny, I drove the same road and it was empty."
                            : "Traffic, huh. Alright.",
                            () => gs.GetCount("LiesToBoss") >= 2 ? Annoyed : Neutral),

                // Monday only.
                new GameEvent("MondayMeeting")
                    .When(() => isMonday)
                    .With(Boss)
                    .Narrate("The Monday all-hands meeting drags on.")
                    .Say(Boss, "Last thing: the quarterly report is due Friday. No extensions. Everyone clear?")

                    .Choice("Pay attention", () =>
                    {
                        gs.AddHistory("Paid attention in the Monday meeting");
                        gs.ChangeEnergy(-1);
                        gs.AddPermanentFlag("KnowsTheDeadline");
                        gs.AdvanceTo(T(10));
                    })
                        .Reply(Player, "Clear. Friday.")
                        .Reply(Boss, "Good. Glad someone's listening.", Happy)

                    .Choice("Zone out", () =>
                    {
                        gs.AddHistory("Zoned out in the Monday meeting");
                        gs.AdvanceTo(T(10));
                    })
                        .Reply(None, "You catch the words \"Friday\" and \"no extensions.\" Probably not important."),

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

                // The coworker warms up to you the more you hang out.
                new GameEvent("Lunch")
                    .When(() => gs.IsAfter(12))
                    .With(Coworker, Happy)
                    .Say(Coworker, coworkerFriendship >= 2
                        ? "Saved you a spot, obviously. Tacos?"
                        : "Hey, a few of us are grabbing tacos. You in?", Happy)

                    .Choice("Join them", () =>
                    {
                        gs.AddHistory("Had lunch with coworkers");
                        gs.AddCount("CoworkerFriendship");
                        gs.ChangeMood(2);
                        gs.AddTime(60);
                    })
                        .Reply(Coworker, "Knew you'd cave. Let's go!", Happy)

                    .Choice("Eat at your desk", () =>
                    {
                        gs.AddHistory("Ate lunch at the desk");
                        gs.AddFlag("Productive");
                        gs.ChangeEnergy(1);
                        gs.AddTime(30);
                    })
                        .Reply(Coworker, "Suit yourself. More salsa for us."),

                // Tuesday and Thursday: the coworker needs a hand.
                new GameEvent("CoworkerHelp")
                    .When(() => coworkerNeedsHelp)
                    .With(Coworker, Sad)
                    .Narrate("Your coworker rolls their chair over to your desk.")
                    .Say(Coworker, "Hey... do you have a minute? My spreadsheet just ate itself.", Sad)

                    .Choice("Help them", () =>
                    {
                        gs.AddHistory("Helped a coworker fix their spreadsheet");
                        gs.AddCount("CoworkerFriendship");
                        gs.AddPermanentFlag("CoworkerOwesYou");
                        gs.ChangeMood(1);
                        gs.AddTime(45);
                    })
                        .Reply(None, "Forty-five minutes and three undo buttons later, it's fixed.")
                        .Reply(Coworker, "You're a lifesaver. I owe you one. Seriously.", Happy)

                    .Choice("Sorry, I'm swamped", () =>
                    {
                        gs.AddHistory("Turned down a coworker who needed help");
                        gs.AddFlag("Productive");
                    })
                        .Reply(Coworker, "No worries... I'll figure it out.", Sad),

                // Wednesday only: it's the coworker's birthday.
                new GameEvent("BirthdayCake")
                    .When(() => isWednesday)
                    .With(Coworker, Happy)
                    .Say(Coworker, "Cake in the break room! It's my birthday, don't make me eat it alone.", Happy)

                    .Choice("Have a slice", () =>
                    {
                        gs.AddHistory("Had birthday cake with a coworker");
                        gs.AddCount("CoworkerFriendship");
                        gs.ChangeMood(1);
                        gs.ChangeEnergy(1);
                        gs.AddTime(15);
                    })
                        .Reply(Player, "Happy birthday!", Happy)
                        .Reply(Coworker, "Thank you! Take the corner piece, it has the most frosting.", Happy)

                    .Choice("Keep working", () =>
                    {
                        gs.AddHistory("Skipped the birthday cake to keep working");
                        gs.AddFlag("Productive");
                    })
                        .Reply(Coworker, "Oh. Okay. I'll... save you a slice.", Sad),

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

                // Friday only: the boss looks back on your week.
                new GameEvent("FridayReview")
                    .When(() => isFriday)
                    .With(Boss)
                    .Narrate("Your boss stops by your desk.")
                    .Say(Boss, () =>
                    {
                        int productive = gs.GetCount("ProductiveDays") + (gs.HasFlag("Productive") ? 1 : 0);
                        int late = gs.GetCount("TimesLate");

                        if (late >= 3) return "My office. Now. We need to talk about your attendance.";
                        if (productive >= 4) return "Great work this week. Really. I've noticed.";
                        return "Solid week. Have a good weekend.";
                    }, () =>
                    {
                        int productive = gs.GetCount("ProductiveDays") + (gs.HasFlag("Productive") ? 1 : 0);
                        if (gs.GetCount("TimesLate") >= 3) return Annoyed;
                        return productive >= 4 ? Happy : Neutral;
                    })

                    .Choice("Nod")
                        .Reply(Boss, () => gs.GetCount("TimesLate") >= 3
                            ? "Close the door behind you."
                            : "Mm-hm. Don't let it go to your head.",
                            () => gs.GetCount("TimesLate") >= 3 ? Annoyed : Neutral)

                    .Choice("Ask about a raise", null, () => gs.GetCount("TimesLate") <= 1)
                        .Reply(Player, "Actually... could we talk about a raise?", Surprised)
                        .Reply(Boss, () => gs.GetCount("ProductiveDays") >= 3
                            ? "...Put something on my calendar for Monday."
                            : "Let's see a few more weeks like this one first.",
                            () => gs.GetCount("ProductiveDays") >= 3 ? Surprised : Neutral),

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
                // Chain reaction: helping your coworker earlier pays off on Friday.
                new GameEvent("Drinks")
                    .When(() => gs.IsBefore(18))
                    .With(Coworker, Happy)
                    .Say(Coworker, () =>
                    {
                        if (isFriday && gs.HasPermanentFlag("CoworkerOwesYou"))
                            return "It's Friday! And the first round's on me. I owe you, remember?";
                        if (isFriday)
                            return "It's Friday! The whole office is heading to the bar. You're coming.";
                        return "A few of us are hitting the bar across the street. You coming?";
                    }, Happy)

                    .Choice("Join them", () =>
                    {
                        gs.AddHistory("Went for drinks with coworkers");
                        gs.AddFlag("WentForDrinks");
                        gs.AddCount("CoworkerFriendship");
                        gs.ChangeMood(isFriday ? 3 : 2);
                        if (isFriday && gs.HasPermanentFlag("CoworkerOwesYou")) gs.ChangeMood(1);
                        gs.ChangeEnergy(-1);
                        gs.AddTime(90);
                    })
                        .Reply(Coworker, "That's what I like to hear!", Happy)

                    .Choice("Head home", () => gs.AddHistory("Skipped drinks"))
                        .Reply(Coworker, "Boo. Next time, then!"),

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
                new GameEvent("NeighborCookies")
                    .When(CookiesShow)
                    .With(Neighbor, Happy)
                    .Narrate("Your neighbor catches you at the door, holding a plate of cookies.")
                    .Say(Neighbor, "You've been such good company this week. Oatmeal raisin. Don't make that face.", Happy)

                    .Choice("Thank them", () =>
                    {
                        gs.AddHistory("Got cookies from the neighbor");
                        gs.AddPermanentFlag("GotCookies");
                        gs.ChangeMood(2);
                    })
                        .Reply(Player, "These smell amazing. Thank you.", Happy)
                        .Reply(Neighbor, "Bring the plate back whenever. Goodnight, dear!", Happy),

                // Across the week: brushing them off twice has consequences.
                new GameEvent("NeighborCold")
                    .When(ColdShoulderShows)
                    .With(Neighbor, Annoyed)
                    .Narrate("Your neighbor sees you coming, turns, and pointedly goes inside.")
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