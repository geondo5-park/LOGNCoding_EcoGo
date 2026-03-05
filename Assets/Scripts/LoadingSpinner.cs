using UnityEngine;

public class LoadingSpinner : MonoBehaviour
{
    [SerializeField] private float rotateSpeed = 360f;

    private void Update()
    {
        transform.Rotate(0, 0, -rotateSpeed * Time.deltaTime);
    }
}
