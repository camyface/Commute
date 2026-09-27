using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Pairs a location with its background image (filled in from the Inspector).
[Serializable]
public class LocationBackground
{
    public Location location;
    public Sprite sprite;
}

// A character's name and portraits (filled in from the Inspector).
// Only Neutral is required; missing expressions fall back to it.
[Serializable]
public class CharacterData
{
    public CharacterId id;
    public string displayName;
    public Sprite neutral;
    public Sprite happy;
    public Sprite annoyed;
    public Sprite sad;
    public Sprite surprised;

    public Sprite GetSprite(Expression expression)
    {
        Sprite sprite = null;
        switch (expression)
        {
            case Expression.Happy: sprite = happy; break;
            case Expression.Annoyed: sprite = annoyed; break;
            case Expression.Sad: sprite = sad; break;
            case Expression.Surprised: sprite = surprised; break;
        }
        return sprite != null ? sprite : neutral;
    }
}

// Runs the week and shows events on screen.
// Put this on an empty GameObject called "EventManager".
//
// Button wiring (do this once, in the Inspector):
//   ChoiceButton1  -> EventUI.ChooseOption(0)
//   ChoiceButton2  -> EventUI.ChooseOption(1)
//   SummaryButton  -> EventUI.SummaryButtonPressed()
public class EventUI : MonoBehaviour
{
    [Header("Event Panel")]
    public GameObject eventPanel;
    public TMP_Text eventText;
    public Button[] choiceButtons;

    [Header("HUD")]
    public TMP_Text timeText;      // day + clock in the top-right corner
    public TMP_Text statusText;    // optional: energy / mood

    [Header("Backgrounds")]
    public Image backgroundImage;
    public LocationBackground[] backgrounds;

    [Header("Characters")]
    public Image characterImage;       // the other character (right side)
    public Image playerImage;          // the player (left side)
    [Tooltip("Color of the speaker's name in front of their line, e.g. \"Name: Hello there!\"")]
    public Color speakerNameColor = new Color(1f, 0.85f, 0.4f, 1f);
    public CharacterData[] characters; // include an entry with id Player for the player's portraits
    public string continueLabel = "Continue";

    [Header("Speaker Highlight")]
    public Color speakingColor = Color.white;
    public Color listeningColor = new Color(0.55f, 0.55f, 0.55f, 1f);  // the "shadow"
    public float highlightFadeSpeed = 10f;

    private Color characterTargetColor = Color.white;
    private Color playerTargetColor = Color.white;
    private Expression playerExpression = Expression.Neutral;

    [Header("Sounds")]
    public AudioSource ambientAudioSource;
    public AudioSource specialAudioSource;
    public AudioClip[] ambientAudioClips;   // one per Location, in enum order
    [Serializable]
    public class SpecialAudioClip
    {
        public string id;       // e.g. "Doorbell", "CarHorn"
        public AudioClip clip;
    }
    public List<SpecialAudioClip> specialAudioClips;

    [Header("Summary Panel")]
    public GameObject summaryPanel;
    public TMP_Text summaryText;
    public TMP_Text summaryButtonText;   // label on the summary panel's button
    public ScrollRect summaryScroll;     // optional: the Scroll View around SummaryText

    private List<DayStage> day;
    private int stageIndex;
    private int eventIndex;
    private List<EventChoice> currentChoices = new List<EventChoice>();
    private System.Random random = new System.Random();

    // Dialogue playback
    private readonly Queue<DialogueLine> lineQueue = new Queue<DialogueLine>();
    private List<EventChoice> choicesAfterLines;   // null = go to next event when lines finish
    private bool waitingForContinue;

    // Lets event code call things like EventUI.Instance.PlaySpecial(0).
    public static EventUI Instance { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        StartWeek();
    }

    // Smoothly fades portraits toward lit / shadowed.
    void Update()
    {
        float t = Time.deltaTime * highlightFadeSpeed;

        if (characterImage != null)
            characterImage.color = Color.Lerp(characterImage.color, characterTargetColor, t);

        if (playerImage != null)
            playerImage.color = Color.Lerp(playerImage.color, playerTargetColor, t);
    }

    // ================= BUTTONS =================

    public void ChooseOption(int option)
    {
        // Mid-conversation: the only button is "Continue".
        if (waitingForContinue)
        {
            waitingForContinue = false;
            ShowNextLine();
            return;
        }

        if (option < 0 || option >= currentChoices.Count) return;

        EventChoice choice = currentChoices[option];
        choice.onChoose?.Invoke();

        // Let the character react before moving on.
        if (choice.replies.Count > 0)
            PlayLines(choice.replies, null);
        else
            ShowNextEvent();
    }

