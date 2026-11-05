using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Educational VR heart-replacement challenge. The anatomy and sequence are
/// stylized gameplay and do not represent a clinical surgical procedure.
/// </summary>
public sealed class SurgeryGameController : MonoBehaviour
{
    [SerializeField] QuestHandRobotController robotControl;
    [SerializeField] Rigidbody nativeHeart;
    [SerializeField] Rigidbody donorHeart;
    [SerializeField] BoxCollider discardTray;
    [SerializeField] SphereCollider heartSocket;
    [SerializeField] TextMesh hud;
    [SerializeField] TextMesh banner;

    bool started;
    bool nativeRemoved;
    bool complete;
    float startTime;
    float completeTime;
    float restartHold;
    float messageUntil;
    int score;
    int misses;
    string message = "Left pinch: move arm   |   Right pinch: grip";

    void Awake()
    {
        if (robotControl == null || nativeHeart == null || donorHeart == null ||
            discardTray == null || heartSocket == null || hud == null || banner == null)
        {
            Debug.LogError("Heart replacement game has missing scene references.");
            enabled = false;
            return;
        }
        // Imported URDF tool colliders must not push the patient, room or the
        // kinematically carried hearts. The grasp helper handles pickup.
        var robot = GameObject.Find("niryo_one");
        var field = GameObject.Find("Surgery training field");
        if (robot != null && field != null)
        {
            var robotColliders = robot.GetComponentsInChildren<Collider>(true);
            var fieldColliders = field.GetComponentsInChildren<Collider>(true);
            foreach (var robotCollider in robotColliders)
            {
                foreach (var fieldCollider in fieldColliders)
                    Physics.IgnoreCollision(robotCollider, fieldCollider, true);
                Physics.IgnoreCollision(robotCollider, nativeHeart.GetComponent<Collider>(), true);
                Physics.IgnoreCollision(robotCollider, donorHeart.GetComponent<Collider>(), true);
            }
        }
        banner.text = "HEART REPLACEMENT\n1. Remove native heart  2. Place donor heart";
    }

    void OnEnable()
    {
        if (robotControl == null) return;
        robotControl.ControlEngaged += StartProcedure;
        robotControl.ObjectGrasped += OnGrasped;
        robotControl.ObjectReleased += OnReleased;
    }

    void OnDisable()
    {
        if (robotControl == null) return;
        robotControl.ControlEngaged -= StartProcedure;
        robotControl.ObjectGrasped -= OnGrasped;
        robotControl.ObjectReleased -= OnReleased;
    }

    void StartProcedure()
    {
        if (started || complete) return;
        started = true;
        startTime = Time.time;
        Announce("Move the native heart to the left disposal tray", 4f);
    }

    void OnGrasped(Rigidbody item)
    {
        if (complete) return;
        if (item == nativeHeart && !nativeRemoved)
            Announce("Native heart secured. Move it to the left tray.", 3f);
        else if (item == donorHeart)
            Announce(nativeRemoved ? "Place donor heart into the open chest" :
                "Remove the native heart first", 3f);
    }

    void OnReleased(Rigidbody item)
    {
        if (complete) return;
        if (item == nativeHeart && !nativeRemoved)
        {
            if (InBounds(discardTray, item.position, 0.04f))
            {
                nativeRemoved = true;
                score += 100;
                item.isKinematic = true;
                item.useGravity = false;
                item.position = discardTray.bounds.center;
                item.gameObject.SetActive(false);
                banner.text = "NATIVE HEART REMOVED\nPlace the donor heart in the open chest";
                Announce("Native heart removed: +100", 4f);
                Debug.Log("Heart game: native heart removed");
            }
            else Miss("Move the native heart to the left disposal tray");
        }
        else if (item == donorHeart)
        {
            if (!nativeRemoved)
            {
                Miss("Remove the native heart before implanting the donor heart");
                return;
            }
            if (Vector3.Distance(item.position, heartSocket.transform.position) <= 0.065f)
            {
                item.isKinematic = true;
                item.useGravity = false;
                item.position = heartSocket.transform.position;
                item.rotation = Quaternion.identity;
                complete = true;
                completeTime = Time.time - startTime;
                score += 150 + Mathf.Max(0, 120 - Mathf.RoundToInt(completeTime));
                banner.text = "HEART REPLACEMENT COMPLETE\nHold both pinches for 2 s to replay";
                Announce("Donor heart placed: +150", 4f);
                Debug.Log("Heart game complete: score=" + score +
                    " misses=" + misses + " time=" + completeTime.ToString("F1") + "s");
            }
            else Miss("Release the donor heart inside the open chest");
        }
    }

    static bool InBounds(BoxCollider target, Vector3 point, float margin)
    {
        var bounds = target.bounds;
        bounds.Expand(margin);
        return bounds.Contains(point);
    }

    void Miss(string instruction)
    {
        misses++;
        score = Mathf.Max(0, score - 5);
        Announce(instruction + "  (-5)", 4f);
    }

    void Update()
    {
        if (hud == null) return;
        var elapsed = complete ? completeTime : (started ? Time.time - startTime : 0f);
        var stage = complete ? "COMPLETE" : nativeRemoved ? "STEP 2/2: DONOR HEART" :
            "STEP 1/2: REMOVE HEART";
        var instruction = Time.time < messageUntil ? message :
            complete ? "Hold both pinches to replay" : nativeRemoved ?
            "Move donor heart from right tray into chest" :
            "Move native heart into left tray";
        if (complete)
        {
            restartHold = robotControl.TwoHandPinchHeld ? restartHold + Time.deltaTime : 0f;
            if (restartHold >= 2f)
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
        hud.text = stage + "   SCORE " + score + "\n" +
            "TIME " + elapsed.ToString("F1") + "s   MISSES " + misses + "\n" +
            instruction;
    }

    void Announce(string text, float seconds)
    {
        message = text;
        messageUntil = Time.time + seconds;
    }
}
