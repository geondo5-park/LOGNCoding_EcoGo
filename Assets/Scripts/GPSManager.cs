using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Android 권한 API는 Android 빌드에서만 사용 가능
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

// iOS 네이티브 권한 확인용 (CoreLocation)
#if UNITY_IOS
using UnityEngine.iOS;
#endif

public class GPSManager : MonoBehaviour
{
    // ── UI 참조 ──
    // 위도/경도 정보를 실시간으로 표시할 UI 텍스트
    public TMP_Text text_latLong;
    public TMP_Text text_remainDistance;
    
    public bool isFirstCardPopup = false;

    // 위도 배열
    public double[] lats;

    // 경도 배열
    public double[] longs;

    public GameObject cardPopup;
    
    IEnumerator Start()
    {
        // ── 1. 플랫폼별 위치 권한 요청 ──
        yield return StartCoroutine(RequestLocationPermission());
        
        // ── 2. 위치 서비스 활성화 확인 ──
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("[GPSManager] 위치 서비스가 비활성화 상태입니다. 기기 설정에서 위치를 켜주세요.");
            yield break;
        }
        
        // ── 3. 위치 서비스 시작 ──
        // 매개변수: desiredAccuracyInMeters : 원하는 서비스 정확도, updateDistanceInMeters : 위치 업데이트 최소 거리
        Input.location.Start(0.5f, 0.5f);
        
        // ── 4. 위치 서비스가 초기화될 때까지 최대 20초 대기 ──
        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }
        if (maxWait < 1)
        {
            Debug.LogError("[GPSManager] 위치 서비스 초기화 시간 초과 (Timed out)");
            yield break;
        }

        // ── 5. 초기화 결과 확인 ──
        // 위치 서비스 시작 실패 시 종료
        if (Input.location.status == LocationServiceStatus.Failed)
        {
            Debug.LogError("[GPSManager] 위치를 확인할 수 없습니다 (Unable to determine device location)");
            yield break;
        }
        else
        {
            // 위치 서비스 시작 성공 - 최초 위치 정보 로그 출력
            Debug.Log("[GPSManager] Location: " 
                + Input.location.lastData.latitude + " " 
                + Input.location.lastData.longitude + " " 
                + Input.location.lastData.altitude + " " 
                + Input.location.lastData.horizontalAccuracy + " " 
                + Input.location.lastData.timestamp);

            // ── 6. 실시간 위치 표시 ──
            // 매 프레임마다 UI 텍스트에 현재 위도/경도를 갱신하여 표시
            while (true)
            {
                yield return null;
                text_latLong.text = Input.location.lastData.latitude + " / " + Input.location.lastData.longitude;
            }
        }

        // Input.location.Stop(); // 위 무한 루프로 인해 도달하지 않는 코드
    }

    /// <summary>
    /// 플랫폼별 위치 권한 요청 코루틴.
    /// - Android: Permission.RequestUserPermission 사용
    /// - iOS: Input.location.Start() 호출 시 자동으로 권한 다이얼로그 표시
    ///        (Info.plist에 NSLocationWhenInUseUsageDescription 설정 필요)
    /// - Editor: 권한 체크 없이 바로 통과
    /// </summary>
    private IEnumerator RequestLocationPermission()
    {
#if UNITY_ANDROID
        // Android: 정밀 위치(Fine Location) 권한을 허용할 때까지 반복 요청
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            // 사용자가 권한 다이얼로그에 응답할 시간을 줌
            yield return new WaitForSeconds(1f);
            
            // 권한이 부여될 때까지 대기 (최대 10초)
            float timeout = 10f;
            while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation) && timeout > 0f)
            {
                yield return new WaitForSeconds(0.5f);
                timeout -= 0.5f;
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                Debug.LogError("[GPSManager] Android 위치 권한이 거부되었습니다.");
                yield break;
            }
        }
        Debug.Log("[GPSManager] Android 위치 권한 획득 완료");

#elif UNITY_IOS
        // iOS: Input.location.Start()를 호출하면 시스템이 자동으로 권한 다이얼로그를 표시.
        // Unity Player Settings → Other Settings → Location Usage Description 에
        // 위치 사용 목적을 반드시 기입해야 합니다.
        // (또는 Info.plist의 NSLocationWhenInUseUsageDescription 키에 직접 설정)
        Debug.Log("[GPSManager] iOS - 위치 권한 요청은 위치 서비스 시작 시 자동으로 진행됩니다.");
        yield return null;

#else
        // Unity Editor 또는 기타 플랫폼: 권한 체크 건너뛰기
        Debug.Log("[GPSManager] Editor/기타 플랫폼 - 권한 체크를 건너뜁니다.");
        yield return null;
#endif
    }

    /// <summary>
    /// 매 프레임마다 현재 위치와 목표 지점 사이의 거리를 확인.
    /// 목표 지점(lats[0], longs[0])으로부터 20m 이내에 진입하면 카드 팝업을 표시.
    /// </summary>
    void Update()
    {
        // GPS가 정상 동작 중일 때만 거리 계산 수행
        if (Input.location.status == LocationServiceStatus.Running)
        {
            // 현재 디바이스의 위도/경도 가져오기
            double myLat = Input.location.lastData.latitude;
            double myLong = Input.location.lastData.longitude;

            CardPopup(myLat, myLong);   
        }
    }

    private void CardPopup(double myLat, double myLong)
    {
        double remainDistance = distance(myLat, myLong, lats[0], longs[0]);
        text_remainDistance.text = remainDistance.ToString("F6");
       
        // 목표 지점으로부터 20m 이내에 진입한 경우
        if (remainDistance <= 20f)
        {
            if (!isFirstCardPopup)
            {
                isFirstCardPopup = true;
                cardPopup.SetActive(true);
            }
        }
    }

    /// <summary>
    /// 하버사인(Haversine) 공식을 이용한 두 GPS 좌표 간 지표면 거리 계산.
    /// </summary>
    private double distance(double lat1, double lon1, double lat2, double lon2)
    {
        double theta = lon1 - lon2;
        double dist = Math.Sin(Deg2Rad(lat1)) * Math.Sin(Deg2Rad(lat2)) + Math.Cos(Deg2Rad(lat1)) * Math.Cos(Deg2Rad(lat2)) * Math.Cos(Deg2Rad(theta));
        
        dist = Math.Acos(dist);
        dist = Rad2Deg(dist);
        dist = dist * 60 * 1.1515;
        dist = dist * 1609.344;

        return dist;
    }

    private double Deg2Rad(double deg)
    {
        return (deg * Mathf.PI / 180.0f);
    }
    
    private double Rad2Deg(double rad)
    {
        return (rad * 180.0f / Mathf.PI);
    }

    public void OnOpenBox()
    {
        cardPopup.SetActive(true);
    }
    
}