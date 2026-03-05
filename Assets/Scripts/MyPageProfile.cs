 using UnityEngine;
using TMPro;
using Firebase.Firestore;
using Firebase.Auth;

/// <summary>
/// MyPage 탭에서 Firestore의 유저 프로필 정보를 가져와 표시합니다.
/// Firestore 경로: users/{uid} → name, level, points, email
/// </summary>
public class MyPageProfile : MonoBehaviour
{
    [Header("=== UI 텍스트 ===")]
    [Tooltip("닉네임 표시 텍스트")]
    [SerializeField] private TMP_Text nameText;

    [Tooltip("이메일 표시 텍스트")]
    [SerializeField] private TMP_Text emailText;

    [Tooltip("레벨 표시 텍스트")]
    [SerializeField] private TMP_Text levelText;

    [Tooltip("포인트 표시 텍스트")]
    [SerializeField] private TMP_Text pointsText;

    // Firestore 인스턴스
    private FirebaseFirestore db;
    private bool isLoaded = false;

    private void OnEnable()
    {
        // 이미 pre-fetch로 로드됐으면 다시 호출하지 않음
        if (!isLoaded)
            LoadUserProfile();
    }

    /// <summary>
    /// Firestore에서 유저 프로필 데이터를 가져와 UI에 표시합니다.
    /// </summary>
    public async void LoadUserProfile()
    {
        // Firebase Auth에서 현재 로그인된 유저 확인
        if (FirebaseAuthManager.Instance == null || !FirebaseAuthManager.Instance.IsLoggedIn)
        {
            Debug.LogWarning("[MyPageProfile] User is not logged in.");
            return;
        }

        string uid = FirebaseAuthManager.Instance.CurrentUser.UserId;

        if (db == null)
            db = FirebaseFirestore.DefaultInstance;

        try
        {
            DocumentReference docRef = db.Collection("users").Document(uid);
            DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();

            if (snapshot.Exists)
            {
                // name
                if (snapshot.ContainsField("name") && nameText != null)
                    nameText.text = snapshot.GetValue<string>("name");

                // email
                if (snapshot.ContainsField("email") && emailText != null)
                    emailText.text = snapshot.GetValue<string>("email");

                // level
                if (snapshot.ContainsField("level") && levelText != null)
                {
                    long level = snapshot.GetValue<long>("level");
                    levelText.text = $"Lv. {level}";
                }

                // points
                if (snapshot.ContainsField("points") && pointsText != null)
                {
                    long points = snapshot.GetValue<long>("points");
                    pointsText.text = $"{points:N0}P";
                }

                Debug.Log($"[MyPageProfile] Profile loaded: {snapshot.GetValue<string>("name")}");
                isLoaded = true;
            }
            else
            {
                Debug.LogWarning($"[MyPageProfile] No user document found for uid: {uid}");
                SetDefaultValues();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MyPageProfile] Failed to load profile: {ex.Message}");
            SetDefaultValues();
        }
    }

    /// <summary>
    /// 데이터를 가져올 수 없을 때 기본값 표시
    /// </summary>
    private void SetDefaultValues()
    {
        if (nameText != null) nameText.text = "-";
        if (emailText != null) emailText.text = "-";
        if (levelText != null) levelText.text = "Lv. -";
        if (pointsText != null) pointsText.text = "- P";
    }
}
