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

        codes = new int[][]
        {
            codeData.code0,
            codeData.code1,
            codeData.code2,
            codeData.code3
        };

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
        if (codeData == null || codes == null)
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

        for (int i = 0; i < destinationTiles.Length && i < codes.Length; i++)
        {
            int bit = codes[i][frameIndex % codeData.codeLength];

            if (tileRenderers[i] != null)
            {
                tileRenderers[i].material = bit == 1 ? tileBrightMaterial : tileDarkMaterial;
            }
        }

        frameIndex++;
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
