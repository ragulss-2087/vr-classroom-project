using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Adds walk and computer controls during Play mode only. No scene placements are saved or changed.
/// </summary>
public sealed class ClassroomPlayModeController : MonoBehaviour
{
    private const float ClosedDoorZDegrees = 0.537f;
    private const float WalkSpeed = 3.2f;
    private const float MouseSensitivity = 2.2f;
    private const float PlayerHeight = 1.7f;
    private const float PlayerRadius = 0.28f;
    private const float CameraHeight = 1.58f;
    private const float WallMargin = 0.45f;
    private const float ComputerUseDistance = 2.5f;
    private const float Student3TravelSpeed = 1.25f;
    private const float Student3TurnSpeed = 120f;
    // Walking.fbx is a 38-frame (~1.27 s) walk cycle. This speed makes each
    // 4.35 m leg take about four complete cycles, keeping the gait in step.
    private const float WalkingCharacterSpeed = 0.86f;
    private static readonly Vector3 WalkingStart = new Vector3(0.657999992f, 0.328000009f, 2.83800006f);
    private static readonly Vector3 WalkingEnd = new Vector3(0.657999992f, 0.328000009f, -1.50999999f);

    private Transform door;
    private Renderer[] doorRenderers;
    private Camera sceneCamera;
    private Camera secondDisplayCamera;
    private Bounds roomBounds;
    private CharacterController player;
    private Transform originalCameraParent;
    private Vector3 originalCameraLocalPosition;
    private Quaternion originalCameraLocalRotation;
    private Vector3 originalCameraLocalScale;
    private Transform[] computers = new Transform[0];
    private readonly List<Collider> runtimeColliders = new List<Collider>();
    private Transform currentLookTarget;
    private float currentLookDistance = float.PositiveInfinity;
    private Transform student3;
    private Transform walkingCharacter;
    private Quaternion walkingBaseRotation;
    private bool walkingTowardEnd = true;
    private Quaternion student3BaseRotation;
    private readonly Vector3[] student3Route =
    {
        new Vector3(0.779f, 1.057f, 0.94f),
        new Vector3(0.779f, 1.06f, -3.722f),
        new Vector3(-2.311f, 1.06f, -3.722f),
        new Vector3(-2.311f, 1.06f, 1.629f)
    };
    private int student3Waypoint = 1;
    private float student3Heading;
    private float yaw;
    private float pitch;
    private bool cameraWasReparented;
    private bool computerOpen;
    private string activeComputerName;
    private string computerPage = "Desktop";

    private void Awake()
    {
        sceneCamera = Camera.main;
        InitializeRoomBounds();
        InitializeComputers();
        student3 = GameObject.Find("Student3")?.transform;
        if (student3 != null)
            student3BaseRotation = student3.localRotation;

        walkingCharacter = GameObject.Find("walking")?.transform;
        if (walkingCharacter != null)
        {
            walkingCharacter.localPosition = WalkingStart;
            walkingBaseRotation = walkingCharacter.localRotation;
        }

        var doorObject = GameObject.Find("Door");
        if (doorObject != null)
        {
            door = doorObject.transform;
            doorRenderers = doorObject.GetComponentsInChildren<Renderer>(true);
        }
    }

    private void Start()
    {
        KeepSingleAudioListener();
        AddRuntimeAssetColliders();
        InitializeWalkRig();
        InitializeSecondDisplay();
        LockCursor();
    }

    private void Update()
    {
        UpdateStudent3Route();
        UpdateWalkingCharacterRoute();
        if (sceneCamera == null || player == null)
            return;

        if (computerOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                CloseComputer();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            UnlockCursor();
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (Input.GetMouseButtonDown(0))
                LockCursor();
            return;
        }

        LookAround();
        MovePlayer();
        currentLookTarget = FindLookedAtComputer(out currentLookDistance);

        if (Input.GetKeyDown(KeyCode.E))
            TryUseComputer(currentLookTarget, currentLookDistance);

        if (Input.GetMouseButtonDown(0))
            CloseDoorIfClicked();
    }

    private void UpdateWalkingCharacterRoute()
    {
        if (walkingCharacter == null)
            return;

        var targetPosition = walkingTowardEnd ? WalkingEnd : WalkingStart;
        walkingCharacter.localPosition = Vector3.MoveTowards(walkingCharacter.localPosition,
            targetPosition, WalkingCharacterSpeed * Time.deltaTime);

        var heading = walkingTowardEnd ? 0f : 180f;
        var targetRotation = Quaternion.Euler(0f, heading, 0f) * walkingBaseRotation;
        // Turn at the route endpoint. Rotating while translating makes the
        // character appear to slide sideways during the turnaround.
        walkingCharacter.localRotation = targetRotation;

        if ((walkingCharacter.localPosition - targetPosition).sqrMagnitude < 0.0001f)
            walkingTowardEnd = !walkingTowardEnd;
    }

