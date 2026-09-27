using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Title screen buttons.
// Put this on an empty GameObject called "MenuManager" in MenuScene.
//
// Button wiring (do this once, in the Inspector):
//   StartButton -> MainMenu.StartGame()
public class MainMenu : MonoBehaviour
{
    [Tooltip("The gameplay scene. It must be in the Build Profiles scene list.")]
    public string gameSceneName = "SampleScene";

    [Tooltip("Optional: shows \"Endings found: 4 / 11\". Hidden until the player finds their first ending.")]
    public TMP_Text endingsText;

    void Start()
    {
        PauseMenu.ForceUnpause();      // in case we came back from the pause menu
        GameSettings.ApplyVolume();
        ShowEndingsFound();
    }

    public void StartGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    private void ShowEndingsFound()
    {
        if (endingsText == null) return;

        int found = 0;
        foreach (string title in DayEvents.AllEndingTitles)
        {
            if (PlayerPrefs.GetInt("Ending_" + title, 0) == 1) found++;
        }

        endingsText.gameObject.SetActive(found > 0);
        endingsText.text = $"Endings found: {found} / {DayEvents.AllEndingTitles.Length}";
    }
}