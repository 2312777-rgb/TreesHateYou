using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CraftedFurniture : MonoBehaviour { }

public class FurniturePlacement : MonoBehaviour
{
    public bool IsHolding => held != null;
    public bool HasPickedUpChair { get; private set; }
    public bool HasPlacedChair { get; private set; }
    public int LastInteractionFrame { get; private set; } = -1;
    public bool HasPickupTarget => FindTarget() != null;
    private Transform held;
    private Vector3 originalScale;
    private Collider[] colliders;
    private bool[] colliderStates;
    private Camera view;
    private DailyStoryController story;
    private WorldItemReticle tools;
    private FirewoodCollector wood;
    private void Start()
    {
        view = GetComponentInChildren<Camera>();
        story = GetComponent<DailyStoryController>();
        tools = GetComponent<WorldItemReticle>();
        wood = GetComponent<FirewoodCollector>();
    }
    private CraftedFurniture FindTarget()
    {
        if (view == null || IsHolding) return null;
        foreach (RaycastHit hit in SortedHits())
        {
            if (hit.transform.IsChildOf(transform)) continue;
            return hit.collider.GetComponentInParent<CraftedFurniture>();
        }
        return null;
    }
    private RaycastHit[] SortedHits()
    {
        RaycastHit[] hits = Physics.RaycastAll(view.ViewportPointToRay(new Vector3(.5f,.5f,0f)), 3f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a,b) => a.distance.CompareTo(b.distance));
        return hits;
    }
    private void Update()
    {
        if (view == null || Cursor.lockState != CursorLockMode.Locked || (story != null && story.IsTransitioning)) return;
#if ENABLE_INPUT_SYSTEM
        bool pickup = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        bool place = Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool pickup = Input.GetKeyDown(KeyCode.E);
        bool place = Input.GetKeyDown(KeyCode.G);
#else
        bool pickup = false, place = false;
#endif
        if (IsHolding) { if (place) TryPlace(); return; }
        CraftedFurniture target = FindTarget();
        if (!pickup || target == null || (tools != null && tools.IsHoldingItem) ||
            (wood != null && (wood.IsCarryingWood || wood.IsThrowing))) return;
        LastInteractionFrame = Time.frameCount;
        held = target.transform;
        HasPickedUpChair = true;
        originalScale = held.lossyScale;
        colliders = held.GetComponentsInChildren<Collider>();
        colliderStates = new bool[colliders.Length];
        for (int i=0;i<colliders.Length;i++) { colliderStates[i]=colliders[i].enabled; colliders[i].enabled=false; }
        Rigidbody body = held.GetComponent<Rigidbody>();
        if (body != null) { body.isKinematic=true; body.useGravity=false; }
        Bounds bounds = BoundsOf(held);
        held.SetParent(view.transform,true);
        held.localRotation=Quaternion.Euler(0f,180f,0f);
        held.localScale *= .65f / Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z,.01f);
        held.position += view.transform.TransformPoint(new Vector3(0f,-.25f,1.1f))-BoundsOf(held).center;
    }
    private void TryPlace()
    {
        RaycastHit? surface = null;
        foreach (RaycastHit hit in SortedHits())
        {
            if (hit.transform.IsChildOf(transform)) continue;
            if (hit.normal.y >= .8f) surface = hit;
            break;
        }
        if (!surface.HasValue) return;
        Vector3 oldPos=held.localPosition, oldScale=held.localScale;
        Quaternion oldRotation=held.localRotation;
        held.SetParent(null,true);
        held.localScale=originalScale;
        held.rotation=Quaternion.Euler(0f,view.transform.eulerAngles.y,0f);
        Bounds bounds=BoundsOf(held);
        held.position += surface.Value.point + Vector3.up*(bounds.extents.y+.025f)-bounds.center;
        bounds=BoundsOf(held);
        bool blocked=false;
        foreach(Collider obstacle in Physics.OverlapBox(bounds.center, Vector3.Max(bounds.extents-Vector3.one*.015f,Vector3.one*.01f),
            Quaternion.identity,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(!obstacle.transform.IsChildOf(transform) && !obstacle.transform.IsChildOf(held)) { blocked=true; break; }
        // Require support at the corners as well, so the chair cannot hang off a table or ledge.
        for(int x=-1;x<=1;x+=2)
        for(int z=-1;z<=1;z+=2)
        {
            Vector3 foot=new Vector3(bounds.center.x+x*bounds.extents.x*.8f,bounds.min.y+.1f,
                bounds.center.z+z*bounds.extents.z*.8f);
            if(!Physics.Raycast(foot,Vector3.down,.25f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) blocked=true;
        }
        if(blocked)
        {
            held.SetParent(view.transform,false); held.localPosition=oldPos; held.localRotation=oldRotation; held.localScale=oldScale;
            return;
        }
        for(int i=0;i<colliders.Length;i++) if(colliders[i]!=null) colliders[i].enabled=colliderStates[i];
        LastInteractionFrame=Time.frameCount;
        held=null;
        HasPlacedChair = true;
    }
    public static Bounds BoundsOf(Transform item)
    {
        Renderer[] renderers=item.GetComponentsInChildren<Renderer>();
        Bounds bounds=new Bounds(item.position,Vector3.zero);
        if(renderers.Length>0) bounds=renderers[0].bounds;
        foreach(Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }
    private void OnGUI()
    {
        if(story!=null && story.IsTransitioning) return;
        if(!IsHolding && !HasPickupTarget) return;
        string prompt=IsHolding ? "G — Đặt ghế lên bề mặt trống" :
            (tools!=null && tools.IsHoldingItem) || (wood!=null && wood.IsCarryingWood)
            ? "Cất dụng cụ và gỗ trước khi cầm ghế" : "E — Cầm ghế gỗ";
        GUI.Box(new Rect((Screen.width-420f)*.5f,Screen.height-90f,420f,40f),prompt,
            new GUIStyle(GUI.skin.box){fontSize=18,alignment=TextAnchor.MiddleCenter});
    }
}
