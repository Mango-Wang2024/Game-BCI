using UnityEngine;

public class DestinationCarMover : MonoBehaviour
{
    private const int MaxTargetCount = 16;

    public Transform car;
    public Transform[] destinationTiles;

    public float moveSpeed = 5f;
    public float rotateSpeed = 8f;
    public float stopDistance = 0.2f;

    private Transform currentDestination;
    public bool HasArrived { get; private set; } = true;

    void Awake()
    {
        NormalizeDestinationTiles();
    }

    void Update()
    {
        MoveCar();
    }

    public void SetDestination(int index)
    {
        if (destinationTiles == null || index < 0 || index >= destinationTiles.Length || destinationTiles[index] == null)
        {
            Debug.LogWarning("Destination index outside range: " + index);
            return;
        }

        HasArrived = false;
        currentDestination = destinationTiles[index];
    }

    void MoveCar()
    {
        if (car == null || currentDestination == null)
            return;

        Vector3 target = currentDestination.position;
        target.y = car.position.y;

        Vector3 direction = target - car.position;

        if (direction.magnitude <= stopDistance)
        {
            currentDestination = null;
            HasArrived = true;
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        car.rotation = Quaternion.Slerp(car.rotation, targetRotation, rotateSpeed * Time.deltaTime);
        car.position = Vector3.MoveTowards(car.position, target, moveSpeed * Time.deltaTime);
    }

    void NormalizeDestinationTiles()
    {
        if (destinationTiles == null || destinationTiles.Length == 0)
        {
            destinationTiles = FindDestinationTilesByName();
        }

        System.Array.Sort(destinationTiles, CompareTileNames);

        if (destinationTiles.Length <= MaxTargetCount)
        {
            return;
        }

        Transform[] firstTargets = new Transform[MaxTargetCount];
        System.Array.Copy(destinationTiles, firstTargets, MaxTargetCount);
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
}
