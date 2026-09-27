using System.Collections.Generic;
using UnityEngine;

// Stores everything about the player's week.
// Put this on an empty GameObject called "GameState" in your scene.
public class GameState : MonoBehaviour
{
    public static GameState Instance { get; private set; }

    // Daily flag: set it to skip the rest of the day (e.g. after a night in jail).
    public const string EndDayFlag = "EndDayNow";

    private static readonly string[] DayNames =
        { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    [Header("Week")]
    public int totalDays = 5;
    [Range(0f, 1f)] public float rainChance = 0.35f;

    [Header("Starting Values")]
    public int startTimeMinutes = 390;   // 6:30 AM (minutes since midnight)
    public int startEnergy = 5;
    public int startMood = 5;
    public int startMoney = 80;
    public int startStanding = 5;        // job standing, 0-10. Hits 0 = fired.

    [Header("Work Hours")]
    public int workStartMinutes = 480;   // 8:00 AM
    public int workEndMinutes = 1020;    // 5:00 PM

    [Header("Current State (read-only at runtime)")]
    public int currentDay = 1;
    public int timeMinutes;
    public int energy;
    public int mood;
    public int money;
    public int standing;
    public bool isRainy;
    public int bedTimeMinutes;
    public Location currentLocation;
    public bool weekOver;          // true = the week ended early (e.g. fired)
    public string weekOverReason;

    // Cleared every morning (e.g. "AteBreakfast", "LateToWork").
    private readonly HashSet<string> flags = new HashSet<string>();

    // Last the whole week (e.g. "HasDog", "LostPhone").
    private readonly HashSet<string> permanentFlags = new HashSet<string>();

    // Numbers that build up over the week (e.g. "TimesLate", "VagrantKindness").
    private readonly Dictionary<string, int> counters = new Dictionary<string, int>();

    private readonly List<(int day, string text)> history = new List<(int, string)>();
    private readonly List<string> dayRecaps = new List<string>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResetState();
    }

    // ================= WEEK / DAY =================

    // Full reset for a brand-new week.
    public void ResetState()
    {
        currentDay = 1;
        timeMinutes = startTimeMinutes;
        energy = startEnergy;
        mood = startMood;
        money = startMoney;
        standing = startStanding;
        bedTimeMinutes = 0;
        currentLocation = Location.Home;
        weekOver = false;
        weekOverReason = null;

        flags.Clear();
        permanentFlags.Clear();
        counters.Clear();
        history.Clear();
        dayRecaps.Clear();

        RollWeather();
    }

    // Call when a day's events are finished, before showing the summary.
    public void EndDay()
    {
        if (HasFlag("Productive"))
        {
            AddCount("ProductiveDays");
            ChangeStanding(1);
        }

        string how;
        if (HasFlag("Jailed")) how = "spent the night in jail";
        else if (HasFlag("LateToWork")) how = $"late to work, in bed by {FormatTime(bedTimeMinutes)}";
        else how = $"on time, in bed by {FormatTime(bedTimeMinutes)}";

        dayRecaps.Add($"{DayName}: {how}, ${money} left");
    }

    // Moves to the next morning. Energy recovers based on when you went to bed.
    public void StartNewDay()
    {
        currentDay++;

        int recovery;
        if (bedTimeMinutes <= TimeOf(22)) recovery = 4;
        else if (bedTimeMinutes <= TimeOf(23, 30)) recovery = 2;
        else recovery = 0;

        ChangeEnergy(recovery);

        // No phone = no alarm. You wake up late.
        timeMinutes = HasPermanentFlag("LostPhone") ? TimeOf(7, 5) : startTimeMinutes;

        currentLocation = Location.Home;
        flags.Clear();
        RollWeather();
    }

    public void GoToBed()
    {
        bedTimeMinutes = timeMinutes;
        AddHistory("Went to bed");
    }

    // Skip the rest of today's events and go straight to the day summary.
    public void EndDayEarly() => AddFlag(EndDayFlag);

    // End the whole week right now (e.g. fired). Shows the week summary.
    public void EndWeek(string reason)
    {
        weekOver = true;
        weekOverReason = reason;
    }

    private void RollWeather()
    {
        isRainy = Random.value < rainChance;
    }

    public bool IsLastDay => currentDay >= totalDays || weekOver;
    public string DayName => DayNames[(currentDay - 1) % DayNames.Length];

    public IReadOnlyList<string> GetDayRecaps() => dayRecaps;

    // ================= TIME =================

    // Converts a clock time to minutes since midnight. TimeOf(17, 30) = 5:30 PM.
    public static int TimeOf(int hour, int minute = 0)
    {
        return hour * 60 + minute;
    }

    public void AddTime(int minutes)
    {
        timeMinutes += minutes;
    }

    // Jumps forward to a time, but never backwards.
    public void AdvanceTo(int targetMinutes)
    {
        if (timeMinutes < targetMinutes)
            timeMinutes = targetMinutes;
    }

    // True if it's earlier than hour:minute.
    public bool IsBefore(int hour, int minute = 0) => timeMinutes < TimeOf(hour, minute);

    // True if it's hour:minute or later.
    public bool IsAfter(int hour, int minute = 0) => timeMinutes >= TimeOf(hour, minute);

    public bool IsLateForWork() => timeMinutes > workStartMinutes;

    public string GetTimeString() => FormatTime(timeMinutes);

    public static string FormatTime(int totalMinutes)
    {
        int hours = (totalMinutes / 60) % 24;
        int minutes = totalMinutes % 60;
        string suffix = hours >= 12 ? "PM" : "AM";

        int displayHours = hours % 12;
        if (displayHours == 0) displayHours = 12;

        return $"{displayHours}:{minutes:D2} {suffix}";
    }

    // ================= STATS =================

    public void ChangeEnergy(int amount)
    {
        energy = Mathf.Clamp(energy + amount, 0, 10);
    }

    public void ChangeMood(int amount)
    {
        mood = Mathf.Clamp(mood + amount, 0, 10);
    }

    public void ChangeMoney(int amount)
    {
        money = Mathf.Max(0, money + amount);
    }

    public bool CanAfford(int amount) => money >= amount;

    public void ChangeStanding(int amount)
    {
        standing = Mathf.Clamp(standing + amount, 0, 10);
    }

    // ================= FLAGS & COUNTERS =================

    public void AddFlag(string flag) => flags.Add(flag);
    public bool HasFlag(string flag) => flags.Contains(flag);
    public void RemoveFlag(string flag) => flags.Remove(flag);

    public void AddPermanentFlag(string flag) => permanentFlags.Add(flag);
    public bool HasPermanentFlag(string flag) => permanentFlags.Contains(flag);
    public void RemovePermanentFlag(string flag) => permanentFlags.Remove(flag);

    public void AddCount(string key, int amount = 1)
    {
        counters.TryGetValue(key, out int current);
        counters[key] = current + amount;
    }

    public void SetCount(string key, int value)
    {
        counters[key] = value;
    }

    public int GetCount(string key)
    {
        counters.TryGetValue(key, out int value);
        return value;
    }

    // ================= HISTORY =================

    public void AddHistory(string entry)
    {
        history.Add((currentDay, $"[{GetTimeString()}] {entry}"));
        Debug.Log($"{DayName} history: {entry}");
    }

    public List<string> GetHistoryForDay(int day)
    {
        var result = new List<string>();
        foreach (var h in history)
        {
            if (h.day == day) result.Add(h.text);
        }
        return result;
    }
}