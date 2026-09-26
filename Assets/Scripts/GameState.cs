using System.Collections.Generic;
using UnityEngine;


public class GameState : MonoBehaviour
{
    public static GameState Instance { get; private set; }

    [Header("Starting Values")]
    public int startTimeMinutes = 390;  
    public int startEnergy = 5;
    public int startMood = 5;

    [Header("Work")]
    public int workStartMinutes = 480;  

    [Header("Current State (read-only at runtime)")]
    public int timeMinutes;
    public int energy;
    public int mood;

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
        flags.Clear();
        history.Clear();
    }


    public void AddTime(int minutes)
    {
        timeMinutes += minutes;
    }

    public void ChangeEnergy(int amount)
    {
        energy = Mathf.Clamp(energy + amount, 0, 10);
    }

    public void ChangeMood(int amount)
    {
        mood = Mathf.Clamp(mood + amount, 0, 10);
    }


    public void AddFlag(string flag)
    {
        flags.Add(flag);
    }

    public bool HasFlag(string flag)
    {
        return flags.Contains(flag);
    }

    public void RemoveFlag(string flag)
    {
        flags.Remove(flag);
    }


    public void AddHistory(string entry)
    {
        history.Add($"[{GetTimeString()}] {entry}");
        Debug.Log($"History: {entry}");
    }

    public IReadOnlyList<string> GetHistory()
    {
        return history;
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

    public bool IsLate()
    {
        return timeMinutes > workStartMinutes;
    }

    public int MinutesLate()
    {
        return Mathf.Max(0, timeMinutes - workStartMinutes);
    }
}