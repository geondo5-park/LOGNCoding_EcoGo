using UnityEngine;
using UnityEngine.UI;

using TMPro;
using Firebase;
using Firebase.Auth;
using System.Threading.Tasks;

/// <summary>
/// Firebase 이메일/비밀번호 로그인 매니저
/// - LoginValidator와 함께 사용하여 유효성 검사 후 Firebase 로그인 수행
/// </summary>
public class FirebaseAuthManager : MonoBehaviour
{
    // ─── 싱글톤 ───
    public static FirebaseAuthManager Instance { get; private set; }

    [Header("=== 입력 필드 ===")]
    [Tooltip("이메일 입력 필드")]
    [SerializeField] private TMP_InputField emailInputField;

    [Tooltip("비밀번호 입력 필드")]
    [SerializeField] private TMP_InputField passwordInputField;

    [Header("=== 버튼 ===")]
    [Tooltip("로그인 버튼")]
    [SerializeField] private Button loginButton;

    [Header("=== UI 피드백 ===")]
    [Tooltip("상태/에러 메시지를 표시할 텍스트")]
    [SerializeField] private TMP_Text statusText;

    [Tooltip("로딩 중 표시할 오브젝트 (선택사항)")]
    [SerializeField] private GameObject loadingIndicator;

    [Header("=== 로그인 후 전환 ===")]
    [Tooltip("로그인 성공 후 활성화할 네비게이션 바")]
    [SerializeField] private BottomNavigationBar navigationBar;

    [Tooltip("로그인 성공 후 활성화할 네비게이션 바 GameObject")]
    [SerializeField] private GameObject navigationBarObject;

    [Tooltip("로그인 성공 후 비활성화할 로그인 패널")]
    [SerializeField] private GameObject loginPanel;

    [Header("=== MyPage Data (Pre-fetch) ===")]
    [Tooltip("MyPage profile script")]
    [SerializeField] private MyPageProfile myPageProfile;

    [Tooltip("MyPage plant stats script")]
    [SerializeField] private MyPagePlantStats myPagePlantStats;

    [Header("=== 유효성 검사 ===")]
    [Tooltip("LoginValidator 컴포넌트 (같은 오브젝트 또는 별도 오브젝트에 부착)")]
    [SerializeField] private LoginValidator loginValidator;

    // Firebase 인증 인스턴스
    private FirebaseAuth auth;
    private FirebaseUser currentUser;
    private bool isFirebaseReady = false;

    // 자동 로그인 플래그 (Firebase 콜백은 메인 스레드가 아니므로 Update에서 처리)
    private bool pendingAutoLogin = false;

    // ─── 프로퍼티 ───
    public FirebaseUser CurrentUser => currentUser;
    public bool IsFirebaseReady => isFirebaseReady;
    public bool IsLoggedIn => currentUser != null;

    private void Awake()
    {
        // 싱글톤 설정
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        InitializeFirebase();
    }

    private void Start()
    {
        if (loginButton != null)
            loginButton.onClick.AddListener(OnLoginButtonClicked);

        SetLoading(false);
        ClearStatus();
    }

    private void Update()
    {
        // 자동 로그인 대기 중이면 메인 스레드에서 UI 전환 수행
        if (pendingAutoLogin)
        {
            pendingAutoLogin = false;
            Debug.Log($"[FirebaseAuthManager] 자동 로그인 처리: {currentUser.Email}");
            GoToMainUI();
        }
    }

    #region Firebase 초기화

