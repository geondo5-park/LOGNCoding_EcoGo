using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using Firebase.Firestore;

// Android 권한 API는 Android 빌드에서만 사용 가능
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

// iOS 네이티브 권한 확인용 (CoreLocation)
#if UNITY_IOS
using UnityEngine.iOS;
#endif

[System.Serializable]
public class PlantData
{
    public string id;
    public double lat;
    public double lng;
    public bool isSpawned;
}

public class GPSManager : MonoBehaviour
{
    // ── UI 참조 ──
    public TMP_Text text_latLong;
    public TMP_Text text_remainDistance;
    public GameObject cardPopup;
    [Tooltip("획득한 카드의 이미지를 보여줄 UI Image 컴포넌트")]
    public UnityEngine.UI.Image acquiredCardImage;
    [Tooltip("카드 팝업(UI)을 닫을 버튼(X 버튼)")]
    public GameObject closeButton;
    private bool isFirstCardPopup = false;
    private int _spawnedCount = 0;

    [Header("AR Spawn Settings")]
    [Tooltip("AR 구동 시 메인 카메라. 비워두면 Camera.main을 사용합니다.")]
    public Transform arCamera;
    
    [Tooltip("스폰될 프리팹이 카메라 앞 몇 m 거리에 위치할지 설정")]
    public float spawnDistance = 1.5f;

    [Tooltip("스폰될 프리팹의 크기 (기본 0.3f)")]
    public float spawnScale = 0.3f;

    [Tooltip("공용 카드 프리팹 (Resources/Prefabs/CardPrefab 등에서 로드 권장)")]
    public GameObject cardPrefab;

    [HideInInspector]
    public List<PlantData> plantList = new List<PlantData>();

    IEnumerator Start()
    {
        // ── 0. CSV 데이터 데이터 로드 ──
        yield return StartCoroutine(LoadCSV());

        // ── Firestore에서 이미 발견한 식물 필터링 ──
        Task filterTask = FilterDiscoveredPlantsAsync();
        yield return new WaitUntil(() => filterTask.IsCompleted);

        // ── CardPrefab이 할당되지 않았다면 Resources에서 로드 시도 ──
        if (cardPrefab == null)
        {
            cardPrefab = Resources.Load<GameObject>("Prefabs/CardPrefab");
        }

        // ── 1. 플랫폼별 위치 권한 요청 ──
        yield return StartCoroutine(RequestLocationPermission());
        
        // ── 2. 위치 서비스 활성화 확인 ──
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("[GPSManager] 위치 서비스가 비활성화 상태입니다. 기기 설정에서 위치를 켜주세요.");
            yield break;
        }
        
        // ── 3. 위치 서비스 시작 (정밀도 1.0m 모드) ──
        Input.location.Start(1f, 1f);
        
