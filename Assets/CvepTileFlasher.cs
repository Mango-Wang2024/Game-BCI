using System;
using UnityEngine;

[Serializable]
public class CvepCodeData
{
    public int presentationRateHz;
    public int codeLength;
    public int[] subset;
    public int[] code0;
    public int[] code1;
    public int[] code2;
    public int[] code3;
    public int[] code4;
    public int[] code5;
    public int[] code6;
    public int[] code7;
    public int[] code8;
    public int[] code9;
    public int[] code10;
    public int[] code11;
    public int[] code12;
    public int[] code13;
    public int[] code14;
    public int[] code15;
}

public class CvepTileFlasher : MonoBehaviour
{
    public Transform[] destinationTiles;
    public Material tileDarkMaterial;
    public Material tileBrightMaterial;
    public Material tileCueMaterial;
    public Material tileFeedbackMaterial;

    private CvepCodeData codeData;
    private int[][] codes;
    private Renderer[] tileRenderers;

    private bool isFlashing = false;
    private int frameIndex = 0;
    private float frameTimer = 0f;

    void Start()
    {
        TextAsset json = Resources.Load<TextAsset>("cvep_codes_unity");

        if (json == null)
        {
            Debug.LogError("Could not find Resources/cvep_codes_unity.json");
            return;
        }

        codeData = JsonUtility.FromJson<CvepCodeData>(json.text);

        codes = BuildCodeArray();

        if (destinationTiles == null)
        {
            Debug.LogError("No destination tiles assigned to CvepTileFlasher.");
            tileRenderers = new Renderer[0];
            return;
        }

        tileRenderers = new Renderer[destinationTiles.Length];

        for (int i = 0; i < destinationTiles.Length; i++)
        {
            if (destinationTiles[i] != null)
            {
                tileRenderers[i] = destinationTiles[i].GetComponent<Renderer>();
            }
        }
    }

    void Update()
    {
        if (isFlashing)
        {
            FlashOneFrame();
        }
    }

    public void StartFlashing()
    {
        if (codeData == null || codes == null || codes.Length == 0)
        {
            return;
        }

        isFlashing = true;
        frameIndex = 0;
        frameTimer = 0f;
    }

    public void StopFlashing()
    {
        isFlashing = false;
        SetAllTiles(tileDarkMaterial);
    }

    public void ShowCue(int targetIndex)
    {
        ShowSingleTarget(targetIndex, tileCueMaterial != null ? tileCueMaterial : tileBrightMaterial);
    }

    public void ShowFeedback(int targetIndex)
    {
        ShowSingleTarget(targetIndex, tileFeedbackMaterial != null ? tileFeedbackMaterial : tileBrightMaterial);
    }

    public void ClearHighlight()
    {
        StopFlashing();
    }

    void FlashOneFrame()
    {
        frameTimer += Time.deltaTime;
        float frameDuration = 1f / codeData.presentationRateHz;

        if (frameTimer < frameDuration)
        {
            return;
        }

        frameTimer -= frameDuration;

        for (int i = 0; i < tileRenderers.Length && i < codes.Length; i++)
        {
            if (codes[i] == null || codes[i].Length == 0)
            {
                continue;
            }

            int bit = codes[i][frameIndex % codeData.codeLength];

            if (tileRenderers[i] != null)
            {
                tileRenderers[i].material = bit == 1 ? tileBrightMaterial : tileDarkMaterial;
            }
        }

        frameIndex++;
    }

    int[][] BuildCodeArray()
    {
        int[][] allCodes = new int[][]
        {
            codeData.code0,
            codeData.code1,
            codeData.code2,
            codeData.code3,
            codeData.code4,
            codeData.code5,
            codeData.code6,
            codeData.code7,
            codeData.code8,
            codeData.code9,
            codeData.code10,
            codeData.code11,
            codeData.code12,
            codeData.code13,
            codeData.code14,
            codeData.code15
        };

        int tileCount = destinationTiles != null ? destinationTiles.Length : 0;
        int nCodes = Mathf.Min(tileCount, allCodes.Length);
        int[][] selectedCodes = new int[nCodes][];

        for (int i = 0; i < nCodes; i++)
        {
            selectedCodes[i] = allCodes[i];
        }

        return selectedCodes;
    }

    void SetAllTiles(Material material)
    {
        if (tileRenderers == null)
        {
            return;
        }

        foreach (Renderer renderer in tileRenderers)
        {
            if (renderer != null)
            {
                renderer.material = material;
            }
        }
    }

    void ShowSingleTarget(int targetIndex, Material material)
    {
        StopFlashing();

        if (tileRenderers == null || targetIndex < 0 || targetIndex >= tileRenderers.Length)
        {
            return;
        }

        if (tileRenderers[targetIndex] != null)
        {
            tileRenderers[targetIndex].material = material;
        }
    }
}
