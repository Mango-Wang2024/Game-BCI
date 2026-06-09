using UnityEngine;

public class DestinationCarMover : MonoBehaviour
{
    public Transform car;
    public Transform[] destinationTiles;

    public float moveSpeed = 5f;
    public float rotateSpeed = 8f;
    public float stopDistance = 0.2f;

    private Transform currentDestination;
    public bool HasArrived { get; private set; } = true;

    void Update()
    {
        MoveCar();
    }

    public void SetDestination(int index)
    {
        if (destinationTiles == null || index >= destinationTiles.Length)
            return;

        HasArrived = false;
        currentDestination = destinationTiles[index];

        CvepTileFlasher flasher = GetComponent<CvepTileFlasher>();
        if (flasher != null)
            flasher.StopFlashing();
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
}
