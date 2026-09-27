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

    public void StartGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }
}
