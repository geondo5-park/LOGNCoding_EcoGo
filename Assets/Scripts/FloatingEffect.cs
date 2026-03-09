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
            // 핵심 버그 픽스: ARCore 앵커(위성)가 잘못된 고도를 잡아 땅 밑이나 우주로 날려버리더라도,
            // X, Z(물리적 지형 위/경도 위치)는 그대로 살려두되, 
            // Y(상하 높이)만 무조건 유저의 카메라 눈높이(0.5m 아래)로 강제 고정시킵니다!
            float baseHeight = camTransform.position.y - 0.5f;
            float floatingOffset = Mathf.Sin(Time.time * frequency) * amplitude;
            
            // 월드 포지션을 덮어씌움 (부모인 앵커의 높이를 완전히 무시함)
            transform.position = new Vector3(transform.position.x, baseHeight + floatingOffset, transform.position.z);
        }
    }
}
