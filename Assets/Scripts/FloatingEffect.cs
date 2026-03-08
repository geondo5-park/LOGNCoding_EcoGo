using UnityEngine;

/// <summary>
/// 프리팹이 소환되었을 때 위아래로 부드럽게 둥실둥실 움직이게 만드는 스크립트입니다.
/// </summary>
public class FloatingEffect : MonoBehaviour
{
    [Header("=== Floating Settings ===")]
    [Tooltip("상하 이동 진폭 (얼마나 높게 움직일지)")]
    public float amplitude = 0.1f;
    
    [Tooltip("상하 이동 속도")]
    public float frequency = 1f;

    private Vector3 startPos;
    private Transform camTransform;

    void Start()
    {
        startPos = transform.position;
        camTransform = Camera.main.transform;
    }

    void Update()
    {
        // 1. 상하 둥실둥실 효과
        float newY = startPos.y + Mathf.Sin(Time.time * frequency) * amplitude;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);

        // 2. 빌보드 효과 (항상 유저/카메라를 바라보게 함)
        if (camTransform != null)
        {
            // 카메라가 있는 방향 벡터 계산
            Vector3 lookDir = camTransform.position - transform.position;
            lookDir.y = 0; // 위아래로 눕거나 기울어지지 않도록 높이 축 고정

            if (lookDir != Vector3.zero)
            {
                // 스프라이트는 기본적으로 반대 방향(-Z)을 바라보아야 카메라 쪽에 앞면이 제대로 보입니다! (투명해지거나 사라짐 방지)
                transform.rotation = Quaternion.LookRotation(-lookDir);
            }
        }
    }
}
