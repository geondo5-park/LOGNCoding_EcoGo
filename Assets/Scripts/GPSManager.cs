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
using UnityEngine.UI;

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
    [Tooltip("카드를 획득했을 때 안내 메시지를 보여줄 텍스트 (예: You have acquired a card!)")]
    public TMP_Text text_acquisitionStatus;

    public Button button_goToBack;
    private int _spawnedCount = 0;

    [Header("AR / Geospatial Settings")]
    [Tooltip("현재 씬의 메인 카메라")]
    public Transform arCamera;
    
    [Tooltip("식물 등장 반경 (이 반경 내로 접근 시 현실 세계 위치에 카드 앵커를 고정시킵니다. 기본 50m 권장)")]
    public float spawnRadius = 50f;

    [Tooltip("카드를 클릭하여 획득할 수 있는 최소 거리 (이보다 멀면 다가가야 함. 기본 10m)")]
    public float clickableDistance = 10f;

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
    private string _debugMessage = ""; // 디버그용 메시지 저장 변수

    void Awake()
    {
        // Manager 자동 탐색 (Inspcctor에서 세팅 못 했을 경우 방지)
        if (earthManager == null) earthManager = FindFirstObjectByType<AREarthManager>();
        if (anchorManager == null) anchorManager = FindFirstObjectByType<ARAnchorManager>();
        button_goToBack.onClick.AddListener(GoBackToHome);
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
                    text_latLong.text = $"[VPS Active]\nLat: {pose.Latitude:F5} / Lng: {pose.Longitude:F5}\n<color=yellow>{_debugMessage}</color>";
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
                    // [핵심] 실제 거리 계산 (카메라와 카드 사이의 월드 거리)
                    float currentDist = Vector3.Distance(Camera.main.transform.position, hit.collider.transform.position);

                    if (currentDist <= clickableDistance)
                    {
                        // 10m 이내일 때만 획득 애니메이션 실행
                        catchEffect.OnClicked();
                    }
                    else
                    {
                        // 너무 멀면 무시 (디버그 로그만 남김)
                        Debug.Log($"[GPSManager] 거리 부족으로 획득 실패 (현재: {currentDist:F1}m / 목표: {clickableDistance}m)");
                    }
                }
            }
        }
    }

    private PlantData _nearestPlant;
    private Queue<PlantData> _spawnQueue = new Queue<PlantData>();
    private bool _isSpawningFromQueue = false;
    private Dictionary<string, float> _spawnCooldowns = new Dictionary<string, float>();

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
            // [수정] 스폰 반경(spawnRadius) 안에 들어왔을 때만 스폰을 시도하도록 조건 추가!
            if (dist <= spawnRadius && !plant.isSpawned && !_spawnQueue.Contains(plant))
            {
                if (!_spawnCooldowns.ContainsKey(plant.id) || Time.time > _spawnCooldowns[plant.id])
                {
                    _spawnQueue.Enqueue(plant);
                }
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
                // 현재 씬에 실제로 존재하고 활성화된 카드의 개수를 파악합니다.
                int activeSpawnedCount = 0;
                foreach(var card in spawnedCards.Values) {
                    if(card != null && card.activeInHierarchy) activeSpawnedCount++;
                }

                text_remainDistance.text = $"[가장 가까운 목표] {dirStr} {minDistance:F1}m\n" +
                                           $"<b>(현재 필드: {activeSpawnedCount}개 / 누적 생성: {_spawnedCount}개)</b>\n" +
                                           $"주변을 둘러보며 카드를 찾아보세요!";
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
        if (cardPrefab == null)
        {
            Debug.LogError("[GPSManager] cardPrefab이 할당되지 않았습니다. Resources에서 로드 시도 중...");
            cardPrefab = Resources.Load<GameObject>("Prefabs/CardPrefab");
            if (cardPrefab == null)
            {
                // 최후의 수단: Quad라도 생성
                cardPrefab = GameObject.CreatePrimitive(PrimitiveType.Quad);
                cardPrefab.name = "DummyCardPrefab";
                cardPrefab.SetActive(false); // 템플릿용이므로 비활성화
            }
        }

        ARGeospatialAnchor earthAnchor = null;
        GameObject spawnedObj = null;
        Transform camTrans = (arCamera != null) ? arCamera : Camera.main.transform;

        // 1. 지구 트래킹이 정상일 때만 앵커 추가 시도
        if (earthManager != null && earthManager.EarthTrackingState == TrackingState.Tracking)
        {
            GeospatialPose pose = earthManager.CameraGeospatialPose;
            double currentAltitude = pose.Altitude;
            
            try
            {
                if (anchorManager != null && anchorManager.subsystem != null && anchorManager.subsystem.running)
                {
                    // 정확도가 충분할 때만 앵커 생성을 시도하여 불필요한 실패 방지 (필요 시 주석 해제)
                    // if (pose.HorizontalAccuracy < 15.0f && pose.VerticalAccuracy < 15.0f) 
                    earthAnchor = anchorManager.AddAnchor(plant.lat, plant.lng, currentAltitude, Quaternion.identity);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GPSManager] AddAnchor 예외 발생 (Fallback 스폰 진행): {e.Message}");
            }
        }

        if (earthAnchor != null)
        {
            // 스폰 성공: 앵커의 자식으로 등록되어 카메라가 아닌 "실제 지형 좌표"에 고정됨
            spawnedObj = Instantiate(cardPrefab, earthAnchor.transform.position, earthAnchor.transform.rotation, earthAnchor.transform);
            _debugMessage = $"'{plant.id}' 앵커 스폰 성공!";
            Debug.Log($"[GPSManager] '{plant.id}' 카드가 실제 지형 앵커에 스폰되었습니다!");
        }
        else
        {
            // 2. Fallback: 앵커 생성이 실패하면 카메라 기준 상대 좌표로 강제 스폰
            if (earthManager != null && earthManager.EarthTrackingState == TrackingState.Tracking)
            {
                GeospatialPose myPose = earthManager.CameraGeospatialPose;
                
                // 위도/경도 차이를 미터 단위로 대략 계산
                double dLat = plant.lat - myPose.Latitude;
                double dLng = plant.lng - myPose.Longitude;
                
                // 1도당 미터 환산 (대략적)
                float zMeters = (float)(dLat * 111319.9);
                float xMeters = (float)(dLng * 111319.9 * Math.Cos(myPose.Latitude * Math.PI / 180.0));
                
                // ARCore Geospatial에서는 X=East, Z=North로 정렬됨
                Vector3 spawnAt = camTrans.position + new Vector3(xMeters, -0.5f, zMeters);
                
                // 앵커 대신 직접 생성 후 ARAnchor 컴포넌트 추가 (Extension 메서드와의 이름 충돌 방지)
                spawnedObj = Instantiate(cardPrefab, spawnAt, Quaternion.identity);
                if (spawnedObj.GetComponent<ARAnchor>() == null)
                {
                    spawnedObj.AddComponent<ARAnchor>();
                }
                _debugMessage = $"'{plant.id}' 일반 로컬 스폰 성공!";
                Debug.Log($"[GPSManager] '{plant.id}' 앵커 생성 실패로 인해 일반 좌표에 생성 후 ARAnchor를 부여했습니다.");
                
                // 카드 명단에 등록
                if (spawnedObj != null && !spawnedCards.ContainsKey(plant.id))
                {
                    spawnedCards.Add(plant.id, spawnedObj);
                }
            }
        }

        if (spawnedObj != null)
        {
            spawnedObj.SetActive(true);
            _spawnedCount++; // 실제 생성 성공 시에만 카운트 증가
            
            // 프리팹 내부의 Dummy 등의 잔재 제거
            if (spawnedObj.name == "DummyCardPrefab") spawnedObj.name = $"Card_{plant.id}";

            // [개선] 로컬 좌표 초기화
            if (spawnedObj.transform.parent != null)
            {
                spawnedObj.transform.localPosition = Vector3.zero;
                spawnedObj.transform.localRotation = Quaternion.identity;
            }

            // 한 번만 카메라를 바라보도록 초기 회전 설정 (계속 따라다니지 않음)
            Vector3 lookDir = spawnedObj.transform.position - camTrans.position;
            lookDir.y = 0; // 수직 유지
            if (lookDir != Vector3.zero)
            {
                spawnedObj.transform.rotation = Quaternion.LookRotation(lookDir);
            }

            // 거리 및 위치 차이 디버그 로그
            Vector3 camPos = camTrans.position;
            float debugDist = Vector3.Distance(camPos, spawnedObj.transform.position);
            Debug.Log($"[GPSManager] '{plant.id}' 렌더링 완료. 카메라 위치: {camPos}, 실제 거리: {debugDist:F1}m");

            // 4. 둥실둥실 효과 추가
            if (spawnedObj.GetComponent<FloatingEffect>() == null)
            {
                spawnedObj.AddComponent<FloatingEffect>();
            }

            // 5. 식물 텍스처(이미지) 로드
            Texture2D plantTexture = null;
            Sprite plantSprite = Resources.Load<Sprite>($"Plants/{plant.id}");
            if (plantSprite != null)
            {
                plantTexture = plantSprite.texture;
            }
            else
            {
                plantTexture = Resources.Load<Texture2D>($"Plants/{plant.id}");
            }

            // 2D 스프라이트 렌더러가 존재하면 삭제 (3D 객체로 사용할 것이므로)
            SpriteRenderer sprRenderer = spawnedObj.GetComponent<SpriteRenderer>();
            if (sprRenderer == null) sprRenderer = spawnedObj.GetComponentInChildren<SpriteRenderer>();
            if (sprRenderer != null) Destroy(sprRenderer);

            // 3D 메쉬 렌더러와 필터 세팅
            MeshRenderer meshRenderer = spawnedObj.GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = spawnedObj.GetComponentInChildren<MeshRenderer>();
            
            MeshFilter meshFilter = spawnedObj.GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = spawnedObj.GetComponentInChildren<MeshFilter>();

            // 스폰 객체가 3D 구조가 아니면 기본 Quad 형태로 생성
            if (meshRenderer == null)
            {
                if (meshFilter == null) meshFilter = spawnedObj.AddComponent<MeshFilter>();
                
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                meshFilter.mesh = quad.GetComponent<MeshFilter>().sharedMesh;
                Destroy(quad);

                meshRenderer = spawnedObj.AddComponent<MeshRenderer>();
                // [수정] 조명을 완전히 무시하고 가장 밝게 출력되는 URP/Unlit 또는 Unlit/Transparent 적용
                Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlitShader == null) unlitShader = Shader.Find("Unlit/Transparent");
                
                meshRenderer.material = new Material(unlitShader);
                // URP Unlit의 경우 Transparent 설정을 위해 키워드 및 태그 설정이 필요할 수 있으나, 기본적으로 Texture만 잘 나와도 됨
                if (unlitShader.name.Contains("Universal Render Pipeline"))
                {
                    meshRenderer.material.SetFloat("_Surface", 1); // 1 is Transparent
                    meshRenderer.material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    meshRenderer.material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    meshRenderer.material.SetInt("_ZWrite", 0);
                    meshRenderer.material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    meshRenderer.material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
            }

            if (plantTexture != null && meshRenderer != null)
            {
                // 머티리얼에 카드 이미지 적용 (URP는 _BaseMap, 레거시는 _MainTex)
                if (meshRenderer.material.HasProperty("_BaseMap"))
                    meshRenderer.material.SetTexture("_BaseMap", plantTexture);
                else
                    meshRenderer.material.mainTexture = plantTexture;
                
                meshRenderer.material.color = Color.white; // 밝기 100% 보장

                // 텍스처의 원본 종횡비(Aspect Ratio)를 계산하여 객체 스케일에 반영
                float aspect = (float)plantTexture.width / plantTexture.height;
                
                // 야외에서 잘 보이도록 기본 스케일 (25.0f)
                float targetScale = spawnScale * 25.0f;
                spawnedObj.transform.localScale = new Vector3(targetScale * aspect, targetScale, 1.0f);
            }
            else
            {
                spawnedObj.transform.localScale = Vector3.one * (spawnScale * 25.0f);
            }

            // [추가] 실시간 카메라 바라보기 효과 (밝기와 시인성을 위해 필수)
            if (spawnedObj.GetComponent<FaceCameraAR>() == null)
            {
                spawnedObj.AddComponent<FaceCameraAR>();
            }

            // 6. 클릭을 위한 콜라이더 추가 (3D 형태 반영)
            BoxCollider boxCol = spawnedObj.GetComponent<BoxCollider>();
            if (boxCol == null) boxCol = spawnedObj.AddComponent<BoxCollider>();
            
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                Vector3 boundsSize = meshFilter.sharedMesh.bounds.size;
                // 터치를 쉽게 하기 위해 콜라이더 크기를 메쉬 원본보다 폭과 높이를 2배 넓게 잡아줍니다. (두께는 최소 0.5f 지정)
                boxCol.size = new Vector3(
                    boundsSize.x > 0 ? boundsSize.x * 2.0f : 2.5f, 
                    boundsSize.y > 0 ? boundsSize.y * 2.0f : 2.5f, 
                    Mathf.Max(boundsSize.z, 0.5f)
                );
            }
            else
            {
                // 기본 임의 크기 할당
                boxCol.size = new Vector3(2.5f, 2.5f, 0.5f);
            }

            // 7. 획득 이펙트 컴포넌트 추가
            CatchCardEffect catchEffect = spawnedObj.GetComponent<CatchCardEffect>();
            if (catchEffect == null) catchEffect = spawnedObj.AddComponent<CatchCardEffect>();
            
            catchEffect.gpsManager = this;
            catchEffect.plantId = plant.id;
            
            // 네비게이션을 위해 등록 (Fallback에서 이미 등록했을 수 있으므로 체크)
            if (!spawnedCards.ContainsKey(plant.id))
            {
                spawnedCards.Add(plant.id, spawnedObj);
            }
            
            _spawnCooldowns.Remove(plant.id);
        }
        else
        {
            _debugMessage = $"'{plant.id}' 스폰 실패. 재시도 중...";
            Debug.LogWarning($"[GPSManager] '{plant.id}' 생성 실패. 5초 후 다시 시도합니다.");
            plant.isSpawned = false; // 실패했으므로 추후 재시도 가능하도록 리셋
            _spawnCooldowns[plant.id] = Time.time + 5f;
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
        if (text_acquisitionStatus != null) text_acquisitionStatus.text = "You have acquired a new plant card!";
        
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
            // 스프라이트가 카메라를 바라보되, Y축 방향만 회전하도록 설정 (수직 유지)
            Vector3 lookDir = transform.position - camTransform.position;
            lookDir.y = 0; // Y축 방향 고정 (위아래로 기울어지지 않게)
            
            if (lookDir != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(lookDir);
            }
        }
    }
}
