using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class EventUI : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text eventText;
    public Button[] choiceButtons;
    public TMP_Text statusText;   

    [Header("Summary Panel")]
    public GameObject eventPanel;    
    public GameObject summaryPanel;
    public TMP_Text summaryText;

    private string currentEvent;

    void Start()
    {
        summaryPanel.SetActive(false);
        ShowAlarmEvent();
    }


    public void ChooseOption(int option)
    {
        switch (currentEvent)
        {
            case "Alarm": HandleAlarmChoice(option); break;
            case "Breakfast": HandleBreakfastChoice(option); break;
            case "LeaveHouse": HandleLeaveHouseChoice(option); break;
            case "Neighbor": HandleNeighborChoice(option); break;
            case "Route": HandleRouteChoice(option); break;
            case "Arrive": ShowSummary(); break;
            default:
                Debug.LogWarning($"No handler for event: {currentEvent}");
                break;
        }
    }


    private void ShowAlarmEvent()
    {
        currentEvent = "Alarm";
        SetEvent("Your alarm goes off at 6:30 AM.",
                 "1. Get up",
                 "2. Snooze");
    }

    private void HandleAlarmChoice(int option)
    {
        var gs = GameState.Instance;

        if (option == 0)
        {
            gs.AddHistory("Got up when the alarm went off");
        }
        else
        {
            gs.AddTime(10);
            gs.ChangeEnergy(1);
            gs.AddFlag("SnoozedAlarm");
            gs.AddHistory("Snoozed the alarm for 10 minutes");
        }

        gs.AddTime(30); 
        ShowBreakfastEvent();
    }


    private void ShowBreakfastEvent()
    {
        currentEvent = "Breakfast";

        string text = GameState.Instance.HasFlag("SnoozedAlarm")
            ? "You're dressed, but running a little behind. Eat breakfast anyway?"
            : "You're dressed with some time to spare. Eat breakfast?";

        SetEvent(text, "1. Eat breakfast", "2. Skip breakfast");
    }

    private void HandleBreakfastChoice(int option)
    {
        var gs = GameState.Instance;

        if (option == 0)
        {
            gs.AddTime(15);
            gs.ChangeEnergy(2);
            gs.AddFlag("AteBreakfast");
            gs.AddHistory("Ate breakfast");
        }
        else
        {
            gs.ChangeEnergy(-1);
            gs.AddFlag("SkippedBreakfast");
            gs.AddHistory("Skipped breakfast");
        }

        ShowLeaveHouseEvent();
    }


    private void ShowLeaveHouseEvent()
    {
        currentEvent = "LeaveHouse";
        SetEvent("You grab your keys. The sky looks cloudy. Take an umbrella?",
                 "1. Take umbrella",
                 "2. Leave it");
    }

    private void HandleLeaveHouseChoice(int option)
    {
        var gs = GameState.Instance;

        if (option == 0)
        {
            gs.AddFlag("HasUmbrella");
            gs.AddHistory("Took an umbrella");
        }
        else
        {
            gs.AddHistory("Left the umbrella at home");
        }

        ShowNeighborEvent();
    }


    private void ShowNeighborEvent()
    {
        currentEvent = "Neighbor";
        SetEvent("Your neighbor waves you over. They look like they want to chat.",
                 "1. Stop and chat",
                 "2. Wave and keep walking");
    }

    private void HandleNeighborChoice(int option)
    {
        var gs = GameState.Instance;

        if (option == 0)
        {
            gs.AddTime(15);
            gs.ChangeMood(1);
            gs.AddFlag("ChattedWithNeighbor");
            gs.AddHistory("Chatted with the neighbor");
        }
        else
        {
            gs.ChangeMood(-1);
            gs.AddFlag("IgnoredNeighbor");
            gs.AddHistory("Brushed off the neighbor");
        }

        ShowRouteEvent();
    }


    private void ShowRouteEvent()
    {
        currentEvent = "Route";
        SetEvent($"It's {GameState.Instance.GetTimeString()}. How do you get to work?",
                 "1. Take the bus",
                 "2. Walk");
    }

    private void HandleRouteChoice(int option)
    {
        var gs = GameState.Instance;

        if (option == 0)
        {
            gs.AddTime(45);
            gs.AddHistory("Took the bus");

            if (gs.HasFlag("SnoozedAlarm"))
            {
                gs.AddTime(10);
                gs.AddHistory("Missed the usual bus and waited for the next one");
            }
        }
        else
        {
            gs.AddTime(60);
            gs.ChangeEnergy(-1);
            gs.AddHistory("Walked to work");

            if (gs.HasFlag("SkippedBreakfast"))
            {
                gs.ChangeEnergy(-1);
                gs.AddHistory("Felt light-headed on the walk");
            }

            if (!gs.HasFlag("HasUmbrella"))
            {
                gs.ChangeMood(-2);
                gs.AddFlag("GotWet");
                gs.AddHistory("Got caught in the rain");
            }
        }

        ShowArriveEvent();
    }


    private void ShowArriveEvent()
    {
        currentEvent = "Arrive";
        var gs = GameState.Instance;

        string text = gs.IsLate()
            ? $"You arrive at {gs.GetTimeString()}, {gs.MinutesLate()} minutes late. Your boss notices."
            : $"You arrive at {gs.GetTimeString()}. Right on time.";

        if (gs.HasFlag("GotWet"))
            text += "\nYou're also dripping wet.";

        SetEvent(text, "See summary");
    }



    private void ShowSummary()
    {
        currentEvent = "Summary";
        var gs = GameState.Instance;

        var sb = new StringBuilder();
        sb.AppendLine(gs.IsLate() ? "<b>You were late.</b>" : "<b>You made it on time!</b>");
        sb.AppendLine($"Energy: {gs.energy}   Mood: {gs.mood}");
        sb.AppendLine();

        foreach (string entry in gs.GetHistory())
            sb.AppendLine(entry);

        summaryText.text = sb.ToString();

        if (eventPanel != null) eventPanel.SetActive(false);
        summaryPanel.SetActive(true);
    }

    public void RestartDay()
    {
        summaryPanel.SetActive(false);
        if (eventPanel != null) eventPanel.SetActive(true);

        GameState.Instance.ResetState();
        ShowAlarmEvent();
    }

 
    private void SetEvent(string text, params string[] choices)
    {
        eventText.text = text;

        for (int i = 0; i < choiceButtons.Length; i++)
        {
            bool used = i < choices.Length;
            choiceButtons[i].gameObject.SetActive(used);

            if (used)
                choiceButtons[i].GetComponentInChildren<TMP_Text>().text = choices[i];
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (statusText == null) return;

        var gs = GameState.Instance;
        statusText.text = $"{gs.GetTimeString()}   Energy: {gs.energy}   Mood: {gs.mood}";
    }
}