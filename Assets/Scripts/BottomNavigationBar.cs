using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 하단 네비게이션 바 컨트롤러
/// 5개의 탭 버튼을 관리하며, 선택된 탭의 아이콘 + 텍스트 색상을 변경합니다.
/// </summary>
public class BottomNavigationBar : MonoBehaviour
{
    [Header("=== 탭 버튼 (5개) ===")]
    [Tooltip("각 탭에 해당하는 버튼 (순서대로 할당)")]
    [SerializeField] private Button[] tabButtons = new Button[5];

    [Header("=== 탭 아이콘 (Image) ===")]
    [Tooltip("각 버튼의 아이콘 Image 컴포넌트 (순서대로 할당)")]
    [SerializeField] private Image[] tabIcons = new Image[5];

    [Header("=== 탭 텍스트 (TextMeshPro) ===")]
    [Tooltip("각 버튼의 라벨 TextMeshProUGUI 컴포넌트 (순서대로 할당)")]
    [SerializeField] private TextMeshProUGUI[] tabTexts = new TextMeshProUGUI[5];

    [Header("=== 탭 패널 (GameObject) ===")]
    [Tooltip("각 탭에 해당하는 콘텐츠 패널 (순서대로 할당)")]
    [SerializeField] private GameObject[] tabPanels = new GameObject[5];

    [Header("=== 색상 설정 ===")]
    [Tooltip("기본 색상 - 비활성 상태 (아이콘 + 텍스트)")]
    [SerializeField] private Color defaultColor = new Color(0x3D / 255f, 0x4D / 255f, 0x65 / 255f, 1f); // #3D4D65

    [Tooltip("선택된 색상 - 활성 상태 (아이콘 + 텍스트)")]
    [SerializeField] private Color selectedColor = new Color(0x00 / 255f, 0xC4 / 255f, 0x8C / 255f, 1f); // #00C48C (민트 그린)

    [Header("=== 초기 설정 ===")]
    [Tooltip("시작 시 선택될 탭 인덱스 (0부터 시작)")]
    [SerializeField] private int defaultTabIndex = 0;

    // 현재 선택된 탭 인덱스
    private int currentTabIndex = -1;

    private void Start()
    {
        // 각 버튼에 클릭 이벤트 등록
        for (int i = 0; i < tabButtons.Length; i++)
        {
            if (tabButtons[i] != null)
            {
                int index = i; // 클로저를 위한 로컬 변수
                tabButtons[i].onClick.AddListener(() => OnTabButtonClicked(index));
            }
        }

        // 초기 탭 선택
        SelectTab(defaultTabIndex);
    }

    /// <summary>
    /// 탭 버튼 클릭 시 호출
    /// </summary>
    /// <param name="tabIndex">클릭된 탭의 인덱스</param>
    private void OnTabButtonClicked(int tabIndex)
    {
        // 이미 선택된 탭이면 무시
        if (tabIndex == currentTabIndex)
            return;

        SelectTab(tabIndex);
    }

    /// <summary>
    /// 특정 탭을 선택하고 UI를 업데이트합니다.
    /// </summary>
    /// <param name="tabIndex">선택할 탭의 인덱스</param>
    public void SelectTab(int tabIndex)
    {
        // 유효성 검사
        if (tabIndex < 0 || tabIndex >= tabButtons.Length)
        {
            Debug.LogWarning($"[BottomNavigationBar] 잘못된 탭 인덱스: {tabIndex}");
            return;
        }

        currentTabIndex = tabIndex;

        // 모든 탭 아이콘 색상, 텍스트 색상, 패널 업데이트
        for (int i = 0; i < tabButtons.Length; i++)
        {
            bool isSelected = (i == tabIndex);
            Color targetColor = isSelected ? selectedColor : defaultColor;

            // 아이콘 색상 변경
            if (i < tabIcons.Length && tabIcons[i] != null)
            {
                tabIcons[i].color = targetColor;
            }

            // 텍스트 색상 변경
            if (i < tabTexts.Length && tabTexts[i] != null)
            {
                tabTexts[i].color = targetColor;
            }

            // 패널 활성화/비활성화
            if (i < tabPanels.Length && tabPanels[i] != null)
            {
                tabPanels[i].SetActive(isSelected);
            }
        }
    }

    /// <summary>
    /// 현재 선택된 탭 인덱스를 반환합니다.
    /// </summary>
    public int GetCurrentTabIndex()
    {
        return currentTabIndex;
    }

    private void OnDestroy()
    {
        // 이벤트 리스너 정리
        for (int i = 0; i < tabButtons.Length; i++)
        {
            if (tabButtons[i] != null)
            {
                tabButtons[i].onClick.RemoveAllListeners();
            }
        }
    }
}
