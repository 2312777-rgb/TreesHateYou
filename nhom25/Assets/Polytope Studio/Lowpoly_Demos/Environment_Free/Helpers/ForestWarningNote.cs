using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ForestWarningNote : MonoBehaviour
{
    private const string Message = "Bạn đã làm khu rừng nổi giận.\nBây giờ khu rừng sẽ phái hộ vệ\ntới tìm kiếm và giết bạn.";
    private Transform note;
    private Camera view;
    private Collider noteCollider;
    private Vector3 restingPosition;
    private Quaternion restingRotation;
    private bool held;
    private bool reading;
    public bool IsReading => reading;
    public bool HasLoweredAfterReading { get; private set; }
    public bool IsOnTable { get; private set; }
    private Vector3 readingPosition;
    private readonly System.Collections.Generic.List<Renderer> hiddenToolRenderers = new();
    private Material engravingMaterial;
    public bool IsHeld => held;
    public bool IsNearEntranceNote => note != null && !held &&
        (note.position - entranceNotePosition).sqrMagnitude < 0.01f &&
        (transform.position - note.position).sqrMagnitude <= 9f;
    private Vector3 entranceNotePosition;
    public int LastPlacementFrame { get; private set; } = -1;

    private void Start()
    {
        view = GetComponentInChildren<Camera>();
        GameObject house = GameObject.Find("medieval_house_lite_v2");
        if (house == null || view == null) return;
        LumberjackDoorInteract entrance = null;
        float nearest = float.PositiveInfinity;
        foreach (LumberjackDoorInteract door in FindObjectsByType<LumberjackDoorInteract>())
        {
            float distance = (door.transform.position - house.transform.position).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            entrance = door;
        }
        if (entrance == null) return;
        Renderer doorRenderer = entrance.GetComponent<Renderer>();
        Vector3 center = doorRenderer != null ? doorRenderer.bounds.center : entrance.transform.position;
        Vector3 outside = Vector3.ProjectOnPlane(center - house.transform.position, Vector3.up).normalized;
        if (outside == Vector3.zero) outside = entrance.transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, outside);
        Vector3 spot = center + outside * 0.85f + right * 0.65f;
        if (Physics.Raycast(spot + Vector3.up * 0.25f, Vector3.down, out RaycastHit ground,
            4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            spot.y = ground.point.y + 0.09f;
        restingPosition = entranceNotePosition = spot;
        restingRotation = Quaternion.LookRotation(outside) * Quaternion.Euler(72f, 0f, 10f);
        GameObject bark = new GameObject("Forest Warning Bark");
        note = bark.transform;
        note.localScale = Vector3.one * 1.5f;
        Mesh mesh = CreateBarkMesh();
        bark.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer surface = bark.AddComponent<MeshRenderer>();
        GameObject woodPrefab = Resources.Load<GameObject>("Chopping/Firewood");
        Renderer wood = woodPrefab != null ? woodPrefab.GetComponentInChildren<Renderer>() : null;
        if (wood != null)
        {
            Material[] materials = wood.sharedMaterials;
            surface.sharedMaterial = System.Array.Find(materials, material =>
                material != null && material.name.ToLowerInvariant().Contains("bark")) ?? wood.sharedMaterial;
        }
        BoxCollider bounds = bark.AddComponent<BoxCollider>();
        bounds.center = mesh.bounds.center;
        bounds.size = mesh.bounds.size;
        noteCollider = bounds;

        GameObject engraving = new GameObject("Engraved warning");
        engraving.transform.SetParent(note, false);
        engraving.transform.localPosition = new Vector3(0f, 0.014f, 0f);
        TextMesh letters = engraving.AddComponent<TextMesh>();
        letters.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Shader engravingShader = Resources.Load<Shader>("BarkEngraving");
        if (engravingShader != null)
        {
            engravingMaterial = new Material(engravingShader);
            engravingMaterial.mainTexture = letters.font.material.mainTexture;
            letters.GetComponent<MeshRenderer>().sharedMaterial = engravingMaterial;
        }
        else letters.GetComponent<MeshRenderer>().sharedMaterial = letters.font.material;
        letters.fontSize = 64;
        letters.fontStyle = FontStyle.Bold;
        letters.characterSize = 0.02f;
        letters.anchor = TextAnchor.MiddleCenter;
        letters.alignment = TextAlignment.Center;
        letters.color = new Color(0.84f, 0.65f, 0.39f);
        letters.text = Message;
        Vector3 textSize = letters.GetComponent<Renderer>().bounds.size;
        engraving.transform.localScale = Vector3.one * Mathf.Min(
            0.42f / Mathf.Max(textSize.x, 0.001f), 0.24f / Mathf.Max(textSize.y, 0.001f));
        engraving.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        note.SetPositionAndRotation(restingPosition, restingRotation);
    }

    private static Mesh CreateBarkMesh()
    {
        Vector2[] edge =
        {
            new Vector2(-.26f,-.14f), new Vector2(-.16f,-.19f), new Vector2(-.05f,-.17f),
            new Vector2(.09f,-.20f), new Vector2(.16f,-.16f), new Vector2(.25f,-.14f),
            new Vector2(.23f,-.03f), new Vector2(.27f,.06f), new Vector2(.22f,.17f),
            new Vector2(.10f,.18f), new Vector2(.01f,.16f), new Vector2(-.10f,.20f),
            new Vector2(-.23f,.16f), new Vector2(-.25f,.05f), new Vector2(-.22f,-.03f)
        };
        int count = edge.Length;
        Vector3[] points = new Vector3[count * 2 + 2];
        Vector2[] uv = new Vector2[points.Length];
        var triangles = new System.Collections.Generic.List<int>();
        for (int i = 0; i < count; i++)
        {
            Vector2 p = edge[i];
            points[i] = new Vector3(p.x, Mathf.Sin(i * 2.4f) * .009f, p.y);
            points[i + count] = new Vector3(p.x * .97f, -.022f + Mathf.Cos(i * 1.8f) * .007f, p.y * .97f);
            int next = (i + 1) % count;
            triangles.AddRange(new[] { count * 2, next, i, count * 2 + 1, i + count, next + count,
                i, next, next + count, i, next + count, i + count });
        }
        points[count * 2] = Vector3.up * .012f;
        points[count * 2 + 1] = Vector3.down * .024f;
        for (int i = 0; i < points.Length; i++)
            uv[i] = new Vector2(points[i].x / .55f + .5f, points[i].z / .4f + .5f);
        Mesh mesh = new Mesh { name = "Jagged bark fragment", vertices = points, uv = uv, triangles = triangles.ToArray() };
        mesh.RecalculateNormals();
        return mesh;
    }
    private bool IsAimedAt()
    {
        return note != null && view != null && Physics.Raycast(
            view.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out RaycastHit hit,
            2.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.collider == noteCollider;
    }

    private void Update()
    {
        if (note == null || Cursor.lockState != CursorLockMode.Locked) return;
#if ENABLE_INPUT_SYSTEM
        bool pressed = Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool pressed = Input.GetKeyDown(KeyCode.F);
#else
        bool pressed = false;
#endif
        if (reading && (transform.position - readingPosition).sqrMagnitude > 0.0025f)
        {
            EndReading();
            HasLoweredAfterReading = true;
            note.localPosition = new Vector3(-0.34f, -0.25f, 0.82f);
            note.localRotation = Quaternion.Euler(-58f, 12f, -10f);
        }

        if (!pressed) return;
        if (held) { TryPlace(); return; }
        if (!IsAimedAt()) return;
        FirewoodCollector wood = GetComponent<FirewoodCollector>();
        FurniturePlacement furniture = GetComponent<FurniturePlacement>();
        if ((wood != null && wood.IsThrowing) ||
            (furniture != null && furniture.IsHolding)) return;
        held = true;
        IsOnTable = false;
        reading = true;
        readingPosition = transform.position;
        GetComponent<WorldItemReticle>()?.HideHeldToolForReading(hiddenToolRenderers);
        noteCollider.enabled = false;
        note.SetParent(view.transform, true);
        note.localScale = Vector3.one * 1.3f;
        note.localPosition = new Vector3(0f, 0f, 0.82f);
        note.localRotation = Quaternion.Euler(-72f, 0f, 0f);
    }

    private void TryPlace()
    {
        Ray ray = view.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit[] hits = Physics.RaycastAll(ray, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.IsChildOf(transform)) continue;
            Vector3 alongSurface = Vector3.ProjectOnPlane(view.transform.right, hit.normal);
            if (alongSurface.sqrMagnitude < 0.001f)
                alongSurface = Vector3.ProjectOnPlane(view.transform.up, hit.normal);
            restingRotation = Quaternion.LookRotation(Vector3.Cross(alongSurface, hit.normal), hit.normal);
            restingPosition = hit.point + hit.normal * (0.035f);
            GameObject table = GameObject.Find("Lumberjack_Interior_Table");
            IsOnTable = table != null && hit.transform.IsChildOf(table.transform) && hit.normal.y > 0.7f;
            PutBack();
            LastPlacementFrame = Time.frameCount;
            return;
        }
    }

    private void PutBack()
    {
        EndReading();
        held = false;
        note.SetParent(null, true);
        note.localScale *= 1.5f;
        note.SetPositionAndRotation(restingPosition, restingRotation);
        noteCollider.enabled = true;
    }

    private void EndReading()
    {
        if (reading && note != null) note.localScale = Vector3.one;
        reading = false;
        foreach (Renderer renderer in hiddenToolRenderers)
            if (renderer != null) renderer.forceRenderingOff = false;
        hiddenToolRenderers.Clear();
    }

    private void OnDestroy()
    {
        EndReading();
        if (engravingMaterial != null) Destroy(engravingMaterial);
        if (note != null) Destroy(note.gameObject);
    }
}
