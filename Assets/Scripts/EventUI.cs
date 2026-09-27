using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Pairs a backdrop with its background image (filled in from the Inspector).
[Serializable]
public class BackdropImage
{
    public Backdrop backdrop;
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
//   ChoiceButton3  -> EventUI.ChooseOption(2)
//   SummaryButton  -> EventUI.SummaryButtonPressed()
public class EventUI : MonoBehaviour
{
    [Header("Event Panel")]
    public GameObject eventPanel;
    public TMP_Text eventText;
    public Button[] choiceButtons;

    [Header("HUD")]
    public TMP_Text timeText;      // day + clock in the top-right corner
    public TMP_Text statusText;    // optional: energy / mood / money / job

    [Header("Backgrounds")]
    public Image backgroundImage;
    public BackdropImage[] backgrounds;   // one per Backdrop

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

    // Named sound effects. A clip whose id matches an event id (e.g. "Alarm", "PoliceRaid")
    // plays automatically when that event starts. Others can be played with PlaySpecialAudio("id").
    [Serializable]
    public class SpecialAudioClip
    {
        public string id;       // e.g. "Doorbell", "CarHorn"
        public AudioClip clip;
    }
    public List<SpecialAudioClip> specialAudioClips;

    [Header("Radiant Events")]
    [Tooltip("Chance (0-1) that a random event from RadiantEvents.cs pops up at the start of each stage.")]
    [Range(0f, 1f)] public float radiantEventChance = 0.1f;

    [Header("Summary Panel")]
    public GameObject summaryPanel;
    public TMP_Text summaryText;
    public TMP_Text summaryButtonText;   // label on the summary panel's button
    public ScrollRect summaryScroll;     // optional: the Scroll View around SummaryText

    private List<DayStage> day;
    private int stageIndex;
    private int eventIndex;
    private List<EventChoice> currentChoices = new List<EventChoice>();
    private readonly HashSet<string> usedGroups = new HashSet<string>();
    private bool radiantRolled;   // one radiant roll per stage

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
        usedGroups.Clear();
        radiantRolled = false;

        summaryPanel.SetActive(false);
        if (eventPanel != null) eventPanel.SetActive(true);

        ShowNextEvent();
    }

    // Finds the next event whose conditions pass, moving through locations in order.
    // Grouped events: the first time a group is reached, one eligible event is picked at random.
    private void ShowNextEvent()
    {
        var gs = GameState.Instance;

        while (stageIndex < day.Count)
        {
            // Fired, arrested, etc: skip whatever is left of the day.
            if (gs.weekOver || gs.HasFlag(GameState.EndDayFlag)) break;

            DayStage stage = day[stageIndex];

            // Arriving at a new stage: small chance of a radiant event first.
            if (!radiantRolled)
            {
                radiantRolled = true;
                GameEvent radiant = PickRadiantEvent(stage.location);
                if (radiant != null)
                {
                    ShowEvent(radiant, stage);
                    return;
                }
            }

            while (eventIndex < stage.events.Count)
            {
                GameEvent ev = stage.events[eventIndex];
                eventIndex++;

                if (!string.IsNullOrEmpty(ev.group))
                {
                    if (!usedGroups.Add(ev.group)) continue;   // group already rolled
                    ev = PickFromGroup(stage, ev.group);
                    if (ev == null) continue;
                }
                else if (!ev.CanShow())
                {
                    continue;
                }

                ShowEvent(ev, stage);
                return;
            }

            stageIndex++;
            eventIndex = 0;
            usedGroups.Clear();
            radiantRolled = false;
        }

        ShowDaySummary();
    }