    private void UpdateStudent3Route()
    {
        if (student3 == null || student3Waypoint >= student3Route.Length)
            return;

        var targetPosition = student3Route[student3Waypoint];
        student3.localPosition = Vector3.MoveTowards(student3.localPosition, targetPosition,
            Student3TravelSpeed * Time.deltaTime);

        if ((student3.localPosition - targetPosition).sqrMagnitude > 0.0001f)
            return;

        if (student3Waypoint == 1)
            student3Heading = 90f;
        else if (student3Waypoint == 2)
            student3Heading = 180f;

        var targetRotation = Quaternion.Euler(0f, student3Heading, 0f) * student3BaseRotation;
        student3.localRotation = Quaternion.RotateTowards(student3.localRotation, targetRotation,
            Student3TurnSpeed * Time.deltaTime);

        if (Quaternion.Angle(student3.localRotation, targetRotation) < 0.1f)
        {
            if (student3Waypoint < student3Route.Length - 1)
                student3Waypoint++;
            else
                student3Waypoint = student3Route.Length;
        }
    }

    private void LateUpdate()
    {
        if (sceneCamera != null && secondDisplayCamera != null)
            secondDisplayCamera.transform.SetPositionAndRotation(sceneCamera.transform.position,
                sceneCamera.transform.rotation);
    }

    private void OnDisable()
    {
        UnlockCursor();
        RestoreCamera();
        if (secondDisplayCamera != null)
            Destroy(secondDisplayCamera.gameObject);
        for (var i = 0; i < runtimeColliders.Count; i++)
            if (runtimeColliders[i] != null)
                Destroy(runtimeColliders[i]);
        runtimeColliders.Clear();
    }

    private void InitializeRoomBounds()
    {
        var floor = GameObject.Find("Floor");
        var renderers = floor != null ? floor.GetComponentsInChildren<Renderer>(true) : null;
        if (renderers != null && renderers.Length > 0)
        {
            roomBounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                roomBounds.Encapsulate(renderers[i].bounds);
        }
        else
        {
            roomBounds = new Bounds(Vector3.zero, new Vector3(10f, 0f, 10f));
        }
    }

    private void InitializeComputers()
    {
        var allTransforms = FindObjectsOfType<Transform>(true);
        var found = new List<Transform>();
        for (var i = 0; i < allTransforms.Length; i++)
        {
            var name = allTransforms[i].name.ToLowerInvariant();
            if (name.StartsWith("full pc with chair") || name == "room pc")
                found.Add(allTransforms[i]);
        }

        if (found.Count == 0)
        {
            for (var i = 0; i < allTransforms.Length; i++)
                if (allTransforms[i].name.ToLowerInvariant().StartsWith("computer table"))
                    found.Add(allTransforms[i]);
        }
        computers = found.ToArray();
    }

