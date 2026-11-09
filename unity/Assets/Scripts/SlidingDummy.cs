using UnityEngine;

// Slides this object left and right around a FIXED centre (its position at startup), forever.
// Put it on the dummy. Moves in LOCAL space, so it follows the room/parent it sits in.
// Disabling/enabling the script pauses/resumes the motion without ever moving the centre.
public class SlidingDummy : MonoBehaviour
{
  [SerializeField] private float halfDistance = 2f;   // how far to each side of the centre (units)
  [SerializeField] private float speed = 1.5f;        // units per second (or radians/s if Smooth is on)
  [SerializeField] private bool smooth = true;        // true = eases at the ends, false = constant speed
  [SerializeField] private Vector3 axis = Vector3.right;   // local slide direction

  private Vector3 _center;   // captured once; never changes
  private float _t;          // motion phase; keeps its value while the script is disabled

  private void Awake()
  {
    _center = transform.localPosition;
  }

  private void Update()
  {
    _t += Time.deltaTime;

    float offset = smooth
        ? Mathf.Sin(_t * speed) * halfDistance
        : Mathf.PingPong(_t * speed + halfDistance, halfDistance * 2f) - halfDistance;

    transform.localPosition = _center + axis.normalized * offset;
  }

  // Optional: call to snap back to the centre and restart the motion (e.g. when restarting a level).
  public void ResetToCenter()
  {
    _t = 0f;
    transform.localPosition = _center;
  }
}