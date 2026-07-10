using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;

public enum BciRunMode
{
    OnlineGame,
    Calibration
}

public enum BciInteractionMode
{
    Game,
    Test
}

public enum BciTestDisplayMode
{
    OfflineNTrain,
    OnlineNTrain,
    ZeroTrain
}

public class BciGameTrialManager : MonoBehaviour
{
    private const int UnityTargetCount = 16;

    public CvepTileFlasher flasher;
    public UdpMarkerSender markerSender;
    public UdpDecoderReceiver decoderReceiver;
    public DestinationCarMover carMover;

    public Camera selectionCamera;
    public Camera driveCamera;
    public Transform driveCameraTarget;
    public Vector3 driveCameraOffset = new Vector3(0f, 4.2f, -7.5f);
    public Vector3 driveCameraLookOffset = new Vector3(0f, 1.2f, 2.5f);
    public float driveCameraFollowSpeed = 6f;
    public float driveCameraRotateSpeed = 8f;
    public bool useViewTransition = true;
    public float viewTransitionSeconds = 0.8f;

    public Renderer groundRenderer;
    public Renderer[] viewSwitchTileRenderers;
    public Material groundSelectionMaterial;
    public Material groundDriveMaterial;
    public Material tileSelectionMaterial;
    public Material tileDriveMaterial;

    public bool showTileNumberLabels = true;
    public Color tileNumberLabelColor = Color.white;
    public float tileNumberLabelFontSize = 14f;
    public float tileNumberLabelHeight = 0.08f;
    public float minimumTileNumberLabelFontSize = 14f;

    public TMP_Text statusText;
    [HideInInspector]
    public BciRunMode runMode = BciRunMode.OnlineGame;
    public BciInteractionMode interactionMode = BciInteractionMode.Game;
    [HideInInspector]
    public BciTestDisplayMode testDisplayMode = BciTestDisplayMode.ZeroTrain;
    public TMP_Text testOverlayText;
    public Color testOverlayTextColor = Color.black;
    public int testOverlayFontSize = 48;
    public Vector2 testOverlayOffset = new Vector2(32f, -72f);
    public string waitingText = "Waiting...";
    public string pressStartText = "Press C to start";
    public string recognizingText = "Start recognizing the target";
    public string movingText = "Moving to the destination";
    public string finishedText = "Finished";

    public int totalTrials = 16;
    public int calibrationTrials = 16;
    public bool forceSixteenTargetLayout = true;
    public float firstReadySeconds = 5f;
    public float interTrialSeconds = 2f;
    public float cueSeconds = 0.7f;
    public float postCueBlinkSeconds = 1.5f;
    public float trialDurationSeconds = 8.4f;
    public bool useZeroTrainingWarmUp = true;
    public float zeroTrainingWarmUpSeconds = 12f;
    public float decoderWaitSeconds = 2f;
    public float feedbackSeconds = 0.7f;
    public float postArrivalDriveViewSeconds = 1f;
    public bool enableKeyboardDebug = true;
    public bool useCue = true;
    public bool useBalancedRandomCueTargets = true;
    public int randomSeed = -1;
    public int[] cueTargets = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
    public bool enableOnlineAccuracyPlot = true;
    public float accuracyPlotDelaySeconds = 3f;
    public string accuracyModeLabel = "Zero-training";
    public string accuracyOutputDirectory = "/Users/wang/dp-cvep-1/cvep_speller_env/data/online_accuracy";
    public int[] onlineAccuracyTrueTargets = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

    public string startTrialMarker = "start_trial";
    public string stopTrialMarker = "stop_trial";
    public string cueStartMarker = "start_cue";
    public string cueStopMarker = "stop_cue";
    public string forceDecisionMarker = "force_decision";

    private bool isRunning = false;
    private bool isDebugMoving = false;
    private bool canStart = false;
    private int[] runtimeCueTargets;
    private TextMeshProUGUI[] tileNumberLabels;
    private Canvas tileNumberLabelCanvas;
    private TMP_Text runtimeTestOverlayText;
    private string testOverlayDetailText = "";
    private bool isSelectionViewActive = true;
    private readonly List<AccuracyTrial> onlineAccuracyRows = new List<AccuracyTrial>();

    private struct AccuracyTrial
    {
        public int trialId;
        public int trueTarget;
        public int predictedTarget;
        public bool hasPrediction;

        public bool IsCorrect
        {
            get { return trueTarget >= 0 && hasPrediction && trueTarget == predictedTarget; }
        }
    }

    void Start()
    {
        if (decoderReceiver == null)
        {
            decoderReceiver = FindObjectOfType<UdpDecoderReceiver>();
        }

        if (decoderReceiver == null)
        {
            Debug.LogError("[CHECK] No UdpDecoderReceiver found. Online trials cannot receive decoder outputs.");
        }

        ApplySixteenTargetDefaults();
        CreateTileNumberLabels();
        ConfigureStatusTextLayout();
        ApplyInteractionModeVisibility();
        SetSelectionView();
        UpdateTestOverlay();
        canStart = false;
        SetStatusText(waitingText);
    }

    public void PrepareForMode(BciRunMode mode)
    {
        PrepareForMode(mode, testDisplayMode);
    }

    public void PrepareForMode(BciRunMode mode, BciTestDisplayMode displayMode)
    {
        if (isRunning)
        {
            Debug.LogWarning("Cannot switch Unity BCI mode while a run is active.");
            return;
        }

        StopAllCoroutines();
        runMode = mode;
        testDisplayMode = displayMode;
        canStart = false;
        runtimeCueTargets = null;
        ApplySixteenTargetDefaults();
        CreateTileNumberLabels();
        ConfigureStatusTextLayout();
        ApplyInteractionModeVisibility();
        flasher.ClearHighlight();
        SetSelectionView();
        UpdateTestOverlay();
        StartCoroutine(ShowStartPrompt());
        Debug.Log("Prepared Unity BCI mode: " + runMode + " / " + testDisplayMode);
    }

    void Update()
    {
        if (canStart && !isRunning && Input.GetKeyDown(KeyCode.C))
        {
            StartCoroutine(RunTrials());
        }

        if (enableKeyboardDebug && !IsTestMode() && !isRunning && !isDebugMoving)
        {
            int debugDestination = GetDebugDestinationKey();
            if (debugDestination >= 0)
            {
                StartCoroutine(RunDebugDriveMove(debugDestination));
            }
        }

        FollowCarCamera();
    }

