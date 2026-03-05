using UnityEngine;
using UnityEngine.UI;

public class HomeScene : MonoBehaviour
{
    public Button goToTutorialButton;
    
    void Start()
    {
        goToTutorialButton.onClick.AddListener(OnClickGoToTutorialButton);
    }

    private void OnClickGoToTutorialButton()
    {
        Application.OpenURL("https://ecogo-pjt.web.app/");
    }
}
