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
        
        // 클릭 즉시 백그라운드에서 Firebase 저장을 시작
        if (gpsManager != null)
        {
            gpsManager.UpdatePlantProgress(plantId);
        }

        // 둥실둥실 및 카메라바라보기 효과를 모두 정지 (직접 회전 제어하기 위함)
        FloatingEffect floating = GetComponent<FloatingEffect>();
        if (floating != null) floating.enabled = false;
        
        FaceCameraAR faceCam = GetComponent<FaceCameraAR>();
        if (faceCam != null) faceCam.enabled = false;

        StartCoroutine(CollectAnimation());
    }

    private IEnumerator CollectAnimation()
    {
        Transform cam = Camera.main.transform;
        
        Vector3 startPos = transform.position;
        Vector3 startScale = transform.localScale;
        
        // 1단계: 하늘로 번쩍! 튀어오르며 커지기 (시선 끌기) - 속도 상향
        float jumpDuration = 0.4f;
        float elapsed = 0f;
        
        Vector3 jumpPos = startPos + Vector3.up * 2.0f; 
        Vector3 explodeScale = startScale * 1.5f;

        while (elapsed < jumpDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / jumpDuration;
            
            // 빠른 발진, 부드러운 정착 (Ease-Out)
            float easeOut = 1f - (1f - t) * (1f - t);
            
            transform.position = Vector3.Lerp(startPos, jumpPos, easeOut);
            transform.localScale = Vector3.Lerp(startScale, explodeScale, easeOut);
            
            // 우아하게 도는 스핀 (시간이 짧아졌으므로 회전 속도 증가)
            transform.Rotate(0, 1000f * Time.deltaTime, 0);

            yield return null;
        }

        // 극적인 연출을 위한 허공 정지 타임 (짧게)
        yield return new WaitForSeconds(0.1f);
        
        // 2단계: 카메라(스마트폰) 속으로 부드럽게 빨려들어오기 - 속도 대폭 상향
        float suckDuration = 0.5f;
        elapsed = 0f;
        
        Vector3 midPos = transform.position;
        Vector3 midScale = transform.localScale;
        
        while (elapsed < suckDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / suckDuration;
            
            // 점점 가속 (Ease-In)
            float easeIn = t * t * t;
            
            // 유저 폰 딱 중앙 하단(주머니 속) 위치로 흡수
            Vector3 targetPos = cam.position + (cam.forward * 0.4f) - (cam.up * 0.6f);
            
            transform.position = Vector3.Lerp(midPos, targetPos, easeIn);
            transform.localScale = Vector3.Lerp(midScale, Vector3.zero, easeIn);
            
            // 스핀 속도 증가
            transform.Rotate(0, 2000f * Time.deltaTime, 0);
            
            yield return null;
        }

        // 애니메이션이 끝나면 GPS Manager에게 UI 팝업(뒤집기 화면)을 띄우라고 지시
        if (gpsManager != null)
        {
            gpsManager.ShowAcquiredCardUI(plantId);
        }

        // 화면에서 빨려들어간 3D 원본 오브젝트는 완벽히 파괴
        Destroy(gameObject);
    }
}
