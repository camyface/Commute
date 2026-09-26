using System.Collections.Generic;
using UnityEngine;

// Stores everything about the player's day.
// Put this on an empty GameObject called "GameState" in your scene.
public class GameState : MonoBehaviour
{
    public static GameState Instance { get; private set; }

    [Header("Starting Values")]
    public int startTimeMinutes = 390;   // 6:30 AM (minutes since midnight)
    public int startEnergy = 5;
    public int startMood = 5;

    [Header("Work Hours")]
    public int workStartMinutes = 480;   // 8:00 AM
    public int workEndMinutes = 1020;    // 5:00 PM

    [Header("Current State (read-only at runtime)")]
    public int timeMinutes;
    public int energy;
    public int mood;
    public Location currentLocation;

    private readonly HashSet<string> flags = new HashSet<string>();
    private readonly List<string> history = new List<string>();

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

    public void ResetState()
    {
        timeMinutes = startTimeMinutes;
        energy = startEnergy;
        mood = startMood;
        currentLocation = Location.Home;
        flags.Clear();
        history.Clear();
    }

    // ---------- Time ----------

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
    public bool IsBefore(int hour, int minute = 0)
    {
        return timeMinutes < TimeOf(hour, minute);
    }

    // True if it's hour:minute or later.
    public bool IsAfter(int hour, int minute = 0)
    {
        return timeMinutes >= TimeOf(hour, minute);
    }

    public bool IsLateForWork()
    {
        return timeMinutes > workStartMinutes;
    }

    public string GetTimeString()
    {
        int hours = (timeMinutes / 60) % 24;
        int minutes = timeMinutes % 60;
        string suffix = hours >= 12 ? "PM" : "AM";

        int displayHours = hours % 12;
        if (displayHours == 0) displayHours = 12;

        return $"{displayHours}:{minutes:D2} {suffix}";
    }

    // ---------- Stats ----------

    public void ChangeEnergy(int amount)
    {
        energy = Mathf.Clamp(energy + amount, 0, 10);
    }

    public void ChangeMood(int amount)
    {
        mood = Mathf.Clamp(mood + amount, 0, 10);
    }

    // ---------- Flags ----------

    public void AddFlag(string flag) => flags.Add(flag);
    public bool HasFlag(string flag) => flags.Contains(flag);
    public void RemoveFlag(string flag) => flags.Remove(flag);

    // ---------- History ----------

    public void AddHistory(string entry)
    {
        history.Add($"[{GetTimeString()}] {entry}");
        Debug.Log($"History: {entry}");
    }

    public IReadOnlyList<string> GetHistory()
    {
        return history;
    }
}