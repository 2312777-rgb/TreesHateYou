using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class DailyStoryController : MonoBehaviour
{
    public DailyStoryProgress Progress { get; } = new DailyStoryProgress();
    public bool IsTransitioning { get; private set; }
    public bool HasInteractionPrompt => !IsTransitioning && Target() != null;
    private FirewoodCollector collector;
    private WorldItemReticle reticle;
    private Camera view;
    private GameObject fireplace;
    private FurniturePlacement furniturePlacement;
    private GameObject woodStorage;
    private GameObject toolShelf;
    private StorageWoodDisplay storageDisplay;
    private const float DialogueFadeDuration = 0.65f;
    private const float BedtimeThoughtDuration = 2f;
    private float bedtimeThoughtUntil;
    private const float StoryLineDuration = 5f;
    private int lastStoryBeat = -1;
    private bool leftBedOnFirstDay;
    private bool leftBedOnSecondDay;
    private string pendingStoryFollowUp;
    private string storyLine;
    private float storyLineUntil;
    private int firstDayStumpsCleared;
    private bool redAfternoon;
    private Color daylightSunColor;
    private GameObject sleepOverlay;
    private UnityEngine.UI.Image sleepBackground;
    private UnityEngine.UI.Text sleepCaption;
    private bool sunsetStarted;
    private bool sunsetRunning;
    private Material originalSky;
    private Material nightSky;
    private Light sun;
    private float daylightIntensity;
    private Color daylightAmbient;
    private UnityEngine.Rendering.AmbientMode daylightAmbientMode;
    private Color daylightSkyColor, daylightEquatorColor, daylightGroundColor;
    private UnityEngine.Rendering.SphericalHarmonicsL2 daylightProbe;
    private readonly Dictionary<MeshRenderer, UnityEngine.Rendering.ShadowCastingMode> originalShadowModes =
        new Dictionary<MeshRenderer, UnityEngine.Rendering.ShadowCastingMode>();
    private readonly Dictionary<MeshRenderer, bool> originalReceiveShadows = new Dictionary<MeshRenderer, bool>();
    private Color daylightFog;
    private float daylightAmbientIntensity;
    private ParticleSystem[] fireEffects;
    private Light[] fireLights;
    private Light hearthRoomLight;
    private readonly List<Light> roomFillLights = new List<Light>();
    private Coroutine hearthLightFade;
    private const float HearthLightIntensity = 1.8f;
    private const float EveningFadeSeconds = 5f;
    private AudioSource[] fireSounds;
    private readonly List<GameObject> fireplaceRoots = new List<GameObject>();

    private void Start()
    {
        if (GetComponent<ForestWarningNote>() == null) gameObject.AddComponent<ForestWarningNote>();
        collector = GetComponent<FirewoodCollector>();
        reticle = GetComponent<WorldItemReticle>();
        view = GetComponentInChildren<Camera>();
        furniturePlacement = GetComponent<FurniturePlacement>();
        if (furniturePlacement == null) furniturePlacement = gameObject.AddComponent<FurniturePlacement>();
        fireplace = GameObject.Find("Lumberjack_Fireplace");
        FireplaceCollisionSetup.Ensure(fireplace);
        PlaceChairUnderTable();
        woodStorage = GameObject.Find("Lumberjack_Supply_Crate");
        toolShelf = GameObject.Find("Lumberjack_Interior_Shelf");
        if (woodStorage != null)
        {
            storageDisplay = woodStorage.GetComponent<StorageWoodDisplay>();
            if (storageDisplay == null) storageDisplay = woodStorage.AddComponent<StorageWoodDisplay>();
            storageDisplay.SetCount(Progress.StoredWood);
        }
        originalSky = RenderSettings.skybox;
        sun = RenderSettings.sun;
        if (sun == null)
            foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional) { sun = light; break; }
        daylightIntensity = sun != null ? sun.intensity : 1f;
        daylightSunColor = sun != null ? sun.color : Color.white;
        daylightAmbient = RenderSettings.ambientLight;
        daylightAmbientMode = RenderSettings.ambientMode;
        daylightSkyColor = RenderSettings.ambientSkyColor;
        daylightEquatorColor = RenderSettings.ambientEquatorColor;
        daylightGroundColor = RenderSettings.ambientGroundColor;
        daylightProbe = RenderSettings.ambientProbe;
        daylightAmbientIntensity = RenderSettings.ambientIntensity;
        daylightFog = RenderSettings.fogColor;
        // The house contains its own nested Fire_fireplace prefab, in addition
        // to the named homestead effect. Control both instead of leaving one lit.
        foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool isFireplace = candidate.name == "Lumberjack_Fireplace" ||
                candidate.name.StartsWith("Fire_fireplace", System.StringComparison.Ordinal);
            if (!isFireplace) continue;
            if (fireplace != null && Vector3.Distance(candidate.position, fireplace.transform.position) > 15f) continue;
            fireplaceRoots.Add(candidate.gameObject);
        }
        var effects = new HashSet<ParticleSystem>();
        var lights = new HashSet<Light>();
        var sounds = new HashSet<AudioSource>();
        foreach (GameObject root in fireplaceRoots)
        {
            effects.UnionWith(root.GetComponentsInChildren<ParticleSystem>(true));
            lights.UnionWith(root.GetComponentsInChildren<Light>(true));
            sounds.UnionWith(root.GetComponentsInChildren<AudioSource>(true));
        }
        fireEffects = new List<ParticleSystem>(effects).ToArray();
        fireLights = new List<Light>(lights).ToArray();
        fireSounds = new List<AudioSource>(sounds).ToArray();
        SetFire(false);
    }

    private bool CanUseHearth => !Progress.FireLit &&
        (Progress.Day == 1 || (Progress.Day == 2 && lastStoryBeat == 15 && !IsTransitioning));

    private GameObject Target()
    {
        ForestWarningNote warning = GetComponent<ForestWarningNote>();
        if (warning != null && (warning.IsHeld || warning.LastPlacementFrame == Time.frameCount))
            return !sunsetRunning && reticle != null && reticle.IsHoldingItem && IsAimingAtObject(toolShelf)
                ? toolShelf : null;
        if (furniturePlacement != null && (furniturePlacement.IsHolding || furniturePlacement.HasPickupTarget ||
            furniturePlacement.LastInteractionFrame == Time.frameCount)) return null;
        if (view == null || collector == null || collector.IsThrowing || collector.HasPickupTarget ||
            collector.LastPickupFrame == Time.frameCount) return null;
        if (reticle != null && (reticle.HasToolPickupTarget || reticle.LastPickupFrame == Time.frameCount)) return null;
        if (sunsetRunning) return null;
        if (reticle != null && reticle.IsHoldingItem && IsAimingAtObject(toolShelf)) return toolShelf;
        if (IsAimingAtObject(woodStorage)) return woodStorage;
        if (CanUseHearth)
        {
            foreach (GameObject root in fireplaceRoots)
                if (CanReachFireplace(root)) return root;
            return null;
        }
        return null;
    }

    private bool TryStoreHeldTool()
    {
        if (IsTransitioning || Cursor.lockState != CursorLockMode.Locked || reticle == null ||
            !reticle.IsHoldingItem || !IsAimingAtObject(toolShelf)) return false;
        bool returningAxe = reticle.IsHoldingAxe;
        if (!reticle.ReturnToolToShelf()) return false;
        if (returningAxe) Progress.SetAxeReturned(true);
        return true;
    }

    private bool IsAimingAtObject(GameObject target)
    {
        if (target == null || view == null) return false;
        Ray ray = view.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float distance = float.MaxValue;
        // Test the object's own oriented mesh bounds. The shelf above the crate must
        // not capture interactions merely because it is in the camera's broad view cone.
        foreach (MeshFilter mesh in target.GetComponentsInChildren<MeshFilter>())
        {
            if (mesh.sharedMesh == null || mesh.GetComponent<CollectibleFirewood>() != null) continue;
            Ray localRay = new Ray(mesh.transform.InverseTransformPoint(ray.origin),
                mesh.transform.InverseTransformVector(ray.direction));
            if (!mesh.sharedMesh.bounds.IntersectRay(localRay, out float entry)) continue;
            Vector3 hit = mesh.transform.TransformPoint(localRay.GetPoint(entry));
            distance = Mathf.Min(distance, Vector3.Distance(ray.origin, hit));
        }
        if (distance > 3f) return false;
        foreach (RaycastHit hit in Physics.RaycastAll(ray, distance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(target.transform)) continue;
            if (hit.distance < distance - 0.05f) return false;
        }
        return true;
    }

    private bool CanReachFireplace(GameObject root)
    {
        if (root == null) return false;
        Bounds opening = new Bounds(root.transform.position + Vector3.up * 0.65f, new Vector3(2f, 2.4f, 2f));
        Ray ray = view.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Vector3.Distance(view.transform.position, opening.ClosestPoint(view.transform.position)) > 2.5f ||
            !opening.IntersectRay(ray, out float entry) || entry > 3f) return false;
        RaycastHit[] hits = Physics.RaycastAll(ray, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.IsChildOf(transform)) continue;
            // Accept the stone surround when the aimed point is within the hearth.
            // A wall outside this region still blocks interaction.
            return opening.Contains(hit.point) || hit.distance >= entry;
        }
        return true;
    }

    private void Update()
    {
        if (!IsTransitioning) UpdateStoryLine();
        if (IsTransitioning || Cursor.lockState != CursorLockMode.Locked) return;
#if ENABLE_INPUT_SYSTEM
        bool pressed = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        bool takePressed = Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool pressed = Input.GetKeyDown(KeyCode.E);
        bool takePressed = Input.GetKeyDown(KeyCode.F);
#else
        bool pressed = false;
        bool takePressed = false;
#endif
        GameObject target = Target();
        if ((!pressed && !takePressed) || target == null) return;
        if (target == toolShelf)
        {
            if (pressed) TryStoreHeldTool();
            return;
        }
        if (target == woodStorage)
        {
            if (takePressed)
            {
                if (Progress.StoredWood > 0 && collector.TakeStoredWood())
                {
                    Progress.WithdrawWood();
                    if (storageDisplay != null) storageDisplay.SetCount(Progress.StoredWood);
                }
                return;
            }
            collector.ThrowIntoStorage(woodStorage.transform, () =>
            {
                Progress.DepositWood(1);
                if (storageDisplay != null) storageDisplay.SetCount(Progress.StoredWood);
            });
            return;
        }
        if (CanUseHearth)
        {
            if (Progress.HearthWood < DailyStoryProgress.FirewoodCost)
            {
                if (!pressed) return;
                collector.ThrowIntoStorage(target.transform, () => Progress.LoadHearthWood(),
                    DailyStoryProgress.FirewoodCost - Progress.HearthWood);
                return;
            }
            if (takePressed && Progress.LightFire(Progress.StoredWood))
            {
                SetFire(true);
                if (Progress.Day == 1)
                    StartCoroutine(SunsetAfterFire());
                else
                {
                    storyLine = "Bên ngoài đã xảy ra chuyện gì đó. Mình cần ra ngoài kiểm tra.";
                    storyLineUntil = Time.unscaledTime + StoryLineDuration;
                }
                if (storageDisplay != null) storageDisplay.SetCount(Progress.StoredWood);
            }
        }
    }

    public void OnPlayerLeftBed()
    {
        if (Progress.Day == 1) leftBedOnFirstDay = true;
        else if (Progress.Day == 2) leftBedOnSecondDay = true;
        UpdateStoryLine();
    }

    private void UpdateStoryLine()
    {
        if (pendingStoryFollowUp != null)
        {
            if (Time.unscaledTime < storyLineUntil) return;
            storyLine = pendingStoryFollowUp;
            pendingStoryFollowUp = null;
            storyLineUntil = Time.unscaledTime + StoryLineDuration;
            return;
        }

        int beat;
        string line;
        if (redAfternoon)
        {
            ForestWarningNote warning = GetComponent<ForestWarningNote>();
            if (lastStoryBeat >= 14)
            {
                if (lastStoryBeat == 14 && Time.unscaledTime >= storyLineUntil && !IsTransitioning)
                {
                    lastStoryBeat = 15;
                    StartCoroutine(ShowEarthquake());
                }
                if (lastStoryBeat == 15 && Progress.FireLit && Time.unscaledTime >= storyLineUntil && IsOutsideHouse())
                {
                    lastStoryBeat = 16;
                    storyLine = "Những cái cây bên ngoài đã biến thành một khu rừng đáng sợ. Mình thấy lo về chuyện sắp xảy ra.";
                    storyLineUntil = Time.unscaledTime + StoryLineDuration;
                }
                if (lastStoryBeat == 16 && Time.unscaledTime >= storyLineUntil)
                {
                    lastStoryBeat = 17;
                    storyLine = "Tôi có cảm giác tảng đá này là lời giải thích cho những chuyện đang diễn ra. Mình cần tiến lại gần để kiểm tra.";
                    storyLineUntil = Time.unscaledTime + StoryLineDuration;
                }
                return;
            }
            if (lastStoryBeat == 13)
            {
                if (warning == null || !warning.IsOnTable || reticle == null ||
                    !reticle.IsPickaxeStored || !AreDoorsClosed()) return;
                beat = 14;
                line = "Bây giờ có chuyện gì đó đang diễn ra, và tôi nghĩ mình đang gặp nguy hiểm.";
            }
            else if (warning != null && warning.HasLoweredAfterReading)
            {
                beat = 13;
                line = "Mình cần cất cái cuốc, đặt lời cảnh báo này lên bàn rồi đi đóng cửa lại.";
            }
            else if (lastStoryBeat == 11 && Time.unscaledTime >= storyLineUntil &&
                warning != null && warning.IsNearEntranceNote)
            {
                beat = 12;
                line = "Có gì đó kỳ lạ ngay trước cửa nhà. Mình cầm lên xem thử.";
            }
            else
            {
                beat = 11;
                line = "Tôi có cảm giác bất an về chuyện này. Mình cần phải về nhà trú ẩn.";
            }
        }
        else if (Progress.Day == 2)
        {
            bool readyForCleanupLine = leftBedOnSecondDay && lastStoryBeat >= 9 &&
                Time.unscaledTime >= storyLineUntil;
            beat = readyForCleanupLine ? 10 : 9;
            line = readyForCleanupLine
                ? "Mình cần dọn những gốc cây đã chặt hôm qua. Đi lấy cuốc để dọn thôi."
                : "Trời đã sáng. Vậy mà khu rừng vẫn im lặng như đêm qua.";
        }
        else if (Progress.IsNight)
        {
            bool doorsClosed = AreDoorsClosed();
            beat = doorsClosed ? 8 : 7;
            line = doorsClosed
                ? "Đúng là một ngày dài. Bây giờ mình cần lên giường nghỉ ngơi."
                : "Mình cần đóng cửa thật kỹ trước khi đi ngủ.";
        }
        else if (Progress.FireLit)
        {
            beat = 6;
            line = "Lửa đã bén. Trời đang tối dần.";
        }
        else if (Progress.DepositedWood >= DailyStoryProgress.RequiredStoredWood)
        {
            beat = 4;
            line = "Củi đã đủ cho đêm nay. Sao mình vẫn thấy bất an nhỉ?";
        }
        else if (Progress.TreesCut >= DailyStoryProgress.RequiredTrees && lastStoryBeat >= 2)
        {
            if (Time.unscaledTime < storyLineUntil && lastStoryBeat == 2) return;
            beat = 3;
            line = "Đã chặt đủ cây rồi. Mình cần về nhà cất rìu, rồi quay lại thu thập gỗ cho vào kho.";
        }
        else if (Progress.TreesCut > 0)
        {
            beat = 2;
            line = "Cây vừa đổ, cả khu rừng bỗng im bặt.";
        }
        else if (leftBedOnFirstDay)
        {
            beat = 1;
            line = "Mình cần chuẩn bị củi để tối nay ấm hơn. Đi lấy rìu chặt cây thôi.";
        }
        else
        {
            beat = 0;
            line = "Đêm đầu tiên ở căn nhà này lạnh hơn mình tưởng.";
        }
        if (beat <= lastStoryBeat) return;
        lastStoryBeat = beat;
        storyLine = line;
        storyLineUntil = Time.unscaledTime + StoryLineDuration;
        if (beat == 2) pendingStoryFollowUp = "Mình cần chặt đủ 3 cây cho hôm nay.";
        if (beat == 4)
            pendingStoryFollowUp = $"Sắp đến đêm rồi. Mình cần chuẩn bị lò sưởi. Lấy {DailyStoryProgress.FirewoodCost} khúc gỗ cho vào lò để căn phòng ấm hơn tối nay.";
    }

    private IEnumerator SunsetAfterFire()
    {
        sunsetStarted = true;
        yield return Sunset();
        EnsureHearthRoomLight();
        if (hearthRoomLight != null)
        {
            hearthLightFade = StartCoroutine(FadeHearthLight());
            yield return hearthLightFade;
        }
        Progress.FinishSunset();
    }

    private IEnumerator Sunset()
    {
        sunsetRunning = true;
        if (originalSky != null)
        {
            nightSky = new Material(originalSky);
            RenderSettings.skybox = nightSky;
        }
        float exposure = nightSky != null && nightSky.HasProperty("_Exposure") ? nightSky.GetFloat("_Exposure") : 1f;
        float elapsed = 0f;
        while (elapsed < EveningFadeSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / EveningFadeSeconds);
            if (sun != null) sun.intensity = Mathf.Lerp(daylightIntensity, 0.06f, t);
            RenderSettings.ambientLight = Color.Lerp(daylightAmbient, new Color(0.08f, 0.1f, 0.17f), t);
            RenderSettings.ambientIntensity = Mathf.Lerp(daylightAmbientIntensity, 0.15f, t);
            RenderSettings.fogColor = Color.Lerp(daylightFog, new Color(0.025f, 0.035f, 0.07f), t);
            if (nightSky != null && nightSky.HasProperty("_Exposure")) nightSky.SetFloat("_Exposure", Mathf.Lerp(exposure, 0.08f, t));
            yield return null;
        }
        DynamicGI.UpdateEnvironment();
        sunsetRunning = false;
    }

    private void RestoreDaylight()
    {
        foreach (var entry in originalShadowModes)
            if (entry.Key != null) entry.Key.shadowCastingMode = entry.Value;
        foreach (var entry in originalReceiveShadows)
            if (entry.Key != null) entry.Key.receiveShadows = entry.Value;
        originalShadowModes.Clear();
        originalReceiveShadows.Clear();
        RenderSettings.skybox = originalSky;
        RenderSettings.ambientMode = daylightAmbientMode;
        RenderSettings.ambientSkyColor = daylightSkyColor;
        RenderSettings.ambientEquatorColor = daylightEquatorColor;
        RenderSettings.ambientGroundColor = daylightGroundColor;
        RenderSettings.ambientProbe = daylightProbe;
        RenderSettings.ambientLight = daylightAmbient;
        RenderSettings.ambientIntensity = daylightAmbientIntensity;
        RenderSettings.fogColor = daylightFog;
        if (sun != null)
        {
            sun.intensity = daylightIntensity;
            sun.color = daylightSunColor;
        }
        if (nightSky != null) { Destroy(nightSky); nightSky = null; }
        DynamicGI.UpdateEnvironment();
    }

    private void OnDestroy()
    {
        if (sleepOverlay != null) Destroy(sleepOverlay);
        if (hearthRoomLight != null) Destroy(hearthRoomLight.gameObject);
        foreach (Light light in roomFillLights) if (light != null) Destroy(light.gameObject);
        if (sunsetStarted) RestoreDaylight();
    }

    private void SetFire(bool lit)
    {
        if (fireEffects != null) foreach (ParticleSystem effect in fireEffects)
        {
            if (lit) effect.gameObject.SetActive(true);
            var main = effect.main;
            main.playOnAwake = false;
            var particleLights = effect.lights;
            particleLights.enabled = false;
            if (lit) effect.Play(false);
            else
            {
                effect.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                effect.gameObject.SetActive(false);
            }
        }
        if (fireLights != null) foreach (Light light in fireLights)
        {
            light.enabled = false;
            light.intensity = 0f;
            LightFlickerEffect flicker = light.GetComponent<LightFlickerEffect>();
            if (flicker != null) flicker.enabled = false;
        }
        if (fireSounds != null) foreach (AudioSource sound in fireSounds)
        {
            if (lit) sound.Play();
            else sound.Stop();
        }
        if (hearthLightFade != null) { StopCoroutine(hearthLightFade); hearthLightFade = null; }
        if (!lit && hearthRoomLight != null)
        {
            hearthRoomLight.intensity = 0f;
            hearthRoomLight.enabled = false;
            foreach (Light light in roomFillLights)
                if (light != null) { light.intensity = 0f; light.enabled = false; }
        }
    }

    private void EnsureHearthRoomLight()
    {
        if (hearthRoomLight != null || fireplace == null) return;
        EnsureHouseLightOcclusion();
        GameObject lamp = new GameObject("Hearth Warm Room Light");
        lamp.transform.SetParent(fireplace.transform, false);
        Vector3 inward = woodStorage != null
            ? Vector3.ProjectOnPlane(woodStorage.transform.position - fireplace.transform.position, Vector3.up).normalized
            : Vector3.ProjectOnPlane(transform.position - fireplace.transform.position, Vector3.up).normalized;
        lamp.transform.position = fireplace.transform.position + Vector3.up * 0.9f + inward * 0.65f;
        hearthRoomLight = lamp.AddComponent<Light>();
        hearthRoomLight.type = LightType.Point;
        hearthRoomLight.lightmapBakeType = LightmapBakeType.Realtime;
        hearthRoomLight.color = new Color(1f, 0.62f, 0.30f);
        hearthRoomLight.range = 8f;
        hearthRoomLight.intensity = 0f;
        hearthRoomLight.shadows = LightShadows.Soft;
        hearthRoomLight.shadowStrength = 1f;
        hearthRoomLight.shadowBias = 0.005f;
        hearthRoomLight.shadowNormalBias = 0.01f;
        hearthRoomLight.shadowNearPlane = 0.03f;
        hearthRoomLight.renderMode = LightRenderMode.ForcePixel;
        hearthRoomLight.bounceIntensity = 0f;
        // Warm reflected light fills the room without changing the outdoor ambient light.
        GameObject bed = GameObject.Find("Lumberjack_Bed");
        Vector3 farSide = bed != null ? bed.transform.position
            : woodStorage != null ? woodStorage.transform.position : transform.position;
        for (int i = 0; i < 2; i++)
        {
            GameObject fill = new GameObject("Hearth Interior Fill " + (i + 1));
            fill.transform.SetParent(fireplace.transform, true);
            fill.transform.position = Vector3.Lerp(lamp.transform.position, farSide, i == 0 ? 0.4f : 0.8f);
            fill.transform.position = new Vector3(fill.transform.position.x, lamp.transform.position.y + 1.4f,
                fill.transform.position.z);
            Light light = fill.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.color = new Color(1f, 0.75f, 0.48f);
            light.range = 10f;
            light.intensity = 0f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 1f;
            light.shadowBias = 0.005f;
            light.shadowNormalBias = 0.01f;
            light.shadowNearPlane = 0.03f;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0f;
            roomFillLights.Add(light);
        }
    }

    private void EnsureHouseLightOcclusion()
    {
        GameObject house = GameObject.Find("medieval_house_lite_v2");
        if (house == null) return;
        foreach (MeshRenderer renderer in house.GetComponentsInChildren<MeshRenderer>(true))
        {
            // Thin modular walls need to cast shadows from both faces for interior point lights.
            // Leave transparent glazing alone so real openings still transmit light.
            bool opaque = false;
            foreach (Material material in renderer.sharedMaterials)
                if (material != null && material.renderQueue < 3000) { opaque = true; break; }
            if (!opaque) continue;
            if (!originalShadowModes.ContainsKey(renderer))
            {
                originalShadowModes.Add(renderer, renderer.shadowCastingMode);
                originalReceiveShadows.Add(renderer, renderer.receiveShadows);
            }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            renderer.receiveShadows = true;
        }
    }

    private IEnumerator FadeHearthLight()
    {
        hearthRoomLight.enabled = true;
        foreach (Light light in roomFillLights) if (light != null) light.enabled = true;
        float start = hearthRoomLight.intensity;
        float elapsed = 0f;
        while (elapsed < EveningFadeSeconds && hearthRoomLight != null)
        {
            elapsed += Time.deltaTime;
            hearthRoomLight.intensity = Mathf.Lerp(start, HearthLightIntensity,
                Mathf.SmoothStep(0f, 1f, elapsed / EveningFadeSeconds));
            foreach (Light light in roomFillLights)
                if (light != null) light.intensity = 1.4f * Mathf.SmoothStep(0f, 1f, elapsed / EveningFadeSeconds);
            yield return null;
        }
        if (hearthRoomLight != null) hearthRoomLight.intensity = HearthLightIntensity;
        foreach (Light light in roomFillLights) if (light != null) light.intensity = 1.4f;
        hearthLightFade = null;
        // Only this local light changes; the night sky, sun and ambient lighting stay dark.
    }

    private void PlaceChairUnderTable()
    {
        GameObject table = GameObject.Find("Lumberjack_Interior_Table");
        GameObject prefab = Resources.Load<GameObject>("Story/WoodenChair");
        if (table == null || prefab == null) return;
        Renderer tabletop = table.GetComponent<Renderer>();
        if (tabletop == null) return;

        Bounds tableBounds = tabletop.bounds;
        Vector3 outward = Vector3.ProjectOnPlane(transform.position - tableBounds.center, Vector3.up).normalized;
        if (outward == Vector3.zero) outward = table.transform.forward;
        Vector3 position = tableBounds.center + outward * Mathf.Min(tableBounds.extents.x, tableBounds.extents.z) * 0.65f;
        position.y = tableBounds.min.y;
        GameObject chair = Instantiate(prefab, position, Quaternion.LookRotation(outward));
        chair.name = "Wooden Chair";
        Bounds chairBounds = FurniturePlacement.BoundsOf(chair.transform);
        chair.transform.localScale *= Mathf.Min(0.55f, tableBounds.size.y * 0.8f) / Mathf.Max(chairBounds.size.y, 0.01f);
        chairBounds = FurniturePlacement.BoundsOf(chair.transform);
        chair.transform.position += Vector3.up * (position.y - chairBounds.min.y + 0.01f);
        chair.AddComponent<CraftedFurniture>();
        if (chair.GetComponentInChildren<Collider>() == null)
            foreach (MeshFilter mesh in chair.GetComponentsInChildren<MeshFilter>())
                if (mesh.sharedMesh != null) mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
    }

    public void SleepToNextDay()
    {
        if (IsTransitioning || sunsetRunning) return;
        if (Progress.IsNight && !AreDoorsClosed()) return;
        if (!IsTransitioning && Progress.CanSleepToNextDay) StartCoroutine(NextMorning());
    }

    private bool IsOutsideHouse()
    {
        GameObject house = GameObject.Find("medieval_house_lite_v2");
        if (house == null) return false;
        foreach (LumberjackDoorInteract door in FindObjectsByType<LumberjackDoorInteract>())
        {
            Vector3 doorway = door.transform.position;
            Vector3 outward = Vector3.ProjectOnPlane(doorway - house.transform.position, Vector3.up).normalized;
            if (Vector3.Dot(transform.position - doorway, outward) > 1f) return true;
        }
        return false;
    }

    private bool AreDoorsClosed()
    {
        foreach (LumberjackDoorInteract door in FindObjectsByType<LumberjackDoorInteract>())
            if (door.isActiveAndEnabled && !door.IsFullyClosed) return false;
        return true;
    }

    private void ShowSleepOverlay()
    {
        if (sleepOverlay == null)
        {
            sleepOverlay = new GameObject("Sleep Fullscreen Blackout", typeof(UnityEngine.Canvas),
                typeof(UnityEngine.UI.CanvasScaler));
            Canvas canvas = sleepOverlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;
            var scaler = sleepOverlay.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            GameObject background = new GameObject("Black", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            background.transform.SetParent(sleepOverlay.transform, false);
            RectTransform rect = background.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            sleepBackground = background.GetComponent<UnityEngine.UI.Image>();
            sleepBackground.color = Color.black;
            GameObject caption = new GameObject("Next Day", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            caption.transform.SetParent(background.transform, false);
            RectTransform captionRect = caption.GetComponent<RectTransform>();
            captionRect.anchorMin = Vector2.zero; captionRect.anchorMax = Vector2.one;
            captionRect.offsetMin = Vector2.zero; captionRect.offsetMax = Vector2.zero;
            var text = caption.GetComponent<UnityEngine.UI.Text>();
            sleepCaption = text;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 36;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = "Ngày tiếp theo...";
        }
        sleepBackground.color = new Color(0f, 0f, 0f, 0f);
        sleepCaption.color = new Color(1f, 1f, 1f, 0f);
        sleepOverlay.SetActive(true);
    }

    private IEnumerator NextMorning()
    {
        IsTransitioning = true;
        bedtimeThoughtUntil = Time.unscaledTime + BedtimeThoughtDuration;
        yield return new WaitForSecondsRealtime(BedtimeThoughtDuration);
        bedtimeThoughtUntil = 0f;
        ShowSleepOverlay();
        float elapsed = 0f;
        while (elapsed < 2f)
        {
            elapsed += Time.unscaledDeltaTime;
            sleepBackground.color = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, elapsed / 2f));
            yield return null;
        }
        sleepBackground.color = Color.black;
        elapsed = 0f;
        while (elapsed < 0.6f)
        {
            elapsed += Time.unscaledDeltaTime;
            sleepCaption.color = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, elapsed / 0.6f));
            yield return null;
        }
        sleepCaption.color = Color.white;
        yield return new WaitForSecondsRealtime(4.5f);
        Progress.AdvanceDay();
        SetFire(false);
        RestoreDaylight();
        // Let the restored environment lighting settle while the screen is still black.
        yield return new WaitForSecondsRealtime(0.5f);
        GetComponent<TreeChoppingSystem>()?.RefreshChoppedTerrain();
        // Remove the caption first, then reveal the restored morning scene.
        elapsed = 0f;
        while (elapsed < 0.6f)
        {
            elapsed += Time.unscaledDeltaTime;
            sleepCaption.color = new Color(1f, 1f, 1f,
                1f - Mathf.SmoothStep(0f, 1f, elapsed / 0.6f));
            yield return null;
        }
        sleepCaption.color = Color.clear;
        elapsed = 0f;
        while (elapsed < 2f)
        {
            elapsed += Time.unscaledDeltaTime;
            sleepBackground.color = new Color(0f, 0f, 0f,
                1f - Mathf.SmoothStep(0f, 1f, elapsed / 2f));
            yield return null;
        }
        sleepOverlay.SetActive(false);
        IsTransitioning = false;
    }

    public void RecordFirstDayStumpCleared()
    {
        if (Progress.Day != 2 || redAfternoon || IsTransitioning) return;
        firstDayStumpsCleared++;
        if (firstDayStumpsCleared == DailyStoryProgress.RequiredTrees)
            StartCoroutine(ShowRedAfternoon());
    }

    private IEnumerator ShowRedAfternoon()
    {
        IsTransitioning = true;
        PT_PlayerMovement movement = GetComponent<PT_PlayerMovement>();
        bool restoreMovement = movement != null && movement.enabled;
        if (restoreMovement) movement.enabled = false;

        ShowSleepOverlay();
        sleepCaption.text = "Chớp mắt cái đã đến buổi chiều.\nCó chuyện gì đó kỳ lạ vừa xảy ra...";
        yield return FadeAfternoonOverlay(0f, 1f);
        sleepCaption.color = Color.white;
        yield return new WaitForSecondsRealtime(4f);

        sleepCaption.color = Color.clear;
        if (originalSky != null)
        {
            nightSky = new Material(originalSky);
            nightSky.SetColor("_Tint", new Color(0.95f, 0.17f, 0.12f));
            RenderSettings.skybox = nightSky;
        }
        if (sun != null) sun.color = new Color(1f, 0.48f, 0.32f);
        RenderSettings.fogColor = new Color(0.42f, 0.13f, 0.12f);
        DynamicGI.UpdateEnvironment();
        yield return FadeAfternoonOverlay(1f, 0f);

        sleepOverlay.SetActive(false);
        redAfternoon = true;
        IsTransitioning = false;
        if (restoreMovement && movement != null) movement.enabled = true;
        UpdateStoryLine();
    }

    private IEnumerator ShowEarthquake()
    {
        IsTransitioning = true;
        PT_PlayerMovement movement = GetComponent<PT_PlayerMovement>();
        bool restoreMovement = movement != null && movement.enabled;
        if (restoreMovement) movement.enabled = false;

        Transform cameraTransform = view != null ? view.transform : null;
        Vector3 rest = cameraTransform != null ? cameraTransform.localPosition : Vector3.zero;
        ShowSleepOverlay();
        sleepCaption.color = Color.clear;
        const float blackoutAt = 2.5f;
        const float revealAt = 3.5f;
        const float duration = 5.5f;
        bool transformed = false;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float opacity = elapsed < revealAt
                ? Mathf.SmoothStep(0f, 1f, (elapsed - 0.5f) / 2f)
                : Mathf.SmoothStep(1f, 0f, (elapsed - revealAt) / 2f);
            sleepBackground.color = new Color(0f, 0f, 0f, opacity);
            if (cameraTransform != null)
                cameraTransform.localPosition = rest + new Vector3(
                    Mathf.Sin(elapsed * 57f), Mathf.Sin(elapsed * 73f), 0f) * (elapsed < revealAt ? 0.09f : 0f);

            if (!transformed && elapsed >= blackoutAt)
            {
                transformed = true;
                ShowBloodMoonNight();
                GameObject spookyTree = Resources.Load<GameObject>("Spooky Tree");
                if (spookyTree != null) GetComponent<TreeChoppingSystem>()?.TransformFruitTrees(spookyTree);
                foreach (Collider stone in FindObjectsByType<Collider>())
                {
                    if (stone.name != "PT_Menhir_Rock_02 Collision") continue;
                    GameObject runeGlow = new GameObject("Rune Glow");
                    runeGlow.transform.SetParent(stone.transform, false);
                    runeGlow.transform.position = stone.bounds.center + Vector3.up * stone.bounds.extents.y * 0.55f;
                    Light glow = runeGlow.AddComponent<Light>();
                    glow.type = LightType.Point;
                    glow.color = new Color(0.05f, 0.68f, 1f);
                    glow.intensity = 2.2f;
                    glow.range = 3.5f;
                }
            }
            yield return null;
        }
        if (cameraTransform != null) cameraTransform.localPosition = rest;
        sleepOverlay.SetActive(false);

        if (restoreMovement && movement != null) movement.enabled = true;
        storyLine = "Mình cần thắp sáng căn phòng. Trong kho vẫn còn củi để nhóm lò.";
        storyLineUntil = Time.unscaledTime + StoryLineDuration;
        IsTransitioning = false;
    }

    private void ShowBloodMoonNight()
    {
        if (nightSky != null)
        {
            if (nightSky.HasProperty("_Tint")) nightSky.SetColor("_Tint", new Color(0.17f, 0.11f, 0.21f));
            if (nightSky.HasProperty("_Exposure")) nightSky.SetFloat("_Exposure", 0.12f);
        }
        if (sun != null)
        {
            sun.color = new Color(0.52f, 0.35f, 0.62f);
            sun.intensity *= 0.15f;
        }
        RenderSettings.ambientIntensity *= 0.35f;
        RenderSettings.fogColor = new Color(0.065f, 0.035f, 0.08f);
        DynamicGI.UpdateEnvironment();

        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null) return;
        Vector3 direction = Quaternion.Euler(0f, 25f, 0f) * transform.forward;
        direction.y = 0.45f;
        GameObject moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        moon.name = "Blood Moon";
        float distance = view != null ? Mathf.Min(160f, view.farClipPlane * 0.7f) : 160f;
        moon.transform.position = transform.position + direction.normalized * distance;
        moon.transform.localScale = Vector3.one * (distance * 0.1f);
        moon.GetComponent<Collider>().enabled = false;
        Renderer renderer = moon.GetComponent<Renderer>();
        renderer.material = new Material(shader) { color = new Color(0.95f, 0.045f, 0.07f) };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private IEnumerator FadeAfternoonOverlay(float from, float to)
    {
        const float duration = 2f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            sleepBackground.color = new Color(0f, 0f, 0f, Mathf.SmoothStep(from, to, elapsed / duration));
            yield return null;
        }
    }

    private void OnGUI()
    {
        bool showingBedtimeThought = IsTransitioning && Time.unscaledTime < bedtimeThoughtUntil;
        if (collector == null || (IsTransitioning && !showingBedtimeThought)) return;
        string subtitleText = showingBedtimeThought ? "Hôm nay khu rừng có gì đó kỳ lạ..." : storyLine;
        float remaining = (showingBedtimeThought ? bedtimeThoughtUntil : storyLineUntil) - Time.unscaledTime;
        if (remaining > 0f && !string.IsNullOrEmpty(subtitleText))
        {
            float elapsed = (showingBedtimeThought ? BedtimeThoughtDuration : StoryLineDuration) - remaining;
            float alpha = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(Mathf.Min(elapsed, remaining) / DialogueFadeDuration));
            float width = Mathf.Min(820f, Screen.width - 40f);
            GUIStyle subtitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.027f), 18, 28),
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, alpha) }
            };
            float height = subtitle.CalcHeight(new GUIContent(subtitleText), width - 32f) + 24f;
            Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height - 145f - height, width, height);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 12f, width - 32f, height - 24f), subtitleText, subtitle);
        }
    }
}
