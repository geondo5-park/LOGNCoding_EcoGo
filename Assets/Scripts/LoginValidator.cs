using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text.RegularExpressions;

/// <summary>
/// 로그인 화면의 이메일/비밀번호 유효성 검사를 담당하는 스크립트
/// - 이메일: 올바른 이메일 형식인지 확인 (예: user@example.com)
/// - 비밀번호: 6자 이상인지 확인
/// </summary>
public class LoginValidator : MonoBehaviour
{
    [Header("입력 필드")]
    [Tooltip("이메일 입력 필드 (TMP_InputField)")]
    [SerializeField] private TMP_InputField emailInputField;

    [Tooltip("비밀번호 입력 필드 (TMP_InputField)")]
    [SerializeField] private TMP_InputField passwordInputField;

    [Header("에러 메시지")]
    [Tooltip("에러 메시지를 표시할 TextMeshPro (이메일/비밀번호 공용)")]
    [SerializeField] private TMP_Text errorText;

    [Header("로그인 버튼")]
    [Tooltip("로그인 버튼")]
    [SerializeField] private Button loginButton;

    // 이메일 유효성 검사용 정규식 패턴
    // 기본적인 이메일 형식: xxx@xxx.xxx
    private const string EmailPattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";

    // 비밀번호 최소 길이
    private const int MinPasswordLength = 6;

    // 실제 비밀번호 저장 (화면에는 *로 표시)
    private string actualPassword = "";

    private void Start()
    {
        // 시작 시 에러 메시지 숨기기
        if (errorText != null) errorText.text = "";

        // 입력 필드 변경 시 실시간 검증
        if (emailInputField != null)
            emailInputField.onValueChanged.AddListener(OnEmailChanged);

        if (passwordInputField != null)
        {
            // 비밀번호 필드를 Standard 타입으로 강제 설정
            // (Password 타입은 한국어 IME와 충돌하므로 직접 마스킹 처리)
            passwordInputField.contentType = TMP_InputField.ContentType.Standard;
            passwordInputField.onValueChanged.AddListener(OnPasswordChanged);
        }

        // 로그인 버튼 클릭 이벤트 연결
        if (loginButton != null)
            loginButton.onClick.AddListener(OnLoginButtonClicked);
    }

    /// <summary>
    /// 이메일 입력이 변경될 때 호출 (실시간 검증)
    /// </summary>
    private void OnEmailChanged(string email)
    {
        // 입력 중에는 에러 메시지를 지워서 UX 개선
        ClearError();
    }

    /// <summary>
    /// 비밀번호 입력이 변경될 때 호출 (실시간 검증)
    /// 내장 Password 마스킹 대신 직접 * 마스킹 처리
    /// </summary>
    private void OnPasswordChanged(string displayText)
    {
        // 입력 중에는 에러 메시지를 지워서 UX 개선
        ClearError();

        // 표시된 텍스트에서 실제 비밀번호 역추적
        int displayLen = displayText.Length;
        int actualLen = actualPassword.Length;

        if (displayLen > actualLen)
        {
            // 문자가 추가됨: *가 아닌 새 문자를 찾아서 실제 비밀번호에 추가
            for (int i = 0; i < displayText.Length; i++)
            {
                if (displayText[i] != '*')
                {
                    actualPassword = actualPassword.Insert(i, displayText[i].ToString());
                    break;
                }
            }
        }
        else if (displayLen < actualLen)
        {
            // 문자가 삭제됨: 커서 위치를 기반으로 실제 비밀번호에서도 삭제
            int caretPos = passwordInputField.caretPosition;
            int deleteIndex = caretPos;
            int deleteCount = actualLen - displayLen;
            if (deleteIndex >= 0 && deleteIndex + deleteCount <= actualLen)
                actualPassword = actualPassword.Remove(deleteIndex, deleteCount);
            else
                actualPassword = actualPassword.Substring(0, displayLen);
        }

        // 리스너 일시 해제 후 *로 마스킹 표시
        passwordInputField.onValueChanged.RemoveListener(OnPasswordChanged);
        int caret = passwordInputField.caretPosition;
        passwordInputField.text = new string('*', actualPassword.Length);
        passwordInputField.caretPosition = caret;
        passwordInputField.onValueChanged.AddListener(OnPasswordChanged);
    }

