using System.Collections.Generic;
using static CharacterId;   // lets you write Boss instead of CharacterId.Boss
using static Expression;    // lets you write Happy instead of Expression.Happy

// All the day's content lives here. BuildDay() is called fresh every morning.
//
// Each day runs through these stages in order:
// Home -> Outside Home -> Commute -> Outside Work -> Work -> Outside Work
//      -> Commute Home -> Outside Home -> Home
//
// VARIETY:  events with the same .Group("Name") in a stage form a random pool.
//           One eligible event is picked each day. .Once() events never repeat.
// STORY ARCS (consequences that carry across the week):
//   Money      - bus, coffee, lunch, taxis, bills. Payday is Wednesday (docked for lateness).
//   Job        - standing 0-10. Late, lies, naps and broken promises lower it. 0 = fired.
//   Vagrant    - kindness to Joe pays off: he returns stolen things and warns you of danger.
//   Petitioner - sign her petition or lose the morning bus from Thursday on.
//   Thug       - shortcut muggings, a shady "delivery job", police raid, jail cell.
//   Dog        - befriend a stray, adopt it, feed it... it protects you.
//   Bills      - ignore the electricity bill and the power gets cut.
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
        bool rainy = gs.isRainy;

        // ---------- Shorthands ----------
        int T(int hour, int minute = 0) => GameState.TimeOf(hour, minute);
        bool Has(string flag) => gs.HasFlag(flag);
        bool HasP(string flag) => gs.HasPermanentFlag(flag);
        int Count(string key) => gs.GetCount(key);
        bool Afford(int amount) => gs.CanAfford(amount);
        bool Roll(float chance) => UnityEngine.Random.value < chance;
        void Spend(int amount) => gs.ChangeMoney(-amount);

        // ---------- Story state ----------
        bool HasDog() => HasP("HasDog");
        bool RouteCut() => day >= 4 && !HasP("PetitionSigned");
        bool FreeBus() => HasP("FreeBus");
        int BusFare() => FreeBus() ? 0 : 3;
        bool ThugActive() => !HasP("ReportedThug") && !HasP("ThugCaught");
        bool LostPhone() => HasP("LostPhone");
        bool PowerOff() => HasP("PowerCut") && !HasP("PaidBill");
        int Kindness() => Count("VagrantKindness");
        bool IsRushHour() => gs.timeMinutes >= T(17) && gs.timeMinutes < T(18, 30);
        bool CookiesShow() => Count("NeighborFriendship") >= 2 && !HasP("GotCookies") && gs.IsBefore(20);
        bool ColdShoulderShows() => Count("NeighborFriendship") <= -2;

        int PayCheck()
        {
            int pay = 120 - 15 * Count("TimesLate");
            return pay < 40 ? 40 : pay;
        }

        // Records whether you were late the moment you walk into work.
        void CheckIn()
        {
            if (gs.IsLateForWork())
            {
                int late = gs.timeMinutes - gs.workStartMinutes;
                gs.AddFlag("LateToWork");
                gs.AddCount("TimesLate");
                gs.ChangeStanding(-1);
                gs.AddHistory($"Arrived at work {late} minutes late (job standing -1)");
            }
            else
            {
                gs.AddHistory("Arrived at work on time");
            }
        }

        // Mugged: lose your cash, wallet and phone (no alarm until replaced).
        void GetMugged()
        {
            int lost = gs.money;
            gs.SetCount("StolenCash", lost);
            gs.ChangeMoney(-lost);
            gs.AddPermanentFlag("LostWallet");
            gs.AddPermanentFlag("LostPhone");
            gs.ChangeMood(-3);
            gs.AddHistory($"Got mugged: lost ${lost}, your wallet and your phone");
        }

        // Standing hit 0? Your boss fires you and the week ends. Placed at a few points in the workday.
        GameEvent FiredCheck(string id) => new GameEvent(id)
            .When(() => gs.standing <= 0)
            .Scene(Backdrop.MeetingRoom)
            .With(Boss, Annoyed)
            .Narrate("Your boss calls you into the meeting room. Someone from HR is already sitting there.")
            .Say(Boss, "I'll keep this short. We're letting you go. Security will walk you out.", Annoyed)
            .Choice("...", () =>
            {
                gs.AddHistory("Got fired");
                gs.AddPermanentFlag("Fired");
                gs.EndWeek("Fired");
            });

        return new List<DayStage>
        {
            // =====================================================================
            // HOME (morning)
            // =====================================================================
            new DayStage(Location.Home,

                new GameEvent("Alarm", () =>
                    {
                        string text;
                        if (Count("JailDay") == day - 1 && day > 1)
                            text = $"{gs.DayName}. You got home from the police station at dawn. The alarm feels like a personal attack.";
                        else if (LostPhone())
                            text = $"{gs.DayName}. No phone means no alarm. Sunlight wakes you at {gs.GetTimeString()}. You're already behind.";
                        else if (HasDog())
                            text = $"{gs.DayName} morning. Your dog's cold nose beats your alarm by a full minute.";
                        else
                            text = $"{gs.DayName} morning. Your alarm goes off at {gs.GetTimeString()}.";

                        if (rainy) text += "\nRain is tapping against the window.";
                        if (gs.energy <= 3) text += "\nYou feel exhausted.";
                        return text;
                    })
                    .Choice("Get up", () =>
                    {
                        gs.AddHistory("Got up");
                        gs.AddTime(30);
                    })
                    .Choice("Snooze", () =>
                    {
                        gs.AddHistory("Snoozed the alarm");
                        gs.AddFlag("SnoozedAlarm");
                        if (!HasDog()) gs.ChangeEnergy(1);
                        gs.AddTime(40);
                    }, () => !LostPhone())
                        .Reply(Dog, () => HasDog() ? "*Licks your face relentlessly. Snooze denied.*" : "", Happy),

                // Consequence: you forgot to feed the dog last night.
                new GameEvent("ChewedShoes")
                    .When(() => day > 1 && Count("ShoesChewedDay") == day - 1)
                    .With(Dog, Sad)
                    .Narrate("Your good shoes are in pieces by the door. Someone was hungry last night.")
                    .Say(Dog, "*avoids eye contact*", Sad)
                    .Choice("Dig out your old sneakers", () =>
                    {
                        gs.AddHistory("Dog chewed your shoes");
                        gs.ChangeMood(-1);
                        gs.AddTime(15);
                    }),

                new GameEvent("Breakfast", () =>
                    {
                        if (PowerOff()) return "The power's still off. The fridge smells... questionable.";
                        return Has("SnoozedAlarm") || LostPhone()
                            ? "You're dressed, but running behind. Eat breakfast anyway?"
                            : "You're dressed with time to spare. Eat breakfast?";
                    })
                    .Choice("Eat breakfast", () =>
                    {
                        gs.AddHistory("Ate breakfast");
                        gs.AddFlag("AteBreakfast");
                        gs.ChangeEnergy(PowerOff() ? 1 : 2);
                        gs.AddTime(15);
                    })
                    .Choice("Share it with your dog", () =>
                    {
                        gs.AddHistory("Shared breakfast with the dog");
                        gs.AddFlag("AteBreakfast");
                        gs.ChangeEnergy(1);
                        gs.ChangeMood(1);
                        gs.AddTime(15);
                    }, HasDog)
                        .Reply(Dog, "*inhales half your toast*", Happy)
                    .Choice("Skip breakfast", () =>
                    {
                        gs.AddHistory("Skipped breakfast");
                        gs.AddFlag("SkippedBreakfast");
                        gs.ChangeEnergy(-1);
                    }),

                // ---------- Random morning at home ----------
                new GameEvent("Bill")
                    .Group("HomeMorning", 2f)
                    .Once()
                    .When(() => day <= 3)
                    .Narrate("An envelope is waiting under your door: ELECTRICITY BILL — $30. FINAL NOTICE.")
                    .Choice("Pay it now ($30)", () =>
                    {
                        Spend(30);
                        gs.AddPermanentFlag("PaidBill");
                        gs.AddHistory("Paid the electricity bill ($30)");
                    }, () => Afford(30))
                    .Choice("Deal with it later", () =>
                    {
                        gs.AddPermanentFlag("IgnoredBill");
                        gs.AddHistory("Ignored the electricity bill");
                    })
                        .Reply(None, "You toss it on the counter. Future you's problem."),

                new GameEvent("ColdShower")
                    .Group("HomeMorning")
                    .Chance(0.5f)
                    .Narrate("The hot water is out. Again.")
                    .Choice("Brave the cold shower", () =>
                    {
                        gs.AddHistory("Took a freezing shower");
                        gs.ChangeMood(-1);
                        gs.ChangeEnergy(1);
                    })
                    .Choice("Wait for it to warm up", () =>
                    {
                        gs.AddHistory("Waited for the hot water");
                        gs.AddTime(15);
                    }),

                new GameEvent("NewsOnTV", () => ThugActive()
                        ? "The morning news: \"...another mugging near the old warehouse district. Police urge commuters to avoid the area.\""
                        : "The morning news: \"...police made an arrest near the old warehouse district thanks to a citizen's tip.\"")
                    .Group("HomeMorning")
                    .Chance(0.5f)
                    .When(() => !PowerOff())
                    .Choice("Noted"),

                // ---------- Umbrella ----------
                new GameEvent("Umbrella", rainy
                        ? "It's pouring outside. Take an umbrella?"
                        : "The sky looks clear. Take an umbrella anyway?")
                    .When(() => !HasP("GaveUmbrella"))
                    .Choice("Take umbrella", () =>
                    {
                        gs.AddHistory("Took an umbrella");
                        gs.AddFlag("HasUmbrella");
                    })
                    .Choice("Leave it", () => gs.AddHistory("Left the umbrella at home")),

                new GameEvent("NoUmbrella", "It's pouring, and your umbrella is with Joe now. Oh well.")
                    .When(() => rainy && HasP("GaveUmbrella"))
                    .Choice("Pull your hood up")
            ),

            // =====================================================================
            // OUTSIDE HOME (morning)
            // =====================================================================
            new DayStage(Location.OutsideHome,

                // Time-based: the neighbor is only outside early.
                new GameEvent("Neighbor")
                    .When(() => gs.IsBefore(7, 20))
                    .With(Neighbor, Count("NeighborFriendship") <= -1 ? Sad : Neutral)
                    .Narrate(rainy
                        ? "Your neighbor is on their porch, watching the rain."
                        : "Your neighbor is out front, watering their plants.")
                    .Say(Neighbor, () =>
                        {
                            int f = Count("NeighborFriendship");
                            if (f >= 1) return "Well, if it isn't my favorite early bird!";
                            if (f <= -1) return "Oh. Morning.";
                            return "Good morning! Off to work already?";
                        }, () =>
                        {
                            int f = Count("NeighborFriendship");
                            return f >= 1 ? Happy : f <= -1 ? Sad : Neutral;
                        })
                    .Choice("Stop and chat", () =>
                    {
                        gs.AddHistory("Chatted with the neighbor");
                        gs.AddCount("NeighborFriendship");
                        gs.ChangeMood(1);
                        gs.AddTime(15);
                    })
                        .Reply(Neighbor, () => HasDog()
                            ? "And who's this handsome fellow? You adopted him? Oh, you big softie."
                            : "You know, you're the first person on this street to actually stop and talk.", Happy)
                        .Reply(Player, "I should get going, but this was nice.", Happy)
                    .Choice("Wave and keep walking", () =>
                    {
                        gs.AddHistory("Brushed off the neighbor");
                        gs.AddCount("NeighborFriendship", -1);
                        gs.ChangeMood(-1);
                    })
                        .Reply(Neighbor, "...Right. Busy, busy.", Sad),

                // ---------- Random street encounters ----------
                new GameEvent("StrayDog")
                    .Group("StreetMorning", 1.5f)
                    .When(() => !HasDog() && !HasP("DogGone"))
                    .With(Dog, Sad)
                    .Narrate(() => Count("DogTrust") >= 1
                        ? "The scruffy dog is back, tail already wagging when it sees you."
                        : "A scruffy dog with no collar starts following you down the sidewalk.")
                    .Say(Dog, "*whines hopefully*", Sad)
                    .Choice("Scratch behind its ears", () =>
                    {
                        gs.AddHistory("Petted the stray dog");
                        gs.AddCount("DogTrust");
                        gs.ChangeMood(1);
                        gs.AddTime(5);
                    })
                        .Reply(Dog, "*leans its whole body against your leg*", Happy)
                    .Choice("Give it your breakfast bar", () =>
                    {
                        gs.AddHistory("Fed the stray dog");
                        gs.AddCount("DogTrust", 2);
                        gs.ChangeEnergy(-1);
                        gs.ChangeMood(1);
                    }, () => !Has("AteBreakfast"))
                        .Reply(Dog, "*gone in one bite. It's decided you're family now.*", Happy)
                    .Choice("Shoo it away", () =>
                    {
                        gs.AddHistory("Shooed the stray dog away");
                        gs.AddCount("DogShooed");
                        if (Count("DogShooed") >= 2) gs.AddPermanentFlag("DogGone");
                    })
                        .Reply(Dog, "*slinks away, looking back once*", Sad),

                new GameEvent("PetitionerFirst")
                    .Group("StreetMorning", 2f)
                    .When(() => day <= 3 && !HasP("MetPetitioner"))
                    .With(Petitioner)
                    .Narrate("A woman with a clipboard steps into your path.")
                    .Say(Petitioner, "Morning! The city wants to cut the Route 9 morning bus starting Thursday. We need signatures to stop it. Got a second?")
                    .Choice("Sign it", () =>
                    {
                        gs.AddPermanentFlag("MetPetitioner");
                        gs.AddPermanentFlag("PetitionSigned");
                        gs.AddHistory("Signed the Route 9 petition");
                        gs.ChangeMood(1);
                        gs.AddTime(5);
                    })
                        .Reply(Petitioner, "Thank you! Every name counts.", Happy)
                    .Choice("Sign and donate $10", () =>
                    {
                        Spend(10);
                        gs.AddPermanentFlag("MetPetitioner");
                        gs.AddPermanentFlag("PetitionSigned");
                        gs.AddPermanentFlag("Donated");
                        gs.AddHistory("Signed the petition and donated $10");
                        gs.ChangeMood(2);
                        gs.AddTime(5);
                    }, () => Afford(10))
                        .Reply(Petitioner, "You're a star. I won't forget this.", Happy)
                    .Choice("Not today", () =>
                    {
                        gs.AddPermanentFlag("MetPetitioner");
                        gs.AddHistory("Walked past the petitioner");
                    })
                        .Reply(Petitioner, "...Right. Enjoy walking on Thursday.", Annoyed),

                // Payoff: the petition worked.
                new GameEvent("PetitionVictory")
                    .Group("StreetMorning", 5f)
                    .Once()
                    .When(() => day >= 4 && HasP("PetitionSigned"))
                    .With(Petitioner, Happy)
                    .Narrate("The woman with the clipboard spots you and runs over, beaming.")
                    .Say(Petitioner, "We did it! Route 9 stays! The city backed down last night.", Happy)
                    .Say(Petitioner, () => HasP("Donated")
                        ? "And your donation covered the printing. Here, take my spare monthly pass. You earned it."
                        : "Here, the transit union gave us spare passes for supporters. Take one!", Happy)
                    .Choice("Thank you!", () =>
                    {
                        gs.AddPermanentFlag("FreeBus");
                        gs.AddHistory("Got a free bus pass from the petitioner");
                        gs.ChangeMood(2);
                    })
                        .Reply(Petitioner, "No, thank YOU. See you around, neighbor.", Happy),

                new GameEvent("QuietStreet", () => rainy
                        ? "Rain drums on parked cars. The street is empty."
                        : "The street is quiet. A few birds, a distant siren.")
                    .Group("StreetMorning", 0.8f)
                    .Choice("Keep going")
            ),

            // =====================================================================
            // COMMUTE TO WORK
            // =====================================================================
            new DayStage(Location.CommuteToWork,

                new GameEvent("RouteToWork", () =>
                    {
                        string text = $"It's {gs.GetTimeString()}. How do you get to work?";
                        if (RouteCut()) text = "A sign is taped to the bus stop: ROUTE 9 MORNING SERVICE CANCELLED.\n" + text;
                        if (!Afford(BusFare())) text += "\nYour wallet is empty.";
                        return text;
                    })
                    .Choice("Take the bus ($3)", () =>
                    {
                        Spend(3);
                        gs.AddHistory("Took the bus ($3)");
                        gs.AddFlag("TookBusToWork");
                        gs.AddTime(45);
                    }, () => !RouteCut() && !FreeBus() && Afford(3))
                    .Choice("Take the bus (free pass)", () =>
                    {
                        gs.AddHistory("Took the bus on your free pass");
                        gs.AddFlag("TookBusToWork");
                        gs.AddTime(45);
                    }, () => !RouteCut() && FreeBus())
                    .Choice("Take a taxi ($15)", () =>
                    {
                        Spend(15);
                        gs.AddHistory("Took a taxi to work ($15)");
                        gs.AddFlag("TookTaxiToWork");
                        gs.AddTime(25);
                    }, () => RouteCut() && Afford(15))
                    .Choice("Walk", () =>
                    {
                        gs.AddHistory("Walked to work");
                        gs.AddFlag("WalkedToWork");
                        gs.ChangeEnergy(Has("SkippedBreakfast") ? -2 : -1);
                        gs.AddTime(60);
                    })
                    .Choice("Shortcut through the warehouses", () =>
                    {
                        gs.AddHistory("Cut through the warehouse district");
                        gs.AddFlag("WalkedToWork");
                        gs.AddFlag("TookShortcut");
                        gs.ChangeEnergy(-1);
                        gs.AddTime(35);
                    }),

                // Chain reaction: running late means missing the usual bus.
                new GameEvent("MissedBus", "You watch your usual bus pull away just as you reach the stop.")
                    .When(() => Has("TookBusToWork") && (Has("SnoozedAlarm") || LostPhone()))
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
                        .Reply(BusDriver, () => Count("BusChases") >= 2
                            ? "You again? I'm starting to think you enjoy the cardio."
                            : "Cutting it close, aren't we? Hop on.",
                            () => Count("BusChases") >= 2 ? Happy : Surprised),

                // ---------- On the bus (random) ----------
                new GameEvent("QuietBus", () => rainy
                        ? "Rain streaks the bus windows. You find a seat near the heater."
                        : "You snag a window seat and watch the city wake up.")
                    .Group("BusMorning")
                    .Scene(Backdrop.BusDay)
                    .When(() => Has("TookBusToWork"))
                    .Choice("Rest your eyes", () =>
                    {
                        gs.AddHistory("Dozed on the bus");
                        gs.ChangeEnergy(1);
                    }),

                new GameEvent("VagrantBus")
                    .Group("BusMorning", 2f)
                    .Scene(Backdrop.BusDay)
                    .When(() => Has("TookBusToWork") && !HasP("VagrantHoused"))
                    .With(Vagrant, Sad)
                    .Narrate(() => Kindness() >= 1
                        ? "Joe waves at you from the back of the bus, then shuffles over with his paper cup."
                        : "A man in a threadbare coat shuffles down the aisle, holding out a paper cup.")
                    .Say(Vagrant, () => Kindness() >= 1
                        ? "Morning, friend. Don't suppose you've got a little something today?"
                        : "Spare some change? Just trying to get a hot meal.", Sad)
                    .Choice("Give him $5", () =>
                    {
                        Spend(5);
                        gs.AddCount("VagrantKindness");
                        gs.AddHistory("Gave Joe $5");
                        gs.ChangeMood(1);
                    }, () => Afford(5))
                        .Reply(Vagrant, () => Kindness() >= 2
                            ? "You're good people. I keep my eyes open on these streets. I'll look out for you."
                            : "Bless you. Name's Joe. I won't forget it.", Happy)
                    .Choice("Give him your umbrella", () =>
                    {
                        gs.RemoveFlag("HasUmbrella");
                        gs.AddPermanentFlag("GaveUmbrella");
                        gs.AddCount("VagrantKindness", 2);
                        gs.AddHistory("Gave Joe your umbrella");
                        gs.ChangeMood(2);
                    }, () => rainy && Has("HasUmbrella"))
                        .Reply(Vagrant, "You sure? ...Thank you. Really. Name's Joe.", Surprised)
                    .Choice("Look away", () => gs.AddHistory("Ignored the man asking for change"))
                        .Reply(Vagrant, "...Yeah. Figured.", Sad),

                new GameEvent("BusDriverChat")
                    .Group("BusMorning")
                    .Scene(Backdrop.BusDay)
                    .When(() => Has("TookBusToWork"))
                    .With(BusDriver, Happy)
                    .Say(BusDriver, () => FreeBus()
                        ? "Ooh, a Route 9 supporter pass! You're one of the good ones."
                        : "Morning! You're becoming a regular.", Happy)
                    .Choice("Chat about the weather", () =>
                    {
                        gs.AddHistory("Chatted with the bus driver");
                        gs.ChangeMood(1);
                    })
                        .Reply(BusDriver, rainy ? "Rain like this, half my route calls in sick." : "Enjoy it while it lasts!")
                    .Choice("Just nod"),

                // Second chance to sign.
                new GameEvent("PetitionerBus")
                    .Group("BusMorning", 3f)
                    .Once()
                    .Scene(Backdrop.BusDay)
                    .When(() => Has("TookBusToWork") && HasP("MetPetitioner") && !HasP("PetitionSigned") && day <= 3)
                    .With(Petitioner, Annoyed)
                    .Say(Petitioner, "Oh. It's you. Enjoying the bus? We submit the petition Wednesday night. Last chance.", Annoyed)
                    .Choice("Fine, I'll sign", () =>
                    {
                        gs.AddPermanentFlag("PetitionSigned");
                        gs.AddHistory("Signed the petition on the bus");
                    })
                        .Reply(Petitioner, "Better late than never. Thank you.", Happy)
                    .Choice("Still no", () => gs.AddHistory("Refused the petition again"))
                        .Reply(Petitioner, "Suit yourself.", Annoyed),

                // ---------- Walking ----------
                new GameEvent("Rain", "Halfway there, the rain gets even heavier.")
                    .When(() => rainy && Has("WalkedToWork") && !Has("HasUmbrella"))
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

                // ---------- Warehouse shortcut (random, dangerous) ----------
                new GameEvent("QuietShortcut", "You hurry past the abandoned warehouse. Broken windows stare down at you, but nobody's around.")
                    .Group("Shortcut")
                    .Scene(Backdrop.Warehouse)
                    .When(() => Has("TookShortcut"))
                    .Choice("Keep moving"),

                new GameEvent("ThugMugging")
                    .Group("Shortcut", HasP("ThugAngry") ? 20f : 1.5f)   // cross him and he comes looking for you
                    .Scene(Backdrop.Warehouse)
                    .When(() => Has("TookShortcut") && ThugActive())
                    .With(Thug, Annoyed)
                    .Narrate(() => HasP("ThugAngry")
                        ? "The man from the warehouse job is waiting by the loading dock. He's been expecting you."
                        : "A big man steps out from behind the warehouse loading dock, blocking your path.")
                    .Say(Thug, () => HasP("ThugAngry")
                        ? "You dumped my package. That was expensive. Now it's YOUR expense."
                        : "Wallet. Phone. Don't make this hard.", Annoyed)
                    .Choice("Hand them over", () => GetMugged())
                        .Reply(Thug, "Smart.", Happy)
                        .Reply(None, "He disappears around the corner. No wallet, no phone, no alarm tomorrow.")
                    .Choice("Run for it", () =>
                    {
                        if (Roll(0.5f))
                        {
                            gs.AddFlag("Escaped");
                            gs.AddHistory("Outran a mugger");
                            gs.ChangeEnergy(-3);
                        }
                        else
                        {
                            gs.ChangeEnergy(-2);
                            GetMugged();
                        }
                    })
                        .Reply(None, () => Has("Escaped")
                            ? "You sprint until your lungs burn. When you look back, he's gone."
                            : "You make it ten steps before he grabs your collar. He takes everything.")
                    .Choice("Let your dog handle it", () =>
                    {
                        gs.AddHistory("Your dog scared off a mugger");
                        gs.RemovePermanentFlag("ThugAngry");
                        gs.ChangeMood(2);
                    }, HasDog)
                        .Reply(Dog, "*GRRRRR* WOOF! WOOF!", Annoyed)
                        .Reply(Thug, "Whoa! Call it off! Call it off!", Surprised)
                    .Choice("Shout for help", () =>
                    {
                        if (Kindness() >= 2)
                        {
                            gs.AddFlag("JoeSavedYou");
                            gs.RemovePermanentFlag("ThugAngry");
                            gs.AddHistory("Joe scared off a mugger for you");
                            gs.AddCount("VagrantKindness");
                        }
                        else
                        {
                            GetMugged();
                        }
                    }, () => !HasDog())
                        .Reply(Vagrant, () => Has("JoeSavedYou") ? "HEY! Leave my friend alone! I've got the cops on speed dial!" : "", Annoyed)
                        .Reply(Thug, () => Has("JoeSavedYou") ? "...Tch. Not worth it." : "Nobody's coming, genius.", Annoyed)
            ),

            // =====================================================================
            // OUTSIDE WORK (morning)
            // =====================================================================
            new DayStage(Location.OutsideWork,

                // Across the week: the barista starts to remember you.
                new GameEvent("ArriveAtWork", () => gs.IsLateForWork()
                        ? $"You reach the office at {gs.GetTimeString()}. You're late."
                        : $"You reach the office at {gs.GetTimeString()}. The coffee cart out front is open.")
                    .Choice("Grab a coffee ($4)", () =>
                    {
                        Spend(4);
                        gs.AddCount("CoffeeVisits");
                        gs.AddHistory("Grabbed a coffee ($4)");
                        gs.ChangeEnergy(2);
                        gs.AddTime(Count("CoffeeVisits") >= 3 ? 2 : 5);   // regulars get served faster
                        CheckIn();
                    }, () => gs.timeMinutes <= gs.workStartMinutes - 5 && Afford(4))
                        .Reply(Barista, () =>
                        {
                            int visits = Count("CoffeeVisits");
                            string line;
                            if (visits == 1) line = "Morning! What can I get you?";
                            else if (visits == 2) line = "Back again! Same as yesterday?";
                            else line = "Your usual's already poured. Have a good one!";

                            if (Has("GotWet")) line += " ...You might want a towel with that.";
                            return line;
                        }, () => Has("GotWet") ? Surprised : Count("CoffeeVisits") >= 2 ? Happy : Neutral)
                    .Choice("Head inside", CheckIn)
            ),

            // =====================================================================
            // WORK
            // =====================================================================
            new DayStage(Location.Work,

                FiredCheck("FiredMorning"),

                // Across the week: the boss gets less patient each time you're late.
                new GameEvent("BossLate")
                    .When(() => Has("LateToWork"))
                    .With(Boss, Annoyed)
                    .Narrate("Your boss is waiting at your desk.")
                    .Say(Boss, () =>
                    {
                        int times = Count("TimesLate");
                        if (times <= 1) return "Rough morning?";
                        if (times == 2) return "That's twice this week.";
                        return "This is becoming a pattern. HR is asking questions.";
                    }, Annoyed)
                    .Choice("Apologize", () =>
                    {
                        gs.AddHistory("Apologized to the boss");
                        gs.ChangeMood(-1);
                    })
                        .Reply(Player, "Sorry. It won't happen again.", Sad)
                        .Reply(Boss, () => Count("TimesLate") >= 3
                            ? "You said that last time. Consider this your final warning."
                            : "Fine. Just don't make a habit of it.",
                            () => Count("TimesLate") >= 3 ? Annoyed : Neutral)
                    .Choice("Blame the traffic", () =>
                    {
                        gs.AddCount("LiesToBoss");
                        if (Count("LiesToBoss") >= 2)
                        {
                            gs.ChangeStanding(-2);
                            gs.AddHistory("Got caught lying to the boss (job standing -2)");
                        }
                        else
                        {
                            gs.AddHistory("Blamed the traffic");
                        }
                    })
                        .Reply(Player, "Traffic was a nightmare this morning.")
                        .Reply(Boss, () => Count("LiesToBoss") >= 2
                            ? "Traffic. Again. Funny, I drove the same road and it was empty. Don't lie to me."
                            : "Traffic, huh. Alright.",
                            () => Count("LiesToBoss") >= 2 ? Annoyed : Neutral),

                // Consequence: a night in jail doesn't stay secret.
                new GameEvent("JailFollowUp")
                    .When(() => day > 1 && Count("JailDay") == day - 1)
                    .Scene(Backdrop.MeetingRoom)
                    .With(Boss, Annoyed)
                    .Narrate("Your boss pulls you into the meeting room and shuts the door.")
                    .Say(Boss, "The police called last night to verify your employment. Care to explain?", Annoyed)
                    .Choice("Tell the truth", () =>
                    {
                        gs.AddHistory("Came clean to the boss about the arrest");
                        gs.ChangeMood(-1);
                    })
                        .Reply(Player, "I made a really stupid decision. It won't happen again.", Sad)
                        .Reply(Boss, "...I appreciate the honesty. One more incident and you're done here.")
                    .Choice("It was a misunderstanding", () =>
                    {
                        gs.ChangeStanding(-2);
                        gs.AddHistory("Lied to the boss about the arrest (job standing -2)");
                    })
                        .Reply(Boss, "A misunderstanding. Right. I've seen the report.", Annoyed),

                // Monday only.
                new GameEvent("MondayMeeting")
                    .When(() => isMonday)
                    .Scene(Backdrop.MeetingRoom)
                    .With(Boss)
                    .Narrate("The Monday all-hands meeting drags on.")
                    .Say(Boss, "Last thing: the Henderson client presentation is Wednesday. And reports are due Friday. Everyone clear?")
                    .Choice("Pay attention", () =>
                    {
                        gs.AddHistory("Paid attention in the Monday meeting");
                        gs.ChangeEnergy(-1);
                        gs.ChangeStanding(1);
                        gs.AddPermanentFlag("KnowsTheDeadline");
                        gs.AdvanceTo(T(10));
                    })
                        .Reply(Player, "Clear.")
                        .Reply(Boss, "Good. Glad someone's listening.", Happy)
                    .Choice("Zone out", () =>
                    {
                        gs.AddHistory("Zoned out in the Monday meeting");
                        gs.AdvanceTo(T(10));
                    })
                        .Reply(None, "You catch the words \"Wednesday\" and \"presentation.\" Probably not important."),

                new GameEvent("MorningWork", () => gs.energy <= 2
                        ? "You stare at your screen. The words swim. You are running on fumes."
                        : "You settle in at your desk. Your inbox is overflowing.")
                    .Choice("Focus hard", () =>
                    {
                        gs.AddHistory("Powered through the morning");
                        gs.AddFlag("Productive");
                        gs.ChangeEnergy(-2);
                        gs.AdvanceTo(T(12));
                    }, () => gs.energy >= 2)
                    .Choice("Take it easy", () =>
                    {
                        gs.AddHistory("Took it easy all morning");
                        gs.ChangeMood(1);
                        gs.AdvanceTo(T(12));
                    }),

                // ---------- Random morning at work ----------
                new GameEvent("VolunteerAsk")
                    .Group("WorkMorning", 3f)
                    .Once()
                    .When(() => day <= 2)
                    .Scene(Backdrop.MeetingRoom)
                    .With(Boss)
                    .Narrate("Your boss gathers the team in the meeting room.")
                    .Say(Boss, "I need someone to lead the Henderson presentation on Wednesday. Big client. Any volunteers?")
                    .Choice("Volunteer", () =>
                    {
                        gs.AddPermanentFlag("Volunteered");
                        gs.ChangeStanding(1);
                        gs.AddHistory("Volunteered to lead Wednesday's presentation");
                    })
                        .Reply(Boss, "Excellent. Don't let me down. Prep well.", Happy)
                    .Choice("Avoid eye contact", () => gs.AddHistory("Dodged the presentation"))
                        .Reply(Boss, "Nobody? Wonderful. I'll do it myself. Again.", Annoyed),

                new GameEvent("CoworkerHelp")
                    .Group("WorkMorning")
                    .Once()
                    .With(Coworker, Sad)
                    .Narrate("Your coworker rolls their chair over to your desk.")
                    .Say(Coworker, "Hey... do you have a minute? My spreadsheet just ate itself and it's due at noon.", Sad)
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

                new GameEvent("NetworkOutage", "The office network goes down. Everyone is staring at frozen screens.")
                    .Group("WorkMorning")
                    .Once()
                    .Choice("Try to fix it", () =>
                    {
                        if (Roll(0.6f))
                        {
                            gs.AddFlag("FixedNetwork");
                            gs.AddFlag("Productive");
                            gs.ChangeStanding(2);
                            gs.AddHistory("Fixed the office network (job standing +2)");
                        }
                        else
                        {
                            gs.ChangeEnergy(-1);
                            gs.AddHistory("Tried and failed to fix the network");
                            gs.AddTime(30);
                        }
                    }, () => gs.energy >= 3)
                        .Reply(None, () => Has("FixedNetwork")
                            ? "You unplug the router, plug it back in, and the whole office cheers."
                            : "You poke at cables for thirty minutes. IT arrives and gives you a look.")
                        .Reply(Boss, () => Has("FixedNetwork") ? "Nice work. I noticed that." : "", Happy)
                    .Choice("Take a long coffee break", () =>
                    {
                        gs.AddHistory("Took a long break during the outage");
                        gs.ChangeMood(1);
                        gs.ChangeEnergy(1);
                        gs.AddTime(30);
                    }),

                new GameEvent("StayLateFavor")
                    .Group("WorkMorning")
                    .Once()
                    .When(() => !isFriday)
                    .With(Boss)
                    .Say(Boss, "I need someone to stay late tonight and finish the Henderson file. Can I count on you?")
                    .Choice("Sure, I'll stay", () =>
                    {
                        gs.AddFlag("PromisedStayLate");
                        gs.ChangeStanding(1);
                        gs.AddHistory("Promised the boss you'd stay late");
                    })
                        .Reply(Boss, "Great. I'll check in before I leave.", Happy)
                    .Choice("I can't tonight", () =>
                    {
                        gs.ChangeStanding(-1);
                        gs.AddHistory("Turned down the boss's request (job standing -1)");
                    })
                        .Reply(Boss, "Hm. Noted.", Annoyed),

                // ---------- Lunch ----------
                new GameEvent("Lunch")
                    .When(() => gs.IsAfter(12))
                    .With(Coworker, Happy)
                    .Say(Coworker, () => Count("CoworkerFriendship") >= 2
                        ? "Saved you a spot, obviously. Tacos?"
                        : "Hey, a few of us are grabbing tacos. You in?", Happy)
                    .Choice("Join them ($12)", () =>
                    {
                        Spend(12);
                        gs.AddHistory("Had lunch with coworkers ($12)");
                        gs.AddCount("CoworkerFriendship");
                        gs.ChangeMood(2);
                        gs.ChangeEnergy(1);
                        gs.AddTime(60);
                    }, () => Afford(12))
                        .Reply(Coworker, "Knew you'd cave. Let's go!", Happy)
                    .Choice("Let them cover you", () =>
                    {
                        gs.AddHistory("Your coworker paid for your lunch");
                        gs.AddCount("CoworkerFriendship");
                        gs.ChangeMood(2);
                        gs.ChangeEnergy(1);
                        gs.AddTime(60);
                    }, () => !Afford(12) && Count("CoworkerFriendship") >= 2)
                        .Reply(Coworker, "Don't even worry about it. You'd do the same.", Happy)
                    .Choice("Eat at your desk", () =>
                    {
                        gs.AddHistory("Worked through lunch");
                        gs.AddFlag("Productive");
                        gs.ChangeEnergy(-1);
                        gs.AddTime(30);
                    })
                        .Reply(Coworker, "Suit yourself. More salsa for us."),

                FiredCheck("FiredAfterLunch"),

                // Wednesday: the big presentation you volunteered for.
                new GameEvent("ClientPresentation")
                    .When(() => isWednesday && HasP("Volunteered"))
                    .Scene(Backdrop.MeetingRoom)
                    .With(Boss)
                    .Narrate("The Henderson clients file into the meeting room. Every eye turns to you.")
                    .Say(Boss, "Whenever you're ready.")
                    .Choice("Present from your notes", () =>
                    {
                        bool win = gs.energy >= 3 || Roll(0.5f);
                        if (win)
                        {
                            gs.AddPermanentFlag("PresentationWin");
                            gs.ChangeStanding(3);
                            gs.ChangeMoney(50);
                            gs.ChangeMood(2);
                            gs.AddHistory("Nailed the client presentation (job standing +3, $50 bonus)");
                        }
                        else
                        {
                            gs.AddPermanentFlag("PresentationFail");
                            gs.ChangeStanding(-2);
                            gs.AddHistory("Too tired to present well (job standing -2)");
                        }
                    }, () => Count("ProductiveDays") >= 1)
                        .Reply(None, () => HasP("PresentationWin")
                            ? "Your prep pays off. The clients nod along, then sign on the spot."
                            : "Your notes are good, but you're so tired you lose your place twice.")
                        .Reply(Boss, () => HasP("PresentationWin") ? "That was fantastic. There's a bonus in this week's check." : "We'll talk later.",
                            () => HasP("PresentationWin") ? Happy : Annoyed)
                    .Choice("Wing it", () =>
                    {
                        if (Roll(0.3f))
                        {
                            gs.AddPermanentFlag("PresentationWin");
                            gs.ChangeStanding(2);
                            gs.AddHistory("Winged the presentation and somehow pulled it off (job standing +2)");
                        }
                        else
                        {
                            gs.AddPermanentFlag("PresentationFail");
                            gs.ChangeStanding(-3);
                            gs.ChangeMood(-2);
                            gs.AddHistory("Bombed the client presentation (job standing -3)");
                        }
                    })
                        .Reply(None, () => HasP("PresentationWin")
                            ? "Pure charisma. You have no idea what you said, but they loved it."
                            : "Ten minutes in, a client asks a question you can't answer. Then another. Then another.")
                        .Reply(Boss, () => HasP("PresentationWin") ? "...Huh. Nice save." : "My office. After this.",
                            () => HasP("PresentationWin") ? Surprised : Annoyed),

                // ---------- Random afternoon at work ----------
                new GameEvent("BirthdayCake")
                    .Group("WorkAfternoon")
                    .Once()
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
                        gs.AddCount("CoworkerFriendship", -1);
                        gs.AddFlag("Productive");
                    })
                        .Reply(Coworker, "Oh. Okay. I'll... save you a slice.", Sad),

                new GameEvent("LayoffRumors")
                    .Group("WorkAfternoon")
                    .Once()
                    .When(() => day >= 2)
                    .With(Coworker, Surprised)
                    .Say(Coworker, "Psst. Layoffs are coming. I heard they're picking names by Friday.", Surprised)
                    .Say(Coworker, () => gs.standing <= 3
                        ? "And... I think I heard yours. I'm so sorry."
                        : "Don't worry, you're safe. Probably. Your name never came up.",
                        () => gs.standing <= 3 ? Sad : Neutral)
                    .Choice("Keep your head down and work", () =>
                    {
                        gs.AddFlag("Productive");
                        gs.ChangeStanding(1);
                        gs.ChangeMood(-1);
                        gs.AddHistory("Worked extra hard after hearing layoff rumors");
                    })
                    .Choice("Shrug it off", () => gs.AddHistory("Ignored the layoff rumors")),

                new GameEvent("QuietAfternoon", "The afternoon crawls by. Emails, a meeting that could've been an email, more emails.")
                    .Group("WorkAfternoon", 0.7f)
                    .Choice("Keep going"),

                // Stat-based: only shows if you're running low.
                new GameEvent("AfternoonSlump", "It's mid-afternoon and you can barely keep your eyes open.")
                    .When(() => gs.energy <= 3)
                    .Choice("Sneak a nap", () =>
                    {
                        gs.ChangeEnergy(2);
                        gs.AddTime(20);
                        if (Roll(0.35f))
                        {
                            gs.AddFlag("CaughtNapping");
                            gs.ChangeStanding(-2);
                            gs.AddHistory("Got caught napping at work (job standing -2)");
                        }
                        else
                        {
                            gs.AddHistory("Napped in the break room");
                        }
                    })
                        .Reply(None, () => Has("CaughtNapping")
                            ? "You wake up to someone clearing their throat."
                            : "Twenty glorious minutes. Nobody noticed.")
                        .Reply(Boss, () => Has("CaughtNapping") ? "Comfortable?" : "", Annoyed)
                    .Choice("Push through", () =>
                    {
                        gs.AddHistory("Pushed through the slump");
                        gs.ChangeEnergy(-1);
                    }),

                // Wednesday: payday, docked for lateness.
                new GameEvent("Payday", () =>
                    {
                        string text = $"Payday! Your check comes through: ${PayCheck()}.";
                        if (Count("TimesLate") > 0) text += $"\nA note from payroll: docked ${15 * Count("TimesLate")} for lateness.";
                        return text;
                    })
                    .When(() => isWednesday)
                    .Choice("Finally", () =>
                    {
                        gs.ChangeMoney(PayCheck());
                        gs.AddHistory($"Got paid ${PayCheck()}");
                        gs.ChangeMood(1);
                    }),

                // Friday: the boss looks back on your week.
                new GameEvent("FridayReview")
                    .When(() => isFriday)
                    .Scene(Backdrop.MeetingRoom)
                    .With(Boss)
                    .Narrate("Your boss calls you into the meeting room for your weekly review.")
                    .Say(Boss, () =>
                    {
                        if (gs.standing <= 2) return "I'm going to be blunt. You're one mistake away from being let go.";
                        if (gs.standing >= 8) return "Honestly? Best week anyone on this team has had in months.";
                        return "Solid week. Nothing to write home about, but solid.";
                    }, () => gs.standing <= 2 ? Annoyed : gs.standing >= 8 ? Happy : Neutral)
                    .Choice("Nod")
                        .Reply(Boss, () => gs.standing <= 2 ? "Close the door on your way out." : "Have a good weekend.",
                            () => gs.standing <= 2 ? Annoyed : Neutral)
                    .Choice("Ask about a raise", () =>
                    {
                        if (gs.standing >= 8)
                        {
                            gs.AddPermanentFlag("GotRaise");
                            gs.ChangeMoney(100);
                            gs.AddHistory("Got a raise, with a $100 signing bonus");
                        }
                        else
                        {
                            gs.ChangeStanding(-1);
                            gs.AddHistory("Asked for a raise and got shot down (job standing -1)");
                        }
                    }, () => gs.standing >= 5)
                        .Reply(Player, "Actually... could we talk about a raise?", Surprised)
                        .Reply(Boss, () => HasP("GotRaise")
                            ? "...You know what? You've earned it. I'll put it through today."
                            : "Let's see a few more weeks like this one first.",
                            () => HasP("GotRaise") ? Happy : Annoyed),

                FiredCheck("FiredEndOfDay"),

                new GameEvent("EndOfDay", () =>
                    {
                        string text = Has("Productive")
                            ? "It's nearly 5:00 PM and you got a lot done today."
                            : "It's nearly 5:00 PM and you didn't get much done today.";
                        if (Has("PromisedStayLate")) text += "\nYou did promise the boss you'd stay late...";
                        if (isFriday) text += "\nThe weekend is so close.";
                        return text;
                    })
                    .Choice("Leave on time", () =>
                    {
                        gs.AdvanceTo(gs.workEndMinutes);
                        gs.AddHistory("Left work on time");
                    }, () => !Has("PromisedStayLate"))
                    .Choice("Sneak out anyway", () =>
                    {
                        gs.AdvanceTo(gs.workEndMinutes);
                        gs.ChangeStanding(-3);
                        gs.AddHistory("Broke your promise to stay late (job standing -3)");
                    }, () => Has("PromisedStayLate"))
                        .Reply(None, "You make it to the elevator. Your phone buzzes. It's your boss. You don't answer.")
                    .Choice("Stay late", () =>
                    {
                        gs.AdvanceTo(gs.workEndMinutes);
                        gs.AddHistory("Stayed late to catch up");
                        gs.AddFlag("StayedLate");
                        gs.AddFlag("Productive");
                        if (Has("PromisedStayLate")) gs.ChangeStanding(1);
                        gs.ChangeMood(-1);
                        gs.ChangeEnergy(-1);
                        gs.AddTime(90);
                    })
            ),

            // =====================================================================
            // OUTSIDE WORK (evening)
            // =====================================================================
            new DayStage(Location.OutsideWork, Backdrop.OutsideWorkEvening,

                // Payoff: reporting the thug.
                new GameEvent("PoliceReward")
                    .Once()
                    .When(() => HasP("ReportedThug") && day > Count("ReportedDay"))
                    .Narrate("A police officer is waiting outside the office. \"You're the one who called in the tip? We picked him up at the warehouse last night. There's a reward.\"")
                    .Choice("Accept the $50", () =>
                    {
                        gs.ChangeMoney(50);
                        gs.AddPermanentFlag("ThugCaught");
                        gs.AddHistory("Got a $50 reward for helping catch the mugger");
                        gs.ChangeMood(2);
                    }),

                // Time-based: only if you left before 6 PM.
                new GameEvent("Drinks")
                    .When(() => gs.IsBefore(18))
                    .With(Coworker, Happy)
                    .Say(Coworker, () =>
                    {
                        if (isFriday && HasP("CoworkerOwesYou"))
                            return "It's Friday! And drinks are on me tonight. I owe you, remember?";
                        if (isFriday)
                            return "It's Friday! The whole office is heading to the bar. You're coming.";
                        return "A few of us are hitting the bar across the street. You coming?";
                    }, Happy)
                    .Choice("Join them ($15)", () =>
                    {
                        Spend(15);
                        gs.AddHistory("Went for drinks with coworkers ($15)");
                        gs.AddFlag("WentForDrinks");
                        gs.AddCount("CoworkerFriendship");
                        gs.ChangeMood(isFriday ? 3 : 2);
                        gs.ChangeEnergy(-1);
                        gs.AddTime(90);
                    }, () => Afford(15) && !(isFriday && HasP("CoworkerOwesYou")))
                        .Reply(Coworker, "That's what I like to hear!", Happy)
                    .Choice("Join them (they're buying)", () =>
                    {
                        gs.AddHistory("Your coworker bought you drinks");
                        gs.AddFlag("WentForDrinks");
                        gs.AddCount("CoworkerFriendship");
                        gs.ChangeMood(4);
                        gs.ChangeEnergy(-1);
                        gs.AddTime(90);
                    }, () => isFriday && HasP("CoworkerOwesYou"))
                        .Reply(Coworker, "To the spreadsheet savior!", Happy)
                    .Choice("Head home", () => gs.AddHistory("Skipped drinks"))
                        .Reply(Coworker, () => Afford(15) ? "Boo. Next time, then!" : "Broke, huh? Been there.", Neutral),

                // ---------- Random evening outside the office ----------
                new GameEvent("ThugOffer")
                    .Group("OfficeEvening", 2f)
                    .Once()
                    .When(() => ThugActive() && day >= 2 && day <= 4)
                    .With(Thug)
                    .Narrate("A man in a leather jacket is leaning against the wall outside the office. He falls into step beside you.")
                    .Say(Thug, () => HasP("LostWallet")
                        ? "Hey, I remember you. No hard feelings, yeah? Let me make it up to you."
                        : (gs.money < 20 ? "You look like someone who's running low on cash." : "You look like someone who could use some easy money."))
                    .Say(Thug, "Drop a package at the old warehouse tonight. A hundred bucks, cash. No questions.")
                    .Choice("Take the job", () =>
                    {
                        gs.AddFlag("AcceptedThugJob");
                        gs.AddHistory("Agreed to deliver a package to the warehouse");
                    })
                        .Reply(Thug, "Smart. Warehouse loading dock, tonight. Don't open it.", Happy)
                    .Choice("No thanks", () => gs.AddHistory("Turned down a shady job"))
                        .Reply(Thug, "Your loss.", Annoyed)
                    .Choice("Report him to the police", () =>
                    {
                        gs.AddPermanentFlag("ReportedThug");
                        gs.SetCount("ReportedDay", day);
                        gs.AddHistory("Reported the man to the police");
                        gs.AddTime(30);
                    })
                        .Reply(None, "You duck into the lobby and call the police. The officer takes your statement and says they'll look into it."),

                new GameEvent("VagrantDoorway")
                    .Group("OfficeEvening")
                    .When(() => !HasP("VagrantHoused"))
                    .With(Vagrant, Sad)
                    .Narrate(() => $"{(Kindness() >= 1 ? "Joe" : "A man in a threadbare coat")} is huddled in the office doorway, trying to stay {(rainy ? "dry" : "warm")}.")
                    .Say(Vagrant, "Evening. Cold one tonight.", Sad)
                    .Choice("Give him $10", () =>
                    {
                        Spend(10);
                        gs.AddCount("VagrantKindness");
                        gs.AddHistory("Gave Joe $10");
                        gs.ChangeMood(1);
                    }, () => Afford(10))
                        .Reply(Vagrant, "That's a hot meal and then some. Thank you.", Happy)
                    .Choice("Tell him about the shelter on 5th", () =>
                    {
                        gs.AddCount("VagrantKindness");
                        gs.AddTime(15);
                        if (Kindness() >= 3) gs.AddPermanentFlag("VagrantHoused");
                        gs.AddHistory("Walked Joe to the shelter");
                    })
                        .Reply(Vagrant, () => HasP("VagrantHoused")
                            ? "You'd walk me there? ...Nobody's done that before. Alright. Let's go."
                            : "Shelter's full most nights. But thanks for thinking of me.",
                            () => HasP("VagrantHoused") ? Happy : Sad)
                    .Choice("Walk past", () => gs.AddHistory("Walked past the man in the doorway")),

                new GameEvent("PetitionerEvening")
                    .Group("OfficeEvening", 3f)
                    .Once()
                    .When(() => HasP("MetPetitioner") && !HasP("PetitionSigned") && day <= 3)
                    .With(Petitioner)
                    .Say(Petitioner, "Still time to sign! We submit tonight. We're only a few names short.")
                    .Choice("Sign it", () =>
                    {
                        gs.AddPermanentFlag("PetitionSigned");
                        gs.AddHistory("Signed the petition at the last minute");
                    })
                        .Reply(Petitioner, "That might be the one that does it. Thank you!", Happy)
                    .Choice("Walk away", () => gs.AddHistory("Refused the petition"))
                        .Reply(Petitioner, "Don't come crying to me Thursday.", Annoyed),

                new GameEvent("QuietOfficeEvening", () => rainy
                        ? "Streetlights shimmer on the wet pavement outside the office."
                        : "The sun is setting behind the office towers.")
                    .Group("OfficeEvening", 0.8f)
                    .Choice("Head for home")
            ),

            // =====================================================================
            // COMMUTE HOME
            // =====================================================================
            new DayStage(Location.CommuteHome,

                new GameEvent("RouteHome", () =>
                    {
                        if (Has("AcceptedThugJob"))
                            return "The package under your arm is heavier than it looks. The warehouse is ten minutes away.";
                        return IsRushHour()
                            ? $"It's {gs.GetTimeString()}, peak rush hour. How do you get home?"
                            : $"It's {gs.GetTimeString()}. How do you get home?";
                    })
                    .Choice("Head to the warehouse", () =>
                    {
                        gs.AddHistory("Headed to the warehouse with the package");
                        gs.AddTime(10);
                    }, () => Has("AcceptedThugJob"))
                    .Choice("Take the bus ($3)", () =>
                    {
                        Spend(3);
                        gs.AddFlag("TookBusHome");
                        gs.AddHistory(IsRushHour() ? "Took a packed rush-hour bus home ($3)" : "Took the bus home ($3)");
                        gs.AddTime(IsRushHour() ? 65 : 40);
                    }, () => !Has("AcceptedThugJob") && !FreeBus() && Afford(3))
                    .Choice("Take the bus (free pass)", () =>
                    {
                        gs.AddFlag("TookBusHome");
                        gs.AddHistory("Took the bus home on your free pass");
                        gs.AddTime(IsRushHour() ? 65 : 40);
                    }, () => !Has("AcceptedThugJob") && FreeBus())
                    .Choice("Walk", () =>
                    {
                        gs.AddFlag("WalkedHome");
                        gs.AddHistory("Walked home");
                        gs.ChangeEnergy(-1);
                        gs.AddTime(60);
                    }, () => !Has("AcceptedThugJob"))
                    .Choice("Ride with your coworker", () =>
                    {
                        gs.AddFlag("RodeWithCoworker");
                        gs.AddHistory("Got a ride home from your coworker");
                        gs.AddTime(25);
                    }, () => !Has("AcceptedThugJob") && Count("CoworkerFriendship") >= 2)
                    .Choice("Take a taxi ($25)", () =>
                    {
                        Spend(25);
                        gs.AddFlag("TookTaxiHome");
                        gs.AddHistory("Took a taxi home ($25)");
                        gs.AddTime(20);
                    }, () => !Has("AcceptedThugJob") && Count("CoworkerFriendship") < 2 && Afford(25)),

                // ---------- The warehouse job ----------
                new GameEvent("VagrantWarning")
                    .When(() => Has("AcceptedThugJob") && Kindness() >= 1)
                    .With(Vagrant, Surprised)
                    .Narrate("Joe catches your sleeve at the corner.")
                    .Say(Vagrant, "Whoa, whoa. Wherever you're taking that box... don't. Cops have been watching that warehouse all week.", Surprised)
                    .Choice("Dump the package and go home", () =>
                    {
                        gs.RemoveFlag("AcceptedThugJob");
                        gs.AddPermanentFlag("ThugAngry");
                        gs.AddFlag("WalkedHome");
                        gs.AddHistory("Dumped the package after Joe's warning");
                        gs.AddTime(40);
                    })
                        .Reply(Vagrant, "Good call, friend. Watch your back for a while, though.", Happy)
                    .Choice("Go anyway", () =>
                    {
                        gs.AddFlag("IgnoredWarning");
                        gs.AddHistory("Ignored Joe's warning");
                    })
                        .Reply(Vagrant, "...Your funeral.", Sad),

                new GameEvent("WarehouseDrop")
                    .When(() => Has("AcceptedThugJob"))
                    .Scene(Backdrop.Warehouse)
                    .With(Thug)
                    .Narrate("The abandoned warehouse is dark. A single bulb swings over the loading dock.")
                    .Say(Thug, "Right on time. Box.")
                    .Choice("Hand it over", () =>
                    {
                        gs.AddFlag("DeliveredPackage");
                        gs.AddFlag("WalkedHome");
                        gs.ChangeMoney(100);
                        gs.AddHistory("Delivered the package for $100");
                        gs.AddTime(20);
                    })
                        .Reply(Thug, "Pleasure doing business.", Happy),

                new GameEvent("PoliceRaid")
                    .When(() => Has("DeliveredPackage") && (Has("IgnoredWarning") || Roll(0.5f)))
                    .Scene(Backdrop.Warehouse)
                    .With(Thug, Surprised)
                    .Narrate("Headlights flood the loading dock. \"POLICE! NOBODY MOVE!\"")
                    .Say(Thug, "You brought COPS?!", Surprised)
                    .Choice("Run", () =>
                    {
                        if (Roll(0.4f))
                        {
                            gs.AddFlag("WalkedHome");
                            gs.ChangeEnergy(-3);
                            gs.ChangeMood(-2);
                            gs.AddHistory("Escaped a police raid");
                        }
                        else
                        {
                            gs.AddFlag("Caught");
                            gs.ChangeMoney(-100);   // the cash is evidence
                        }
                    })
                        .Reply(None, () => Has("Caught")
                            ? "You trip over a pallet. An officer is on you in seconds."
                            : "You squeeze through a gap in the fence and don't stop running until you're home.")
                    .Choice("Put your hands up", () =>
                    {
                        gs.AddFlag("Caught");
                        gs.ChangeMoney(-100);   // the cash is evidence
                    }),

                new GameEvent("JailCell")
                    .When(() => Has("Caught"))
                    .Scene(Backdrop.JailCell)
                    .Narrate("They take the $100 as evidence. You spend the night in a holding cell that smells like old coffee and regret.")
                    .Narrate(() => $"A guard raps on the bars. \"Bail's $50. Or you can wait for the morning.\" You have ${gs.money}.")
                    .Choice("Pay bail ($50)", () =>
                    {
                        Spend(50);
                        gs.AdvanceTo(T(23, 30));
                        gs.AddHistory("Paid $50 bail");
                    }, () => Afford(50))
                    .Choice("Wait it out", () =>
                    {
                        gs.AdvanceTo(T(29));   // 5:00 AM
                        gs.AddHistory("Spent the night in a cell");
                    }),

                // After the cell: finish the day. Runs on the same stage so the choice above plays first.
                new GameEvent("Released")
                    .When(() => Has("Caught"))
                    .Scene(Backdrop.JailCell)
                    .Narrate(() => $"You're released at {gs.GetTimeString()} with a court date and a pounding headache.")
                    .Choice("Go home", () =>
                    {
                        gs.AddPermanentFlag("Arrested");
                        gs.AddFlag("Jailed");
                        gs.SetCount("JailDay", day);
                        gs.ChangeStanding(-2);
                        gs.ChangeMood(-3);
                        gs.AddHistory("Arrested at the warehouse (job standing -2)");
                        gs.GoToBed();
                        gs.EndDayEarly();
                    }),

                // ---------- Bus home (random) ----------
                new GameEvent("QuietBusEvening", "The bus hums through the evening traffic. You nearly doze off against the window.")
                    .Group("BusEvening")
                    .Scene(Backdrop.BusEvening)
                    .When(() => Has("TookBusHome"))
                    .Choice("Close your eyes", () => gs.ChangeEnergy(1)),

                new GameEvent("BusDriverEvening")
                    .Group("BusEvening")
                    .Scene(Backdrop.BusEvening)
                    .When(() => Has("TookBusHome"))
                    .With(BusDriver)
                    .Say(BusDriver, () => IsRushHour() ? "Squeeze in, folks! Plenty of room in the back! There's no room in the back." : "Long day? You look it.")
                    .Choice("Laugh", () => gs.ChangeMood(1))
                    .Choice("Sigh"),

                new GameEvent("PetitionerNews")
                    .Group("BusEvening", 3f)
                    .Once()
                    .Scene(Backdrop.BusEvening)
                    .When(() => Has("TookBusHome") && HasP("PetitionSigned") && day <= 3)
                    .With(Petitioner, Happy)
                    .Say(Petitioner, "Hey, it's you! We're submitting the petition tonight. Fingers crossed!", Happy)
                    .Choice("Good luck!", () => gs.ChangeMood(1)),

                // ---------- Car home ----------
                new GameEvent("CoworkerRide")
                    .When(() => Has("RodeWithCoworker"))
                    .Scene(Backdrop.CarEvening)
                    .With(Coworker, Happy)
                    .Narrate("Your coworker's car smells like air freshener and old fries.")
                    .Say(Coworker, "So... honestly. How are you liking it here?", Happy)
                    .Choice("Honestly? It's rough.", () =>
                    {
                        gs.AddCount("CoworkerFriendship");
                        gs.ChangeMood(1);
                    })
                        .Reply(Coworker, "Same. Glad it's not just me. Same time tomorrow?", Happy)
                    .Choice("It's great!", () => gs.ChangeMood(1))
                        .Reply(Coworker, "Liar. But okay.", Happy),

                new GameEvent("TaxiRide", "The meter ticks up faster than you'd like. City lights blur past the window.")
                    .When(() => Has("TookTaxiHome"))
                    .Scene(Backdrop.CarEvening)
                    .Choice("Watch the lights", () => gs.ChangeEnergy(1)),

                // ---------- Walking home ----------
                new GameEvent("WalkHome", () => rainy && !Has("HasUmbrella")
                        ? "The rain hasn't let up. You're soaked by the second block."
                        : "Streetlights flicker on as you walk. The city is winding down.")
                    .When(() => Has("WalkedHome") && !Has("Caught"))
                    .Choice("Keep walking", () =>
                    {
                        if (rainy && !Has("HasUmbrella")) gs.ChangeMood(-1);
                    })
            ),

            // =====================================================================
            // OUTSIDE HOME (evening)
            // =====================================================================
            new DayStage(Location.OutsideHome, Backdrop.OutsideHomeEvening,

                // Payoff: kindness to Joe gets your things back.
                new GameEvent("VagrantReturns")
                    .Once()
                    .When(() => HasP("LostWallet") && Kindness() >= 2)
                    .With(Vagrant, Happy)
                    .Narrate("Joe is sitting on your front step, holding something.")
                    .Say(Vagrant, "Found these in a dumpster behind the warehouse. Cash was gone, but I figured you'd want the rest.", Happy)
                    .Choice("Joe... thank you.", () =>
                    {
                        gs.RemovePermanentFlag("LostWallet");
                        gs.RemovePermanentFlag("LostPhone");
                        gs.ChangeMood(3);
                        gs.AddHistory("Joe returned your wallet and phone");
                    })
                        .Reply(Vagrant, "You looked out for me. I look out for you. That's how it works.", Happy),

                new GameEvent("DogOnDoorstep")
                    .When(() => !HasDog() && !HasP("DogGone") && Count("DogTrust") >= 2)
                    .With(Dog, Happy)
                    .Narrate("The scruffy dog from this morning is sitting on your doorstep like it lives here.")
                    .Say(Dog, "*thump thump thump* goes its tail.", Happy)
                    .Choice("Adopt it ($20 for supplies)", () =>
                    {
                        Spend(20);
                        gs.AddPermanentFlag("HasDog");
                        gs.ChangeMood(3);
                        gs.AddHistory("Adopted the stray dog ($20)");
                    }, () => Afford(20))
                        .Reply(Dog, "*zooms in circles around your living room*", Happy)
                    .Choice("Adopt it anyway", () =>
                    {
                        gs.AddPermanentFlag("HasDog");
                        gs.ChangeMood(3);
                        gs.AddHistory("Adopted the stray dog");
                    }, () => !Afford(20))
                        .Reply(Dog, "*curls up on your only towel*", Happy)
                    .Choice("Take it to the shelter", () =>
                    {
                        gs.AddPermanentFlag("DogGone");
                        gs.ChangeMood(-1);
                        gs.AddTime(30);
                        gs.AddHistory("Took the stray dog to the shelter");
                    })
                        .Reply(Dog, "*looks back at you from the kennel*", Sad),

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
                        gs.ChangeEnergy(1);
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

                new GameEvent("Doorstep", () => PowerOff()
                        ? $"It's {gs.GetTimeString()}. Every house on the street has lights on except yours."
                        : $"It's {gs.GetTimeString()}. The porch light flickers as you find your keys.")
                    .When(() => !CookiesShow() && !ColdShoulderShows())
                    .Choice("Go inside")
            ),

            // =====================================================================
            // HOME (evening)
            // =====================================================================
            new DayStage(Location.Home,

                // Consequence: ignored bill.
                new GameEvent("PowerCut", () => HasP("PowerCut")
                        ? "Still no power. The dark apartment is getting old."
                        : "You flip the light switch. Nothing. A red notice is taped to the fridge: DISCONNECTED FOR NON-PAYMENT.")
                    .When(() => HasP("IgnoredBill") && !HasP("PaidBill") && day >= 4)
                    .Choice("Pay the bill + late fee ($45)", () =>
                    {
                        Spend(45);
                        gs.AddPermanentFlag("PaidBill");
                        gs.AddHistory("Paid the bill and late fee to get the power back ($45)");
                    }, () => Afford(45))
                        .Reply(None, "Twenty minutes on hold later, the lights flicker back on.")
                    .Choice("Sit in the dark", () =>
                    {
                        gs.AddPermanentFlag("PowerCut");
                        gs.ChangeMood(-2);
                        gs.AddHistory("Spent the evening without power");
                    }),

                new GameEvent("Dinner", () => gs.IsAfter(20)
                        ? "It's late and you're starving. What about dinner?"
                        : "You're finally home. What about dinner?")
                    .Choice("Cook something", () =>
                    {
                        gs.AddHistory("Cooked dinner");
                        gs.ChangeEnergy(1);
                        gs.ChangeMood(1);
                        gs.AddTime(45);
                    }, () => gs.energy >= 2 && !PowerOff())
                    .Choice("Order takeout ($15)", () =>
                    {
                        Spend(15);
                        gs.AddHistory("Ordered takeout ($15)");
                        gs.ChangeMood(1);
                        gs.AddTime(30);
                    }, () => Afford(15))
                    .Choice("Eat crackers over the sink", () =>
                    {
                        gs.AddHistory("Had crackers for dinner");
                        gs.ChangeEnergy(-1);
                        gs.ChangeMood(-1);
                    }, () => !Afford(15) || PowerOff()),

                // Consequence: having a dog is a responsibility.
                new GameEvent("FeedDog")
                    .When(HasDog)
                    .With(Dog, Sad)
                    .Say(Dog, "*stares at the empty bowl. Then at you. Then at the bowl.*", Sad)
                    .Choice("Feed it ($5)", () =>
                    {
                        Spend(5);
                        gs.ChangeMood(1);
                        gs.AddHistory("Fed the dog ($5)");
                    }, () => Afford(5))
                        .Reply(Dog, "*happy crunching noises*", Happy)
                    .Choice("Share your dinner", () =>
                    {
                        gs.ChangeEnergy(-1);
                        gs.ChangeMood(1);
                        gs.AddHistory("Shared your dinner with the dog");
                    })
                        .Reply(Dog, "*licks the plate clean. And your hand.*", Happy)
                    .Choice("Forget it", () =>
                    {
                        gs.SetCount("ShoesChewedDay", day);
                        gs.AddHistory("Forgot to feed the dog");
                    })
                        .Reply(Dog, "*sulks off toward the front door, where your shoes are*", Annoyed),

                // Consequence: no phone = no alarm.
                new GameEvent("ReplacePhone", "Without a phone, you won't have an alarm tomorrow.")
                    .When(LostPhone)
                    .Choice("Buy a cheap phone ($40)", () =>
                    {
                        Spend(40);
                        gs.RemovePermanentFlag("LostPhone");
                        gs.AddHistory("Bought a replacement phone ($40)");
                    }, () => Afford(40))
                    .Choice("Go without", () => gs.AddHistory("Went without a phone")),

                // Bedtime decides how much energy you get back tomorrow.
                new GameEvent("Bedtime", () =>
                    {
                        string text = gs.energy <= 2
                            ? $"It's {gs.GetTimeString()} and you're completely wiped out."
                            : $"It's {gs.GetTimeString()}. Time to wind down.";
                        if (HasDog()) text += "\nYour dog is already snoring at the foot of the bed.";
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
                    }, () => !PowerOff())
            )
        };

    }
}