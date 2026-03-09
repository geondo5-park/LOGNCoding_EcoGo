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

    [Tooltip("자동 로그인을 확인하는 동안 띄워둘 스플래시 화면 (선택사항)")]
    [SerializeField] private GameObject splashPanel;

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
    private bool pendingShowLogin = false;

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
            InitializeFirebase();
        }
        else if (Instance != this)
        {
            // 다른 씬(AR 등)에서 Home 씬으로 돌아왔을 때, 파괴될 새 객체의 UI들을 기존 싱글톤이 물려받음
            Instance.UpdateUIReferences(this);
            Destroy(gameObject);
            return;
        }
    }

    /// <summary>
    /// Home 씬이 리로드되었을 때, 기존 파괴된 UI들을 새로운 UI 레퍼런스로 교체하고
    /// 로그인 상태라면 스플래시 건너뛰기 등 즉시 메인 화면을 표시해주는 함수입니다.
    /// </summary>
    public void UpdateUIReferences(FirebaseAuthManager newSceneManager)
    {
        this.emailInputField = newSceneManager.emailInputField;
        this.passwordInputField = newSceneManager.passwordInputField;
        this.loginButton = newSceneManager.loginButton;
        this.statusText = newSceneManager.statusText;
        this.loadingIndicator = newSceneManager.loadingIndicator;
        this.navigationBar = newSceneManager.navigationBar;
        this.navigationBarObject = newSceneManager.navigationBarObject;
        this.loginPanel = newSceneManager.loginPanel;
        this.splashPanel = newSceneManager.splashPanel;
        this.myPageProfile = newSceneManager.myPageProfile;
        this.myPagePlantStats = newSceneManager.myPagePlantStats;
        this.loginValidator = newSceneManager.loginValidator;

        if (this.loginButton != null)
        {
            this.loginButton.onClick.RemoveAllListeners();
            this.loginButton.onClick.AddListener(this.OnLoginButtonClicked);
        }

        this.SetLoading(false);
        this.ClearStatus();

        if (this.IsLoggedIn)
        {
            // 이미 로그인 상태로 Home에 도착했으므로 스플래시와 로그인을 꺼버리고 메인 화면으로 전환
            if (this.splashPanel != null) this.splashPanel.SetActive(false);
            if (this.loginPanel != null) this.loginPanel.SetActive(false);
            if (this.navigationBarObject != null) this.navigationBarObject.SetActive(true);
            if (this.navigationBar != null) this.navigationBar.SelectTab(0);

            // MyPage 데이터 자동 리로드
            if (this.myPageProfile != null) this.myPageProfile.LoadUserProfile();
            if (this.myPagePlantStats != null) this.myPagePlantStats.LoadPlantStats();
        }
        else
        {
            if (this.loginPanel != null) this.loginPanel.SetActive(true);
            if (this.splashPanel != null) 
            {
                this.splashPanel.SetActive(true);
                StartCoroutine(HideSplashRoutine());
            }
        }
    }

    private void Start()
    {
        if (Instance != this) return; // 이미 UpdateUIReferences에서 처리됨

        if (loginButton != null)
            loginButton.onClick.AddListener(OnLoginButtonClicked);

        SetLoading(false);
        ClearStatus();

        // 밑에 깔려있을 로그인 패널을 미리 켜둠 (스플래시가 꺼졌을 때 자동로그인이 안된 상태면 보이도록)
        if (loginPanel != null) loginPanel.SetActive(true);
        
        // 스플래시 화면을 가장 위에서 2초 동안 띄움
        if (splashPanel != null) 
        {
            splashPanel.SetActive(true);
            StartCoroutine(HideSplashRoutine());
        }
    }

    private System.Collections.IEnumerator HideSplashRoutine()
    {
        // 최소 2.0초 동안 스플래시 화면을 유지합니다.
        yield return new WaitForSeconds(1.5f);
        
        if (splashPanel != null) 
        {
            // CanvasGroup 컴포넌트가 없으면 추가해서 알파(투명도) 값을 조절할 수 있게 만듭니다
            CanvasGroup cg = splashPanel.GetComponent<CanvasGroup>();
            if (cg == null) cg = splashPanel.AddComponent<CanvasGroup>();

            float fadeDuration = 0.5f; // 서서히 사라지는 시간 (0.5초)
            float elapsed = 0f;

            // 서서히 투명해지도록 애니메이션
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                cg.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
                yield return null;
            }
            
            // 완전히 투명해지면 패널을 끄고 다음을 위해 알파값을 다시 1로 복구
            splashPanel.SetActive(false);
            cg.alpha = 1f;
        }
    }

    private void Update()
    {
        // 자동 로그인 성공 시 메인 UI로 전환
        if (pendingAutoLogin)
        {
            pendingAutoLogin = false;
            Debug.Log($"[FirebaseAuthManager] 자동 로그인 처리: {currentUser.Email}");
            GoToMainUI();
        }

        // 로그인 내역이 없을 때 (스플래시는 2초 뒤 알아서 꺼지고 loginPanel은 밑에 켜져 있으므로 플래그만 소비)
        if (pendingShowLogin)
        {
            pendingShowLogin = false;
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
                else
                {
                    // 로그인된 유저가 없으면 로그인 창을 띄움
                    pendingShowLogin = true;
                }
            }
            else
            {
                Debug.LogError($"[FirebaseAuthManager] Firebase 의존성 오류: {dependencyStatus}");
                isFirebaseReady = false;
                // 에러가 나도 일단 빈 화면에 멈추면 안되니 로그인 창은 오픈.
                pendingShowLogin = true;
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

        Debug.Log($"[FirebaseAuthManager] 로그인 시도 중... 이메일: {email}, 비밀번호 값: '{password}' (길이: {password.Length})");

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
            Debug.LogWarning($"[FirebaseAuthManager] Login failed (FirebaseException) - ErrorCode: {(AuthError)ex.ErrorCode}, Message: {ex.Message}");
            if (ex.InnerException != null)
                Debug.LogWarning($"[FirebaseAuthManager] InnerException: {ex.InnerException.Message}");
        }
        catch (System.AggregateException ex)
        {
            // Firebase Task가 실패할 경우 AggregateException으로 포장되어 옵니다.
            foreach (var innerEx in ex.InnerExceptions)
            {
                if (innerEx is FirebaseException fbEx)
                {
                    string errorMessage = GetFirebaseErrorMessage(fbEx);
                    SetStatus(errorMessage, true);
                    Debug.LogWarning($"[FirebaseAuthManager] Login failed (Aggregate FirebaseException) - ErrorCode: {(AuthError)fbEx.ErrorCode}, Message: {fbEx.Message}\n입력된 비밀번호 길이: {password.Length}");
                }
                else
                {
                    SetStatus("An error occurred during login.", true);
                    Debug.LogError($"[FirebaseAuthManager] Aggregate 내부 일반 예외: {innerEx.Message}\n{innerEx.StackTrace}");
                }
            }
        }
        catch (System.Exception ex)
        {
            SetStatus("An error occurred during login.", true);
            Debug.LogError($"[FirebaseAuthManager] 일반 예외 발생: {ex.Message}\n{ex.StackTrace}");
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
        {
            return loginValidator.GetActualPassword();
        }

        Debug.LogError("[FirebaseAuthManager] 🚨 LoginValidator 스크립트가 인스펙터에 연결되어 있지 않습니다! 로그인 필드의 별표(*) 문자가 그대로 Firebase에 전송되고 있습니다. Inspector에서 LoginValidator 필드에 할당해주세요!");
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
        // 스플래시는 2초 후 코루틴이 무조건 끄므로 여기서는 로그인 화면만 메인 메뉴로 변경합니다.
        if (loginPanel != null)
            loginPanel.SetActive(false);

        if (navigationBarObject != null)
            navigationBarObject.SetActive(true);

        if (navigationBar != null)
            navigationBar.SelectTab(0);

        // UI 전환을 완전히 마친 후 Firestore 데이터 미리 가져오기 진행
        if (myPageProfile != null)
            myPageProfile.LoadUserProfile();

        if (myPagePlantStats != null)
            myPagePlantStats.LoadPlantStats();
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