    IEnumerator RunTrials()
    {
        isRunning = true;
        canStart = false;
        ApplySixteenTargetDefaults();
        ApplyInteractionModeVisibility();
        PrepareCueTargets();
        onlineAccuracyRows.Clear();
        SetSelectionView();
        SetStatusText("");
        testOverlayDetailText = "";
        UpdateTestOverlay();

        if (runMode == BciRunMode.Calibration)
        {
            yield return StartCoroutine(RunCalibrationTrials());
        }
        else
        {
            yield return StartCoroutine(RunOnlineGameTrials());
        }

        yield return StartCoroutine(SwitchToSelectionView());
        SetStatusText(finishedText);

        if (enableOnlineAccuracyPlot && onlineAccuracyRows.Count > 0)
        {
            yield return StartCoroutine(SaveOnlineAccuracyPlotAfterDelay());
        }

        isRunning = false;
    }

    IEnumerator RunOnlineGameTrials()
    {
        if (decoderReceiver == null)
        {
            Debug.LogError("[CHECK] Online run stopped: no UdpDecoderReceiver is assigned.");
            yield break;
        }

        if (!decoderReceiver.IsReady)
        {
            Debug.LogError("[CHECK] Online run stopped: neither the primary nor fallback decoder-result receiver is ready.");
            SetStatusText("Decoder receiver not ready");
            yield break;
        }

        Debug.Log("[CHECK] Unity decoder-result receiver is ready before online trials start.");

        for (int trialId = 1; trialId <= GetTrialCount(); trialId++)
        {
            yield return StartCoroutine(SwitchToSelectionView());

            if (trialId > 1)
            {
                SetStatusText("");
                yield return new WaitForSeconds(interTrialSeconds);
                SetStatusText("");
            }

            SetStatusText(recognizingText);
            int trueTarget = GetOnlineAccuracyTrueTarget(trialId);
            decoderReceiver.ClearTrialResult(trialId);
            markerSender.SendRawMarkerToLslBridge(FormatOnlineTrialMarker(startTrialMarker, trialId, trueTarget));
            markerSender.SendMarker(startTrialMarker, trialId);

            int decodedClass = -1;
            string decoderError = "";
            flasher.StartFlashing();
            float stimulationStartTime = Time.realtimeSinceStartup;
            float stimulationDuration = GetOnlineTrialDuration(trialId);
            while (Time.realtimeSinceStartup - stimulationStartTime < stimulationDuration)
            {
                if (decoderReceiver.TryGetClassForTrial(trialId, out decodedClass)
                    || decoderReceiver.TryGetErrorForTrial(trialId, out decoderError))
                {
                    Debug.Log("[CHECK] Trial " + trialId + " stimulation stopped early after "
                        + (Time.realtimeSinceStartup - stimulationStartTime).ToString("F3")
                        + "s because the decoder returned a result.");
                    break;
                }

                yield return null;
            }

            flasher.StopFlashing();
            markerSender.SendRawMarkerToLslBridge(FormatOnlineTrialMarker(stopTrialMarker, trialId, trueTarget));

            if (decodedClass < 0 && string.IsNullOrEmpty(decoderError))
            {
                markerSender.SendRawMarkerToLslBridge("force_decision;trial=" + trialId);
                markerSender.SendMarker(forceDecisionMarker, trialId);

                float waitUntil = Time.time + Mathf.Max(decoderWaitSeconds, 1.5f);
                float nextRetryTime = Time.time + 0.25f;

                while (Time.time < waitUntil)
                {
                    if (decoderReceiver.TryGetClassForTrial(trialId, out decodedClass))
                    {
                        break;
                    }

                    if (decoderReceiver.TryGetErrorForTrial(trialId, out decoderError))
                    {
                        break;
                    }

                    if (Time.time >= nextRetryTime)
                    {
                        markerSender.SendMarker(forceDecisionMarker, trialId);
                        nextRetryTime = Time.time + 0.25f;
                    }

                    yield return null;
                }
            }

            if (!string.IsNullOrEmpty(decoderError))
            {
                Debug.LogWarning("[CHECK] Decoder rejected trial " + trialId + ": " + decoderError);
                RecordOnlineAccuracyTrial(trialId, trueTarget, -1, false);
                SetRecognizedTargetOverlay(trialId, -1, false);
                SetStatusText("EEG stream unavailable");
                continue;
            }

            if (decodedClass < 0)
            {
                Debug.LogWarning("[CHECK] No decoder result received for trial " + trialId
                    + " even though the Unity receiver was ready.");
                RecordOnlineAccuracyTrial(trialId, trueTarget, -1, false);
                SetRecognizedTargetOverlay(trialId, -1, false);
                continue;
            }

            if (!IsValidTarget(decodedClass))
            {
                Debug.LogWarning("Ignoring decoder result outside Unity target range: " + decodedClass);
                RecordOnlineAccuracyTrial(trialId, trueTarget, decodedClass, true);
                SetRecognizedTargetOverlay(trialId, decodedClass, true);
                continue;
            }

            RecordOnlineAccuracyTrial(trialId, trueTarget, decodedClass, true);
            SetRecognizedTargetOverlay(trialId, decodedClass, true);

            flasher.ShowFeedback(decodedClass);
            yield return new WaitForSeconds(feedbackSeconds);
            flasher.ClearHighlight();

            if (IsTestMode())
            {
                continue;
            }

            yield return StartCoroutine(SwitchToDriveView());
            SetStatusText(movingText);
            carMover.SetDestination(decodedClass);

            while (!carMover.HasArrived)
            {
                yield return null;
            }

            yield return new WaitForSeconds(postArrivalDriveViewSeconds);
        }
    }