    // "Next day" after days 1-4, "Play again" after day 5.
    public void SummaryButtonPressed()
    {
        if (GameState.Instance.IsLastDay)
        {
            StartWeek();
        }
        else
        {
            GameState.Instance.StartNewDay();
            StartDay();
        }
    }

    // ================= WEEK / DAY FLOW =================

    private void StartWeek()
    {
        GameState.Instance.ResetState();
        StartDay();
    }

    private void StartDay()
    {
        day = DayEvents.BuildDay();
        stageIndex = 0;
        eventIndex = 0;

        summaryPanel.SetActive(false);
        if (eventPanel != null) eventPanel.SetActive(true);

        ShowNextEvent();
    }

    // Finds the next event whose conditions pass, moving through locations in order.
    private void ShowNextEvent()
    {
        while (stageIndex < day.Count)
        {
            DayStage stage = day[stageIndex];

            while (eventIndex < stage.events.Count)
            {
                GameEvent ev;
                if (GetRadiantEventChance() && RadiantEvents.gameEvents.ContainsKey(stage.location))
                {
                    ev = RadiantEvents.gameEvents.TryGetValue(stage.location, out GameEvent @event) ? @event : null;
                }
                else
                {
                    ev = stage.events[eventIndex];
                    eventIndex++;
                }

                if (ev.CanShow())
                {
                    ShowEvent(ev, stage.location);
                    return;
                }
            }

            stageIndex++;
            eventIndex = 0;
        }

        ShowDaySummary();
    }

    private bool GetRadiantEventChance()
    {
        return random.NextDouble() < 0.1; // 10% chance for a radiant event
    }

    private void ShowEvent(GameEvent ev, Location location)
    {
        GameState.Instance.currentLocation = location;
        SetBackground(location);
        SetAudio(location);

        // Start with whoever is "present", or nobody.
        // The player appears whenever someone else is on screen.
        playerExpression = Expression.Neutral;
        ShowPortrait(ev.presentCharacter, ev.presentExpression);
        ShowPlayer(ev.presentCharacter != CharacterId.None || EventHasPlayerLines(ev));
        SetSpeaker(CharacterId.None, instant: true);

        List<EventChoice> choices = ev.GetAvailableChoices();
        if (choices.Count == 0)
            choices.Add(new EventChoice { label = continueLabel });

        if (choices.Count > choiceButtons.Length)
            Debug.LogWarning($"Event '{ev.id}' has {choices.Count} choices but only {choiceButtons.Length} buttons.");

        PlayLines(ev.lines, choices);
    }

    // ================= DIALOGUE =================

    // Plays lines one at a time. Shows the choices on the last line,
    // or moves to the next event if there are none (used for replies).
    private void PlayLines(List<DialogueLine> lines, List<EventChoice> choicesAtEnd)
    {
        lineQueue.Clear();
        foreach (var line in lines)
            lineQueue.Enqueue(line);

        choicesAfterLines = choicesAtEnd;
        ShowNextLine();
    }

    private void ShowNextLine()
    {
        if (lineQueue.Count == 0)
        {
            if (choicesAfterLines != null) ShowChoices(choicesAfterLines);
            else ShowNextEvent();
            return;
        }

        DisplayLine(lineQueue.Dequeue());

        if (lineQueue.Count == 0 && choicesAfterLines != null)
            ShowChoices(choicesAfterLines);
        else
            ShowContinue();
    }

    private void DisplayLine(DialogueLine line)
    {
        string text = line.Text;

        switch (line.speaker)
        {
            case CharacterId.None:
                // Narration: no name, keep whoever is on screen.
                eventText.text = text;
                break;

            case CharacterId.Player:
                // You're talking: keep the other character's portrait up.
                eventText.text = FormatDialogue(GetCharacterName(CharacterId.Player, "You"), text);
                playerExpression = line.Expression;
                ShowPlayer(true);
                break;

            default:
                eventText.text = FormatDialogue(GetCharacterName(line.speaker), text);
                ShowPortrait(line.speaker, line.Expression);
                ShowPlayer(true);
                break;
        }

        SetSpeaker(line.speaker);
        UpdateHUD();
    }

