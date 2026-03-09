using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using Firebase.Firestore;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Google ARCore Geospatial Extensions 패키지
using Google.XR.ARCoreExtensions;
using UnityEngine.EventSystems;

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
    private int _spawnedCount = 0;

    [Header("AR / Geospatial Settings")]
    [Tooltip("현재 씬의 메인 카메라")]
    public Transform arCamera;
    
    [Tooltip("식물 등장 반경 (이 반경 내로 접근 시 현실 세계 위치에 카드 앵커를 고정시킵니다. 기본 30~50m 권장)")]
    public float spawnRadius = 30f;

    [Tooltip("스폰될 프리팹의 크기 (기본 0.3f)")]
    public float spawnScale = 0.3f;

    [Tooltip("공용 카드 프리팹 (Resources/Prefabs/CardPrefab 등에서 로드 권장)")]
    public GameObject cardPrefab;

    [HideInInspector]
    public List<PlantData> plantList = new List<PlantData>();

    [Header("Geospatial Managers")]
    [Tooltip("XR Origin의 AR Earth Manager (할당 안되어 있으면 자동 탐색)")]
    public AREarthManager earthManager;
    [Tooltip("XR Origin의 AR Anchor Manager (할당 안되어 있으면 자동 탐색)")]
    public ARAnchorManager anchorManager;

    [Header("Navigation UI")]
    [Tooltip("카드가 스폰되었을 때 그 방향을 가리킬 화살표 UI (Canvas에 있는 이미지)")]
    public RectTransform navArrowUI;
    
    // 현재 스폰되어 있는 스폰 오브젝트(카드)들을 추적하기 위한 딕셔너리
    private Dictionary<string, GameObject> spawnedCards = new Dictionary<string, GameObject>();

    void Awake()
    {
        // Manager 자동 탐색 (Inspcctor에서 세팅 못 했을 경우 방지)
        if (earthManager == null) earthManager = FindObjectOfType<AREarthManager>();
        if (anchorManager == null) anchorManager = FindObjectOfType<ARAnchorManager>();
    }

    IEnumerator Start()
    {
        // ── 0. 매니저 컴포넌트 존재 유무 확인 ──
        if (earthManager == null || anchorManager == null)
        {
            Debug.LogError("[GPSManager] 'AREarthManager' 또는 'ARAnchorManager'를 찾을 수 없습니다! XR Origin에 두 컴포넌트를 모두 Add Component 해주세요.");
            yield break;
        }

        // ── 1. CSV 데이터 로드 ──
        yield return StartCoroutine(LoadCSV());

        // ── 2. Firestore에서 이미 발견한 식물 필터링 ──
        Task filterTask = FilterDiscoveredPlantsAsync();
        yield return new WaitUntil(() => filterTask.IsCompleted);

        // ── 3. CardPrefab이 없으면 Resources에서 로드 시도 ──
        if (cardPrefab == null)
        {
            cardPrefab = Resources.Load<GameObject>("Prefabs/CardPrefab");
        }

        // ── 4. 플랫폼별 위치 권한 요청 (안드로이드) ──
        yield return StartCoroutine(RequestLocationPermission());

        // ── 5. AR Session이 초기화될 때까지 대기 ──
        if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
        {
            yield return ARSession.CheckAvailability();
        }

        // ── 6. Geospatial 추적 루프 시작 ──
        StartCoroutine(GeospatialTrackingLoop());
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

        if (!string.IsNullOrEmpty(result)) ParseCSV(result);
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
        if (FirebaseAuthManager.Instance == null || !FirebaseAuthManager.Instance.IsLoggedIn) return;

        string uid = FirebaseAuthManager.Instance.CurrentUser.UserId;
        FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

        try
        {
            CollectionReference progressRef = db.Collection("users").Document(uid).Collection("plant_progress");
            QuerySnapshot snapshot = await progressRef.GetSnapshotAsync();

            HashSet<string> discoveredIds = new HashSet<string>();

            foreach (DocumentSnapshot doc in snapshot.Documents)
            {
                if (doc.Exists && doc.ContainsField("is_discovered") && doc.GetValue<bool>("is_discovered"))
                {
                    discoveredIds.Add(doc.Id);
                }
            }

            int originalCount = plantList.Count;
            plantList.RemoveAll(p => discoveredIds.Contains(p.id));

            Debug.Log($"[GPSManager] 필터링 완료: 스폰 가능 식물 {plantList.Count}개.");
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

    IEnumerator GeospatialTrackingLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            // 기기가 Google의 지구 위치 트래킹을 정상적으로 수행하는지 확인
            if (earthManager != null && earthManager.EarthTrackingState == TrackingState.Tracking)
            {
                // 매우 정밀한 GeospatialPose(위도, 경도, 고도 등) 데이터를 가져옴
                GeospatialPose pose = earthManager.CameraGeospatialPose;

                if (text_latLong != null)
                {
                    text_latLong.text = $"[VPS Active]\nLat: {pose.Latitude:F5} / Lng: {pose.Longitude:F5}";
                }

                // 주변 거리를 체크하고 반경 안에 들어오면 실제 현실 지형에 스폰(앵커)
                CheckProximity(pose.Latitude, pose.Longitude);
            }
            else
            {
                if (text_latLong != null)
                {
                    text_latLong.text = "Geospatial Scanning Environment...\n주변 건물과 도로를 스캔해주세요.";
                }
            }
        }
    }

    void Update()
    {
        // 실시간으로 화살표 방향 업데이트 (가장 가까운 식물)
        // UpdateNavigationArrow(_nearestPlant); // 사용자 요청으로 임시 비활성화
        
        // 네비게이션 화살표 강제 숨김
        if (navArrowUI != null && navArrowUI.gameObject.activeSelf)
        {
            navArrowUI.gameObject.SetActive(false);
        }

        // 화면 터치 시 동작
        if (Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began))
        {
            // UI를 터치 중인지 검사하여, UI("획득 팝업창" 등) 뒤에 있는 3D 카드가 터치되는 것을 방지합니다.
            if (EventSystem.current != null)
            {
                // 모바일 터치 처리
                if (Input.touchCount > 0 && EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId))
                    return;
                
                // 에디터 마우스 클릭 처리
                if (EventSystem.current.IsPointerOverGameObject())
                    return;
            }

            // UI 터치가 아닐 때만 3D 오브젝트(AR 카드) 획득 판정 수행
            Vector3 touchPos = Input.touchCount > 0 ? (Vector3)Input.GetTouch(0).position : Input.mousePosition;
            Ray ray = Camera.main.ScreenPointToRay(touchPos);
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

    private PlantData _nearestPlant;
    private Queue<PlantData> _spawnQueue = new Queue<PlantData>();
    private bool _isSpawningFromQueue = false;

    private void CheckProximity(double myLat, double myLong)
    {
        double minDistance = double.MaxValue;
        _nearestPlant = null;

        foreach(var plant in plantList)
        {
            // 아직 획득하지 않은 카드 목적지까지의 거리 측정
            double dist = distance(myLat, myLong, plant.lat, plant.lng);
            if (dist < minDistance) 
            {
                minDistance = dist;
                _nearestPlant = plant;
            }

            // 한꺼번에 스폰하면 ARCore 위성 시스템이 과부하로 뻗으므로, 순차적으로 큐에 담아서 스폰
            if (!plant.isSpawned && !_spawnQueue.Contains(plant))
            {
                _spawnQueue.Enqueue(plant);
            }
        }

        if (!_isSpawningFromQueue && _spawnQueue.Count > 0)
        {
            StartCoroutine(ProcessSpawnQueue());
        }

        if (text_remainDistance != null)
        {
            if (_nearestPlant != null)
            {
                string dirStr = GetDirection(myLat, myLong, _nearestPlant.lat, _nearestPlant.lng);
                text_remainDistance.text = $"[가장 가까운 목표] {dirStr} {minDistance:F1}m\n화살표 방향으로 가서 스폰된 카드를 터치하세요!";
            }
            else
            {
                text_remainDistance.text = "모든 식물을 찾았습니다!";
                if (navArrowUI != null && navArrowUI.gameObject.activeSelf) navArrowUI.gameObject.SetActive(false);
            }
        }
    }

    private IEnumerator ProcessSpawnQueue()
    {
        _isSpawningFromQueue = true;
        while (_spawnQueue.Count > 0)
        {
            PlantData plantToSpawn = _spawnQueue.Dequeue();
            plantToSpawn.isSpawned = true; // 스폰 시도 중으로 마킹
            _spawnedCount++;
            
            yield return StartCoroutine(SpawnPlantOnTerrain(plantToSpawn));
            
            // 한 프레임에 너무 많은 요청을 보내지 않게 0.1초씩 쉬면서 하나씩 정밀하게 전부 생성합니다.
            yield return new WaitForSeconds(0.1f);
        }
        _isSpawningFromQueue = false;
    }

    // 두 위도/경도를 바탕으로 나침반 방향(북/남/동/서)을 계산하는 함수
    private string GetDirection(double myLat, double myLon, double targetLat, double targetLon)
    {
        double dLon = Deg2Rad(targetLon - myLon);
        double y = Math.Sin(dLon) * Math.Cos(Deg2Rad(targetLat));
        double x = Math.Cos(Deg2Rad(myLat)) * Math.Sin(Deg2Rad(targetLat)) - 
                   Math.Sin(Deg2Rad(myLat)) * Math.Cos(Deg2Rad(targetLat)) * Math.Cos(dLon);
        
        double brng = Math.Atan2(y, x);
        brng = Rad2Deg(brng);
        brng = (brng + 360) % 360;

        string[] directions = { "북쪽", "북동쪽", "동쪽", "남동쪽", "남쪽", "남서쪽", "서쪽", "북서쪽", "북쪽" };
        int index = (int)Math.Round((brng % 360) / 45);
        return directions[index];
    }

    // ARCore Streetscape Geometry(지리적 지형)를 활용해 카드를 해당 바닥에 고정하는 코루틴
    private IEnumerator SpawnPlantOnTerrain(PlantData plant)
    {
        if (cardPrefab == null) yield break;

        // 현재 유저(카메라)의 고도를 가져와서 카드가 눈높이 수평에 있도록 앵커 생성
        double currentAltitude = earthManager.CameraGeospatialPose.Altitude;
        
        // 너무 높거나 낮으면 안 보이므로 정확히 유저 카메라 고도와 동일하게 세팅
        ARGeospatialAnchor earthAnchor = anchorManager.AddAnchor(plant.lat, plant.lng, currentAltitude, Quaternion.identity);

        if (earthAnchor != null)
        {
            // 스폰 성공: 앵커의 자식으로 등록되어 카메라가 아닌 "실제 지형 좌표"에 고정됨
            GameObject spawnedObj = Instantiate(cardPrefab, earthAnchor.transform.position, earthAnchor.transform.rotation, earthAnchor.transform);
            
            // 너무 크면 카메라를 덮어버리고, 작으면 안보이므로 3배 크기로 일단 띄움
            spawnedObj.transform.localScale = Vector3.one * (spawnScale * 3.0f); 

            // 3. 카드가 항상 사용자를 바라보게 만드는 스크립트 추가
            if (spawnedObj.GetComponent<FaceCameraAR>() == null)
            {
                spawnedObj.AddComponent<FaceCameraAR>();
            }
            
            // 4. 둥실둥실 효과 추가
            if (spawnedObj.GetComponent<FloatingEffect>() == null)
            {
                spawnedObj.AddComponent<FloatingEffect>();
            }

            // 5. 식물 Sprite 로드
            Sprite plantSprite = Resources.Load<Sprite>($"Plants/{plant.id}");
            SpriteRenderer renderer = spawnedObj.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = spawnedObj.GetComponentInChildren<SpriteRenderer>();

            if (plantSprite != null && renderer != null)
                renderer.sprite = plantSprite;

            // 6. 클릭을 위한 콜라이더 추가
            BoxCollider boxCol = spawnedObj.GetComponent<BoxCollider>();
            if (boxCol == null) boxCol = spawnedObj.AddComponent<BoxCollider>();
            
            if (renderer != null && renderer.sprite != null)
                boxCol.size = new Vector3(renderer.sprite.bounds.size.x * 2.0f, renderer.sprite.bounds.size.y * 2.0f, 0.5f);
            else
                boxCol.size = new Vector3(2.5f, 2.5f, 0.5f);

            // 7. 획득 이펙트 컴포넌트 추가
            CatchCardEffect catchEffect = spawnedObj.GetComponent<CatchCardEffect>();
            if (catchEffect == null) catchEffect = spawnedObj.AddComponent<CatchCardEffect>();
            
            catchEffect.gpsManager = this;
            catchEffect.plantId = plant.id;
            
            // 네비게이션을 위해 등록
            if (!spawnedCards.ContainsKey(plant.id))
            {
                spawnedCards.Add(plant.id, spawnedObj);
            }

            Debug.Log($"[GPSManager] '{plant.id}' 카드가 엑셀 기반 실제 지형 위경도 앵커에 스폰되었습니다!");
        }
        else
        {
            Debug.LogWarning($"[GPSManager] '{plant.id}' 앵커 생성 실패. 다음 프레임에 다시 시도합니다.");
            plant.isSpawned = false;
        }

        yield return null;
    }

    private void UpdateNavigationArrow(PlantData nearestPlant)
    {
        Transform cam = arCamera != null ? arCamera : (Camera.main != null ? Camera.main.transform : null);

        if (navArrowUI == null || cam == null) return;

        if (nearestPlant == null)
        {
            if (navArrowUI.gameObject.activeSelf) navArrowUI.gameObject.SetActive(false);
            return;
        }

        if (!navArrowUI.gameObject.activeSelf) navArrowUI.gameObject.SetActive(true);

        Vector3 targetDir = Vector3.zero;
        bool hasTargetObject = false;

        if (spawnedCards.ContainsKey(nearestPlant.id) && spawnedCards[nearestPlant.id] != null)
        {
            targetDir = spawnedCards[nearestPlant.id].transform.position - cam.position;
            hasTargetObject = true;
        }
        else
        {
            if (earthManager != null && earthManager.EarthTrackingState == TrackingState.Tracking)
            {
                GeospatialPose myPose = earthManager.CameraGeospatialPose;
                double dLon = Deg2Rad(nearestPlant.lng - myPose.Longitude);
                double y = Math.Sin(dLon) * Math.Cos(Deg2Rad(nearestPlant.lat));
                double x = Math.Cos(Deg2Rad(myPose.Latitude)) * Math.Sin(Deg2Rad(nearestPlant.lat)) - 
                           Math.Sin(Deg2Rad(myPose.Latitude)) * Math.Cos(Deg2Rad(nearestPlant.lat)) * Math.Cos(dLon);
                
                double bearing = Math.Atan2(y, x);
                bearing = (bearing * 180.0 / Math.PI + 360) % 360; 
                targetDir = Quaternion.Euler(0, (float)bearing, 0) * Vector3.forward; 
                hasTargetObject = true;
            }
        }

        if (!hasTargetObject) 
        {
            navArrowUI.gameObject.SetActive(false);
            return;
        }

        targetDir.y = 0; 
        Vector3 forward = cam.forward;
        forward.y = 0;

        if (targetDir.sqrMagnitude < 0.01f || forward.sqrMagnitude < 0.01f) return;

        targetDir.Normalize();
        forward.Normalize();

        float crossY = Vector3.Cross(forward, targetDir).y;
        float dot = Vector3.Dot(forward, targetDir);
        float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;

        if (crossY > 0)
        {
            angle = -angle;
        }

        navArrowUI.localEulerAngles = new Vector3(0, 0, angle);
    }

    public void ShowAcquiredCardUI(string plantId)
    {
        if (cardPopup != null) cardPopup.SetActive(true);
        if (acquiredCardImage != null)
        {
            acquiredCardImage.gameObject.SetActive(true);
            Sprite frontSprite = Resources.Load<Sprite>($"Plants/{plantId}");
            Sprite backSprite = Resources.Load<Sprite>($"Plants/{plantId}_back");

            UICardFlipper flipper = acquiredCardImage.GetComponent<UICardFlipper>();
            if (flipper == null) flipper = acquiredCardImage.gameObject.AddComponent<UICardFlipper>();
            flipper.SetCardDatas(frontSprite, backSprite);
        }
        if (closeButton != null) closeButton.SetActive(true);

        plantList.RemoveAll(p => p.id == plantId);
        if (spawnedCards.ContainsKey(plantId))
        {
            spawnedCards.Remove(plantId);
        }
    }

    public async void UpdatePlantProgress(string plantId)
    {
        if (FirebaseAuthManager.Instance == null || !FirebaseAuthManager.Instance.IsLoggedIn) return;

        string uid = FirebaseAuthManager.Instance.CurrentUser.UserId;
        FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

        try
        {
            DocumentReference docRef = db.Collection("users").Document(uid).Collection("plant_progress").Document(plantId);
            Dictionary<string, object> data = new Dictionary<string, object> { { "is_discovered", true } };
            await docRef.SetAsync(data, SetOptions.MergeAll);
            Debug.Log($"[GPSManager] Firestore 기록 완료: {plantId}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GPSManager] 발견 기록 업데이트 오류: {e.Message}");
        }
    }

    public void HideAcquiredCardUI()
    {
        if (cardPopup != null) cardPopup.SetActive(false);
        if (acquiredCardImage != null) acquiredCardImage.gameObject.SetActive(false);
        if (closeButton != null) closeButton.SetActive(false);
    }

    /// <summary>
    /// 뒤로가기 버튼(UI)에서 호출하여 다시 메인(Home) 씬으로 넘어갑니다.
    /// Home 씬이 로드되면 BottomNavigationBar가 자동으로 기본 탭(Home 탭, 인덱스0)을 선택합니다.
    /// </summary>
    public void GoBackToHome()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("Home");
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

// AR에서 스폰된 카드가 플레이어(카메라)를 계속 바라보도록 하는 헬퍼 스크립트
public class FaceCameraAR : MonoBehaviour
{
    private Transform camTransform;
    private Vector3 initialScale;

    void Start()
    {
        if (Camera.main != null)
            camTransform = Camera.main.transform;
            
        initialScale = transform.localScale;
    }

    void Update()
    {
        if (camTransform != null)
        {
            // 스프라이트가 카메라 정면을 바라보게 회전 (Vector3 반대 방향)
            transform.rotation = Quaternion.LookRotation(transform.position - camTransform.position);
            
            // 물리적인 거리가 아주 멀어도 화면에서는 일정 크기 이상으로 보이게 하기 위해
            // 15m 이상 떨어져 있으면 거리에 비례해서 크기를 강제로 키워버립니다 (시각적 크기 유지)
            float dist = Vector3.Distance(transform.position, camTransform.position);
            if (dist > 15f)
            {
                float scaleMulti = 1f + ((dist - 15f) * 0.15f); 
                transform.localScale = initialScale * scaleMulti;
            }
            else
            {
                transform.localScale = initialScale;
            }
        }
    }
}