    // Rolls radiantEventChance, then picks one eligible event from RadiantEvents.cs for this location.
    private GameEvent PickRadiantEvent(Location location)
    {
        if (UnityEngine.Random.value >= radiantEventChance) return null;

        List<GameEvent> candidates = RadiantEvents.For(location).FindAll(e => e.CanShow());
        if (candidates.Count == 0) return null;

        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    // Weighted random pick among the group's events that can show right now.
    private GameEvent PickFromGroup(DayStage stage, string group)
    {
        var candidates = new List<GameEvent>();
        float total = 0f;

        foreach (var ev in stage.events)
        {
            if (ev.group == group && ev.CanShow())
            {
                candidates.Add(ev);
                total += ev.weight;
            }
        }

        if (candidates.Count == 0) return null;

        float roll = UnityEngine.Random.value * total;
        foreach (var ev in candidates)
        {
            roll -= ev.weight;
            if (roll <= 0f) return ev;
        }
        return candidates[candidates.Count - 1];
    }

    private void ShowEvent(GameEvent ev, DayStage stage)
    {
        var gs = GameState.Instance;
        gs.currentLocation = stage.location;
        if (ev.once) gs.AddPermanentFlag(ev.SeenFlag);

        SetBackground(ev.GetBackdrop() ?? stage.backdrop);
        SetAudio(stage.location);
        PlaySpecialAudio(ev.id, warnIfMissing: false);   // plays only if a clip has this event's id

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
        // Skip lines whose text came out empty (used for conditional lines).
        while (lineQueue.Count > 0)
        {
            DialogueLine line = lineQueue.Dequeue();
            string text = line.Text;
            if (string.IsNullOrEmpty(text)) continue;

            DisplayLine(line, text);

            if (!HasMoreLines() && choicesAfterLines != null)
                ShowChoices(choicesAfterLines);
            else
                ShowContinue();
            return;
        }

        if (choicesAfterLines != null) ShowChoices(choicesAfterLines);
        else ShowNextEvent();
    }

    private bool HasMoreLines()
    {
        foreach (var line in lineQueue)
        {
            if (!string.IsNullOrEmpty(line.Text)) return true;
        }
        return false;
    }

    private void DisplayLine(DialogueLine line, string text)
    {
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

    private void SetBackground(Backdrop backdrop)
    {
        if (backgroundImage == null) return;

        foreach (var bg in backgrounds)
        {
            if (bg.backdrop == backdrop)
            {
                backgroundImage.sprite = bg.sprite;
                return;
            }
        }

        Debug.LogWarning($"No background assigned for {backdrop}");
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
    // Call from an event choice, e.g. EventUI.Instance.PlaySpecialAudio("CarHorn")
    public void PlaySpecialAudio(string id, bool warnIfMissing = true)
    {
        if (specialAudioSource == null || specialAudioClips == null) return;

        var audioClip = specialAudioClips.Find(c => c.id == id);
        if (audioClip == null || audioClip.clip == null)
        {
            if (warnIfMissing) Debug.LogWarning($"No special audio clip with id {id}");
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
            statusText.text = $"Energy: {gs.energy}   Mood: {gs.mood}   ${gs.money}   Job: {gs.standing}/10";
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
            if (gs.HasFlag("Jailed")) sb.AppendLine("You spent the night in a jail cell.");
            else sb.AppendLine(gs.HasFlag("LateToWork") ? "You were late to work." : "You made it to work on time.");
            sb.AppendLine($"Energy: {gs.energy}   Mood: {gs.mood}");
            sb.AppendLine($"Money: ${gs.money}   Job standing: {gs.standing}/10");
            if (gs.standing <= 2) sb.AppendLine("<color=#FF6B6B>Your job is hanging by a thread.</color>");
            if (gs.money <= 10) sb.AppendLine("<color=#FF6B6B>You're almost out of money.</color>");
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
        bool fired = gs.HasPermanentFlag("Fired");
        bool arrested = gs.HasPermanentFlag("Arrested");

        sb.AppendLine(fired ? $"<b>You were fired on {gs.DayName}.</b>" : "<b>The week is over.</b>");
        sb.AppendLine();

        foreach (string recap in gs.GetDayRecaps())
            sb.AppendLine(recap);

        sb.AppendLine();
        sb.AppendLine($"Days late: {late}   Productive days: {productive}");
        sb.AppendLine($"Job standing: {gs.standing}/10   Money: ${gs.money}");
        sb.AppendLine($"Final energy: {gs.energy}   Final mood: {gs.mood}");
        sb.AppendLine();

        // ---------- What your choices led to ----------
        if (friendship >= 2) sb.AppendLine("You and your neighbor are becoming friends.");
        else if (friendship <= -2) sb.AppendLine("Your neighbor has stopped waving.");

        if (gs.GetCount("CoworkerFriendship") >= 3) sb.AppendLine("Your coworker considers you a real friend.");
        if (gs.GetCount("CoffeeVisits") >= 3) sb.AppendLine("The barista knows your order by heart.");

        if (gs.HasPermanentFlag("HasDog")) sb.AppendLine("You adopted a scruffy stray. Best decision of the week.");
        else if (gs.HasPermanentFlag("DogGone")) sb.AppendLine("The stray dog found someone else to follow.");

        if (gs.HasPermanentFlag("VagrantHoused")) sb.AppendLine("Joe got a bed at the shelter, partly thanks to you.");
        else if (gs.GetCount("VagrantKindness") >= 2) sb.AppendLine("Joe always saves you a seat on the bus now.");

        if (gs.HasPermanentFlag("PetitionSigned")) sb.AppendLine("Route 9 was saved. Your signature counted.");
        else if (gs.HasPermanentFlag("MetPetitioner")) sb.AppendLine("Route 9 was cut. You had your chance to sign.");

        if (gs.HasPermanentFlag("ThugCaught")) sb.AppendLine("The mugger is behind bars thanks to your tip.");
        if (arrested) sb.AppendLine("You now have an arrest on your record.");
        if (gs.HasPermanentFlag("PresentationWin")) sb.AppendLine("You nailed the client presentation.");
        else if (gs.HasPermanentFlag("PresentationFail")) sb.AppendLine("The client presentation still haunts you.");

        sb.AppendLine();

        // ---------- Ending ----------
        if (fired)
            sb.AppendLine("<b>Ending: Pink Slip.</b> You're cleaning out your desk.");
        else if (arrested)
            sb.AppendLine("<b>Ending: Jailbird.</b> Easy money turned out to be very expensive.");
        else if (gs.standing <= 2)
            sb.AppendLine("<b>Ending: On Thin Ice.</b> Your boss has put you on a final warning.");
        else if (gs.money <= 0)
            sb.AppendLine("<b>Ending: Flat Broke.</b> You made it to the weekend with empty pockets.");
        else if (gs.standing >= 8)
            sb.AppendLine("<b>Ending: Rising Star.</b> Your boss mentions a promotion.");
        else if (gs.mood >= 8)
            sb.AppendLine("<b>Ending: Good Vibes.</b> Work was fine, but you enjoyed your week.");
        else
            sb.AppendLine("<b>Ending: Survived.</b> Another week down.");
    }
}