    IEnumerator RunCalibrationTrials()
    {
        for (int trialId = 1; trialId <= GetTrialCount(); trialId++)
        {
            SetSelectionView();

            if (trialId > 1)
            {
                SetStatusText("");
                yield return new WaitForSeconds(interTrialSeconds);
                SetStatusText("");
            }

            int cueTarget = GetCueTarget(trialId);
            SetCalibrationCueOverlay(trialId, cueTarget);

            if (useCue)
            {
                markerSender.SendRawMarkerToLslBridge($"{cueStartMarker};label={cueTarget};key={cueTarget + 1}");
                flasher.ShowCue(cueTarget);
                yield return new WaitForSeconds(cueSeconds);
                flasher.ClearHighlight();
                markerSender.SendRawMarkerToLslBridge(cueStopMarker);
                yield return new WaitForSeconds(postCueBlinkSeconds);
            }

            SetStatusText("");
            markerSender.SendRawMarkerToLslBridge(startTrialMarker);
            flasher.StartFlashing();
            yield return StartCoroutine(WaitRealtimeSeconds(trialDurationSeconds));
            flasher.StopFlashing();
            markerSender.SendRawMarkerToLslBridge(stopTrialMarker);
            SetStatusText("");
        }
    }

    string FormatOnlineTrialMarker(string marker, int trialId, int target)
    {
        return marker + ";trial=" + trialId + ";label=" + target + ";key=" + (target + 1);
    }

    IEnumerator RunDebugDriveMove(int destinationIndex)
    {
        if (IsTestMode())
        {
            yield break;
        }

        if (!IsValidTarget(destinationIndex))
        {
            Debug.LogWarning("Debug destination outside Unity target range: " + destinationIndex);
            yield break;
        }

        isDebugMoving = true;
        flasher.ClearHighlight();
        yield return StartCoroutine(SwitchToDriveView());
        SetStatusText(movingText);
        carMover.SetDestination(destinationIndex);

        while (!carMover.HasArrived)
        {
            yield return null;
        }

        yield return new WaitForSeconds(postArrivalDriveViewSeconds);
        yield return StartCoroutine(SwitchToSelectionView());
        SetStatusText(pressStartText);
        isDebugMoving = false;
    }

    IEnumerator ShowStartPrompt()
    {
        canStart = false;
        SetStatusText(waitingText);
        yield return new WaitForSeconds(firstReadySeconds);
        SetStatusText(pressStartText);
        canStart = true;
    }

