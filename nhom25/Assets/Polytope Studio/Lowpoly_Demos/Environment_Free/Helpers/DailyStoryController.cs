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
    private GameObject workbench;
    private GameObject woodStorage;
    private StorageWoodDisplay storageDisplay;
    private bool sunsetStarted;
    private bool sunsetRunning;
    private Material originalSky;
    private Material nightSky;
    private Light sun;
    private float daylightIntensity;
    private Color daylightAmbient;
    private Color daylightFog;
    private float daylightAmbientIntensity;
    private ParticleSystem[] fireEffects;
    private Light[] fireLights;
    private AudioSource[] fireSounds;
    private readonly List<GameObject> fireplaceRoots = new List<GameObject>();

    private void Start()
    {
        collector = GetComponent<FirewoodCollector>();
        reticle = GetComponent<WorldItemReticle>();
        view = GetComponentInChildren<Camera>();
        fireplace = GameObject.Find("Lumberjack_Fireplace");
        workbench = GameObject.Find("Lumberjack_Sawing_Station");
        woodStorage = GameObject.Find("Lumberjack_Supply_Crate");
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
        daylightAmbient = RenderSettings.ambientLight;
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

    private GameObject Target()
    {
        if (view == null || collector == null || collector.IsThrowing || collector.HasPickupTarget ||
            collector.LastPickupFrame == Time.frameCount) return null;
        if (reticle != null && (reticle.HasToolPickupTarget || reticle.LastPickupFrame == Time.frameCount)) return null;
        if (sunsetRunning) return null;
        if (CanReachObject(woodStorage)) return woodStorage;
        if (Progress.Day == 1 && Progress.IsNight && !Progress.FireLit)
        {
            foreach (GameObject root in fireplaceRoots)
                if (CanReachFireplace(root)) return root;
            return null;
        }
        GameObject target = Progress.Day == 2 && !Progress.FurnitureCrafted ? workbench : null;
        return CanReachObject(target) ? target : null;
    }

    private bool CanReachObject(GameObject target)
    {
        if (target == null || view == null) return false;
        Vector3 point = target.transform.position + Vector3.up * 0.4f;
        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null) point = renderer.bounds.center;
        Vector3 offset = point - view.transform.position;
        if (offset.magnitude > 3f || Vector3.Dot(view.transform.forward, offset.normalized) < 0.82f) return false;
        RaycastHit[] hits = Physics.RaycastAll(view.transform.position, offset.normalized, offset.magnitude,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(target.transform)) continue;
            // The fireplace's stone surround may be part of the house mesh. Only
            // reject obstacles well in front of the intended interaction point.
            if (hit.distance < offset.magnitude - 0.6f) return false;
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
        if (Progress.IsNight && !sunsetStarted)
        {
            sunsetStarted = true;
            StartCoroutine(Sunset());
        }
        if (IsTransitioning || Cursor.lockState != CursorLockMode.Locked) return;
#if ENABLE_INPUT_SYSTEM
        bool pressed = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool pressed = Input.GetKeyDown(KeyCode.E);
#else
        bool pressed = false;
#endif
        GameObject target = Target();
        if (!pressed || target == null) return;
        if (target == woodStorage)
        {
            collector.ThrowIntoStorage(woodStorage.transform, () =>
            {
                Progress.DepositWood(1);
                if (storageDisplay != null) storageDisplay.SetCount(Progress.StoredWood);
            });
            return;
        }
        if (Progress.Day == 1)
        {
            if (Progress.LightFire(Progress.StoredWood))
            {
                SetFire(true);
                if (storageDisplay != null) storageDisplay.SetCount(Progress.StoredWood);
            }
        }
        else if (Progress.Day == 2 && Progress.StoredWood >= DailyStoryProgress.FurnitureCost &&
            Progress.DayTwoWoodDeposited >= DailyStoryProgress.FurnitureCost)
        {
            GameObject prefab = Resources.Load<GameObject>("Story/WoodenChair");
            if (prefab == null) { Debug.LogWarning("Missing Story/WoodenChair prefab."); return; }
            GameObject chair = CreateChair(prefab);
            if (chair == null) return;
            if (Progress.CraftFurniture(Progress.StoredWood))
            {
                if (storageDisplay != null) storageDisplay.SetCount(Progress.StoredWood);
            }
            else Destroy(chair);
        }
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
        while (elapsed < 5f)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / 5f);
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
        RenderSettings.skybox = originalSky;
        RenderSettings.ambientLight = daylightAmbient;
        RenderSettings.ambientIntensity = daylightAmbientIntensity;
        RenderSettings.fogColor = daylightFog;
        if (sun != null) sun.intensity = daylightIntensity;
        if (nightSky != null) { Destroy(nightSky); nightSky = null; }
        DynamicGI.UpdateEnvironment();
    }

    private void OnDestroy()
    {
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
    }

    private GameObject CreateChair(GameObject prefab)
    {
        Renderer benchRenderer = workbench.GetComponent<Renderer>();
        Vector3 center = benchRenderer != null ? benchRenderer.bounds.center : workbench.transform.position;
        Vector3 towardPlayer = Vector3.ProjectOnPlane(transform.position - center, Vector3.up).normalized;
        if (towardPlayer.sqrMagnitude < 0.1f) towardPlayer = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, towardPlayer);
        Vector3 position = center + right * 1.5f;
        position.y = benchRenderer != null ? benchRenderer.bounds.min.y + 0.8f : position.y;
        if (!Physics.Raycast(position, Vector3.down, out RaycastHit ground, 4f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return null;
        GameObject chair = Instantiate(prefab, ground.point, Quaternion.LookRotation(towardPlayer));
        chair.name = "Crafted Wooden Chair";
        Renderer[] renderers = chair.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Destroy(chair); return null; }
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        chair.transform.localScale *= 0.9f / Mathf.Max(bounds.size.y, 0.01f);
        bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        chair.transform.position += Vector3.up * (ground.point.y - bounds.min.y + 0.02f);
        if (chair.GetComponentInChildren<Collider>() == null)
            foreach (MeshFilter mesh in chair.GetComponentsInChildren<MeshFilter>())
                if (mesh.sharedMesh != null) mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
        return chair;
    }

    public void SleepToNextDay()
    {
        if (!IsTransitioning && Progress.CanSleepToNextDay) StartCoroutine(NextMorning());
    }

    private IEnumerator NextMorning()
    {
        IsTransitioning = true;
        yield return new WaitForSeconds(1.5f);
        Progress.AdvanceDay();
        RestoreDaylight();
        IsTransitioning = false;
    }

    private void OnGUI()
    {
        if (collector == null) return;
        float width = Mathf.Min(360f, Screen.width * 0.42f);
        Rect panel = new Rect(Screen.width - width - 20f, 20f, width, 190f);
        GUI.Box(panel, GUIContent.none);
        GUIStyle heading = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = true };
        GUIStyle text = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
        string title = Progress.Day == 1 ? "Ngày 1 — Giữ ấm căn nhà" : "Ngày 2 — Dựng xây cuộc sống";
        string objective = Progress.Day == 1
            ? Progress.FireLit ? "Lửa đã cháy. Căn nhà cuối cùng cũng ấm lên.\nTrở về giường ngủ để sang ngày 2."
                : !Progress.IsNight ? "Tranh thủ ban ngày tích trữ gỗ cho đêm lạnh.\nCây thông: " + Progress.TreesCut + "/3\nGỗ đã cất: " + Mathf.Min(Progress.DepositedWood, 9) + "/9\nMang gỗ đến thùng dụng cụ và nhấn E để cất."
                    : sunsetRunning ? "Gỗ đã được cất đủ. Trời đang tối dần..."
                    : "Đêm đã xuống. Trở về nhóm lò để giữ ấm.\nDùng 3 gỗ trong kho để đốt lửa.\nGỗ trong kho: " + Progress.StoredWood
            : Progress.FurnitureCrafted ? "Chiếc ghế đầu tiên đã hoàn thành.\nCăn nhà bắt đầu trở thành tổ ấm."
                : "Hôm nay, hãy làm căn nhà tiện nghi hơn.\nMang 5 gỗ mới về thùng, rồi đến bàn cưa chế tạo ghế.\nGỗ đã cất hôm nay: " + Mathf.Min(Progress.DayTwoWoodDeposited, 5) + "/5";
        GUI.Label(new Rect(panel.x + 14f, panel.y + 10f, width - 28f, 32f), title, heading);
        GUI.Label(new Rect(panel.x + 14f, panel.y + 48f, width - 28f, 138f), objective, text);
        if (HasInteractionPrompt)
        {
            int cost = Progress.Day == 1 ? DailyStoryProgress.FirewoodCost : DailyStoryProgress.FurnitureCost;
            int available = Progress.Day == 1 ? Progress.StoredWood : Mathf.Min(Progress.DayTwoWoodDeposited, Progress.StoredWood);
            string prompt = available < cost ? "Cần " + cost + " gỗ (" + available + "/" + cost + ")" :
                Progress.Day == 1 ? "E — Nhóm lò (3 gỗ)" : "E — Chế tạo ghế (5 gỗ)";
            if (Target() == woodStorage)
                prompt = collector.IsCarryingWood ? "E — Quăng gỗ vào thùng" : "Kho gỗ: " + Progress.StoredWood;
            GUIStyle style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect((Screen.width - 300f) * 0.5f, Screen.height - 90f, 300f, 40f), prompt, style);
        }
        if (IsTransitioning)
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
            GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, Screen.height * 0.5f - 30f, Screen.width, 60f), "Sáng hôm sau...", style);
        }
    }
}