    private void ShowContinue()
    {
        waitingForContinue = true;
        SetButtons(new List<string> { continueLabel });
    }

    private void ShowChoices(List<EventChoice> choices)
    {
        waitingForContinue = false;
        choicesAfterLines = null;
        currentChoices = choices;
        SetButtons(choices.ConvertAll(c => c.label));
    }

    private void SetButtons(List<string> labels)
    {
        for (int i = 0; i < choiceButtons.Length; i++)
        {
            bool used = i < labels.Count;
            choiceButtons[i].gameObject.SetActive(used);

            if (used)
                choiceButtons[i].GetComponentInChildren<TMP_Text>().text = labels[i];
        }
    }

    // ================= CHARACTERS =================

    private CharacterData FindCharacter(CharacterId id)
    {
        foreach (var c in characters)
        {
            if (c.id == id) return c;
        }
        return null;
    }

    private string GetCharacterName(CharacterId id, string fallback = null)
    {
        CharacterData data = FindCharacter(id);
        if (data != null && !string.IsNullOrEmpty(data.displayName)) return data.displayName;
        return fallback ?? id.ToString();
    }

    private bool EventHasPlayerLines(GameEvent ev)
    {
        foreach (var line in ev.lines)
        {
            if (line.speaker == CharacterId.Player) return true;
        }
        foreach (var choice in ev.choices)
        {
            foreach (var reply in choice.replies)
            {
                if (reply.speaker == CharacterId.Player) return true;
            }
        }
        return false;
    }

    // Shows or hides the player's portrait, using their current expression.
    private void ShowPlayer(bool show)
    {
        if (playerImage == null) return;

        CharacterData data = FindCharacter(CharacterId.Player);
        Sprite sprite = show && data != null ? data.GetSprite(playerExpression) : null;

        playerImage.gameObject.SetActive(sprite != null);
        if (sprite != null) playerImage.sprite = sprite;
    }

    // Lights up whoever is talking and shadows everyone else.
    // Narration (None) shadows both, so the text reads as the focus.
    private void SetSpeaker(CharacterId speaker, bool instant = false)
    {
        bool playerSpeaking = speaker == CharacterId.Player;
        bool otherSpeaking = speaker != CharacterId.None && !playerSpeaking;

        // With only one portrait on screen and no one talking, keep it lit.
        bool onlyOneVisible = (characterImage == null || !characterImage.gameObject.activeSelf)
                            ^ (playerImage == null || !playerImage.gameObject.activeSelf);
        bool narration = speaker == CharacterId.None;

        characterTargetColor = otherSpeaking || (narration && onlyOneVisible) ? speakingColor : listeningColor;
        playerTargetColor = playerSpeaking || (narration && onlyOneVisible) ? speakingColor : listeningColor;

        if (instant)
        {
            if (characterImage != null) characterImage.color = characterTargetColor;
            if (playerImage != null) playerImage.color = playerTargetColor;
        }
    }

    // Builds "Name: line" with the name bold and colored.
    private string FormatDialogue(string speakerName, string text)
    {
        string hex = ColorUtility.ToHtmlStringRGB(speakerNameColor);
        return $"<b><color=#{hex}>{speakerName}:</color></b> {text}";
    }

    private void ShowPortrait(CharacterId id, Expression expression)
    {
        if (characterImage == null) return;

        CharacterData data = (id == CharacterId.None || id == CharacterId.Player) ? null : FindCharacter(id);
        Sprite sprite = data != null ? data.GetSprite(expression) : null;

        characterImage.gameObject.SetActive(sprite != null);
        if (sprite != null) characterImage.sprite = sprite;
    }

    private void HideCharacter()
    {
        if (characterImage != null) characterImage.gameObject.SetActive(false);
        if (playerImage != null) playerImage.gameObject.SetActive(false);
    }

    // ================= BACKGROUND =================

    private void SetBackground(Location location)
    {
        if (backgroundImage == null) return;

        foreach (var bg in backgrounds)
        {
            if (bg.location == location)
            {
                backgroundImage.sprite = bg.sprite;
                return;
            }
        }

        Debug.LogWarning($"No background assigned for {location}");
    }

    // ================= AUDIO =================