    /// <summary>
    /// Firebase 의존성 확인 및 초기화
    /// </summary>
    private void InitializeFirebase()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
        {
            var dependencyStatus = task.Result;

            if (dependencyStatus == DependencyStatus.Available)
            {
                auth = FirebaseAuth.DefaultInstance;
                auth.StateChanged += OnAuthStateChanged;
                currentUser = auth.CurrentUser;
                isFirebaseReady = true;

                Debug.Log("[FirebaseAuthManager] Firebase 초기화 성공!");

                if (currentUser != null)
                {
                    Debug.Log($"[FirebaseAuthManager] 이미 로그인됨: {currentUser.Email}");
                    pendingAutoLogin = true; // 메인 스레드에서 UI 전환 예약
                }
            }
            else
            {
                Debug.LogError($"[FirebaseAuthManager] Firebase 의존성 오류: {dependencyStatus}");
                isFirebaseReady = false;
            }
        });
    }

    /// <summary>
    /// Firebase 인증 상태 변경 시 호출
    /// </summary>
    private void OnAuthStateChanged(object sender, System.EventArgs e)
    {
        if (auth.CurrentUser != currentUser)
        {
            bool wasLoggedIn = (currentUser != null);
            currentUser = auth.CurrentUser;

            if (currentUser != null && !wasLoggedIn)
                Debug.Log($"[FirebaseAuthManager] 로그인 감지: {currentUser.Email}");
            else if (currentUser == null && wasLoggedIn)
                Debug.Log("[FirebaseAuthManager] 로그아웃 감지");
        }
    }

    #endregion

    #region 로그인

    /// <summary>
    /// 로그인 버튼 클릭 시 호출
    /// </summary>
    private void OnLoginButtonClicked()
    {
        // LoginValidator가 있으면 유효성 검사 먼저 수행
        if (loginValidator != null && !loginValidator.ValidateAll())
            return;

        string email = emailInputField.text.Trim();
        string password = GetPassword();

        SignInWithEmail(email, password);
    }

    /// <summary>
    /// 이메일/비밀번호로 Firebase 로그인
    /// </summary>
    public async void SignInWithEmail(string email, string password)
    {
        if (!isFirebaseReady)
        {
            SetStatus("Firebase is not ready yet.", true);
            return;
        }

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            SetStatus("Please enter your email and password.", true);
            return;
        }

        SetLoading(true);
        if (loginButton != null) loginButton.interactable = false;
        ClearStatus();

        try
        {
            AuthResult result = await auth.SignInWithEmailAndPasswordAsync(email, password);
            currentUser = result.User;

            Debug.Log($"[FirebaseAuthManager] 로그인 성공! 사용자: {currentUser.Email}");
            SetStatus("Login successful!", false);

            // 로그인 성공 후 잠시 대기 후 메인 UI로 전환
            await Task.Delay(500);
            GoToMainUI();
        }
        catch (FirebaseException ex)
        {
            string errorMessage = GetFirebaseErrorMessage(ex);
            SetStatus(errorMessage, true);
            Debug.LogWarning($"[FirebaseAuthManager] Login failed - ErrorCode: {(AuthError)ex.ErrorCode}, Message: {ex.Message}");
            if (ex.InnerException != null)
                Debug.LogWarning($"[FirebaseAuthManager] InnerException: {ex.InnerException.Message}");
        }
        catch (System.Exception ex)
        {
            SetStatus("An error occurred during login.", true);
            Debug.LogError($"[FirebaseAuthManager] 예외 발생: {ex.Message}");
        }
        finally
        {
            SetLoading(false);
            if (loginButton != null) loginButton.interactable = true;
        }
    }

    #endregion

    #region 로그아웃

    /// <summary>
    /// Firebase 로그아웃
    /// </summary>
    public void SignOut()
    {
        if (auth != null)
        {
            auth.SignOut();
            currentUser = null;
            Debug.Log("[FirebaseAuthManager] 로그아웃 완료");
        }
    }

    /// <summary>
    /// 로그아웃 후 로그인 화면으로 돌아가기
    /// </summary>
    public void GoToLoginUI()
    {
        // 모든 탭 패널 비활성화 (MyPage 등)
        if (navigationBar != null)
            navigationBar.DeactivateAllTabs();

        if (navigationBarObject != null)
            navigationBarObject.SetActive(false);

        if (loginPanel != null)
            loginPanel.SetActive(true);

        ClearStatus();
    }

    #endregion

    #region 유틸리티

    /// <summary>
    /// 비밀번호 가져오기 (LoginValidator의 마스킹 처리 고려)
    /// </summary>
    private string GetPassword()
    {
        if (loginValidator != null)
            return loginValidator.GetActualPassword();

        return passwordInputField != null ? passwordInputField.text : "";
    }

    /// <summary>
    /// Returns error message based on Firebase error code
    /// </summary>
    private string GetFirebaseErrorMessage(FirebaseException ex)
    {
        AuthError errorCode = (AuthError)ex.ErrorCode;

        // ex.Message + InnerException 메시지를 합쳐서 패턴 매칭
        string message = ex.Message ?? "";
        if (ex.InnerException != null)
            message += " " + ex.InnerException.Message;
        message = message.ToUpperInvariant();

        switch (errorCode)
        {
            case AuthError.InvalidCredential:
                return "Incorrect email or password.";
            case AuthError.WrongPassword:
                return "Incorrect password.";
            case AuthError.UserNotFound:
                return "Email not found.";
            case AuthError.InvalidEmail:
                return "Invalid email format.";
            case AuthError.UserDisabled:
                return "This account has been disabled. Please contact support.";
            case AuthError.TooManyRequests:
                return "Too many attempts.\nPlease try again later.";
            case AuthError.NetworkRequestFailed:
                return "Please check your network connection.";
            default:
                // Firebase SDK가 Failure로 내려올 때 메시지 내용으로 분기
                if (message.Contains("INVALID_LOGIN_CREDENTIALS") ||
                    message.Contains("WRONG_PASSWORD") ||
                    message.Contains("INVALID_PASSWORD") ||
                    message.Contains("INVALID_CREDENTIAL"))
                    return "Incorrect email or password.";

                if (message.Contains("USER_NOT_FOUND"))
                    return "Email not found.";

                if (message.Contains("INVALID_EMAIL"))
                    return "Invalid email format.";

                if (message.Contains("USER_DISABLED"))
                    return "This account has been disabled. Please contact support.";

                if (message.Contains("TOO_MANY_ATTEMPTS") || message.Contains("TOO_MANY_REQUESTS"))
                    return "Too many attempts.\nPlease try again later.";

                if (message.Contains("NETWORK"))
                    return "Please check your network connection.";

                // INTERNAL 에러는 대부분 잘못된 인증 정보
                if (message.Contains("INTERNAL"))
                    return "Incorrect email or password.";

                return $"An error occurred. ({errorCode})";
        }
    }

    private void SetStatus(string message, bool isError)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = isError ? Color.red : new Color(0x00 / 255f, 0xC4 / 255f, 0x8C / 255f, 1f);
        }
    }

    private void ClearStatus()
    {
        if (statusText != null)
            statusText.text = "";
    }

    private void SetLoading(bool isLoading)
    {
        if (loadingIndicator != null)
            loadingIndicator.SetActive(isLoading);
    }

    /// <summary>
    /// 로그인 패널을 숨기고 네비게이션 바 + 첫 번째 탭을 표시합니다.
    /// 수동 로그인, 자동 로그인 모두 이 메서드를 사용합니다.
    /// </summary>
    private void GoToMainUI()
    {
        // Firestore 데이터 미리 가져오기 (MyPage 탭 열기 전에 로드)
        if (myPageProfile != null)
            myPageProfile.LoadUserProfile();

        if (myPagePlantStats != null)
            myPagePlantStats.LoadPlantStats();

        if (loginPanel != null)
            loginPanel.SetActive(false);

        if (navigationBarObject != null)
            navigationBarObject.SetActive(true);

        if (navigationBar != null)
            navigationBar.SelectTab(0);
    }

    #endregion

    private void OnDestroy()
    {
        if (auth != null)
            auth.StateChanged -= OnAuthStateChanged;

        if (loginButton != null)
            loginButton.onClick.RemoveListener(OnLoginButtonClicked);

        if (Instance == this)
            Instance = null;
    }
}