    private void InitializeWalkRig()
    {
        if (sceneCamera == null)
            return;

        var cameraTransform = sceneCamera.transform;
        originalCameraParent = cameraTransform.parent;
        originalCameraLocalPosition = cameraTransform.localPosition;
        originalCameraLocalRotation = cameraTransform.localRotation;
        originalCameraLocalScale = cameraTransform.localScale;
        yaw = cameraTransform.eulerAngles.y;

        var floorY = roomBounds.min.y;
        var spawn = cameraTransform.position;
        spawn.y = floorY + 0.05f;
        spawn.x = ClampInside(spawn.x, roomBounds.min.x, roomBounds.max.x);
        spawn.z = ClampInside(spawn.z, roomBounds.min.z, roomBounds.max.z);

        var playerObject = new GameObject("Classroom Walk Player (Play Mode)");
        playerObject.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0f, yaw, 0f));
        player = playerObject.AddComponent<CharacterController>();
        player.height = PlayerHeight;
        player.radius = PlayerRadius;
        player.center = Vector3.up * (PlayerHeight * 0.5f);
        player.stepOffset = 0.3f;
        player.slopeLimit = 45f;
        player.skinWidth = 0.06f;
        player.minMoveDistance = 0f;

        cameraTransform.SetParent(playerObject.transform, false);
        cameraTransform.localPosition = Vector3.up * CameraHeight;
        cameraTransform.localRotation = Quaternion.Euler(0f, 0f, 0f);
        cameraTransform.localScale = Vector3.one;
        cameraWasReparented = true;
    }

    private void InitializeSecondDisplay()
    {
        if (sceneCamera == null || Display.displays.Length < 2)
            return;

        Display.displays[1].Activate();
        var displayCameraObject = new GameObject("Classroom Display 2 Camera (Play Mode)");
        displayCameraObject.transform.SetPositionAndRotation(sceneCamera.transform.position,
            sceneCamera.transform.rotation);
        secondDisplayCamera = displayCameraObject.AddComponent<Camera>();
        secondDisplayCamera.CopyFrom(sceneCamera);
        secondDisplayCamera.targetDisplay = 1;
        secondDisplayCamera.rect = new Rect(0f, 0f, 1f, 1f);
        secondDisplayCamera.enabled = true;
    }

    private void LookAround()
    {
        yaw += Input.GetAxis("Mouse X") * MouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * MouseSensitivity;
        pitch = Mathf.Clamp(pitch, -75f, 75f);
        player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        sceneCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void MovePlayer()
    {
        var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude > 1f)
            input.Normalize();

        var move = player.transform.right * input.x + player.transform.forward * input.y;
        player.Move(move * WalkSpeed * Time.deltaTime);

        var position = player.transform.position;
        position.x = ClampInside(position.x, roomBounds.min.x, roomBounds.max.x);
        position.z = ClampInside(position.z, roomBounds.min.z, roomBounds.max.z);
        // Keep the capsule at the classroom floor height while the mesh colliders block furniture and walls.
        position.y = roomBounds.min.y + 0.05f;
        player.transform.position = position;
    }

    private Transform FindLookedAtComputer(out float distance)
    {
        distance = float.PositiveInfinity;
        if (sceneCamera == null)
            return null;

        var ray = sceneCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out var hit, ComputerUseDistance))
            return null;

        distance = hit.distance;
        for (var i = 0; i < computers.Length; i++)
        {
            var computer = computers[i];
            if (computer != null && (hit.transform == computer || hit.transform.IsChildOf(computer)))
                return computer;
        }
        return null;
    }

    private void AddRuntimeAssetColliders()
    {
        // Lightweight bounds colliders cover both regular and skinned assets only during Play mode.
        var renderers = FindObjectsOfType<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || !renderer.gameObject.activeInHierarchy ||
                IsUnderAddedStudents(renderer.transform) ||
                renderer.GetComponent<Collider>() != null)
                continue;

            var collider = renderer.gameObject.AddComponent<BoxCollider>();
            SetBoxToWorldBounds(collider, renderer.bounds);
            runtimeColliders.Add(collider);
        }
    }

    private static bool IsUnderAddedStudents(Transform t)
    {
        while (t != null)
        {
            if (string.Equals(t.name, "Added Students", System.StringComparison.OrdinalIgnoreCase))
                return true;
            t = t.parent;
        }
        return false;
    }

    private static void SetBoxToWorldBounds(BoxCollider collider, Bounds worldBounds)
    {
        var t = collider.transform;
        var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
        {
            var corner = worldBounds.center + Vector3.Scale(worldBounds.extents, new Vector3(x, y, z));
            var local = t.InverseTransformPoint(corner);
            min = Vector3.Min(min, local);
            max = Vector3.Max(max, local);
        }
        collider.center = (min + max) * 0.5f;
        collider.size = Vector3.Max(max - min, Vector3.one * 0.02f);
    }

    private void TryUseComputer(Transform target, float distance)
    {
        if (target == null || distance > ComputerUseDistance)
            return;

        activeComputerName = target.name;
        computerPage = "Desktop";
        computerOpen = true;
        UnlockCursor();
    }

    private void CloseComputer()
    {
        computerOpen = false;
        LockCursor();
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || sceneCamera == null)
            return;

        if (computerOpen)
        {
            DrawComputerPanel();
            return;
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            var crosshair = new Rect(Screen.width * 0.5f - 3f, Screen.height * 0.5f - 3f, 6f, 6f);
            GUI.color = Color.white;
            GUI.Box(crosshair, GUIContent.none);
            GUI.color = Color.white;

            if (currentLookTarget != null && currentLookDistance <= ComputerUseDistance)
                GUI.Label(new Rect(12f, Screen.height - 42f, Screen.width - 24f, 28f),
                    "Press E to use " + currentLookTarget.name);

            GUI.Label(new Rect(12f, 12f, Screen.width - 24f, 28f),
                "WASD: move   Mouse: look   E: use computer   Esc: release mouse");
        }
        else
        {
            GUI.Label(new Rect(12f, Screen.height - 42f, Screen.width - 24f, 28f),
                "Click the Game view to resume walking.");
        }
    }

    private void DrawComputerPanel()
    {
        var width = Mathf.Min(620f, Screen.width - 40f);
        var height = Mathf.Min(420f, Screen.height - 40f);
        var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f,
            width, height);
        GUI.Box(panel, "Classroom Computer — " + activeComputerName);
        GUI.Label(new Rect(panel.x + 24f, panel.y + 42f, width - 48f, 30f), computerPage);

        if (computerPage == "Desktop")
        {
            GUI.Label(new Rect(panel.x + 24f, panel.y + 82f, width - 48f, 55f),
                "Computer is ready. Choose an option:");
            if (GUI.Button(new Rect(panel.x + 24f, panel.y + 150f, width - 48f, 46f), "Classroom Notes"))
                computerPage = "Classroom Notes";
            if (GUI.Button(new Rect(panel.x + 24f, panel.y + 208f, width - 48f, 46f), "Student Workstation"))
                computerPage = "Student Workstation";
        }
        else if (computerPage == "Classroom Notes")
        {
            GUI.Label(new Rect(panel.x + 24f, panel.y + 82f, width - 48f, 120f),
                "Classroom notes are open. Use the Escape key or return button to close this computer.");
            if (GUI.Button(new Rect(panel.x + 24f, panel.y + 208f, width - 48f, 46f), "Back to Desktop"))
                computerPage = "Desktop";
        }
        else
        {
            GUI.Label(new Rect(panel.x + 24f, panel.y + 82f, width - 48f, 120f),
                "This workstation is active. You can return to the desktop or close it.");
            if (GUI.Button(new Rect(panel.x + 24f, panel.y + 208f, width - 48f, 46f), "Back to Desktop"))
                computerPage = "Desktop";
        }

        if (GUI.Button(new Rect(panel.x + 24f, panel.y + height - 66f, width - 48f, 42f), "Close Computer"))
            CloseComputer();
    }

    private void CloseDoorIfClicked()
    {
        if (door == null || doorRenderers == null || doorRenderers.Length == 0 || sceneCamera == null)
            return;

        var ray = sceneCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        var closestDistance = float.PositiveInfinity;
        var hitDoor = false;
        for (var i = 0; i < doorRenderers.Length; i++)
        {
            var renderer = doorRenderers[i];
            if (renderer == null || !renderer.enabled || !renderer.bounds.IntersectRay(ray, out var distance))
                continue;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                hitDoor = true;
            }
        }

        if (!hitDoor)
            return;

        var euler = door.localEulerAngles;
        euler.z = ClosedDoorZDegrees;
        door.localEulerAngles = euler;
    }

    private void KeepSingleAudioListener()
    {
        var listeners = FindObjectsOfType<AudioListener>(true);
        if (listeners.Length < 2)
            return;

        var activeListener = sceneCamera != null ? sceneCamera.GetComponent<AudioListener>() : null;
        if (activeListener == null || !activeListener.enabled || !activeListener.gameObject.activeInHierarchy)
        {
            activeListener = null;
            for (var i = 0; i < listeners.Length; i++)
            {
                if (listeners[i].enabled && listeners[i].gameObject.activeInHierarchy)
                {
                    activeListener = listeners[i];
                    break;
                }
            }
        }

        if (activeListener == null)
            return;

        for (var i = 0; i < listeners.Length; i++)
            if (listeners[i] != activeListener)
                listeners[i].enabled = false;
    }

    private void RestoreCamera()
    {
        if (!cameraWasReparented || sceneCamera == null)
            return;

        sceneCamera.transform.SetParent(originalCameraParent, false);
        sceneCamera.transform.localPosition = originalCameraLocalPosition;
        sceneCamera.transform.localRotation = originalCameraLocalRotation;
        sceneCamera.transform.localScale = originalCameraLocalScale;
        cameraWasReparented = false;
    }

    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private static float ClampInside(float value, float minimum, float maximum)
    {
        if (maximum - minimum <= WallMargin * 2f)
            return (minimum + maximum) * 0.5f;
        return Mathf.Clamp(value, minimum + WallMargin, maximum - WallMargin);
    }
}
