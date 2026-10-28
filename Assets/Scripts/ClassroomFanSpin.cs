using UnityEngine;

public sealed class ClassroomFanSpin : MonoBehaviour
{
    [SerializeField] private float rotationsPerMinute = 240f;
    [SerializeField] private string[] bladeNamePrefixes = { "Wing", "Box" };

    private Transform[] blades;
    private Quaternion[] startingRotations;
    private float spinAngle;

    private void Awake()
    {
        var found = new System.Collections.Generic.List<Transform>();
        for (var fanIndex = 0; fanIndex < transform.childCount; fanIndex++)
        {
            var fan = transform.GetChild(fanIndex);
            for (var partIndex = 0; partIndex < fan.childCount; partIndex++)
            {
                var part = fan.GetChild(partIndex);
                if (!IsBlade(part)) continue;
                found.Add(part);
            }
        }

        blades = found.ToArray();
        startingRotations = new Quaternion[blades.Length];
        for (var i = 0; i < blades.Length; i++)
            startingRotations[i] = blades[i].localRotation;

        if (blades.Length == 0)
            Debug.LogWarning("No fan wings were found under the Fan object.", this);
    }

    private void Update()
    {
        spinAngle = Mathf.Repeat(spinAngle + rotationsPerMinute * 6f * Time.deltaTime, 360f);
        var spin = Quaternion.AngleAxis(spinAngle, Vector3.forward);
        for (var i = 0; i < blades.Length; i++)
            blades[i].localRotation = spin * startingRotations[i];
    }

    private bool IsBlade(Transform part)
    {
        if (part.GetComponent<MeshRenderer>() == null) return false;
        for (var i = 0; i < bladeNamePrefixes.Length; i++)
        {
            if (part.name.StartsWith(bladeNamePrefixes[i], System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
