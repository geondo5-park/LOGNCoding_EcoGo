using UnityEngine;
using System.Collections;

/// <summary>
/// AR 스폰된 카드를 터치(클릭)했을 때, 카드를 획득하는 스핀 애니메이션을 재생하고
/// 파괴되면서 UI를 팝업시키는 스크립트입니다.
/// </summary>
public class CatchCardEffect : MonoBehaviour
{
    public GPSManager gpsManager;
    public string plantId;
    
    private bool isCollected = false;

    public void OnClicked()
    {
        if (isCollected) return;
        isCollected = true;
        
        // 둥실둥실 효과 정지
        FloatingEffect floating = GetComponent<FloatingEffect>();
        if (floating != null) floating.enabled = false;

        StartCoroutine(CollectAnimation());
    }

    private IEnumerator CollectAnimation()
    {
        Transform cam = Camera.main.transform;
        float duration = 0.8f;   // 애니메이션 재생 시간
        float elapsed = 0f;

        Vector3 startPos = transform.position;
        Vector3 startScale = transform.localScale;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            
            // 1. 카메라 쪽으로 점점 당겨오기 (화면 하단 방향)
            Vector3 targetPos = cam.position + (cam.forward * 0.5f) - (cam.up * 0.2f);
            transform.position = Vector3.Lerp(startPos, targetPos, t);
            
            // 2. 화려하게 빙글빙글 회전
            transform.Rotate(0, 1000f * Time.deltaTime, 0);
            
            // 3. 점점 작아지면서 사라짐
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);

            yield return null;
        }

        // 애니메이션이 끝나면 GPS Manager에게 UI를 띄우라고 지시
        if (gpsManager != null)
        {
            gpsManager.ShowAcquiredCardUI(plantId);
        }

        // 3D 카드 오브젝트 파괴
        Destroy(gameObject);
    }
}
