using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Android;

/// <summary>
/// GPS 좌표를 AR 월드 좌표로 변환하여 카드 오브젝트를 배치하는 클래스.
/// 지정된 위도/경도에 3D 카드가 실제 위치에 있는 것처럼 AR 공간에 표시됨.
/// </summary>
public class GPSARCardPlacer : MonoBehaviour
{
    [Header("=== UI 참조 ===")]
    /// <summary>위도/경도 정보를 실시간으로 표시할 UI 텍스트</summary>
    public TMP_Text text_ui;

    /// <summary>목표 지점까지의 거리(m)를 표시할 UI 텍스트</summary>
    public TMP_Text text_distance;

    [Header("=== 목표 GPS 좌표 ===")]
    /// <summary>카드를 배치할 목표 지점의 위도</summary>
    public double targetLat = 37.473221;

    /// <summary>카드를 배치할 목표 지점의 경도</summary>
    public double targetLong = 126.920905;

    [Header("=== 카드 설정 ===")]
    /// <summary>AR 공간에 생성할 카드 프리팹 (3D 오브젝트, Quad 또는 Sprite)</summary>
    public GameObject cardPrefab;

    /// <summary>카드가 배치될 높이 (카메라 기준, 미터)</summary>
    public float cardHeight = 0f;

    /// <summary>카드가 보이는 최대 거리 (미터). 이 거리 이내일 때만 카드가 보임</summary>
    public float maxVisibleDistance = 200f;

    /// <summary>
    /// 실제 거리가 너무 멀 때 카드를 가까이 당겨서 보여줄 최대 배치 거리.
    /// 예: 실제로 100m 떨어져 있어도 카드는 20m 거리에 배치.
    /// </summary>
    public float maxPlacementDistance = 30f;

    [Header("=== AR 카메라 ===")]
    /// <summary>AR 카메라의 Transform (자동으로 Camera.main을 찾음)</summary>
    public Transform arCamera;

    // ── 내부 변수 ──
    private GameObject _spawnedCard;          // 생성된 카드 인스턴스
    private bool _gpsReady = false;           // GPS 초기화 완료 여부
    private double _initialLat, _initialLong; // GPS 최초 수신 시 위치 (기준점)
    private bool _initialPositionSet = false; // 기준점 설정 여부

    IEnumerator Start()
    {
        // AR 카메라가 설정되지 않으면 자동으로 찾기
        if (arCamera == null)
            arCamera = Camera.main.transform;

        // ── 1. 위치 권한 요청 ──
        while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            yield return null;
            Permission.RequestUserPermission(Permission.FineLocation);
        }

        // ── 2. 위치 서비스 활성화 확인 ──
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogError("[GPSARCardPlacer] 위치 서비스가 비활성화되어 있습니다.");
            yield break;
        }

        // ── 3. 위치 서비스 시작 (정확도 2m, 업데이트 간격 1m) ──
        Input.location.Start(2, 1);

        // ── 4. 초기화 대기 (최대 20초) ──
        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (maxWait < 1)
        {
            Debug.LogError("[GPSARCardPlacer] GPS 초기화 타임아웃");
            yield break;
        }

        if (Input.location.status == LocationServiceStatus.Failed)
        {
            Debug.LogError("[GPSARCardPlacer] GPS 시작 실패");
            yield break;
        }

        // ── 5. GPS 준비 완료 ──
        _gpsReady = true;
        Debug.Log("[GPSARCardPlacer] GPS 준비 완료!");
    }

    void Update()
    {
        if (!_gpsReady || Input.location.status != LocationServiceStatus.Running)
            return;

        double myLat = Input.location.lastData.latitude;
        double myLong = Input.location.lastData.longitude;

        // UI에 현재 좌표 표시
        if (text_ui != null)
            text_ui.text = myLat.ToString("F6") + " / " + myLong.ToString("F6");

        // ── 기준점 설정 (최초 1회) ──
        // GPS 최초 수신 위치를 AR 원점(0,0,0)으로 간주
        if (!_initialPositionSet)
        {
            _initialLat = myLat;
            _initialLong = myLong;
            _initialPositionSet = true;
            Debug.Log($"[GPSARCardPlacer] 기준점 설정: {_initialLat}, {_initialLong}");
        }

        // ── 거리 계산 ──
        double remainDistance = HaversineDistance(myLat, myLong, targetLat, targetLong);

        if (text_distance != null)
            text_distance.text = remainDistance.ToString("F1") + "m";

        // ── 카드 배치/업데이트 ──
        if (remainDistance <= maxVisibleDistance)
        {
            PlaceCardAtGPS(myLat, myLong);
        }
        else
        {
            // 범위 밖이면 카드 숨기기
            if (_spawnedCard != null)
                _spawnedCard.SetActive(false);
        }
    }

    /// <summary>
    /// GPS 좌표 차이를 AR 월드 좌표로 변환하여 카드를 배치.
    /// 북쪽 = +Z, 동쪽 = +X 방향으로 매핑.
    /// </summary>
    private void PlaceCardAtGPS(double myLat, double myLong)
    {
        // ── GPS → 미터 변환 ──
        // 위도 1도 ≈ 111,320m
        // 경도 1도 ≈ 111,320m × cos(위도)
        double deltaLat = targetLat - myLat;
        double deltaLong = targetLong - myLong;

        // 북쪽 방향 거리 (미터) → Z축
        double offsetZ = deltaLat * 111320.0;

        // 동쪽 방향 거리 (미터) → X축
        double offsetX = deltaLong * 111320.0 * Math.Cos(myLat * Math.PI / 180.0);

        // 실제 거리
        float actualDistance = (float)Math.Sqrt(offsetX * offsetX + offsetZ * offsetZ);

        // ── 거리 제한 ──
        // 너무 먼 경우, 방향은 유지하되 가까이 배치 (안 보이니까)
        float placementDistance = Mathf.Min(actualDistance, maxPlacementDistance);
        float scale = (actualDistance > 0) ? placementDistance / actualDistance : 1f;

        float worldX = (float)offsetX * scale;
        float worldZ = (float)offsetZ * scale;
        float worldY = cardHeight;

        Vector3 cardPosition = new Vector3(worldX, worldY, worldZ);

        // ── 카드 생성 또는 위치 업데이트 ──
        if (_spawnedCard == null)
        {
            if (cardPrefab != null)
            {
                _spawnedCard = Instantiate(cardPrefab, cardPosition, Quaternion.identity);
                Debug.Log($"[GPSARCardPlacer] 카드 생성 위치: {cardPosition}");
            }
        }
        else
        {
            _spawnedCard.SetActive(true);
            // 부드럽게 위치 업데이트 (GPS 흔들림 완화)
            _spawnedCard.transform.position = Vector3.Lerp(
                _spawnedCard.transform.position, cardPosition, Time.deltaTime * 2f);
        }

        // ── 카드가 항상 카메라를 바라보도록 ──
        if (_spawnedCard != null && arCamera != null)
        {
            Vector3 lookDir = arCamera.position - _spawnedCard.transform.position;
            lookDir.y = 0; // 수평 회전만
            if (lookDir != Vector3.zero)
                _spawnedCard.transform.rotation = Quaternion.LookRotation(-lookDir);
        }
    }

    /// <summary>
    /// 하버사인(Haversine) 공식으로 두 GPS 좌표 간 거리 계산 (미터).
    /// </summary>
    private double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        double R = 6371000; // 지구 반지름 (미터)
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }
}
