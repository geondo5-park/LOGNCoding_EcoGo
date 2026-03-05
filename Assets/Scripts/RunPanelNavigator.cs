using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Run 탭의 패널 전환 (메인 → 상세 → 뒤로가기)
/// </summary>
public class RunPanelNavigator : MonoBehaviour
{
    [Header("=== Panels ===")]
    [Tooltip("Run 메인 패널")]
    [SerializeField] private GameObject runPanel;

    [Tooltip("상세 화면 패널")]
    [SerializeField] private GameObject detailPanel;

    [Header("=== Buttons ===")]
    [Tooltip("상세 화면으로 이동하는 버튼 (Run 패널에 배치)")]
    [SerializeField] private Button goToDetailButton;

    [Tooltip("뒤로가기 버튼 (상세 화면에 배치)")]
    [SerializeField] private Button backButton;

    private void Start()
    {
        // 초기 상태: Run 패널 켜기, 상세 패널 끄기
        if (runPanel != null) runPanel.SetActive(true);
        if (detailPanel != null) detailPanel.SetActive(false);

        // 버튼 이벤트 연결
        if (goToDetailButton != null)
            goToDetailButton.onClick.AddListener(ShowDetail);

        if (backButton != null)
            backButton.onClick.AddListener(ShowRun);
    }

    /// <summary>
    /// 상세 화면 열기
    /// </summary>
    public void ShowDetail()
    {
        if (runPanel != null) runPanel.SetActive(false);
        if (detailPanel != null) detailPanel.SetActive(true);
    }

    /// <summary>
    /// Run 메인으로 돌아가기
    /// </summary>
    public void ShowRun()
    {
        if (detailPanel != null) detailPanel.SetActive(false);
        if (runPanel != null) runPanel.SetActive(true);
    }

    private void OnDestroy()
    {
        if (goToDetailButton != null)
            goToDetailButton.onClick.RemoveListener(ShowDetail);

        if (backButton != null)
            backButton.onClick.RemoveListener(ShowRun);
    }
}
