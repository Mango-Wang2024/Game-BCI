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
    private const int MaxTargetCount = 16;

    public Transform[] destinationTiles;
    public Material tileDarkMaterial;
    public Material tileBrightMaterial;
    public Material tileCueMaterial;
    public Material tileFeedbackMaterial;
    public bool sortDestinationTilesByName = true;

    private CvepCodeData codeData;
    private int[][] codes;
    private Renderer[] tileRenderers;

    private bool isFlashing = false;
    private int frameIndex = 0;

    void Start()
    {
        TextAsset json = Resources.Load<TextAsset>("cvep_codes_unity");

        if (json == null)
        {
            Debug.LogError("Could not find Resources/cvep_codes_unity.json");
            return;
        }

        codeData = JsonUtility.FromJson<CvepCodeData>(json.text);
        ConfigurePresentationFrameRate();
        NormalizeDestinationTiles();

        if (destinationTiles == null || destinationTiles.Length == 0)
        {
            Debug.LogError("No destination tiles assigned to CvepTileFlasher.");
            tileRenderers = new Renderer[0];
            return;
        }

        codes = BuildCodeArray();

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

    void ConfigurePresentationFrameRate()
    {
        if (codeData == null || codeData.presentationRateHz <= 0)
        {
            return;
        }

        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = codeData.presentationRateHz;
        Debug.Log("[CHECK] cVEP flashing uses one code bit per rendered frame; target frame rate "
            + codeData.presentationRateHz + " Hz with VSync enabled.");
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

        int tileCount = destinationTiles != null ? Mathf.Min(destinationTiles.Length, MaxTargetCount) : 0;
        int nCodes = Mathf.Min(tileCount, allCodes.Length);
        int[][] selectedCodes = new int[nCodes][];

        for (int i = 0; i < nCodes; i++)
        {
            selectedCodes[i] = allCodes[i];
        }

        return selectedCodes;
    }

    void NormalizeDestinationTiles()
    {
        if (destinationTiles == null || destinationTiles.Length == 0)
        {
            destinationTiles = FindDestinationTilesByName();
        }

        if (sortDestinationTilesByName)
        {
            Array.Sort(destinationTiles, CompareTileNames);
        }

        if (destinationTiles.Length <= MaxTargetCount)
        {
            return;
        }

        Transform[] firstTargets = new Transform[MaxTargetCount];
        Array.Copy(destinationTiles, firstTargets, MaxTargetCount);
        destinationTiles = firstTargets;
    }

    Transform[] FindDestinationTilesByName()
    {
        Transform[] tiles = new Transform[MaxTargetCount];
        int foundCount = 0;

        for (int i = 0; i < MaxTargetCount; i++)
        {
            GameObject tileObject = GameObject.Find("DestinationTile_" + i);
            if (tileObject != null)
            {
                tiles[i] = tileObject.transform;
                foundCount++;
            }
        }

        if (foundCount == 0)
        {
            return new Transform[0];
        }

        return tiles;
    }

    int CompareTileNames(Transform left, Transform right)
    {
        int leftIndex = GetTrailingNumber(left != null ? left.name : "");
        int rightIndex = GetTrailingNumber(right != null ? right.name : "");
        return leftIndex.CompareTo(rightIndex);
    }

    int GetTrailingNumber(string text)
    {
        int multiplier = 1;
        int value = 0;
        bool foundDigit = false;

        for (int i = text.Length - 1; i >= 0; i--)
        {
            char ch = text[i];
            if (ch < '0' || ch > '9')
            {
                break;
            }

            foundDigit = true;
            value += (ch - '0') * multiplier;
            multiplier *= 10;
        }

        return foundDigit ? value : int.MaxValue;
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