        // ── 4. 위치 서비스가 초기화될 때까지 대기 ──
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
        if (Input.location.status == LocationServiceStatus.Failed)
        {
            Debug.LogError("[GPSManager] 위치를 확인할 수 없습니다 (Unable to determine device location)");
            yield break;
        }
        else
        {
            Debug.Log("[GPSManager] Location Started: " + Input.location.lastData.latitude + " / " + Input.location.lastData.longitude);

            // ── 6. 실시간 위치 표기 업데이트 루프 ──
            while (true)
            {
                yield return new WaitForSeconds(0.5f);
                if (text_latLong != null)
                {
                    text_latLong.text = Input.location.lastData.latitude.ToString("F5") + " / " + Input.location.lastData.longitude.ToString("F5");
                }
            }
        }
    }

    IEnumerator LoadCSV()
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "EcoGo_Plant_Encyclopedia_Detailed.csv");
        string result = "";

        if (path.Contains("://") || path.Contains(":///"))
        {
            using (UnityWebRequest www = UnityWebRequest.Get(path))
            {
                yield return www.SendWebRequest();
                if (www.result == UnityWebRequest.Result.Success)
                    result = www.downloadHandler.text;
                else
                    Debug.LogError("[GPSManager] CSV Load Failed: " + www.error);
            }
        }
        else
        {
            if (System.IO.File.Exists(path))
                result = System.IO.File.ReadAllText(path);
            else
                Debug.LogError("[GPSManager] CSV Load Failed: File not found at " + path);
        }

        if (!string.IsNullOrEmpty(result))
            ParseCSV(result);
    }

    void ParseCSV(string csvText)
    {
        plantList.Clear();
        string[] lines = csvText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        string pattern = @",(?=(?:[^""]*""[^""]*"")*(?![^""]*""))";

        for(int i = 1; i < lines.Length; i++)
        {
            string[] columns = Regex.Split(lines[i], pattern);
            if(columns.Length >= 14)
            {
                string latStr = columns[12].Trim('"', ' ');
                string lngStr = columns[13].Trim('"', ' ');

                if (!string.IsNullOrEmpty(latStr) && !string.IsNullOrEmpty(lngStr))
                {
                    double lat, lng;
                    bool latParsed = double.TryParse(latStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out lat);
                    bool lngParsed = double.TryParse(lngStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out lng);

                    if (latParsed && lngParsed)
                    {
                        PlantData data = new PlantData();
                        data.id = columns[0].Trim('"');
                        data.lat = lat;
                        data.lng = lng;
                        plantList.Add(data);
                    }
                }
            }
        }
        Debug.Log($"[GPSManager] Loaded {plantList.Count} plants from CSV.");
    }

    private async Task FilterDiscoveredPlantsAsync()
    {
        // 로그인이 안 되어 있거나 파이어베이스 매니저가 없다면 스킵
        if (FirebaseAuthManager.Instance == null || !FirebaseAuthManager.Instance.IsLoggedIn)
        {
            Debug.LogWarning("[GPSManager] Firebase 로그인이 되어있지 않거나 매니저가 없습니다.");
            return;
        }

        string uid = FirebaseAuthManager.Instance.CurrentUser.UserId;
        FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

        try
        {
            // 유저의 plant_progress 컬렉션 조회
            CollectionReference progressRef = db.Collection("users").Document(uid).Collection("plant_progress");
            QuerySnapshot snapshot = await progressRef.GetSnapshotAsync();

            HashSet<string> discoveredIds = new HashSet<string>();

            // 발견 기록(is_discovered == true)인 카드의 id 추출
            foreach (DocumentSnapshot doc in snapshot.Documents)
            {
                if (doc.Exists && doc.ContainsField("is_discovered"))
                {
                    if (doc.GetValue<bool>("is_discovered"))
                    {
                        discoveredIds.Add(doc.Id);
                    }
                }
            }

            // 전체 식물 리스트 중에서 이미 발견한 식물 삭제
            int originalCount = plantList.Count;
            plantList.RemoveAll(p => discoveredIds.Contains(p.id));

            Debug.Log($"[GPSManager] 필터링 완료: {originalCount}개 중 이미 발견한 {originalCount - plantList.Count}개 제외. 스폰 가능 식물 {plantList.Count}개.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GPSManager] 식물 발견 기록 필터링 중 오류 발생: {e.Message}");
        }
    }

    private IEnumerator RequestLocationPermission()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            yield return new WaitForSeconds(1f);
            float timeout = 10f;
            while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation) && timeout > 0f)
            {
                yield return new WaitForSeconds(0.5f);
                timeout -= 0.5f;
            }
        }
