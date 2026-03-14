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
        Renderer cardRenderer = GetComponent<Renderer>();
        Material cardMat = (cardRenderer != null) ? cardRenderer.material : null;
        
        Vector3 startPos = transform.position;
        Vector3 startScale = transform.localScale;

        // [강화] 더 큰 4방향 빛 줄기(Cross Shine) 생성
        GameObject shineContainer = new GameObject("ShineEffect");
        shineContainer.transform.position = startPos;
        if (cam != null) shineContainer.transform.LookAt(cam);

        // 빛 줄기의 두께와 길이를 대폭 상향
        GameObject ray1 = CreateShineRay(shineContainer.transform, new Vector3(15f, 0.5f, 0.1f)); 
        GameObject ray2 = CreateShineRay(shineContainer.transform, new Vector3(0.5f, 15f, 0.1f)); 
        Material rayMat1 = ray1.GetComponent<Renderer>().material;
        Material rayMat2 = ray2.GetComponent<Renderer>().material;

        // 0단계: 부드럽게 커지며 빛나기 (갑자기 커지는 현상 수정)
        if (cardMat != null && cardMat.HasProperty("_EmissionColor"))
        {
            cardMat.EnableKeyword("_EMISSION");
            cardMat.SetColor("_EmissionColor", new Color(0.5f, 0.5f, 0.4f) * 2f);
        }

        float shineElapsed = 0f;
        float shineDuration = 0.6f; 
        
        while (shineElapsed < shineDuration)
        {
            shineElapsed += Time.deltaTime;
            float t = shineElapsed / shineDuration;
            float easedT = Mathf.SmoothStep(0f, 1f, t);
            
            // 크기 부드럽게 증가
            transform.localScale = Vector3.Lerp(startScale, startScale * 1.6f, easedT);
            
            // 카드 색상 발광 효과
            if (cardMat != null) cardMat.color = Color.Lerp(Color.white, new Color(2f, 2f, 1.8f), easedT);

            // 빛 줄기 애니메이션
            float scaleValue = Mathf.SmoothStep(0f, 25f, t * 1.5f);
            float alphaValue = 1f - t;
            
            ray1.transform.localScale = new Vector3(scaleValue, 0.4f * alphaValue, 1f);
            ray2.transform.localScale = new Vector3(0.4f * alphaValue, scaleValue, 1f);
            
            Color shineColor = new Color(1f, 1f, 0.9f, alphaValue);
            rayMat1.color = shineColor;
            rayMat2.color = shineColor;
            
            shineContainer.transform.Rotate(0, 0, 120f * Time.deltaTime);

            yield return null;
        }

        if (shineContainer != null) Destroy(shineContainer);
        
        // 1단계: 하늘로 번쩍! 튀어오르기 (모션 시작)
        float jumpDuration = 0.35f;
        float elapsed = 0f;
        Vector3 jumpPos = startPos + Vector3.up * 2.5f; 
        Vector3 explodeScale = startScale * 1.3f;

        while (elapsed < jumpDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / jumpDuration;
            float easeOut = 1f - (1f - t) * (1f - t);
            
            transform.position = Vector3.Lerp(startPos, jumpPos, easeOut);
            transform.localScale = Vector3.Lerp(startScale * 1.6f, explodeScale, easeOut);
            if (cardMat != null) cardMat.color = Color.Lerp(new Color(2f, 2f, 1.8f), Color.white, easeOut);
            
            transform.Rotate(0, 1500f * Time.deltaTime, 0);
            yield return null;
        }

        yield return new WaitForSeconds(0.05f);
        
        // 2단계: 카메라 속으로 빨려들어오기
        float suckDuration = 0.4f;
        elapsed = 0f;
        Vector3 midPos = transform.position;
        Vector3 midScale = transform.localScale;
        
        while (elapsed < suckDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / suckDuration;
            float easeIn = t * t * t;
            
            if (cam != null)
            {
                Vector3 targetPos = cam.position + (cam.forward * 0.4f) - (cam.up * 0.6f);
                transform.position = Vector3.Lerp(midPos, targetPos, easeIn);
            }
            transform.localScale = Vector3.Lerp(midScale, Vector3.zero, easeIn);
            if (cardMat != null) cardMat.color = Color.Lerp(Color.white, Color.gray, easeIn);
            
            transform.Rotate(0, 2500f * Time.deltaTime, 0);
            yield return null;
        }

        if (gpsManager != null) gpsManager.ShowAcquiredCardUI(plantId);
        Destroy(gameObject);
    }

    private GameObject CreateShineRay(Transform parent, Vector3 defaultScale)
    {
        GameObject ray = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ray.transform.SetParent(parent);
        ray.transform.localPosition = Vector3.zero;
        ray.transform.localRotation = Quaternion.identity;
        ray.transform.localScale = defaultScale;
        
        Destroy(ray.GetComponent<Collider>());
        
        Renderer r = ray.GetComponent<Renderer>();
        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null) unlitShader = Shader.Find("Unlit/Transparent");
        r.material = new Material(unlitShader);
        
        if (unlitShader.name.Contains("Universal Render Pipeline"))
        {
            r.material.SetFloat("_Surface", 1);
            r.material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            r.material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            r.material.SetInt("_ZWrite", 0);
            r.material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            r.material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        
        r.material.color = new Color(1f, 1f, 1f, 0f);
        
        return ray;
    }
}
