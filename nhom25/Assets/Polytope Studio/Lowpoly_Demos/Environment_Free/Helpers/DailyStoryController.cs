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
    private WorkbenchWoodDisplay workbenchDisplay;
    private FurniturePlacement furniturePlacement;
    private const int CraftRequiredHits = 5;
    private int craftHits;
    private float craftBarUntil;
    private GameObject woodStorage;
    private GameObject toolShelf;
    private StorageWoodDisplay storageDisplay;
    private const float DialogueDuration = 5f;
    private const float DialogueFadeDuration = 0.65f;
    private string dialogue;
    private float dialogueUntil;
    private float sleepReminderUntil;
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
    private const float HearthLightFadeSeconds = 5f;
    private AudioSource[] fireSounds;
    private readonly List<GameObject> fireplaceRoots = new List<GameObject>();

    private void Start()
    {
        collector = GetComponent<FirewoodCollector>();
        reticle = GetComponent<WorldItemReticle>();
        view = GetComponentInChildren<Camera>();
        furniturePlacement = GetComponent<FurniturePlacement>();
        if (furniturePlacement == null) furniturePlacement = gameObject.AddComponent<FurniturePlacement>();
        fireplace = GameObject.Find("Lumberjack_Fireplace");
        FireplaceCollisionSetup.Ensure(fireplace);
        workbench = GameObject.Find("Lumberjack_Interior_Table");
        if (workbench != null)
        {
            workbenchDisplay = workbench.GetComponent<WorkbenchWoodDisplay>();
            if (workbenchDisplay == null) workbenchDisplay = workbench.AddComponent<WorkbenchWoodDisplay>();
            workbenchDisplay.SetCount(Progress.WorkbenchWood);
        }
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

    private GameObject Target()
    {
        if (furniturePlacement != null && (furniturePlacement.IsHolding || furniturePlacement.HasPickupTarget ||
            furniturePlacement.LastInteractionFrame == Time.frameCount)) return null;
        if (view == null || collector == null || collector.IsThrowing || collector.HasPickupTarget ||
            collector.LastPickupFrame == Time.frameCount) return null;
        if (reticle != null && (reticle.HasToolPickupTarget || reticle.LastPickupFrame == Time.frameCount)) return null;
        if (sunsetRunning) return null;
        if (reticle != null && reticle.IsHoldingItem && IsAimingAtObject(toolShelf)) return toolShelf;
        if (IsAimingAtObject(woodStorage)) return woodStorage;
        if (Progress.Day == 1 && !Progress.FireLit)
        {
            foreach (GameObject root in fireplaceRoots)
                if (CanReachFireplace(root)) return root;
            return null;
        }
        GameObject target = Progress.Day == 2 && !Progress.FurnitureCrafted ? workbench : null;
        return CanReachObject(target) ? target : null;
    }

    public bool TryStoreHeldTool()
    {
        if (IsTransitioning || Cursor.lockState != CursorLockMode.Locked || reticle == null ||
            !reticle.IsHoldingItem || !IsAimingAtObject(toolShelf)) return false;
        bool returningAxe = reticle.IsHoldingAxe;
        if (!reticle.ReturnToolToShelf()) return false;
        if (returningAxe) Progress.SetAxeReturned(true);
        return true;
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
        if (Progress.IsNight && !sunsetStarted)
        {
            sunsetStarted = true;
            StartCoroutine(Sunset());
        }
        UpdateDialogue();
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
        if (!pressed) return;
        if (Progress.Day == 1)
        {
            if (Progress.HearthWood < DailyStoryProgress.FirewoodCost)
            {
                collector.ThrowIntoStorage(target.transform, () => Progress.LoadHearthWood(),
                    DailyStoryProgress.FirewoodCost - Progress.HearthWood);
                return;
            }
            if (Progress.IsNight && Progress.LightFire(Progress.StoredWood))
            {
                SetFire(true);
                if (storageDisplay != null) storageDisplay.SetCount(Progress.StoredWood);
            }
        }
        else if (Progress.Day == 2)
        {
            if (Progress.WorkbenchWood < DailyStoryProgress.FurnitureCost)
            {
                collector.ThrowIntoStorage(workbench.transform, () =>
                {
                    Progress.DeliverWorkbenchWood();
                    if (workbenchDisplay != null) workbenchDisplay.SetCount(Progress.WorkbenchWood);
                }, DailyStoryProgress.FurnitureCost - Progress.WorkbenchWood, true);
                return;
            }

        }
    }

    private enum DialogueTask
    {
        LeaveBed, TakeAxe, ChopTree, CollectLogs, StoreLogs, GatherRemainingWood, ReturnAxe, TakeHearthLogs,
        LoadHearth, WaitForNight, LightFire, CloseDoors, Sleep, TakeHammer, TakeCraftLogs,
        StackCraftLogs, HammerChair, PutHammerAway, PickUpChair, PlaceChair, Finished
    }
    private DialogueTask currentDialogueTask = DialogueTask.LeaveBed;
    private bool taskDialogueShown;
    private int depositGoal;
    private int treeGoal;

    private void SetDialogueTask(DialogueTask task)
    {
        currentDialogueTask = task;
        taskDialogueShown = false;
        if (task == DialogueTask.ChopTree)
        {
            treeGoal = Mathf.Min(Progress.DepositedWood / 3 + 1, DailyStoryProgress.RequiredTrees);
            depositGoal = Mathf.Min((Progress.DepositedWood / 3 + 1) * 3, DailyStoryProgress.RequiredStoredWood);
        }
    }

    private void UpdateDialogue()
    {
        if (IsTransitioning || Time.time < sleepReminderUntil) return;
        if (Progress.Day == 2 && currentDialogueTask < DialogueTask.TakeHammer)
            SetDialogueTask(DialogueTask.TakeHammer);
        if (Progress.Day == 1 && currentDialogueTask == DialogueTask.Sleep && !AreDoorsClosed())
            SetDialogueTask(DialogueTask.CloseDoors);

        // Completion immediately replaces the current instruction. Otherwise its
        // display timer may expire while this task remains pending.
        if (taskDialogueShown)
        {
            if (!DialogueTaskComplete()) return;
            DialogueTask next = currentDialogueTask + 1;
            if (currentDialogueTask == DialogueTask.StoreLogs)
                next = Progress.DepositedWood >= DailyStoryProgress.RequiredStoredWood &&
                    Progress.TreesCut >= DailyStoryProgress.RequiredTrees
                    ? DialogueTask.ReturnAxe : DialogueTask.GatherRemainingWood;
            if (currentDialogueTask == DialogueTask.Sleep || currentDialogueTask == DialogueTask.Finished) return;
            SetDialogueTask(next);
        }
        if (currentDialogueTask == DialogueTask.Finished && taskDialogueShown) return;
        string line;
        switch (currentDialogueTask)
        {
            case DialogueTask.LeaveBed:
                line = "Một ngày mới bắt đầu. Nhấn Space để rời giường."; break;
            case DialogueTask.TakeAxe:
                line = "Đến kệ dụng cụ, nhìn vào rìu và nhấn E để lấy."; break;
            case DialogueTask.ChopTree:
                line = Progress.TreesCut == 0
                    ? "Ra ngoài tìm một cây thông. Đứng gần thân cây và nhấn chuột trái để chặt."
                    : "Tiếp tục chặt một cây thông nữa bằng chuột trái."; break;
            case DialogueTask.CollectLogs:
                line = "Đợi cây đổ thành củi. Nhấn F để nhặt đủ 3 khúc gỗ."; break;
            case DialogueTask.StoreLogs:
                line = "Mang gỗ về thùng, nhìn vào thùng rồi nhấn E để cất."; break;
            case DialogueTask.GatherRemainingWood:
                line = "Mình đã biết cách thu thập gỗ rồi. Hãy chặt đủ tổng cộng 3 cây thông và cất đủ 9 khúc gỗ vào kho."; break;
            case DialogueTask.ReturnAxe:
                line = "Đủ gỗ rồi, đã đến lúc nghỉ ngơi. Mang rìu đến kệ và nhấn G để cất."; break;
            case DialogueTask.TakeHearthLogs:
                line = "Chuẩn bị củi cho buổi tối. Nhìn vào kho và nhấn F lấy 3 khúc gỗ."; break;
            case DialogueTask.LoadHearth:
                line = "Mang 3 khúc củi đến lò, nhìn vào lò và nhấn E để cho củi vào."; break;
            case DialogueTask.WaitForNight:
                line = "Mọi thứ đã sẵn sàng. Trời đang tối dần..."; break;
            case DialogueTask.LightFire:
                line = "Đêm đã xuống. Nhìn vào lò và nhấn E để nhóm lửa."; break;
            case DialogueTask.CloseDoors:
                line = "Lửa đã cháy. Đóng cửa thật kỹ trước khi đi ngủ."; break;
            case DialogueTask.Sleep:
                line = "Cửa đã đóng kín. Trở về giường và nhấn E để ngủ."; break;
            case DialogueTask.TakeHammer:
                line = "Hôm nay mình sẽ làm một chiếc ghế gỗ. Đến kệ, nhìn vào búa và nhấn E để lấy."; break;
            case DialogueTask.TakeCraftLogs:
                line = "Nhìn vào kho và nhấn F để lấy 3 khúc gỗ làm ghế."; break;
            case DialogueTask.StackCraftLogs:
                line = "Mang gỗ đến bàn tròn, nhìn vào bàn rồi nhấn E để xếp đủ 3 khúc lên bàn."; break;
            case DialogueTask.HammerChair:
                line = "Gỗ đã đủ. Cầm búa, nhìn vào bàn và nhấn chuột trái để đóng ghế."; break;
            case DialogueTask.PutHammerAway:
                line = "Ghế gỗ đã xong. Mang búa về kệ và nhấn G để cất."; break;
            case DialogueTask.PickUpChair:
                line = "Quay lại bàn, nhìn vào ghế và nhấn E để cầm lên."; break;
            case DialogueTask.PlaceChair:
                line = "Chọn một bề mặt trống, nhìn xuống chỗ muốn đặt rồi nhấn G để đặt ghế."; break;
            default:
                line = "Chiếc ghế đã được đặt vào chỗ. Căn nhà tiện nghi hơn rồi."; break;
        }
        dialogue = line;
        dialogueUntil = Time.time + DialogueDuration;
        taskDialogueShown = true;
    }

    private bool DialogueTaskComplete()
    {
        switch (currentDialogueTask)
        {
            case DialogueTask.LeaveBed:
                foreach (BedRestInteraction bed in FindObjectsByType<BedRestInteraction>())
                    if (bed.IsResting) return false;
                return true;
            case DialogueTask.TakeAxe: return (reticle != null && reticle.IsHoldingAxe) || Progress.TreesCut > 0;
            case DialogueTask.ChopTree: return Progress.TreesCut >= treeGoal;
            case DialogueTask.CollectLogs:
                return Progress.DepositedWood >= depositGoal || (collector != null &&
                    collector.CarriedWoodCount >= Mathf.Min(3, depositGoal - Progress.DepositedWood));
            case DialogueTask.StoreLogs: return Progress.DepositedWood >= depositGoal;
            case DialogueTask.GatherRemainingWood:
                return Progress.TreesCut >= DailyStoryProgress.RequiredTrees &&
                    Progress.DepositedWood >= DailyStoryProgress.RequiredStoredWood;
            case DialogueTask.ReturnAxe: return Progress.AxeReturned;
            case DialogueTask.TakeHearthLogs: return Progress.IsNight || Progress.FireLit || Progress.HearthWood >= 3 ||
                (collector != null && collector.CarriedWoodCount >= 3 - Progress.HearthWood);
            case DialogueTask.LoadHearth: return Progress.HearthWood >= 3 || Progress.IsNight || Progress.FireLit;
            case DialogueTask.WaitForNight: return Progress.IsNight && sunsetStarted && !sunsetRunning;
            case DialogueTask.LightFire: return Progress.FireLit;
            case DialogueTask.CloseDoors: return AreDoorsClosed();
            case DialogueTask.Sleep: return Progress.Day == 2;
            case DialogueTask.TakeHammer: return Progress.FurnitureCrafted || Progress.WorkbenchWood > 0 ||
                (reticle != null && reticle.IsHoldingHammer);
            case DialogueTask.TakeCraftLogs: return Progress.FurnitureCrafted || Progress.WorkbenchWood >= 3 ||
                (collector != null && collector.CarriedWoodCount >= 3 - Progress.WorkbenchWood);
            case DialogueTask.StackCraftLogs: return Progress.WorkbenchWood >= 3 || Progress.FurnitureCrafted;
            case DialogueTask.HammerChair: return Progress.FurnitureCrafted;
            case DialogueTask.PutHammerAway: return reticle != null && reticle.IsHammerStored && !reticle.IsHoldingItem;
            case DialogueTask.PickUpChair: return furniturePlacement != null && furniturePlacement.HasPickedUpChair;
            case DialogueTask.PlaceChair: return furniturePlacement != null && furniturePlacement.HasPlacedChair;
            default: return false;
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
        if (sun != null) sun.intensity = daylightIntensity;
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
        if (lit)
        {
            EnsureHearthRoomLight();
            if (hearthRoomLight != null) hearthLightFade = StartCoroutine(FadeHearthLight());
        }
        else if (hearthRoomLight != null)
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
        while (elapsed < HearthLightFadeSeconds && hearthRoomLight != null)
        {
            elapsed += Time.deltaTime;
            hearthRoomLight.intensity = Mathf.Lerp(start, HearthLightIntensity,
                Mathf.SmoothStep(0f, 1f, elapsed / HearthLightFadeSeconds));
            foreach (Light light in roomFillLights)
                if (light != null) light.intensity = 1.4f * Mathf.SmoothStep(0f, 1f, elapsed / HearthLightFadeSeconds);
            yield return null;
        }
        if (hearthRoomLight != null) hearthRoomLight.intensity = HearthLightIntensity;
        foreach (Light light in roomFillLights) if (light != null) light.intensity = 1.4f;
        hearthLightFade = null;
        // Only this local light changes; the night sky, sun and ambient lighting stay dark.
    }

    private GameObject CreateChair(GameObject prefab)
    {
        Renderer benchRenderer = workbench.GetComponent<Renderer>();
        Vector3 center = benchRenderer != null ? benchRenderer.bounds.center : workbench.transform.position;
        Vector3 towardPlayer = Vector3.ProjectOnPlane(transform.position - center, Vector3.up).normalized;
        if (towardPlayer.sqrMagnitude < 0.1f) towardPlayer = Vector3.forward;
        Vector3 position = center;
        position.y = benchRenderer != null ? benchRenderer.bounds.max.y : position.y;
        GameObject chair = Instantiate(prefab, position, Quaternion.LookRotation(towardPlayer));
        chair.name = "Crafted Wooden Chair";
        Renderer[] renderers = chair.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Destroy(chair); return null; }
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        chair.transform.localScale *= 0.55f / Mathf.Max(bounds.size.y, 0.01f);
        bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        chair.transform.position += Vector3.up * (position.y - bounds.min.y + 0.02f);

        bounds = FurniturePlacement.BoundsOf(chair.transform);
        chair.transform.position += Vector3.up * (position.y - bounds.min.y + 0.02f);
        chair.AddComponent<CraftedFurniture>();
        if (chair.GetComponentInChildren<Collider>() == null)
            foreach (MeshFilter mesh in chair.GetComponentsInChildren<MeshFilter>())
                if (mesh.sharedMesh != null) mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
        return chair;
    }

    public void HammerWorkbench()
    {
        if (IsTransitioning || Progress.Day != 2 || Progress.FurnitureCrafted ||
            Progress.WorkbenchWood < DailyStoryProgress.FurnitureCost || reticle == null ||
            !reticle.IsHoldingHammer || !IsAimingAtObject(workbench)) return;
        craftHits = Mathf.Min(craftHits + 1, CraftRequiredHits);
        craftBarUntil = Time.time + 2f;
        if (craftHits < CraftRequiredHits) return;
        GameObject prefab = Resources.Load<GameObject>("Story/WoodenChair");
        if (prefab == null) { craftHits = CraftRequiredHits - 1; return; }
        GameObject chair = CreateChair(prefab);
        if (chair == null) { craftHits = CraftRequiredHits - 1; return; }
        if (Progress.CraftFurniture(Progress.WorkbenchWood))
        {
            if (workbenchDisplay != null) workbenchDisplay.SetCount(0);
        }
        else Destroy(chair);
    }

    public void SleepToNextDay()
    {
        if (IsTransitioning) return;
        if (Progress.IsNight && !AreDoorsClosed())
        {
            dialogue = "Mình phải đóng cửa thật kỹ trước khi ngủ. Ra đóng cửa rồi hãy quay lại nghỉ ngơi.";
            dialogueUntil = Time.time + DialogueDuration;
            sleepReminderUntil = dialogueUntil;
            return;
        }
        if (!IsTransitioning && Progress.CanSleepToNextDay) StartCoroutine(NextMorning());
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
        dialogueUntil = 0f;
        sleepReminderUntil = 0f;
        // Let the player see the resting view before the overnight blackout.
        yield return new WaitForSecondsRealtime(2f);
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

    private void OnGUI()
    {
        if (collector == null || IsTransitioning) return;
        if (!Progress.FurnitureCrafted && craftHits > 0 && Time.time < craftBarUntil)
        {
            float width = Mathf.Min(280f, Screen.width * 0.5f);
            Rect bar = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.5f + 65f, width, 14f);
            Color previous = GUI.color;
            GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.85f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(0.9f, 0.65f, 0.2f);
            GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 2f,
                (bar.width - 4f) * craftHits / CraftRequiredHits, bar.height - 4f), Texture2D.whiteTexture);
            GUI.color = previous;
        }
        if (!IsTransitioning && Time.time < dialogueUntil && !string.IsNullOrEmpty(dialogue))
        {
            float remaining = dialogueUntil - Time.time;
            float elapsed = DialogueDuration - remaining;
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
            float height = subtitle.CalcHeight(new GUIContent(dialogue), width - 32f) + 24f;
            Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height - 145f - height, width, height);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 12f, width - 32f, height - 24f), dialogue, subtitle);
        }
        if (HasInteractionPrompt)
        {
            int cost = Progress.Day == 1 ? DailyStoryProgress.FirewoodCost : DailyStoryProgress.FurnitureCost;
            int available = Progress.Day == 1 ? Progress.HearthWood : Progress.WorkbenchWood;
            string prompt = available < cost ? "Cần " + cost + " gỗ (" + available + "/" + cost + ")" :
                Progress.Day == 1 ? "E — Nhóm lò (3 gỗ)" : "E — Chế tạo ghế (3 gỗ)";
            if (Target() == woodStorage)
                prompt = collector.IsCarryingWood ? "E — Cất gỗ | F — Lấy thêm (" + collector.CarriedWoodCount + "/3)"
                    : "F — Lấy gỗ (Kho: " + Progress.StoredWood + ")";
            else if (Progress.Day == 1 && Progress.HearthWood < cost)
                prompt = collector.IsCarryingWood ? "E — Quăng gỗ vào lò (" + Progress.HearthWood + "/3)"
                    : "Lò: " + Progress.HearthWood + "/3 gỗ — Lấy gỗ từ thùng";
            else if (Progress.Day == 1 && !Progress.IsNight)
                prompt = !Progress.AxeReturned ? "Cất rìu lên kệ trước khi trời tối"
                    : "Cần chặt đủ 3 cây và cất 9 khúc gỗ trước đêm";
            else if (Progress.Day == 2 && Target() == workbench)
                prompt = available < cost
                    ? collector.IsCarryingWood ? "E — Đặt gỗ lên bàn (" + available + "/" + cost + ")" : "Mang gỗ từ kho ra bàn (" + available + "/" + cost + ")"
                    : reticle != null && reticle.IsHoldingHammer ? "Chuột trái — Dùng búa chế tạo ghế" : "Cần cầm búa để chế tạo ghế";
            if (Target() == toolShelf) prompt = collector.IsCarryingWood
                ? "G — Thả gỗ trước (" + collector.CarriedWoodCount + "/3)"
                : "G — Cất " + reticle.HeldItemTitle.ToLower() + " lên kệ";
            GUIStyle style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            float promptWidth = Mathf.Min(520f, Screen.width - 20f);
            GUI.Box(new Rect((Screen.width - promptWidth) * 0.5f, Screen.height - 90f, promptWidth, 40f), prompt, style);
        }
    }
}
