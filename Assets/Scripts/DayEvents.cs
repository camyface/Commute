using System.Collections.Generic;
using UnityEngine;

// All the day's content lives here.
// The day runs through these stages in order:
// Home -> Outside Home -> Commute -> Coffee Shop -> Outside Work -> Work -> Outside Work
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

        // Coffee shop: "getting coffee" covers the drive-thru; "in the shop" means you walked in.
        bool GettingCoffee() => gs.HasFlag("StoppedForCoffee") && !gs.HasFlag("LeftCoffeeShop");
        bool InCoffeeShop() => GettingCoffee() && !gs.HasFlag("UsedDriveThru");
        bool ShortCoffeeLine() => gs.IsBefore(7, 40);

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
                    })
                    .Choice("Drive", () =>
                    {
                        gs.AddHistory("Drove to work");
                        gs.AddFlag("DroveToWork");
                        // Time-based: leaving late puts you in rush-hour traffic.
                        if (gs.IsAfter(7, 20)) gs.AddFlag("HitRushHour");
                        gs.AddTime(25);
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

                new GameEvent("Scooters", "Twenty rental scooters are piled across the sidewalk like a modern art installation. There is no way around.")
                    .When(() => gs.HasFlag("WalkedToWork"))
                    .Choice("Throw them all into the woods", () =>
                    {
                        gs.AddHistory("Hurled 20 rental scooters into the woods");
                        gs.AddFlag("ThrewScooters");
                        gs.ChangeMood(2);
                        gs.AddTime(10);
                    })
                    // Faster than walking, even counting the crash.
                    .Choice("Rent one and ride it to work", () =>
                    {
                        gs.AddHistory("Rode a rental scooter to work and ate pavement");
                        gs.AddFlag("CrashedScooter");
                        gs.ChangeMood(-2);
                        gs.AddTime(-20);
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
                    }),

                // Chain reaction: a late start means rush-hour traffic.
                new GameEvent("Traffic", "Brake lights as far as you can see. The highway is a parking lot.")
                    .When(() => gs.HasFlag("HitRushHour"))
                    .Choice("Sit in traffic", () =>
                    {
                        gs.AddHistory("Sat in rush-hour traffic");
                        gs.ChangeMood(-1);
                        gs.AddTime(20);
                    })
                    .Choice("Try the back roads", () =>
                    {
                        gs.AddHistory("Cut through the back roads");
                        gs.ChangeEnergy(-1);
                        gs.AddTime(10);
                    })
            ),

            // ==================== JOE'S BEANS (a block from the office) ====================
            new DayStage(Location.CoffeeShop,

                new GameEvent("CoffeeShop", () => gs.HasFlag("DroveToWork")
                        ? "Joe's Beans is a block from the office. The drive-thru line wraps around the building, but it's moving."
                        : "Joe's Beans is a block from the office. It smells like burnt espresso and ambition.")
                    .Choice("Stop in", () =>
                    {
                        gs.AddHistory("Stopped at Joe's Beans");
                        gs.AddFlag("StoppedForCoffee");
                    })
                    .Choice("Hit the drive-thru", () =>
                    {
                        gs.AddHistory("Hit the Joe's Beans drive-thru");
                        gs.AddFlag("StoppedForCoffee");
                        gs.AddFlag("UsedDriveThru");
                        gs.AddTime(5);
                    }, () => gs.HasFlag("DroveToWork"))
                    .Choice("Keep going", () => gs.AddHistory("Walked past Joe's Beans")),

                // Time-based: the line blows up as 8:00 gets closer.
                new GameEvent("CoffeeLine", () => ShortCoffeeLine()
                        ? "Just you and a guy with a laptop who has clearly been here since 5 AM."
                        : "The line is out the door. Everyone ahead of you is ordering a pour-over.")
                    .When(InCoffeeShop)
                    .Choice("Get in line", () =>
                    {
                        gs.AddHistory("Got in a short line");
                        gs.AddTime(3);
                    }, ShortCoffeeLine)
                    .Choice("Wait it out", () =>
                    {
                        gs.AddHistory("Waited in a long line");
                        gs.AddTime(15);
                    }, () => !ShortCoffeeLine())
                    .Choice("Mobile order", () =>
                    {
                        gs.AddHistory("Placed a mobile order");
                        gs.AddTime(5);
                        // Coin flip: someone else walks off with it.
                        if (Random.value < 0.5f) gs.AddFlag("OrderTaken");
                    }, () => !ShortCoffeeLine())
                    .Choice("Forget it", () =>
                    {
                        gs.AddHistory("Gave up on the coffee line");
                        gs.AddFlag("LeftCoffeeShop");
                    }, () => !ShortCoffeeLine()),

                new GameEvent("OrderTaken", "They call your name. By the time you look up, a guy in a burnt-orange hoodie is walking out sipping your drink.")
                    .When(() => gs.HasFlag("OrderTaken"))
                    .Choice("Order at the counter", () =>
                    {
                        gs.AddHistory("Had a mobile order stolen and reordered");
                        gs.AddTime(10);
                    })
                    .Choice("Forget it", () =>
                    {
                        gs.AddHistory("Had a mobile order stolen and gave up");
                        gs.AddFlag("LeftCoffeeShop");
                        gs.ChangeMood(-1);
                    }),

                new GameEvent("Marisol", "Marisol from Accounting is at the register, patting her pockets. She forgot her wallet.")
                    .When(InCoffeeShop)
                    .Choice("Buy hers too", () =>
                    {
                        gs.AddHistory("Bought Marisol a coffee");
                        gs.AddFlag("BoughtMarisolCoffee");
                        gs.ChangeMood(1);
                        gs.AddTime(3);
                    })
                    .Choice("Suddenly get very into your phone", () =>
                    {
                        gs.AddHistory("Pretended not to see Marisol");
                        gs.AddFlag("DodgedMarisol");
                    }),

                new GameEvent("CoffeeOrder", () => gs.HasFlag("UsedDriveThru")
                        ? "A crackly speaker asks for your order."
                        : "You reach the counter. The chalkboard menu has 43 items and no prices.")
                    .When(GettingCoffee)
                    .Choice("Black coffee", () =>
                    {
                        gs.AddHistory("Ordered a black coffee");
                        gs.AddFlag("HadCoffee");
                        gs.ChangeEnergy(1);
                    })
                    .Choice("Oat milk lavender cold brew, 4 shots", () =>
                    {
                        gs.AddHistory("Ordered a four-shot lavender cold brew");
                        gs.AddFlag("HadCoffee");
                        gs.AddFlag("Jittery");
                        gs.ChangeEnergy(3);
                        gs.AddTime(2);
                    })
                    // Makes up for skipping breakfast at home.
                    .Choice("Coffee and a breakfast taco", () =>
                    {
                        gs.AddHistory("Got a coffee and a breakfast taco");
                        gs.AddFlag("HadCoffee");
                        gs.AddFlag("AteBreakfastTaco");
                        gs.RemoveFlag("SkippedBreakfast");
                        gs.ChangeEnergy(3);
                        gs.AddTime(5);
                    }, () => gs.HasFlag("SkippedBreakfast")),

                new GameEvent("WrongOrder", "You pull away and take a sip. It's a pumpkin spice latte. It's 94 degrees outside.")
                    .When(() => gs.HasFlag("UsedDriveThru"))
                    .Choice("Drink it anyway", () =>
                    {
                        gs.AddHistory("Drank a pumpkin spice latte in 94-degree heat");
                        gs.RemoveFlag("Jittery");
                        gs.ChangeMood(-1);
                    })
                    .Choice("Loop back through", () =>
                    {
                        gs.AddHistory("Went back through the drive-thru to fix the order");
                        gs.AddTime(10);
                    })
            ),

            // ==================== OUTSIDE WORK (morning) ====================
            new DayStage(Location.OutsideWork,

                new GameEvent("Parking", "The office garage is nearly full. There's an open spot on the street, but the meter's broken.")
                    .When(() => gs.HasFlag("DroveToWork"))
                    .Choice("Circle the garage", () =>
                    {
                        gs.AddHistory("Circled the garage for a spot");
                        gs.AddTime(10);
                    })
                    .Choice("Take the street spot", () =>
                    {
                        gs.AddHistory("Parked on the street");
                        gs.AddFlag("ParkedOnStreet");
                    }),

                // Chain reaction: the breakfast taco attracts the locals.
                new GameEvent("Grackles", "You step out with your breakfast taco. A dozen grackles drop off the power lines, screaming like car alarms. They have done this before.")
                    .When(() => gs.HasFlag("AteBreakfastTaco"))
                    .Choice("Surrender the taco", () =>
                    {
                        gs.AddHistory("Lost a breakfast taco to a gang of grackles");
                        gs.AddFlag("RobbedByGrackles");
                        gs.RemoveFlag("AteBreakfastTaco");
                        gs.ChangeEnergy(-2);
                        gs.ChangeMood(-1);
                    })
                    .Choice("Fight for it", () =>
                    {
                        gs.AddHistory("Fought off the grackles. Taco intact, dignity not");
                        gs.AddFlag("FoughtGrackles");
                        gs.ChangeMood(-1);
                        gs.AddTime(5);
                    })
                    // Losing the coffee brings the office coffee cart back as an option.
                    .Choice("Throw your coffee as a decoy", () =>
                    {
                        gs.AddHistory("Sacrificed a coffee to the grackles");
                        gs.RemoveFlag("HadCoffee");
                        gs.ChangeEnergy(-1);
                    }),

                new GameEvent("ArriveAtWork", () => gs.IsLateForWork()
                        ? $"You reach the office at {gs.GetTimeString()}. You're late."
                        : gs.HasFlag("HadCoffee")
                            ? $"You reach the office at {gs.GetTimeString()}, coffee in hand."
                            : $"You reach the office at {gs.GetTimeString()}. The coffee cart out front is open.")
                    // Time-based: coffee only if you have at least 5 minutes to spare (and don't have one yet).
                    .Choice("Grab a coffee", () =>
                    {
                        gs.AddHistory("Grabbed a coffee");
                        gs.AddFlag("HadCoffee");
                        gs.ChangeEnergy(2);
                        gs.AddTime(5);
                        CheckIn();
                    }, () => gs.timeMinutes <= gs.workStartMinutes - 5 && !gs.HasFlag("HadCoffee"))
                    .Choice("Head inside", CheckIn)
            ),

            // ==================== WORK ====================
            new DayStage(Location.Work,

                // Chain reaction: the coffee you bought Marisol pays off.
                new GameEvent("MarisolCovers", "Your boss is heading for your desk when Marisol steps in: \"Oh, they've been down in the garage since 7:30. It's a whole thing.\" She winks at you.")
                    .When(() => gs.HasFlag("LateToWork") && gs.HasFlag("BoughtMarisolCoffee"))
                    .Choice("Mouth \"thank you\"", () =>
                    {
                        gs.AddHistory("Marisol covered for being late");
                        gs.ChangeMood(1);
                    }),

                new GameEvent("BossLate", "Your boss is waiting at your desk. \"Rough morning?\"")
                    .When(() => gs.HasFlag("LateToWork") && !gs.HasFlag("BoughtMarisolCoffee"))
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

                new GameEvent("MorningWork", () => gs.HasFlag("Jittery")
                        ? "You settle in at your desk. A big report is due today. Your leg is bouncing at 140 BPM."
                        : "You settle in at your desk. A big report is due today.")
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

                // Chain reaction: someone filmed the grackle fight.
                new GameEvent("GrackleVideo", "Your phone won't stop buzzing. Someone filmed you fighting grackles for a taco. It has 40,000 views.")
                    .When(() => gs.HasFlag("FoughtGrackles"))
                    .Choice("Lean into it", () =>
                    {
                        gs.AddHistory("Went viral as the Taco Defender");
                        gs.AddFlag("WentViral");
                        gs.ChangeMood(2);
                    })
                    .Choice("Deny everything", () =>
                    {
                        gs.AddHistory("Denied being the person in the grackle video");
                        gs.ChangeMood(-1);
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

                // Chain reaction: dodging Marisol this morning comes back around.
                new GameEvent("MarisolLunch", "Over lunch, Marisol mentions she saw \"someone who looked JUST like you\" at Joe's Beans this morning. She holds eye contact.")
                    .When(() => gs.HasFlag("DodgedMarisol") && gs.HasFlag("LunchWithCoworkers"))
                    .Choice("\"Huh. Weird.\"", () =>
                    {
                        gs.AddHistory("Got called out by Marisol at lunch");
                        gs.ChangeMood(-1);
                    }),

                // Chain reaction: the four-shot cold brew wears off.
                // Drops energy to 2, so the afternoon slump below always follows.
                new GameEvent("CaffeineCrash", "Around 2 PM, all four shots of espresso leave your body at once.")
                    .When(() => gs.HasFlag("Jittery"))
                    .Choice("Oh no", () =>
                    {
                        gs.AddHistory("Crashed hard from the cold brew");
                        if (gs.energy > 2) gs.ChangeEnergy(2 - gs.energy);
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

                // Chain reaction: the street spot wasn't free after all.
                new GameEvent("ParkingTicket", "There's a parking ticket tucked under your windshield wiper. $65.")
                    .When(() => gs.HasFlag("ParkedOnStreet"))
                    .Choice("Ugh", () =>
                    {
                        gs.AddHistory("Got a parking ticket");
                        gs.AddFlag("GotParkingTicket");
                        gs.ChangeMood(-2);
                    }),

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
                    }, () => !gs.HasFlag("DroveToWork"))
                    .Choice("Walk", () =>
                    {
                        gs.AddHistory("Walked home");
                        gs.ChangeEnergy(-1);
                        gs.AddTime(60);
                    }, () => !gs.HasFlag("WentForDrinks") && !gs.HasFlag("DroveToWork"))
                    // The car is at the office, so drive it home unless you've been drinking.
                    .Choice("Drive home", () =>
                    {
                        gs.AddHistory(IsRushHour() ? "Drove home through rush hour" : "Drove home");
                        gs.AddTime(IsRushHour() ? 45 : 25);
                    }, () => gs.HasFlag("DroveToWork") && !gs.HasFlag("WentForDrinks"))
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