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
        
        float elapsed = 0f;
        float halfDuration = flipDuration / 2f;

        // 1. 현재 각도(0도)에서 90도로 카드를 얇게 회전시켜 안 보이게 만듦
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / halfDuration;
            targetImage.transform.localRotation = Quaternion.Euler(0, Mathf.Lerp(0f, 90f, t), 0);
            yield return null;
        }

        // 2. 카드가 90도로 얇아져 안 보일 때 (가장자리) 이미지 교체
        isFront = !isFront;
        targetImage.sprite = isFront ? frontSprite : backSprite;

        // 3. 다시 카드를 펼침 (-90도에서 0도로 회전시켜 계속 한 방향으로 도는 효과 유지)
        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / halfDuration;
            targetImage.transform.localRotation = Quaternion.Euler(0, Mathf.Lerp(-90f, 0f, t), 0);
            yield return null;
        }
        
        // 회전을 완전히 초기화해서 터치가 안 먹히는 뒤집힘 상태(Reverse Graphic)를 방지
        targetImage.transform.localRotation = Quaternion.identity; 
        isFlipping = false;
    }
}