    // Plays the ambient loop for a location. Clips are matched by enum order:
    // 0 Home, 1 OutsideHome, 2 CommuteToWork, 3 OutsideWork, 4 Work, 5 CommuteHome
    private void SetAudio(Location location)
    {
        if (ambientAudioSource == null || ambientAudioClips == null || ambientAudioClips.Length == 0) return;

        int index = (int)location;

        if (index < 0 || index >= ambientAudioClips.Length || ambientAudioClips[index] == null)
        {
            Debug.LogWarning($"No ambient audio clip assigned for {location} ({index})");
            return;
        }

        // Already playing this location's clip: keep it going without restarting.
        if (ambientAudioSource.clip == ambientAudioClips[index] && ambientAudioSource.isPlaying)
            return;

        Debug.Log($"Playing audio for {location}: {ambientAudioClips[index].name} ({index})");
        ambientAudioSource.Stop();
        ambientAudioSource.clip = ambientAudioClips[index];
        ambientAudioSource.Play();
    }

    // Plays a one-shot sound effect over the ambient audio.
    // Call from an event choice, e.g. EventUI.Instance.PlaySpecial(string id)
    public void PlaySpecial(string id)
    {
        if (specialAudioSource == null || specialAudioClips == null) return;

        var audioClip = specialAudioClips.Find(c => c.id == id);
        if (audioClip == null)
        {
            Debug.LogWarning($"No special audio clip with id {id}");
            return;
        }

        specialAudioSource.PlayOneShot(audioClip.clip);
    }

    // ================= HUD =================

    private void UpdateHUD()
    {
        var gs = GameState.Instance;

        if (timeText != null)
            timeText.text = $"{gs.DayName}  {gs.GetTimeString()}";

        if (statusText != null)
            statusText.text = $"Energy: {gs.energy}   Mood: {gs.mood}";
    }

    // ================= SUMMARIES =================

    private void ShowDaySummary()
    {
        var gs = GameState.Instance;
        gs.EndDay();

        var sb = new StringBuilder();

        if (gs.IsLastDay)
        {
            BuildWeekSummary(sb);
            if (summaryButtonText != null) summaryButtonText.text = "Play again";
        }
        else
        {
            sb.AppendLine($"<b>{gs.DayName} is over.</b>");
            sb.AppendLine(gs.HasFlag("LateToWork") ? "You were late to work." : "You made it to work on time.");
            sb.AppendLine($"Energy: {gs.energy}   Mood: {gs.mood}");
            sb.AppendLine();

            foreach (string entry in gs.GetHistoryForDay(gs.currentDay))
                sb.AppendLine(entry);

            if (summaryButtonText != null) summaryButtonText.text = "Next day";
        }

        summaryText.text = sb.ToString();

        HideCharacter();
        if (eventPanel != null) eventPanel.SetActive(false);
        summaryPanel.SetActive(true);

        // Start scrolled to the top.
        if (summaryScroll != null)
        {
            Canvas.ForceUpdateCanvases();
            summaryScroll.verticalNormalizedPosition = 1f;
        }

        UpdateHUD();
    }

    private void BuildWeekSummary(StringBuilder sb)
    {
        var gs = GameState.Instance;
        int late = gs.GetCount("TimesLate");
        int productive = gs.GetCount("ProductiveDays");
        int friendship = gs.GetCount("NeighborFriendship");

        sb.AppendLine("<b>The week is over.</b>");
        sb.AppendLine();

        foreach (string recap in gs.GetDayRecaps())
            sb.AppendLine(recap);

        sb.AppendLine();
        sb.AppendLine($"Days late: {late}   Productive days: {productive}");
        sb.AppendLine($"Final energy: {gs.energy}   Final mood: {gs.mood}");

        if (friendship >= 2) sb.AppendLine("You and your neighbor are becoming friends.");
        else if (friendship <= -2) sb.AppendLine("Your neighbor has stopped waving.");

        if (gs.GetCount("CoworkerFriendship") >= 3) sb.AppendLine("Your coworker considers you a real friend.");
        if (gs.GetCount("CoffeeVisits") >= 3) sb.AppendLine("The barista knows your order by heart.");

        sb.AppendLine();

        // Ending
        if (late >= 3)
            sb.AppendLine("<b>Ending: On Thin Ice.</b> Your boss has put you on a final warning.");
        else if (productive >= 4 && late <= 1)
            sb.AppendLine("<b>Ending: Rising Star.</b> Your boss mentions a promotion.");
        else if (gs.mood >= 8)
            sb.AppendLine("<b>Ending: Good Vibes.</b> Work was fine, but you enjoyed your week.");
        else
            sb.AppendLine("<b>Ending: Survived.</b> Another week down.");
    }
}