using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
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

    [Header("=== 씬 설정 ===")]
    [Tooltip("로그인 성공 후 이동할 씬 이름")]
    [SerializeField] private string mainSceneName = "SampleScene";

    [Header("=== 유효성 검사 ===")]
    [Tooltip("LoginValidator 컴포넌트 (같은 오브젝트 또는 별도 오브젝트에 부착)")]
    [SerializeField] private LoginValidator loginValidator;

    // Firebase 인증 인스턴스
    private FirebaseAuth auth;
    private FirebaseUser currentUser;
    private bool isFirebaseReady = false;

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
                    Debug.Log($"[FirebaseAuthManager] 이미 로그인됨: {currentUser.Email}");
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
            SetStatus("Firebase가 아직 초기화되지 않았습니다.", true);
            return;
        }

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            SetStatus("이메일과 비밀번호를 입력해주세요.", true);
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
            SetStatus("로그인 성공!", false);

            // 로그인 성공 후 메인 씬으로 이동
            await Task.Delay(500);
            SceneManager.LoadScene(mainSceneName);
        }
        catch (FirebaseException ex)
        {
            string errorMessage = GetFirebaseErrorMessage(ex);
            SetStatus(errorMessage, true);
            Debug.LogWarning($"[FirebaseAuthManager] 로그인 실패: {ex.Message}");
        }
        catch (System.Exception ex)
        {
            SetStatus("로그인 중 오류가 발생했습니다.", true);
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
    /// Firebase 에러 코드에 따른 한국어 에러 메시지 반환
    /// </summary>
    private string GetFirebaseErrorMessage(FirebaseException ex)
    {
        AuthError errorCode = (AuthError)ex.ErrorCode;

        switch (errorCode)
        {
            case AuthError.WrongPassword:
                return "비밀번호가 올바르지 않습니다.";
            case AuthError.UserNotFound:
                return "등록되지 않은 이메일입니다.";
            case AuthError.InvalidEmail:
                return "이메일 형식이 올바르지 않습니다.";
            case AuthError.UserDisabled:
                return "비활성화된 계정입니다. 관리자에게 문의하세요.";
            case AuthError.TooManyRequests:
                return "너무 많은 시도가 있었습니다.\n잠시 후 다시 시도해주세요.";
            case AuthError.NetworkRequestFailed:
                return "네트워크 연결을 확인해주세요.";
            default:
                return $"오류가 발생했습니다. ({errorCode})";
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
