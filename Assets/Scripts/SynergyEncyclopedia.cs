using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Firestore;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Generate 탭 - 시너지별 식물 도감
///
/// Firestore 구조:
///   synergies/{synergyId}
///     ├── name: "시너지 이름"
///     └── plant_ids: ["plant1", "plant2", ...] (배열)
///
///   users/{uid}/plant_progress/{plantId}
///     └── is_discovered: true/false
///
/// 발견한 식물은 밝게, 미발견 식물은 어둡게 표시합니다.
/// </summary>
public class SynergyEncyclopedia : MonoBehaviour
{
    [Header("=== Scroll View Content ===")]
    [Tooltip("ScrollView의 Content (VerticalLayoutGroup 포함)")]
    [SerializeField] private Transform contentParent;

    [Header("=== Prefabs ===")]
    [Tooltip("시너지 헤더 프리팹 (시너지 이름 표시용)")]
    [SerializeField] private GameObject synergyHeaderPrefab;

    [Tooltip("식물 이미지 행 프리팹 (HorizontalLayoutGroup 포함)")]
    [SerializeField] private GameObject plantRowPrefab;

    [Tooltip("개별 식물 이미지 프리팹 (Image 컴포넌트 포함)")]
    [SerializeField] private GameObject plantImagePrefab;

    [Header("=== Settings ===")]
    [Tooltip("Resources 폴더 내 식물 이미지 경로 (예: Plants)")]
    [SerializeField] private string imageFolderPath = "Plants";

    [Tooltip("한 행에 표시할 최대 식물 수")]
    [SerializeField] private int maxPerRow = 5;

    [Header("=== Undiscovered Plant Style ===")]
    [Tooltip("미보유 식물에 적용할 어두운 색상")]
    [SerializeField] private Color undiscoveredColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    [Tooltip("보유 식물에 적용할 밝은 색상")]
    [SerializeField] private Color discoveredColor = Color.white;

    private FirebaseFirestore db;
    private bool isLoaded = false;

    // 유저가 발견한 식물 ID 목록
    private HashSet<string> discoveredPlantIds = new HashSet<string>();

    private void OnEnable()
    {
        if (!isLoaded)
            LoadSynergyData();
    }

    /// <summary>
    /// Firestore에서 유저의 발견 정보 + 시너지 데이터를 가져와 도감을 생성합니다.
    /// </summary>
    public async void LoadSynergyData()
    {
        if (db == null)
            db = FirebaseFirestore.DefaultInstance;

        try
        {
            // 기존 콘텐츠 지우기
            ClearContent();

            // Step 1: 유저가 발견한 식물 목록 가져오기
            await LoadDiscoveredPlants();

            // Step 2: synergies 컬렉션의 모든 문서 가져오기
            CollectionReference synergiesRef = db.Collection("synergies");
            QuerySnapshot snapshot = await synergiesRef.GetSnapshotAsync();

            foreach (DocumentSnapshot doc in snapshot.Documents)
            {
                if (!doc.Exists) continue;

                // 시너지 이름 가져오기
                string synergyName = doc.ContainsField("name")
                    ? doc.GetValue<string>("name")
                    : doc.Id;

                // plant_ids 배열 가져오기
                List<object> plantIds = doc.ContainsField("plant_ids")
                    ? doc.GetValue<List<object>>("plant_ids")
                    : new List<object>();

                if (plantIds.Count == 0) continue;

                // 시너지 헤더 생성
                CreateSynergyHeader(synergyName, plantIds.Count);

                // 식물 이미지들을 행 단위로 생성
                CreatePlantRows(plantIds);
            }

            isLoaded = true;
            Debug.Log($"[SynergyEncyclopedia] Loaded {snapshot.Count} synergies. Discovered plants: {discoveredPlantIds.Count}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SynergyEncyclopedia] Failed to load synergy data: {ex.Message}");
        }
    }