#endif
        yield return null;
    }

    void Update()
    {
        if (Input.location.status == LocationServiceStatus.Running)
        {
            double myLat = Input.location.lastData.latitude;
            double myLong = Input.location.lastData.longitude;
            CheckProximity(myLat, myLong);   
        }

        // 터치/클릭 감지로 카드 획득하기
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                CatchCardEffect catchEffect = hit.collider.GetComponent<CatchCardEffect>();
                if (catchEffect != null)
                {
                    catchEffect.OnClicked();
                }
            }
        }
    }

    private void CheckProximity(double myLat, double myLong)
    {
        double minDistance = double.MaxValue;

        foreach(var plant in plantList)
        {
            if (plant.isSpawned) continue;

            double dist = distance(myLat, myLong, plant.lat, plant.lng);
            if (dist < minDistance) minDistance = dist;

            // 20.0m 이내에 도달했을 경우 식물 소환 및 팝업창 활성화
            if (dist <= 20.0f)
            {
                _spawnedCount++;
                SpawnPlant(plant);
                
                // 팝업창 활성화는 이제 클릭 시 수행되므로 여기선 주석 처리
                // if (!isFirstCardPopup && cardPopup != null)
                // {
                //     isFirstCardPopup = true;
                //     cardPopup.SetActive(true);
                // }
            }
        }

        if (text_remainDistance != null)
        {
            if (minDistance != double.MaxValue)
                text_remainDistance.text = $"{plantList.Count} / {_spawnedCount} -- {minDistance:F1}m";
            else
                text_remainDistance.text = "All Found";
        }
    }

    private void SpawnPlant(PlantData plant)
    {
        plant.isSpawned = true;

        if (cardPrefab == null)
        {
            Debug.LogError("[GPSManager] CardPrefab이 설정되지 않았습니다!");
            return;
        }

        // 1. 공용 카드 프리팹 생성
        Transform camTransform = arCamera != null ? arCamera : Camera.main.transform;
        if (camTransform == null) return;

        Vector3 spawnPos = camTransform.position + camTransform.forward * spawnDistance;
        Quaternion rot = Quaternion.LookRotation(camTransform.position - spawnPos);
        GameObject spawnedObj = Instantiate(cardPrefab, spawnPos, rot);
        spawnedObj.transform.localScale = Vector3.one * spawnScale;
        
        // 2. 둥실둥실 효과 체크 및 추가
        if (spawnedObj.GetComponent<FloatingEffect>() == null)
        {
            spawnedObj.AddComponent<FloatingEffect>();
        }

        // 3. 해당 식물에 맞는 이미지(Sprite)를 Resources/Plants 폴더에서 로드하여 교체
        Sprite plantSprite = Resources.Load<Sprite>($"Plants/{plant.id}");
        SpriteRenderer renderer = spawnedObj.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = spawnedObj.GetComponentInChildren<SpriteRenderer>();

        if (plantSprite != null && renderer != null)
        {
            renderer.sprite = plantSprite;
        }
        else
        {
            Debug.LogWarning($"[GPSManager] Resources/Plants/ 폴더에서 '{plant.id}' 이미지를 찾을 수 없습니다.");
        }

        // 4. 클릭 이벤트를 위한 박스 콜라이더 및 획득 이펙트 추가
        BoxCollider boxCol = spawnedObj.GetComponent<BoxCollider>();
        if (boxCol == null) boxCol = spawnedObj.AddComponent<BoxCollider>();
        
        if (renderer != null && renderer.sprite != null)
        {
            // 스프라이트 크기에 맞춰 터치 영역(콜라이더) 자동 조절
            boxCol.size = new Vector3(renderer.sprite.bounds.size.x, renderer.sprite.bounds.size.y, 0.1f);
        }
        else
        {
            boxCol.size = new Vector3(1f, 1f, 0.1f);
        }

        CatchCardEffect catchEffect = spawnedObj.GetComponent<CatchCardEffect>();
        if (catchEffect == null) catchEffect = spawnedObj.AddComponent<CatchCardEffect>();
        
        catchEffect.gpsManager = this;
        catchEffect.plantId = plant.id;
        
        Debug.Log($"[GPSManager] Spawned '{plant.id}' successfully using CardPrefab!");
    }

    public void ShowAcquiredCardUI(string plantId)
    {
        if (cardPopup != null)
        {
            cardPopup.SetActive(true);
        }
        
        if (acquiredCardImage != null)
        {
            // Canvas에서 꺼져있을 수 있으므로 강제 활성화
            acquiredCardImage.gameObject.SetActive(true);
            
            // 앞면, 뒷면 이미지 로드
            Sprite frontSprite = Resources.Load<Sprite>($"Plants/{plantId}");
            Sprite backSprite = Resources.Load<Sprite>($"Plants/{plantId}_back");

            // UI 이미지에 뒤집기 기능(UICardFlipper) 부착 및 데이터 세팅
            UICardFlipper flipper = acquiredCardImage.GetComponent<UICardFlipper>();
            if (flipper == null)
            {
                flipper = acquiredCardImage.gameObject.AddComponent<UICardFlipper>();
            }

            flipper.SetCardDatas(frontSprite, backSprite);
        }

        if (closeButton != null)
        {
            // Canvas에서 X 버튼 강제 활성화
            closeButton.SetActive(true);
        }

        // Firestore에 발견(획득) 기록 업데이트
        UpdatePlantProgress(plantId);
    }

    private async void UpdatePlantProgress(string plantId)
    {
        if (FirebaseAuthManager.Instance == null || !FirebaseAuthManager.Instance.IsLoggedIn)
        {
            Debug.LogWarning("[GPSManager] Firebase 로그인이 되어있지 않아 발견 기록을 저장할 수 없습니다.");
            return;
        }

        string uid = FirebaseAuthManager.Instance.CurrentUser.UserId;
        FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

        try
        {
            DocumentReference docRef = db.Collection("users").Document(uid).Collection("plant_progress").Document(plantId);
            
            Dictionary<string, object> data = new Dictionary<string, object>
            {
                { "is_discovered", true }
            };
            
            // SetOptions.MergeAll을 사용하여 기본 구조 유지 및 필드 추가/수정
            await docRef.SetAsync(data, SetOptions.MergeAll);
            Debug.Log($"[GPSManager] Firestore 기록 완료: {plantId} (is_discovered = true)");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GPSManager] 발견 기록 업데이트 중 오류 발생: {e.Message}");
        }
    }

    /// <summary>
    /// UI의 X 버튼이나 닫기 버튼을 눌렀을 때 팝업창을 닫아줍니다.
    /// 에디터의 Button 컴포넌트 OnClick 이벤트에 연결해 주세요.
    /// </summary>
    public void HideAcquiredCardUI()
    {
        if (cardPopup != null) cardPopup.SetActive(false);
        if (acquiredCardImage != null) acquiredCardImage.gameObject.SetActive(false);
        if (closeButton != null) closeButton.SetActive(false);
    }

    private double distance(double lat1, double lon1, double lat2, double lon2)
    {
        double theta = lon1 - lon2;
        double dist = Math.Sin(Deg2Rad(lat1)) * Math.Sin(Deg2Rad(lat2)) + Math.Cos(Deg2Rad(lat1)) * Math.Cos(Deg2Rad(lat2)) * Math.Cos(Deg2Rad(theta));
        dist = Math.Acos(dist);
        dist = Rad2Deg(dist);
        dist = dist * 60 * 1.1515 * 1609.344;
        return dist;
    }

    private double Deg2Rad(double deg) => (deg * Math.PI / 180.0d);
    private double Rad2Deg(double rad) => (rad * 180.0d / Math.PI);
}
