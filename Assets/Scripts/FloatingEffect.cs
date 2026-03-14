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
    public float frequency = 1.5f;

    private Vector3 startLocalPos;
    private Transform camTransform;

    void Start()
    {
        if (Camera.main != null) camTransform = Camera.main.transform;
    }

    void Update()
    {
        if (camTransform != null)
        {
            // [개선] 월드 좌표를 강제로 덮어쓰면 부모인 앵커의 움직임을 방해하게 됩니다.
            // 대신, 로컬 좌표만 수정하여 수평(X, Z) 고정력은 앵커에게 맡기고,
            // 수직(Y) 높이만 카메라 높이에 맞춰 부드럽게 보정합니다.

            // 현재 부모(앵커)가 있다면 부모의 위치를 기준으로 높이를 계산
            float targetWorldY = camTransform.position.y - 0.5f;
            float floatingOffset = Mathf.Sin(Time.time * frequency) * amplitude;
            
            // 만약 부모가 있다면, 부모의 월드 높이를 뺀 값을 로컬 Y로 설정
            if (transform.parent != null)
            {
                float localY = (targetWorldY + floatingOffset) - transform.parent.position.y;
                transform.localPosition = new Vector3(0, localY, 0);
            }
            else
            {
                // 부모가 없는 Fallback 상태라면 월드 Y만 수정 (X, Z는 유지)
                transform.position = new Vector3(transform.position.x, targetWorldY + floatingOffset, transform.position.z);
            }
        }
    }
}