    /// <summary>
    /// Firestore에서 유저가 발견한 식물 ID 목록을 가져옵니다.
    /// </summary>
    private async Task LoadDiscoveredPlants()
    {
        discoveredPlantIds.Clear();

        if (FirebaseAuthManager.Instance == null || !FirebaseAuthManager.Instance.IsLoggedIn)
        {
            Debug.LogWarning("[SynergyEncyclopedia] User is not logged in. All plants will show as undiscovered.");
            return;
        }

        string uid = FirebaseAuthManager.Instance.CurrentUser.UserId;
        CollectionReference progressRef = db.Collection("users").Document(uid).Collection("plant_progress");
        QuerySnapshot progressSnapshot = await progressRef.GetSnapshotAsync();

        foreach (DocumentSnapshot doc in progressSnapshot.Documents)
        {
            if (doc.Exists && doc.ContainsField("is_discovered"))
            {
                bool isDiscovered = doc.GetValue<bool>("is_discovered");
                if (isDiscovered)
                {
                    discoveredPlantIds.Add(doc.Id);
                }
            }
        }
    }

    /// <summary>
    /// 시너지 이름 헤더를 생성합니다.
    /// </summary>
    private void CreateSynergyHeader(string synergyName, int plantCount)
    {
        if (synergyHeaderPrefab == null || contentParent == null) return;

        GameObject header = Instantiate(synergyHeaderPrefab, contentParent);
        header.name = $"Header_{synergyName}";

        // TMP_Text 찾아서 시너지 이름 설정
        TMP_Text headerText = header.GetComponentInChildren<TMP_Text>();
        if (headerText != null)
            headerText.text = $"{synergyName} ({plantCount})";
    }

    /// <summary>
    /// 식물 이미지들을 행으로 나누어 생성합니다.
    /// 발견한 식물은 밝게, 미발견 식물은 어둡게 표시합니다.
    /// </summary>
    private void CreatePlantRows(List<object> plantIds)
    {
        if (plantRowPrefab == null || plantImagePrefab == null || contentParent == null) return;

        GameObject currentRow = null;
        int currentCount = 0;

        foreach (object idObj in plantIds)
        {
            string plantId = idObj.ToString();

            // 새로운 행이 필요하면 생성
            if (currentRow == null || currentCount >= maxPerRow)
            {
                currentRow = Instantiate(plantRowPrefab, contentParent);
                currentRow.name = $"Row_{plantId}";
                currentCount = 0;
            }

            // 식물 이미지 아이템 생성
            GameObject plantItem = Instantiate(plantImagePrefab, currentRow.transform);
            plantItem.name = $"Plant_{plantId}";

            // Resources에서 이미지 로드
            Image plantImage = plantItem.GetComponentInChildren<Image>();
            if (plantImage != null)
            {
                string resourcePath = string.IsNullOrEmpty(imageFolderPath)
                    ? plantId
                    : $"{imageFolderPath}/{plantId}";

                Sprite sprite = Resources.Load<Sprite>(resourcePath);

                if (sprite != null)
                {
                    plantImage.sprite = sprite;
                    plantImage.preserveAspect = true;
                }
                else
                {
                    Debug.LogWarning($"[SynergyEncyclopedia] Image not found: Resources/{resourcePath}");
                }

                // 보유 여부에 따라 밝기 조절
                bool isDiscovered = discoveredPlantIds.Contains(plantId);
                plantImage.color = isDiscovered ? discoveredColor : undiscoveredColor;
            }

            currentCount++;
        }
    }

    /// <summary>
    /// 기존 콘텐츠를 모두 제거합니다.
    /// </summary>
    private void ClearContent()
    {
        if (contentParent == null) return;

        for (int i = contentParent.childCount - 1; i >= 0; i--)
        {
            Destroy(contentParent.GetChild(i).gameObject);
        }
    }

    /// <summary>
    /// 외부에서 강제 새로고침할 때 사용합니다.
    /// </summary>
    public void Refresh()
    {
        isLoaded = false;
        LoadSynergyData();
    }
}