    /// <summary>
    /// 로그인 버튼 클릭 시 호출
    /// </summary>
    public void OnLoginButtonClicked()
    {
        // 모든 유효성 검사를 수행하고 결과 확인
        bool isValid = ValidateAll();

        if (isValid)
        {
            Debug.Log("[LoginValidator] Login successful! Email: " + emailInputField.text);
            // 참고: 실제 비밀번호는 actualPassword 변수에 저장되어 있음
            // TODO: 여기에 실제 로그인 로직 추가 (서버 통신, 씬 전환 등)
            OnLoginSuccess();
        }
        else
        {
            Debug.LogWarning("[LoginValidator] Login failed - please check your input.");
        }
    }

    /// <summary>
    /// 이메일과 비밀번호 모두 유효성 검사 수행
    /// </summary>
    /// <returns>모든 검증을 통과하면 true</returns>
    public bool ValidateAll()
    {
        // 이메일 먼저 검증 (에러 표시 우선순위: 이메일 > 비밀번호)
        if (!ValidateEmail()) return false;
        if (!ValidatePassword()) return false;

        return true;
    }

    /// <summary>
    /// 이메일 형식 유효성 검사
    /// - 빈 값 체크
    /// - 정규식을 이용한 이메일 형식 체크 (xxx@xxx.xxx)
    /// </summary>
    /// <returns>유효한 이메일이면 true</returns>
    public bool ValidateEmail()
    {
        if (emailInputField == null)
        {
            Debug.LogError("[LoginValidator] Email input field is not assigned.");
            return false;
        }

        string email = emailInputField.text.Trim();

        // 빈 값 체크
        if (string.IsNullOrEmpty(email))
        {
            SetError("Please enter your email.");
            return false;
        }

        // 이메일 형식 체크 (정규식)
        if (!Regex.IsMatch(email, EmailPattern))
        {
            SetError("Invalid email format. (e.g., user@example.com)");
            return false;
        }

        // 유효한 이메일
        return true;
    }

    /// <summary>
    /// 비밀번호 유효성 검사
    /// - 빈 값 체크
    /// - 최소 6자 이상 체크
    /// </summary>
    /// <returns>유효한 비밀번호이면 true</returns>
    public bool ValidatePassword()
    {
        if (passwordInputField == null)
        {
            Debug.LogError("[LoginValidator] Password input field is not assigned.");
            return false;
        }

        // 화면에는 *로 표시되므로 실제 비밀번호 변수 사용
        string password = actualPassword;

        // 빈 값 체크
        if (string.IsNullOrEmpty(password))
        {
            SetError("Please enter your password.");
            return false;
        }

        // 최소 길이 체크 (6자 이상)
        if (password.Length < MinPasswordLength)
        {
            SetError($"Password must be at least {MinPasswordLength} characters. (currently {password.Length})");
            return false;
        }

        // 유효한 비밀번호
        return true;
    }

    /// <summary>
    /// 로그인 성공 시 호출되는 메서드
    /// 필요에 따라 씬 전환, 서버 요청 등을 추가하세요.
    /// </summary>
    private void OnLoginSuccess()
    {
        Debug.Log("[LoginValidator] Processing login...");
        // TODO: 씬 전환 예시
        // UnityEngine.SceneManagement.SceneManager.LoadScene("MainScene");
    }

    /// <summary>
    /// 실제 비밀번호를 반환합니다 (화면에는 *로 표시됨).
    /// FirebaseAuthManager 등 외부에서 사용합니다.
    /// </summary>
    public string GetActualPassword()
    {
        return actualPassword;
    }

    #region 에러 메시지 헬퍼 메서드

    /// <summary>
    /// 에러 메시지 표시 (이메일/비밀번호 공용)
    /// </summary>
    private void SetError(string message)
    {
        if (errorText != null)
        {
            errorText.text = message;
            errorText.color = Color.red;
        }
        Debug.LogWarning("[LoginValidator] Error: " + message);
    }

    /// <summary>
    /// 에러 메시지 지우기
    /// </summary>
    private void ClearError()
    {
        if (errorText != null)
            errorText.text = "";
    }

    #endregion

    private void OnDestroy()
    {
        // 이벤트 리스너 해제 (메모리 누수 방지)
        if (emailInputField != null)
            emailInputField.onValueChanged.RemoveListener(OnEmailChanged);

        if (passwordInputField != null)
            passwordInputField.onValueChanged.RemoveListener(OnPasswordChanged);

        if (loginButton != null)
            loginButton.onClick.RemoveListener(OnLoginButtonClicked);
    }
}
