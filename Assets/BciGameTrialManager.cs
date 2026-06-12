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

public class BciGameTrialManager : MonoBehaviour
{
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

    public Renderer groundRenderer;
    public Renderer[] viewSwitchTileRenderers;
    public Material groundSelectionMaterial;
    public Material groundDriveMaterial;
    public Material tileSelectionMaterial;
    public Material tileDriveMaterial;

    public TMP_Text statusText;
    public BciRunMode runMode = BciRunMode.OnlineGame;
    public string waitingText = "Waiting...";
    public string pressStartText = "Press C to start";
    public string recognizingText = "Start recognizing the target";
    public string movingText = "Moving to the destination";
    public string finishedText = "Finished";

    public int totalTrials = 10;
    public int calibrationTrials = 16;
    public float firstReadySeconds = 5f;
    public float interTrialSeconds = 2f;
    public float cueSeconds = 0.7f;
    public float trialDurationSeconds = 4.2f;
    public float decoderWaitSeconds = 2f;
    public float feedbackSeconds = 0.7f;
    public float postArrivalDriveViewSeconds = 0.5f;
    public bool enableKeyboardDebug = true;
    public bool useCue = true;
    public bool useBalancedRandomCueTargets = true;
    public int randomSeed = -1;
    public int[] cueTargets = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    public bool enableOnlineAccuracyPlot = true;
    public float accuracyPlotDelaySeconds = 3f;
    public string accuracyModeLabel = "Zero-training";
    public string accuracyOutputDirectory = "/Users/wang/dp-cvep-1/cvep_speller_env/data/online_accuracy";
    public int[] onlineAccuracyTrueTargets = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    public string startTrialMarker = "start_trial";
    public string stopTrialMarker = "stop_trial";
    public string cueStartMarker = "start_cue";
    public string cueStopMarker = "stop_cue";
    public string forceDecisionMarker = "force_decision";

