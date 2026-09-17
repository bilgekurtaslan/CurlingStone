using UnityEngine;

[ExecuteAlways]
public class KickBoardBuilder : MonoBehaviour
{
    [Header("Board Size (world units)")]
    [SerializeField] private float length = 30f;
    [SerializeField] private float boardHeight = 1f;
    [SerializeField] private float panelThickness = 0.12f;
    [SerializeField] private float railThickness = 0.18f;
    [SerializeField] private float railHeight = 0.24f;

    [Header("Materials")]
    [SerializeField] private Material panelMaterial;
    [SerializeField] private Material trimMaterial;

    private const string PanelName = "Panel";
    private const string RailName = "TopRail";

    private void Awake()
    {
        Build();
    }

    private void OnValidate()
    {
        Build();
    }

    private void Build()
    {
        BuildPart(
            PanelName,
            PrimitiveType.Cube,
            new Vector3(0f, (boardHeight - railHeight) * 0.5f, 0f),
            Quaternion.identity,
            new Vector3(panelThickness, boardHeight - railHeight, length),
            panelMaterial
        );

        float radius = railThickness * 0.5f;
        float capsuleRadiusScale = radius / 0.5f;
        float capsuleLengthScale = Mathf.Max(0.02f, (length - railThickness) / 2f);

        BuildPart(
            RailName,
            PrimitiveType.Capsule,
            new Vector3(0f, boardHeight - radius, 0f),
            Quaternion.Euler(90f, 0f, 0f),
            new Vector3(capsuleRadiusScale, capsuleLengthScale, capsuleRadiusScale),
            trimMaterial
        );
    }

    private void BuildPart(
        string partName,
        PrimitiveType type,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        Material material)
    {
        Transform existing = transform.Find(partName);

        GameObject part =
            existing != null
                ? existing.gameObject
                : GameObject.CreatePrimitive(type);

        part.name = partName;
        part.transform.SetParent(transform, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = localScale;

        Collider partCollider = part.GetComponent<Collider>();

        if (partCollider != null)
        {
            DestroyPartCollider(partCollider);
        }

        MeshRenderer renderer = part.GetComponent<MeshRenderer>();

        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    private static void DestroyPartCollider(Collider partCollider)
    {
        if (Application.isPlaying)
        {
            Destroy(partCollider);
        }
        else
        {
            DestroyImmediate(partCollider);
        }
    }
}
