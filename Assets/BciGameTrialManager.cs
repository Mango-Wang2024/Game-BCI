using System.Collections;
using System.Collections.Generic;
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

    public TMP_Text statusText;
    public BciRunMode runMode = BciRunMode.OnlineGame;
    public string waitingText = "Waiting...";
    public string pressStartText = "Press C to start";
    public string recognizingText = "Start recognizing the target";
    public string movingText = "Moving to the destination";
    public string finishedText = "Finished";

    public int totalTrials = 10;
    public float firstReadySeconds = 5f;
    public float interTrialSeconds = 2f;
    public float cueSeconds = 0.7f;
    public float trialDurationSeconds = 4.2f;
    public float decoderWaitSeconds = 2f;
    public float feedbackSeconds = 0.7f;
    public float postArrivalDriveViewSeconds = 0.5f;
    public bool useCue = true;
    public bool useBalancedRandomCueTargets = true;
    public int randomSeed = -1;
    public int[] cueTargets = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    public string startTrialMarker = "start_trial";
    public string stopTrialMarker = "stop_trial";
    public string cueStartMarker = "start_cue";
    public string cueStopMarker = "stop_cue";
    public string forceDecisionMarker = "force_decision";

    private bool isRunning = false;
    private bool isDebugMoving = false;
    private bool canStart = false;
    private int[] runtimeCueTargets;

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

        if (!isRunning && !isDebugMoving)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) StartCoroutine(RunDebugDriveMove(0));
            if (Input.GetKeyDown(KeyCode.Alpha2)) StartCoroutine(RunDebugDriveMove(1));
            if (Input.GetKeyDown(KeyCode.Alpha3)) StartCoroutine(RunDebugDriveMove(2));
            if (Input.GetKeyDown(KeyCode.Alpha4)) StartCoroutine(RunDebugDriveMove(3));
        }

        FollowCarCamera();
    }

    IEnumerator RunTrials()
    {
        isRunning = true;
        canStart = false;
        PrepareCueTargets();
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
        isRunning = false;
    }

    IEnumerator RunOnlineGameTrials()
    {
        for (int trialId = 1; trialId <= totalTrials; trialId++)
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
                continue;
            }

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
        for (int trialId = 1; trialId <= totalTrials; trialId++)
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

    void PrepareCueTargets()
    {
        runtimeCueTargets = null;

        if (runMode != BciRunMode.Calibration || !useCue || !useBalancedRandomCueTargets)
        {
            return;
        }

        List<int> targets = new List<int>();
        int nTargets = 4;
        int repeats = totalTrials / nTargets;
        int remainder = totalTrials % nTargets;

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
