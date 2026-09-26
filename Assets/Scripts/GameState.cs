using System.Collections.Generic;
using UnityEngine;

// Stores everything about the player's week.
// Put this on an empty GameObject called "GameState" in your scene.
public class GameState : MonoBehaviour
{
    public static GameState Instance { get; private set; }

    private static readonly string[] DayNames =
        { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    [Header("Week")]
    public int totalDays = 5;

    [Header("Starting Values")]
    public int startTimeMinutes = 390;   // 6:30 AM (minutes since midnight)
    public int startEnergy = 5;
    public int startMood = 5;

    [Header("Work Hours")]
    public int workStartMinutes = 480;   // 8:00 AM
    public int workEndMinutes = 1020;    // 5:00 PM

    [Header("Current State (read-only at runtime)")]
    public int currentDay = 1;
    public int timeMinutes;
    public int energy;
    public int mood;
    public int bedTimeMinutes;
    public Location currentLocation;

    // Cleared every morning (e.g. "AteBreakfast", "LateToWork").
    private readonly HashSet<string> flags = new HashSet<string>();

    // Last the whole week (e.g. "GotCookies").
    private readonly HashSet<string> permanentFlags = new HashSet<string>();

    // Numbers that build up over the week (e.g. "TimesLate", "NeighborFriendship").
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
        bedTimeMinutes = 0;
        currentLocation = Location.Home;

        flags.Clear();
        permanentFlags.Clear();
        counters.Clear();
        history.Clear();
        dayRecaps.Clear();
    }

    // Call when a day's events are finished, before showing the summary.
    public void EndDay()
    {
        if (HasFlag("Productive")) AddCount("ProductiveDays");

        string arrival = HasFlag("LateToWork") ? "late to work" : "on time";
        dayRecaps.Add($"{DayName}: {arrival}, in bed by {FormatTime(bedTimeMinutes)}");
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

        timeMinutes = startTimeMinutes;
        currentLocation = Location.Home;
        flags.Clear();
    }

    public void GoToBed()
    {
        bedTimeMinutes = timeMinutes;
        AddHistory("Went to bed");
    }

    public bool IsLastDay => currentDay >= totalDays;
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

    // ================= FLAGS & COUNTERS =================

    public void AddFlag(string flag) => flags.Add(flag);
    public bool HasFlag(string flag) => flags.Contains(flag);
    public void RemoveFlag(string flag) => flags.Remove(flag);

    public void AddPermanentFlag(string flag) => permanentFlags.Add(flag);
    public bool HasPermanentFlag(string flag) => permanentFlags.Contains(flag);

    public void AddCount(string key, int amount = 1)
    {
        counters.TryGetValue(key, out int current);
        counters[key] = current + amount;
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