    private bool isRunning = false;
    private bool isDebugMoving = false;
    private bool canStart = false;
    private int[] runtimeCueTargets;
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
        SetSelectionView();
        StartCoroutine(ShowStartPrompt());
    }

    public void PrepareForMode(BciRunMode mode)
    {
        if (isRunning)
        {
            Debug.LogWarning("Cannot switch Unity BCI mode while a run is active.");
            return;
        }

        StopAllCoroutines();
        runMode = mode;
        canStart = false;
        runtimeCueTargets = null;
        flasher.ClearHighlight();
        SetSelectionView();
        StartCoroutine(ShowStartPrompt());
        Debug.Log("Prepared Unity BCI mode: " + runMode);
    }

    void Update()
    {
        if (canStart && !isRunning && Input.GetKeyDown(KeyCode.C))
        {
            StartCoroutine(RunTrials());
        }

        if (enableKeyboardDebug && !isRunning && !isDebugMoving)
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
        PrepareCueTargets();
        onlineAccuracyRows.Clear();
        SetSelectionView();
        SetStatusText("");

        if (runMode == BciRunMode.Calibration)
        {
            yield return StartCoroutine(RunCalibrationTrials());
        }
        else
        {
            yield return StartCoroutine(RunOnlineGameTrials());
        }

        SetSelectionView();
        SetStatusText(finishedText);

        if (runMode == BciRunMode.OnlineGame && enableOnlineAccuracyPlot)
        {
            yield return StartCoroutine(SaveOnlineAccuracyPlotAfterDelay());
        }

        isRunning = false;
    }

    IEnumerator RunOnlineGameTrials()
    {
        for (int trialId = 1; trialId <= GetTrialCount(); trialId++)
        {
            SetSelectionView();

            if (trialId > 1)
            {
                SetStatusText(waitingText);
                yield return new WaitForSeconds(interTrialSeconds);
                SetStatusText("");
            }

            SetStatusText(recognizingText);
            markerSender.SendMarker(startTrialMarker, trialId);
            flasher.StartFlashing();
            yield return new WaitForSeconds(trialDurationSeconds);
            flasher.StopFlashing();
            markerSender.SendMarker(forceDecisionMarker, trialId);

            int decodedClass = -1;
            float waitUntil = Time.time + decoderWaitSeconds;

            while (Time.time < waitUntil)
            {
                if (decoderReceiver.TryGetLatestClass(out decodedClass))
                {
                    break;
                }

                yield return null;
            }

            if (decodedClass < 0)
            {
                Debug.LogWarning("No decoder result received for trial " + trialId);
                RecordOnlineAccuracyTrial(trialId, GetOnlineAccuracyTrueTarget(trialId), -1, false);
                continue;
            }

            if (!IsValidTarget(decodedClass))
            {
                Debug.LogWarning("Ignoring decoder result outside Unity target range: " + decodedClass);
                RecordOnlineAccuracyTrial(trialId, GetOnlineAccuracyTrueTarget(trialId), decodedClass, true);
                continue;
            }

            RecordOnlineAccuracyTrial(trialId, GetOnlineAccuracyTrueTarget(trialId), decodedClass, true);

            flasher.ShowFeedback(decodedClass);
            yield return new WaitForSeconds(feedbackSeconds);
            flasher.ClearHighlight();

            SetDriveView();
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
                SetStatusText(waitingText);
                yield return new WaitForSeconds(interTrialSeconds);
                SetStatusText("");
            }

            int cueTarget = GetCueTarget(trialId);

            if (useCue)
            {
                markerSender.SendRawMarkerToLslBridge($"{cueStartMarker};label={cueTarget};key={cueTarget + 1}");
                flasher.ShowCue(cueTarget);
                yield return new WaitForSeconds(cueSeconds);
                flasher.ClearHighlight();
                markerSender.SendRawMarkerToLslBridge(cueStopMarker);
            }

            SetStatusText(recognizingText);
            markerSender.SendRawMarkerToLslBridge(startTrialMarker);
            flasher.StartFlashing();
            yield return new WaitForSeconds(trialDurationSeconds);
            flasher.StopFlashing();
            markerSender.SendRawMarkerToLslBridge(stopTrialMarker);
            SetStatusText("");
        }
    }

    IEnumerator RunDebugDriveMove(int destinationIndex)
    {
        if (!IsValidTarget(destinationIndex))
        {
            Debug.LogWarning("Debug destination outside Unity target range: " + destinationIndex);
            yield break;
        }

        isDebugMoving = true;
        flasher.ClearHighlight();
        SetDriveView();
        SetStatusText(movingText);
        carMover.SetDestination(destinationIndex);

        while (!carMover.HasArrived)
        {
            yield return null;
        }

        yield return new WaitForSeconds(postArrivalDriveViewSeconds);
        SetSelectionView();
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

    void SetSelectionView()
    {
        ApplySelectionMaterials();

        if (selectionCamera != null)
        {
            selectionCamera.enabled = true;
            SetAudioListener(selectionCamera, true);
        }

        if (driveCamera != null)
        {
            driveCamera.enabled = false;
            SetAudioListener(driveCamera, false);
        }
    }

    void SetDriveView()
    {
        ApplyDriveMaterials();

        if (selectionCamera != null)
        {
            selectionCamera.enabled = false;
            SetAudioListener(selectionCamera, false);
        }

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

    void SetStatusText(string text)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }
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

        string modeLabel = string.IsNullOrWhiteSpace(accuracyModeLabel) ? "Online" : accuracyModeLabel.Trim();
        string modeSlug = Slugify(modeLabel);
        if (string.IsNullOrEmpty(modeSlug))
        {
            modeSlug = "online";
        }
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string pngPath = Path.Combine(outputDir, "online_game_accuracy_" + modeSlug + "_" + timestamp + ".png");

        Texture2D plot = BuildOnlineAccuracyPngTexture();
        File.WriteAllBytes(pngPath, plot.EncodeToPNG());
        Destroy(plot);

        Debug.Log("Saved online accuracy PNG: " + pngPath);
        Application.OpenURL(new Uri(pngPath).AbsoluteUri);
    }

    Texture2D BuildOnlineAccuracyPngTexture()
    {
        int nTrials = onlineAccuracyRows.Count;
        List<string> labels = GetAccuracyLabels();
        Dictionary<string, int> labelToY = new Dictionary<string, int>();
        for (int i = 0; i < labels.Count; i++)
        {
            labelToY[labels[i]] = i;
        }

        const int width = 1000;
        const int height = 620;
        const int left = 90;
        const int right = 40;
        const int top = 80;
        const int bottom = 90;
        int plotWidth = width - left - right;
        int plotHeight = height - top - bottom;
        float trialStep = nTrials > 1 ? plotWidth / (float)(nTrials - 1) : 0f;
        float yStep = labels.Count > 1 ? plotHeight / (float)(labels.Count - 1) : 0f;

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        FillTexture(texture, Color.white);

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
        }

        DrawLine(texture, left, top, left, top + plotHeight, new Color(0.15f, 0.15f, 0.15f), 2);
        DrawLine(texture, left, top + plotHeight, left + plotWidth, top + plotHeight, new Color(0.15f, 0.15f, 0.15f), 2);

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

        DrawLegend(texture, width - 230, height - 58);
        texture.Apply();
        return texture;
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
        DrawX(texture, x + 100, y, 7, new Color(0.84f, 0.37f, 0.00f), 3);
        DrawLine(texture, x + 118, y, x + 172, y, new Color(0.84f, 0.37f, 0.00f), 3);
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

        if (runMode != BciRunMode.Calibration || !useCue || !useBalancedRandomCueTargets)
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

        System.Random random = randomSeed >= 0 ? new System.Random(randomSeed) : new System.Random();

        Shuffle(remainderTargets, random);
        for (int i = 0; i < remainder; i++)
        {
            targets.Add(remainderTargets[i]);
        }

        Shuffle(targets, random);
        runtimeCueTargets = targets.ToArray();

        Debug.Log("Unity cue target order: " + string.Join(", ", System.Array.ConvertAll(runtimeCueTargets, item => item.ToString())));
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
