using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Logout confirmation popup
/// - Displays "Do you want to log out?" message
/// - "Yes" button: Firebase sign out + return to login screen
/// - "No" button: close popup
/// </summary>
public class LogoutPopup : MonoBehaviour
{
    [Header("=== Popup UI ===")]
    [Tooltip("Popup panel (including semi-transparent background)")]
    [SerializeField] private GameObject popupPanel;

    [Header("=== Buttons ===")]
    [Tooltip("'Yes' button (confirm logout)")]
    [SerializeField] private Button confirmButton;

    [Tooltip("'No' button (cancel)")]
    [SerializeField] private Button cancelButton;

    [Header("=== Logout Button (placed in MyPage) ===")]
    [Tooltip("Button that opens the logout popup")]
    [SerializeField] private Button logoutButton;

    private void Start()
    {
        // Hide popup on start
        if (popupPanel != null)
            popupPanel.SetActive(false);

        // Register button events
        if (logoutButton != null)
            logoutButton.onClick.AddListener(ShowPopup);

        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmLogout);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(HidePopup);
    }

    /// <summary>
    /// Show logout confirmation popup
    /// </summary>
    public void ShowPopup()
    {
        if (popupPanel != null)
            popupPanel.SetActive(true);
    }

    /// <summary>
    /// Hide popup
    /// </summary>
    public void HidePopup()
    {
        if (popupPanel != null)
            popupPanel.SetActive(false);
    }

    /// <summary>
    /// "Yes" button clicked → Firebase sign out + return to login screen
    /// </summary>
    private void OnConfirmLogout()
    {
        HidePopup();

        if (FirebaseAuthManager.Instance != null)
        {
            FirebaseAuthManager.Instance.SignOut();
            FirebaseAuthManager.Instance.GoToLoginUI();
        }
        else
        {
            Debug.LogError("[LogoutPopup] FirebaseAuthManager instance not found.");
        }
    }

    private void OnDestroy()
    {
        if (logoutButton != null)
            logoutButton.onClick.RemoveListener(ShowPopup);

        if (confirmButton != null)
            confirmButton.onClick.RemoveListener(OnConfirmLogout);

        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(HidePopup);
    }
}