    IEnumerator WaitRealtimeSeconds(float seconds)
    {
        float startTime = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - startTime < seconds)
        {
            yield return null;
        }
    }

    IEnumerator SwitchToSelectionView()
    {
        if (isSelectionViewActive || !useViewTransition || viewTransitionSeconds <= 0f || selectionCamera == null || driveCamera == null)
        {
            SetSelectionView();
            yield break;
        }

        Vector3 startPosition = driveCamera.transform.position;
        Quaternion startRotation = driveCamera.transform.rotation;
        Vector3 endPosition = selectionCamera.transform.position;
        Quaternion endRotation = selectionCamera.transform.rotation;

        driveCamera.enabled = true;
        SetAudioListener(driveCamera, true);
        if (selectionCamera != null)
        {
            selectionCamera.enabled = false;
            SetAudioListener(selectionCamera, false);
        }

        yield return StartCoroutine(AnimateCamera(driveCamera, startPosition, startRotation, endPosition, endRotation));
        SetSelectionView();
    }

    IEnumerator SwitchToDriveView()
    {
        if (!isSelectionViewActive || !useViewTransition || viewTransitionSeconds <= 0f || selectionCamera == null || driveCamera == null)
        {
            SetDriveView();
            yield break;
        }

        Vector3 startPosition = selectionCamera.transform.position;
        Quaternion startRotation = selectionCamera.transform.rotation;
        GetDriveCameraPose(out Vector3 endPosition, out Quaternion endRotation);

        isSelectionViewActive = false;
        ApplyInteractionModeVisibility();
        ApplyDriveMaterials();
        SetTileNumberLabelsVisible(false);
        SetStatusTextVisible(true);

        selectionCamera.enabled = false;
        SetAudioListener(selectionCamera, false);
        driveCamera.enabled = true;
        SetAudioListener(driveCamera, true);
        driveCamera.transform.position = startPosition;
        driveCamera.transform.rotation = startRotation;

        yield return StartCoroutine(AnimateCamera(driveCamera, startPosition, startRotation, endPosition, endRotation));
        FollowCarCamera();
    }

    IEnumerator AnimateCamera(Camera cameraToAnimate, Vector3 startPosition, Quaternion startRotation, Vector3 endPosition, Quaternion endRotation)
    {
        if (cameraToAnimate == null)
        {
            yield break;
        }

        float duration = Mathf.Max(0.01f, viewTransitionSeconds);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            cameraToAnimate.transform.position = Vector3.Lerp(startPosition, endPosition, t);
            cameraToAnimate.transform.rotation = Quaternion.Slerp(startRotation, endRotation, t);
            yield return null;
        }

        cameraToAnimate.transform.position = endPosition;
        cameraToAnimate.transform.rotation = endRotation;
    }

    void SetSelectionView()
    {
        isSelectionViewActive = true;
        ApplyInteractionModeVisibility();
        ApplySelectionMaterials();

        if (selectionCamera != null)
        {
            selectionCamera.enabled = true;
            SetAudioListener(selectionCamera, true);
        }

        SetTileNumberLabelsVisible(true);
        SetStatusTextVisible(false);

        if (driveCamera != null)
        {
            driveCamera.enabled = false;
            SetAudioListener(driveCamera, false);
        }
    }

    void SetDriveView()
    {
        isSelectionViewActive = false;
        ApplyInteractionModeVisibility();
        ApplyDriveMaterials();

        if (selectionCamera != null)
        {
            selectionCamera.enabled = false;
            SetAudioListener(selectionCamera, false);
        }

        SetTileNumberLabelsVisible(false);
        SetStatusTextVisible(true);

        if (driveCamera != null)
        {
            driveCamera.enabled = true;
            SetAudioListener(driveCamera, true);
        }
    }

    void SetAudioListener(Camera camera, bool enabled)
    {
        AudioListener listener = camera.GetComponent<AudioListener>();

        if (listener != null)
        {
            listener.enabled = enabled;
        }
    }

    void ApplySelectionMaterials()
    {
        ApplyGroundMaterial(groundSelectionMaterial);
        ApplyTileMaterial(tileSelectionMaterial);
    }

    void ApplyDriveMaterials()
    {
        ApplyGroundMaterial(groundDriveMaterial);
        ApplyTileMaterial(tileDriveMaterial);
    }

    void ApplyGroundMaterial(Material material)
    {
        if (groundRenderer != null && material != null)
        {
            groundRenderer.material = material;
        }
    }

    void ApplyTileMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        Renderer[] renderers = GetViewSwitchTileRenderers();
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                renderer.material = material;
            }
        }
    }

    Renderer[] GetViewSwitchTileRenderers()
    {
        if (viewSwitchTileRenderers != null && viewSwitchTileRenderers.Length > 0)
        {
            return viewSwitchTileRenderers;
        }

        if (flasher == null || flasher.destinationTiles == null)
        {
            return new Renderer[0];
        }

        Renderer[] renderers = new Renderer[flasher.destinationTiles.Length];
        for (int i = 0; i < flasher.destinationTiles.Length; i++)
        {
            if (flasher.destinationTiles[i] != null)
            {
                renderers[i] = flasher.destinationTiles[i].GetComponent<Renderer>();
            }
        }

        return renderers;
    }

    void FollowCarCamera()
    {
        if (driveCamera == null || !driveCamera.enabled || driveCameraTarget == null)
        {
            return;
        }

        Vector3 targetPosition =
            driveCameraTarget.position
            + driveCameraTarget.right * driveCameraOffset.x
            + driveCameraTarget.up * driveCameraOffset.y
            + driveCameraTarget.forward * driveCameraOffset.z;

        Vector3 lookPosition =
            driveCameraTarget.position
            + driveCameraTarget.right * driveCameraLookOffset.x
            + driveCameraTarget.up * driveCameraLookOffset.y
            + driveCameraTarget.forward * driveCameraLookOffset.z;

        driveCamera.transform.position = Vector3.Lerp(
            driveCamera.transform.position,
            targetPosition,
            driveCameraFollowSpeed * Time.deltaTime
        );

        Quaternion targetRotation = Quaternion.LookRotation(
            lookPosition - driveCamera.transform.position,
            Vector3.up
        );
        driveCamera.transform.rotation = Quaternion.Slerp(
            driveCamera.transform.rotation,
            targetRotation,
            driveCameraRotateSpeed * Time.deltaTime
        );
    }

    void GetDriveCameraPose(out Vector3 targetPosition, out Quaternion targetRotation)
    {
        if (driveCameraTarget == null)
        {
            targetPosition = driveCamera != null ? driveCamera.transform.position : Vector3.zero;
            targetRotation = driveCamera != null ? driveCamera.transform.rotation : Quaternion.identity;
            return;
        }

        targetPosition =
            driveCameraTarget.position
            + driveCameraTarget.right * driveCameraOffset.x
            + driveCameraTarget.up * driveCameraOffset.y
            + driveCameraTarget.forward * driveCameraOffset.z;

        Vector3 lookPosition =
            driveCameraTarget.position
            + driveCameraTarget.right * driveCameraLookOffset.x
            + driveCameraTarget.up * driveCameraLookOffset.y
            + driveCameraTarget.forward * driveCameraLookOffset.z;

        Vector3 lookDirection = lookPosition - targetPosition;
        targetRotation = lookDirection.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(lookDirection, Vector3.up)
            : Quaternion.identity;
    }

    void LateUpdate()
    {
        UpdateTileNumberLabels(GetDestinationTiles());
    }

    void CreateTileNumberLabels()
    {
        Transform[] tiles = GetDestinationTiles();
        if (!showTileNumberLabels || tiles == null || tiles.Length == 0)
        {
            SetTileNumberLabelsVisible(false);
            return;
        }

        if (tileNumberLabels != null && tileNumberLabels.Length == tiles.Length)
        {
            UpdateTileNumberLabels(tiles);
            return;
        }

        DestroyTileNumberLabels();
        tileNumberLabels = new TextMeshProUGUI[tiles.Length];
        Canvas canvas = GetTileNumberLabelCanvas();

        for (int i = 0; i < tiles.Length; i++)
        {
            if (tiles[i] == null)
            {
                continue;
            }

            GameObject labelObject = new GameObject("TileLabel_" + (i + 1));
            labelObject.transform.SetParent(canvas.transform, false);
            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = (i + 1).ToString();
            label.alignment = TextAlignmentOptions.Center;
            label.color = tileNumberLabelColor;
            label.fontSize = GetTileNumberLabelFontSize();
            label.fontStyle = FontStyles.Normal;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(56f, 28f);
            tileNumberLabels[i] = label;
        }

        UpdateTileNumberLabels(tiles);
    }

    void DestroyTileNumberLabels()
    {
        if (tileNumberLabels == null)
        {
            return;
        }

        foreach (TextMeshProUGUI label in tileNumberLabels)
        {
            if (label != null)
            {
                Destroy(label.gameObject);
            }
        }

        tileNumberLabels = null;
    }

    void UpdateTileNumberLabels(Transform[] tiles)
    {
        for (int i = 0; i < tileNumberLabels.Length && i < tiles.Length; i++)
        {
            if (tileNumberLabels[i] == null || tiles[i] == null)
            {
                continue;
            }

            tileNumberLabels[i].text = (i + 1).ToString();
            tileNumberLabels[i].color = tileNumberLabelColor;
            tileNumberLabels[i].fontSize = GetTileNumberLabelFontSize();
            tileNumberLabels[i].fontStyle = FontStyles.Normal;
            Renderer tileRenderer = tiles[i].GetComponent<Renderer>();
            Vector3 labelPosition = tiles[i].position;
            if (tileRenderer != null)
            {
                labelPosition = tileRenderer.bounds.center;
                labelPosition.y = tileRenderer.bounds.max.y;
            }

            PositionTileNumberLabel(tileNumberLabels[i], labelPosition + Vector3.up * tileNumberLabelHeight);
        }

        SetTileNumberLabelsVisible(showTileNumberLabels && selectionCamera != null && selectionCamera.enabled);
    }

    float GetTileNumberLabelFontSize()
    {
        return Mathf.Max(tileNumberLabelFontSize, minimumTileNumberLabelFontSize, 14f);
    }

    Canvas GetTileNumberLabelCanvas()
    {
        if (tileNumberLabelCanvas != null)
        {
            return tileNumberLabelCanvas;
        }

        tileNumberLabelCanvas = FindObjectOfType<Canvas>();
        if (tileNumberLabelCanvas != null)
        {
            return tileNumberLabelCanvas;
        }

        GameObject canvasObject = new GameObject("TileNumberLabelCanvas");
        tileNumberLabelCanvas = canvasObject.AddComponent<Canvas>();
        tileNumberLabelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        return tileNumberLabelCanvas;
    }

    void PositionTileNumberLabel(TextMeshProUGUI label, Vector3 worldPosition)
    {
        if (label == null || selectionCamera == null)
        {
            return;
        }

        Vector3 screenPoint = selectionCamera.WorldToScreenPoint(worldPosition);
        bool isVisible = screenPoint.z > 0f && selectionCamera.enabled;
        label.gameObject.SetActive(isVisible && showTileNumberLabels);
        if (!isVisible)
        {
            return;
        }

        RectTransform canvasRect = tileNumberLabelCanvas.GetComponent<RectTransform>();
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPoint,
            null,
            out localPoint
        );
        label.rectTransform.anchoredPosition = localPoint;
    }

    void SetTileNumberLabelsVisible(bool visible)
    {
        if (tileNumberLabels == null)
        {
            return;
        }

        bool shouldShow = visible && showTileNumberLabels;
        foreach (TextMeshProUGUI label in tileNumberLabels)
        {
            if (label != null)
            {
                label.gameObject.SetActive(shouldShow);
            }
        }
    }

    Transform[] GetDestinationTiles()
    {
        if (flasher != null && flasher.destinationTiles != null && flasher.destinationTiles.Length > 0)
        {
            return flasher.destinationTiles;
        }

        if (carMover != null && carMover.destinationTiles != null && carMover.destinationTiles.Length > 0)
        {
            return carMover.destinationTiles;
        }

        Transform[] tiles = new Transform[UnityTargetCount];
        int foundCount = 0;
        for (int i = 0; i < UnityTargetCount; i++)
        {
            GameObject tileObject = GameObject.Find("DestinationTile_" + i);
            if (tileObject != null)
            {
                tiles[i] = tileObject.transform;
                foundCount++;
            }
        }

        return foundCount > 0 ? tiles : null;
    }

    void SetStatusText(string text)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }

        UpdateTestOverlay();
    }

    void SetStatusTextVisible(bool visible)
    {
        if (statusText != null)
        {
            statusText.gameObject.SetActive(visible);
        }
    }

    void ConfigureStatusTextLayout()
    {
        if (statusText == null)
        {
            return;
        }

        statusText.fontSize = 30f;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.enableWordWrapping = false;

        RectTransform rect = statusText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 72f);
        rect.sizeDelta = new Vector2(800f, 80f);
    }

    bool IsTestMode()
    {
        return interactionMode == BciInteractionMode.Test;
    }

    void ApplyInteractionModeVisibility()
    {
        if (carMover != null && carMover.car != null)
        {
            carMover.car.gameObject.SetActive(!IsTestMode() && !isSelectionViewActive);
        }
    }

    void SetRecognizedTargetOverlay(int trialId, int decodedClass, bool hasPrediction)
    {
        string predictionText = hasPrediction ? (decodedClass + 1).ToString() : "No output";
        testOverlayDetailText = predictionText;
        UpdateTestOverlay();
    }

    void SetCalibrationCueOverlay(int trialId, int cueTarget)
    {
        testOverlayDetailText = (cueTarget + 1).ToString();
        UpdateTestOverlay();
    }

    void UpdateTestOverlay()
    {
        TMP_Text overlay = GetTestOverlayText();
        if (overlay == null)
        {
            return;
        }

        overlay.gameObject.SetActive(true);

        string modeText = IsTestMode()
            ? "Test\n" + GetTestDisplayModeLabel()
            : GetTestDisplayModeLabel();

        overlay.text = string.IsNullOrWhiteSpace(testOverlayDetailText)
            ? modeText
            : modeText + "\n" + testOverlayDetailText;
    }

    TMP_Text GetTestOverlayText()
    {
        if (testOverlayText != null)
        {
            return testOverlayText;
        }

        if (runtimeTestOverlayText != null)
        {
            return runtimeTestOverlayText;
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("BciTestOverlayCanvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        GameObject textObject = new GameObject("BciTestOverlayText");
        textObject.transform.SetParent(canvas.transform, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.TopLeft;
        text.color = testOverlayTextColor;
        text.fontSize = testOverlayFontSize;
        text.enableWordWrapping = false;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = testOverlayOffset;
        rect.sizeDelta = new Vector2(520f, 150f);

        runtimeTestOverlayText = text;
        return runtimeTestOverlayText;
    }

    string GetTestDisplayModeLabel()
    {
        switch (testDisplayMode)
        {
            case BciTestDisplayMode.OfflineNTrain:
                return "Offline n-train";
            case BciTestDisplayMode.OnlineNTrain:
                return "Online n-train";
            case BciTestDisplayMode.ZeroTrain:
                return "0-train";
            default:
                return testDisplayMode.ToString();
        }
    }

    string GetAccuracyPlotModeLabel()
    {
        return GetTestDisplayModeLabel();
    }

    int GetCueTarget(int trialId)
    {
        int index = trialId - 1;

        if (runtimeCueTargets != null && index >= 0 && index < runtimeCueTargets.Length)
        {
            return runtimeCueTargets[index];
        }

        if (cueTargets != null && index >= 0 && index < cueTargets.Length)
        {
            return cueTargets[index];
        }

        return 0;
    }

    float GetOnlineTrialDuration(int trialId)
    {
        if (useZeroTrainingWarmUp && trialId == 1)
        {
            return zeroTrainingWarmUpSeconds;
        }

        return trialDurationSeconds;
    }

    int GetOnlineAccuracyTrueTarget(int trialId)
    {
        int index = trialId - 1;

        if (onlineAccuracyTrueTargets != null && index >= 0 && index < onlineAccuracyTrueTargets.Length)
        {
            return onlineAccuracyTrueTargets[index];
        }

        return -1;
    }

    void RecordOnlineAccuracyTrial(int trialId, int trueTarget, int predictedTarget, bool hasPrediction)
    {
        AccuracyTrial row = new AccuracyTrial
        {
            trialId = trialId,
            trueTarget = trueTarget,
            predictedTarget = predictedTarget,
            hasPrediction = hasPrediction
        };

        onlineAccuracyRows.Add(row);

        string trueText = trueTarget >= 0 ? (trueTarget + 1).ToString() : "";
        string predText = hasPrediction ? (predictedTarget + 1).ToString() : "No output";
        Debug.Log("Accuracy trial " + trialId + ": true=" + trueText + ", predicted=" + predText + ", correct=" + row.IsCorrect);
    }

    IEnumerator SaveOnlineAccuracyPlotAfterDelay()
    {
        if (onlineAccuracyRows.Count == 0)
        {
            Debug.LogWarning("No online accuracy rows available; no accuracy plot saved.");
            yield break;
        }

        if (accuracyPlotDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(accuracyPlotDelaySeconds);
        }

        SaveOnlineAccuracyPlot();
    }

    void SaveOnlineAccuracyPlot()
    {
        string outputDir = string.IsNullOrWhiteSpace(accuracyOutputDirectory)
            ? Path.Combine(Application.persistentDataPath, "online_accuracy")
            : accuracyOutputDirectory.Trim();
        Directory.CreateDirectory(outputDir);

        string modeLabel = GetAccuracyPlotModeLabel();
        string modeSlug = Slugify(modeLabel);
        if (string.IsNullOrEmpty(modeSlug))
        {
            modeSlug = "online";
        }
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filePrefix = IsTestMode() ? "online_test_accuracy" : "online_game_accuracy";
        string pngPath = Path.Combine(outputDir, filePrefix + "_" + modeSlug + "_" + timestamp + ".png");

        Texture2D plot = BuildOnlineAccuracyPngTexture();
        File.WriteAllBytes(pngPath, plot.EncodeToPNG());
        Destroy(plot);

        Debug.Log("Saved online accuracy PNG: " + pngPath);
        Application.OpenURL(new Uri(pngPath).AbsoluteUri);
    }

    Texture2D BuildOnlineAccuracyPngTexture()
    {
        int nTrials = onlineAccuracyRows.Count;
        string modeLabel = GetAccuracyPlotModeLabel();
        List<string> labels = GetAccuracyLabels();
        Dictionary<string, int> labelToY = new Dictionary<string, int>();
        for (int i = 0; i < labels.Count; i++)
        {
            labelToY[labels[i]] = i;
        }

        const int width = 1500;
        const int height = 980;
        const int left = 150;
        const int right = 110;
        const int top = 130;
        const int bottom = 250;
        int plotWidth = width - left - right;
        int plotHeight = height - top - bottom;
        float trialStep = nTrials > 1 ? plotWidth / (float)(nTrials - 1) : 0f;
        float yStep = labels.Count > 1 ? plotHeight / (float)(labels.Count - 1) : 0f;

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        FillTexture(texture, Color.white);

        int correctCount = 0;
        foreach (AccuracyTrial row in onlineAccuracyRows)
        {
            if (row.IsCorrect)
            {
                correctCount++;
            }
        }

        float accuracy = nTrials > 0 ? correctCount / (float)nTrials : 0f;
        Color textColor = new Color(0.10f, 0.10f, 0.10f);
        DrawText(texture, modeLabel + " online accuracy", left, 28, textColor, 4);
        DrawText(
            texture,
            "Accuracy: " + correctCount + "/" + nTrials + " (" + Mathf.RoundToInt(accuracy * 100f) + "%)",
            left,
            78,
            textColor,
            3
        );
        DrawText(texture, "Target", 18, top - 30, textColor, 3);
        DrawText(texture, "Trial", left + plotWidth / 2 - 42, top + plotHeight + 70, textColor, 3);

        for (int i = 0; i < nTrials; i++)
        {
            AccuracyTrial row = onlineAccuracyRows[i];
            int x = Mathf.RoundToInt(left + i * trialStep);
            int bandWidth = nTrials > 1 ? Mathf.Max(18, Mathf.RoundToInt(trialStep * 0.9f)) : 60;
            Color fill = row.trueTarget < 0
                ? new Color(0.94f, 0.94f, 0.94f)
                : (row.IsCorrect ? new Color(0.90f, 0.96f, 0.92f) : new Color(0.98f, 0.90f, 0.90f));
            FillRect(texture, x - bandWidth / 2, top, bandWidth, plotHeight, fill);
        }

        for (int i = 0; i < labels.Count; i++)
        {
            int y = Mathf.RoundToInt(top + plotHeight - i * yStep);
            DrawLine(texture, left, y, left + plotWidth, y, new Color(0.86f, 0.86f, 0.86f), 1);
            DrawText(texture, labels[i], 82, y - 7, textColor, 2);
        }

        DrawLine(texture, left, top, left, top + plotHeight, new Color(0.15f, 0.15f, 0.15f), 2);
        DrawLine(texture, left, top + plotHeight, left + plotWidth, top + plotHeight, new Color(0.15f, 0.15f, 0.15f), 2);

        for (int i = 0; i < nTrials; i++)
        {
            int x = Mathf.RoundToInt(left + i * trialStep);
            DrawLine(texture, x, top + plotHeight, x, top + plotHeight + 7, new Color(0.15f, 0.15f, 0.15f), 1);
            DrawText(texture, (i + 1).ToString(), x - 7, top + plotHeight + 18, textColor, 2);
        }

        int previousTrueX = -1;
        int previousTrueY = -1;
        int previousPredX = -1;
        int previousPredY = -1;

        for (int i = 0; i < nTrials; i++)
        {
            AccuracyTrial row = onlineAccuracyRows[i];
            int x = Mathf.RoundToInt(left + i * trialStep);

            if (row.trueTarget >= 0)
            {
                string trueLabel = (row.trueTarget + 1).ToString();
                int y = Mathf.RoundToInt(top + plotHeight - labelToY[trueLabel] * yStep);
                if (previousTrueX >= 0)
                {
                    DrawLine(texture, previousTrueX, previousTrueY, x, y, new Color(0.13f, 0.13f, 0.13f), 3);
                }
                DrawFilledCircle(texture, x, y, 6, new Color(0.13f, 0.13f, 0.13f));
                previousTrueX = x;
                previousTrueY = y;
            }

            string predLabel = row.hasPrediction ? (row.predictedTarget + 1).ToString() : "No output";
            int predY = Mathf.RoundToInt(top + plotHeight - labelToY[predLabel] * yStep);
            if (previousPredX >= 0)
            {
                DrawLine(texture, previousPredX, previousPredY, x, predY, new Color(0.84f, 0.37f, 0.00f), 3);
            }
            DrawX(texture, x, predY, 7, new Color(0.84f, 0.37f, 0.00f), 3);
            previousPredX = x;
            previousPredY = predY;
        }

        DrawLegend(texture, left + plotWidth - 470, top + plotHeight + 108);
        DrawAccuracyTable(texture, left, top + plotHeight + 145, textColor);
        texture.Apply();
        return texture;
    }

    void DrawAccuracyTable(Texture2D texture, int x, int y, Color textColor)
    {
        StringBuilder trialLine = new StringBuilder("TRIAL ");
        StringBuilder trueLine = new StringBuilder("TRUE  ");
        StringBuilder predLine = new StringBuilder("PRED  ");
        StringBuilder okLine = new StringBuilder("OK    ");

        foreach (AccuracyTrial row in onlineAccuracyRows)
        {
            trialLine.Append(PadPlotCell(row.trialId.ToString()));
            trueLine.Append(PadPlotCell(row.trueTarget >= 0 ? (row.trueTarget + 1).ToString() : "-"));
            predLine.Append(PadPlotCell(row.hasPrediction ? (row.predictedTarget + 1).ToString() : "NO"));
            okLine.Append(PadPlotCell(row.IsCorrect ? "Y" : "N"));
        }

        DrawText(texture, trialLine.ToString(), x, y, textColor, 2);
        DrawText(texture, trueLine.ToString(), x, y + 28, textColor, 2);
        DrawText(texture, predLine.ToString(), x, y + 56, new Color(0.84f, 0.37f, 0.00f), 2);
        DrawText(texture, okLine.ToString(), x, y + 84, textColor, 2);
    }

    string PadPlotCell(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            value = "-";
        }

        if (value.Length == 1)
        {
            return "  " + value + " ";
        }

        if (value.Length == 2)
        {
            return " " + value + " ";
        }

        return value + " ";
    }

    List<string> GetAccuracyLabels()
    {
        SortedSet<int> numericTargets = new SortedSet<int>();
        bool hasNoOutput = false;

        foreach (AccuracyTrial row in onlineAccuracyRows)
        {
            if (row.trueTarget >= 0)
            {
                numericTargets.Add(row.trueTarget + 1);
            }

            if (row.hasPrediction)
            {
                numericTargets.Add(row.predictedTarget + 1);
            }
            else
            {
                hasNoOutput = true;
            }
        }

        List<string> labels = new List<string>();
        foreach (int target in numericTargets)
        {
            labels.Add(target.ToString());
        }

        if (hasNoOutput)
        {
            labels.Add("No output");
        }

        return labels;
    }

    string Slugify(string text)
    {
        StringBuilder slug = new StringBuilder();
        foreach (char ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                slug.Append(ch);
            }
            else if (slug.Length > 0 && slug[slug.Length - 1] != '_')
            {
                slug.Append('_');
            }
        }

        return slug.ToString().Trim('_');
    }

    void FillTexture(Texture2D texture, Color color)
    {
        Color[] pixels = new Color[texture.width * texture.height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        texture.SetPixels(pixels);
    }

    void FillRect(Texture2D texture, int x, int y, int width, int height, Color color)
    {
        int x0 = Mathf.Clamp(x, 0, texture.width);
        int x1 = Mathf.Clamp(x + width, 0, texture.width);
        int y0 = Mathf.Clamp(y, 0, texture.height);
        int y1 = Mathf.Clamp(y + height, 0, texture.height);

        for (int py = y0; py < y1; py++)
        {
            for (int px = x0; px < x1; px++)
            {
                SetPlotPixel(texture, px, py, color);
            }
        }
    }

    void DrawLine(Texture2D texture, int x0, int y0, int x1, int y1, Color color, int thickness)
    {
        int dx = Mathf.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            DrawPoint(texture, x0, y0, color, thickness);
            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    void DrawPoint(Texture2D texture, int x, int y, Color color, int thickness)
    {
        int radius = Mathf.Max(1, thickness) / 2;
        for (int py = y - radius; py <= y + radius; py++)
        {
            for (int px = x - radius; px <= x + radius; px++)
            {
                SetPlotPixel(texture, px, py, color);
            }
        }
    }

    void DrawFilledCircle(Texture2D texture, int centerX, int centerY, int radius, Color color)
    {
        int r2 = radius * radius;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                if (dx * dx + dy * dy <= r2)
                {
                    SetPlotPixel(texture, x, y, color);
                }
            }
        }
    }

    void DrawX(Texture2D texture, int x, int y, int radius, Color color, int thickness)
    {
        DrawLine(texture, x - radius, y - radius, x + radius, y + radius, color, thickness);
        DrawLine(texture, x - radius, y + radius, x + radius, y - radius, color, thickness);
    }

    void DrawLegend(Texture2D texture, int x, int y)
    {
        DrawFilledCircle(texture, x, y, 6, new Color(0.13f, 0.13f, 0.13f));
        DrawLine(texture, x + 18, y, x + 72, y, new Color(0.13f, 0.13f, 0.13f), 3);
        DrawText(texture, "True target", x + 86, y - 9, new Color(0.13f, 0.13f, 0.13f), 2);
        DrawX(texture, x + 250, y, 7, new Color(0.84f, 0.37f, 0.00f), 3);
        DrawLine(texture, x + 268, y, x + 322, y, new Color(0.84f, 0.37f, 0.00f), 3);
        DrawText(texture, "Predicted target", x + 336, y - 9, new Color(0.84f, 0.37f, 0.00f), 2);
    }

    void DrawText(Texture2D texture, string text, int x, int y, Color color, int scale)
    {
        int cursorX = x;
        string upper = text.ToUpperInvariant();
        foreach (char ch in upper)
        {
            if (ch == '\n')
            {
                cursorX = x;
                y += 8 * scale;
                continue;
            }

            string[] glyph = GetPlotGlyph(ch);
            if (glyph == null)
            {
                cursorX += 4 * scale;
                continue;
            }

            for (int row = 0; row < glyph.Length; row++)
            {
                for (int col = 0; col < glyph[row].Length; col++)
                {
                    if (glyph[row][col] != '1')
                    {
                        continue;
                    }

                    FillRect(texture, cursorX + col * scale, y + row * scale, scale, scale, color);
                }
            }

            cursorX += (glyph[0].Length + 1) * scale;
        }
    }

    string[] GetPlotGlyph(char ch)
    {
        switch (ch)
        {
            case 'A': return new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" };
            case 'B': return new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" };
            case 'C': return new[] { "01111", "10000", "10000", "10000", "10000", "10000", "01111" };
            case 'D': return new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" };
            case 'E': return new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" };
            case 'F': return new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" };
            case 'G': return new[] { "01111", "10000", "10000", "10111", "10001", "10001", "01111" };
            case 'H': return new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" };
            case 'I': return new[] { "11111", "00100", "00100", "00100", "00100", "00100", "11111" };
            case 'J': return new[] { "00111", "00010", "00010", "00010", "10010", "10010", "01100" };
            case 'K': return new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" };
            case 'L': return new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" };
            case 'M': return new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" };
            case 'N': return new[] { "10001", "11001", "10101", "10011", "10001", "10001", "10001" };
            case 'O': return new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" };
            case 'P': return new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" };
            case 'Q': return new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" };
            case 'R': return new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" };
            case 'S': return new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" };
            case 'T': return new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" };
            case 'U': return new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" };
            case 'V': return new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" };
            case 'W': return new[] { "10001", "10001", "10001", "10101", "10101", "10101", "01010" };
            case 'X': return new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" };
            case 'Y': return new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" };
            case 'Z': return new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" };
            case '0': return new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" };
            case '1': return new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" };
            case '2': return new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" };
            case '3': return new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" };
            case '4': return new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" };
            case '5': return new[] { "11111", "10000", "10000", "11110", "00001", "00001", "11110" };
            case '6': return new[] { "01110", "10000", "10000", "11110", "10001", "10001", "01110" };
            case '7': return new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" };
            case '8': return new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" };
            case '9': return new[] { "01110", "10001", "10001", "01111", "00001", "00001", "01110" };
            case ':': return new[] { "0", "1", "0", "0", "1", "0", "0" };
            case '/': return new[] { "00001", "00010", "00010", "00100", "01000", "01000", "10000" };
            case '-': return new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" };
            case '%': return new[] { "11001", "11010", "00010", "00100", "01000", "01011", "10011" };
            case '(': return new[] { "0010", "0100", "1000", "1000", "1000", "0100", "0010" };
            case ')': return new[] { "1000", "0100", "0010", "0010", "0010", "0100", "1000" };
            case ' ': return new[] { "000", "000", "000", "000", "000", "000", "000" };
            default: return null;
        }
    }

    void SetPlotPixel(Texture2D texture, int x, int y, Color color)
    {
        if (x < 0 || x >= texture.width || y < 0 || y >= texture.height)
        {
            return;
        }

        texture.SetPixel(x, texture.height - 1 - y, color);
    }

    void PrepareCueTargets()
    {
        runtimeCueTargets = null;

        if (runMode != BciRunMode.Calibration || !useCue)
        {
            return;
        }

        List<int> targets = new List<int>();
        int nTargets = GetTargetCount();
        if (nTargets <= 0)
        {
            Debug.LogWarning("No Unity destination tiles assigned for cue target generation.");
            return;
        }

        int trialCount = GetTrialCount();
        System.Random random = randomSeed >= 0 ? new System.Random(randomSeed) : new System.Random();

        if (!useBalancedRandomCueTargets)
        {
            for (int i = 0; i < trialCount; i++)
            {
                targets.Add(random.Next(nTargets));
            }

            runtimeCueTargets = targets.ToArray();
            Debug.Log("Unity cue target order: " + string.Join(", ", System.Array.ConvertAll(runtimeCueTargets, item => item.ToString())));
            return;
        }

        int repeats = trialCount / nTargets;
        int remainder = trialCount % nTargets;

        for (int repeat = 0; repeat < repeats; repeat++)
        {
            for (int target = 0; target < nTargets; target++)
            {
                targets.Add(target);
            }
        }

        List<int> remainderTargets = new List<int>();
        for (int target = 0; target < nTargets; target++)
        {
            remainderTargets.Add(target);
        }

        Shuffle(remainderTargets, random);
        for (int i = 0; i < remainder; i++)
        {
            targets.Add(remainderTargets[i]);
        }

        Shuffle(targets, random);
        runtimeCueTargets = targets.ToArray();

        Debug.Log("Unity cue target order: " + string.Join(", ", System.Array.ConvertAll(runtimeCueTargets, item => item.ToString())));
    }

    void ApplySixteenTargetDefaults()
    {
        if (!forceSixteenTargetLayout)
        {
            return;
        }

        totalTrials = UnityTargetCount;
        calibrationTrials = UnityTargetCount;
        useBalancedRandomCueTargets = true;

        cueTargets = CreateSequentialTargets();
        onlineAccuracyTrueTargets = CreateSequentialTargets();
    }

    int[] CreateSequentialTargets()
    {
        int[] targets = new int[UnityTargetCount];
        for (int i = 0; i < UnityTargetCount; i++)
        {
            targets[i] = i;
        }

        return targets;
    }

    int GetTargetCount()
    {
        if (carMover != null && carMover.destinationTiles != null && carMover.destinationTiles.Length > 0)
        {
            return carMover.destinationTiles.Length;
        }

        if (flasher != null && flasher.destinationTiles != null && flasher.destinationTiles.Length > 0)
        {
            return flasher.destinationTiles.Length;
        }

        return 0;
    }

    int GetTrialCount()
    {
        return runMode == BciRunMode.Calibration ? calibrationTrials : totalTrials;
    }

    bool IsValidTarget(int targetIndex)
    {
        return targetIndex >= 0 && targetIndex < GetTargetCount();
    }

    int GetDebugDestinationKey()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) return 0;
        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) return 1;
        if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) return 2;
        if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) return 3;
        if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) return 4;
        if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6)) return 5;
        if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7)) return 6;
        if (Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8)) return 7;
        if (Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9)) return 8;
        if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0)) return 9;
        if (Input.GetKeyDown(KeyCode.Q)) return 10;
        if (Input.GetKeyDown(KeyCode.W)) return 11;
        if (Input.GetKeyDown(KeyCode.E)) return 12;
        if (Input.GetKeyDown(KeyCode.R)) return 13;
        if (Input.GetKeyDown(KeyCode.T)) return 14;
        if (Input.GetKeyDown(KeyCode.Y)) return 15;

        return -1;
    }

    void Shuffle(List<int> values, System.Random random)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            int temp = values[i];
            values[i] = values[j];
            values[j] = temp;
        }
    }
}
