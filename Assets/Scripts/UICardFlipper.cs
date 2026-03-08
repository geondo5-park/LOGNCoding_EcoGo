using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.EventSystems;

/// <summary>
/// 획득한 카드 UI 이미지를 클릭(터치)했을 때 뒤집히는 애니메이션을 처리하고,
/// 앞면/뒷면 이미지를 교체해주는 스크립트입니다.
/// </summary>
public class UICardFlipper : MonoBehaviour, IPointerClickHandler
{
    private Image targetImage;
    private Sprite frontSprite;
    private Sprite backSprite;

    private bool isFront = true;
    private bool isFlipping = false;

    // 플립 애니메이션 속도
    public float flipDuration = 0.4f;

    void Awake()
    {
        targetImage = GetComponent<Image>();
    }

    /// <summary>
    /// 카드를 처음 띄울 때 앞면과 뒷면 이미지를 설정합니다.
    /// </summary>
    public void SetCardDatas(Sprite front, Sprite back)
    {
        frontSprite = front;
        backSprite = back;
        
        isFront = true;
        isFlipping = false;
        
        if (targetImage != null)
        {
            targetImage.sprite = frontSprite;
            // 회전값 초기화
            targetImage.transform.localRotation = Quaternion.identity;
        }
    }

    // UI 이미지가 클릭되었을 때 호출됩니다 (EventTrigger 또는 IPointerClickHandler)
    public void OnPointerClick(PointerEventData eventData)
    {
        if (isFlipping) return;
        
        // 앞뒷면 이미지가 모두 제대로 설정되어 있을 때만 뒤집기 허용
        if (frontSprite != null && backSprite != null)
        {
            StartCoroutine(FlipAnimation());
        }
        else
        {
            Debug.LogWarning("[UICardFlipper] 뒷면 이미지가 없어서 뒤집을 수 없습니다.");
        }
    }

    private IEnumerator FlipAnimation()
    {
        isFlipping = true;

        Quaternion startRot = targetImage.transform.localRotation;
        Quaternion midRot = startRot * Quaternion.Euler(0, 90f, 0); // 90도 회전 (안 보이는 상태)
        
        float elapsed = 0f;
        float halfDuration = flipDuration / 2f;

        // 1. 현재 각도 -> 90도 더 회전시켜서 카드를 얇게(안 보이게) 만듦
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            targetImage.transform.localRotation = Quaternion.Lerp(startRot, midRot, elapsed / halfDuration);
            yield return null;
        }

        // 2. 안보일 때 이미지를 교체
        isFront = !isFront;
        targetImage.sprite = isFront ? frontSprite : backSprite;

        // 거꾸로 뒤집힌 이미지 바로잡기 방지 등을 위해 y축 반사 적용
        if (!isFront) 
            targetImage.transform.localRotation = Quaternion.Euler(0, 270f, 0); // 뒷면 시작 각도
        else 
            targetImage.transform.localRotation = Quaternion.Euler(0, 90f, 0); // 앞면 시작 각도

        startRot = targetImage.transform.localRotation;
        Quaternion endRot = startRot * Quaternion.Euler(0, 90f, 0); // 다시 90도 마저 펼침

        // 3. 다시 카드를 펼침
        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            targetImage.transform.localRotation = Quaternion.Lerp(startRot, endRot, elapsed / halfDuration);
            yield return null;
        }
        
        targetImage.transform.localRotation = endRot;
        isFlipping = false;
    }
}
