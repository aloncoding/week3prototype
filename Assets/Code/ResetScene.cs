using UnityEngine;
using UnityEngine.SceneManagement; // Required for managing scenes

public class SceneRestarter : MonoBehaviour
{
    void Update()
    {
        // Check if the player presses the 'R' key
        if (Input.GetKeyDown(KeyCode.R))
        {
            ReloadCurrentScene();
        }
    }

    public void ReloadCurrentScene()
    {
        // Gets the build index of the currently active scene and reloads it
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }
}
