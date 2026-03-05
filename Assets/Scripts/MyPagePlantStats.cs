using UnityEngine;
using TMPro;
using Firebase.Firestore;
using System.Collections.Generic;

/// <summary>
/// MyPage에서 유저가 발견한 식물을 grade별로 분석하여 표시합니다.
///
/// Firestore 경로:
///   1. users/{uid}/plant_progress/{plantId} → is_discovered (bool)
///   2. plants_encyclopedia/{plantId} → grade (int: 1, 2, 3)
///
/// 결과: grade 1 몇 개, grade 2 몇 개, grade 3 몇 개
/// </summary>
public class MyPagePlantStats : MonoBehaviour
{
    [Header("=== Grade Count UI ===")]
    [Tooltip("Grade 1 개수 텍스트")]
    [SerializeField] private TMP_Text grade1CountText;

    [Tooltip("Grade 2 개수 텍스트")]
    [SerializeField] private TMP_Text grade2CountText;

    [Tooltip("Grade 3 개수 텍스트")]
    [SerializeField] private TMP_Text grade3CountText;

    [Header("=== Optional ===")]
    [Tooltip("총 발견 식물 수 텍스트 (선택사항)")]
    [SerializeField] private TMP_Text totalCountText;

    private FirebaseFirestore db;
    private bool isLoaded = false;

    private void OnEnable()
    {
        // 이미 pre-fetch로 로드됐으면 다시 호출하지 않음
        if (!isLoaded)
            LoadPlantStats();
    }

    /// <summary>
    /// Firestore에서 유저의 plant_progress를 조회하고 grade별 개수를 분석합니다.
    /// </summary>
    public async void LoadPlantStats()
    {
        if (FirebaseAuthManager.Instance == null || !FirebaseAuthManager.Instance.IsLoggedIn)
        {
            Debug.LogWarning("[MyPagePlantStats] User is not logged in.");
            return;
        }

        string uid = FirebaseAuthManager.Instance.CurrentUser.UserId;

        if (db == null)
            db = FirebaseFirestore.DefaultInstance;

        try
        {
            // Step 1: users/{uid}/plant_progress에서 is_discovered == true인 문서 가져오기
            CollectionReference plantProgressRef = db.Collection("users").Document(uid).Collection("plant_progress");
            QuerySnapshot progressSnapshot = await plantProgressRef.GetSnapshotAsync();

            List<string> discoveredPlantIds = new List<string>();

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

            Debug.Log($"[MyPagePlantStats] Discovered plants: {discoveredPlantIds.Count}");

            // Step 2: plants_encyclopedia/{plantId}에서 grade 조회 및 카운트
            int grade1 = 0;
            int grade2 = 0;
            int grade3 = 0;

            foreach (string plantId in discoveredPlantIds)
            {
                DocumentReference encyclopediaRef = db.Collection("plants_encyclopedia").Document(plantId);
                DocumentSnapshot encyclopediaDoc = await encyclopediaRef.GetSnapshotAsync();

                if (encyclopediaDoc.Exists && encyclopediaDoc.ContainsField("grade"))
                {
                    long grade = encyclopediaDoc.GetValue<long>("grade");

                    switch (grade)
                    {
                        case 1: grade1++; break;
                        case 2: grade2++; break;
                        case 3: grade3++; break;
                        default:
                            Debug.LogWarning($"[MyPagePlantStats] Unknown grade {grade} for plant: {plantId}");
                            break;
                    }
                }
                else
                {
                    Debug.LogWarning($"[MyPagePlantStats] No grade found for plant: {plantId}");
                }
            }

            // Step 3: UI 업데이트
            if (grade1CountText != null) grade1CountText.text = grade1.ToString();
            if (grade2CountText != null) grade2CountText.text = grade2.ToString();
            if (grade3CountText != null) grade3CountText.text = grade3.ToString();
            if (totalCountText != null) totalCountText.text = (grade1 + grade2 + grade3).ToString();

            Debug.Log($"[MyPagePlantStats] Grade1: {grade1}, Grade2: {grade2}, Grade3: {grade3}");
            isLoaded = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MyPagePlantStats] Failed to load plant stats: {ex.Message}");
            SetDefaultValues();
        }
    }

    private void SetDefaultValues()
    {
        if (grade1CountText != null) grade1CountText.text = "0";
        if (grade2CountText != null) grade2CountText.text = "0";
        if (grade3CountText != null) grade3CountText.text = "0";
        if (totalCountText != null) totalCountText.text = "0";
    }
}
