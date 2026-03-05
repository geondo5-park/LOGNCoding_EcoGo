using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 사각형 이미지의 모서리를 둥글게 해주는 컴포넌트
/// UI Image에 이 스크립트를 추가하면 자동으로 둥근 모서리가 적용됨
/// Inspector에서 cornerRadius 값으로 둥글기 조절 가능
/// </summary>
[RequireComponent(typeof(Image))]
[ExecuteInEditMode]
public class CircleImageEffect : MonoBehaviour
{
    [Header("모서리 둥글기 설정")]
    [Tooltip("모서리 둥글기 (0 = 직각, 0.5 = 최대 둥글기)")]
    [Range(0f, 0.5f)]
    [SerializeField] private float cornerRadius = 0.1f;

    // 둥근 모서리 셰이더 Material (자동 생성됨)
    private Material roundedMaterial;
    private Image image;
    private RectTransform rectTransform;

    private void OnEnable()
    {
        image = GetComponent<Image>();
        rectTransform = GetComponent<RectTransform>();

        // RoundedImage 셰이더를 찾아서 Material 생성
        Shader roundedShader = Shader.Find("UI/RoundedImage");
        if (roundedShader == null)
        {
            Debug.LogError("[CircleImageEffect] 'UI/RoundedImage' 셰이더를 찾을 수 없습니다.");
            return;
        }

        roundedMaterial = new Material(roundedShader);
        image.material = roundedMaterial;

        UpdateMaterial();
    }

    private void Update()
    {
        // 실시간으로 크기/반지름 변경 반영
        UpdateMaterial();
    }

    /// <summary>
    /// Material 속성 업데이트 (크기, 반지름)
    /// </summary>
    private void UpdateMaterial()
    {
        if (roundedMaterial == null || rectTransform == null) return;

        roundedMaterial.SetFloat("_Radius", cornerRadius);
        roundedMaterial.SetFloat("_Width", rectTransform.rect.width);
        roundedMaterial.SetFloat("_Height", rectTransform.rect.height);
    }

    private void OnDisable()
    {
        // Material 원복 및 정리
        if (image != null)
            image.material = null;

        if (roundedMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(roundedMaterial);
            else
                DestroyImmediate(roundedMaterial);
        }
    }
}
