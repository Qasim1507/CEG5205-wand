using System;
using System.Collections;
using UnityEngine;

public enum DummyElement { Fire, Water, Wind, Lightning }

[Serializable]
public class CrystalType
{
  public Material material;
  [Tooltip("The spell that kills the dummy while it shows this crystal.")]
  public DummyElement element;
}

// Put on the dummy (same object as the collider).
// The crystal child (tag "DummyType") shows which element can kill the dummy.
// Call TryHit(spell): the dummy only dies if the spell matches the crystal's element.
// While down it plays "died" (Animator bool "isHit") for deadTime seconds, the crystal is hidden,
// then it returns to "pushed", the crystal reappears with a NEW random type, and sliding resumes.
public class DummyHitReaction : MonoBehaviour
{
  [SerializeField] private Animator animator;
  [SerializeField] private SlidingDummy slider;      // optional: stop sliding while "died"
  [SerializeField] private float deadTime = 5f;

  [Header("Recovery")]
  [Tooltip("Exact name of the default/idle state in the Animator Controller.")]
  [SerializeField] private string pushedStateName = "pushed";
  [Tooltip("Extra pause (seconds) after returning to 'pushed' before sliding resumes.")]
  [SerializeField] private float resumeDelay = 0f;
  [Tooltip("Safety limit so a wrong state name can never freeze the dummy.")]
  [SerializeField] private float recoverTimeout = 3f;

  [Header("Crystal types (material + the spell that kills it)")]
  [SerializeField] private CrystalType[] crystalTypes;
  [SerializeField] private string crystalTag = "DummyType";

  [Header("Debug (read-only, updates in Play mode)")]
  [SerializeField] private string currentRequiredSpell;

  private static readonly int IsHit = Animator.StringToHash("isHit");
  private bool _dead;
  private GameObject _crystalRoot;   // the object tagged "DummyType" (shown/hidden as a whole)
  private Renderer _crystal;         // the renderer whose material is swapped
  private int _typeIndex = -1;

  public bool IsDown => _dead;
  public string RequiredSpell { get; private set; }    // canonical spell name: fire / water / wind / lightning
  public Material CurrentCrystalMaterial => _crystal != null ? _crystal.sharedMaterial : null;

  private void Awake()
  {
    // Find the child tagged "DummyType" (includes inactive children), then the renderer on/below it.
    foreach (var t in GetComponentsInChildren<Transform>(true))
    {
      if (!t.CompareTag(crystalTag)) continue;
      _crystalRoot = t.gameObject;
      _crystal = t.GetComponentInChildren<Renderer>(true);
      break;
    }
    if (_crystalRoot == null)
      Debug.LogError("DummyHitReaction: no child tagged '" + crystalTag + "' found.");

    PickNewCrystalType();                            // random type at start
    SetCrystalVisible(true);
  }

  // Kills the dummy only if `spell` matches the crystal's element. Returns true if it was killed.
  public bool TryHit(string spell)
  {
    if (_dead || animator == null) return false;
    bool match = !string.IsNullOrEmpty(RequiredSpell) &&
                 string.Equals(spell, RequiredSpell, StringComparison.OrdinalIgnoreCase);
    Debug.Log("[match] cast=" + spell + "  required=" + (RequiredSpell ?? "NONE") +
              "  -> " + (match ? "KILL" : "no effect"));
    if (!match) return false;
    StartCoroutine(DieAndRecover());
    return true;
  }

  private IEnumerator DieAndRecover()
  {
    _dead = true;
    if (slider != null) slider.enabled = false;
    SetCrystalVisible(false);                        // crystal disappears when the dummy is hit
    animator.SetBool(IsHit, true);                   // pushed -> died

    yield return new WaitForSeconds(deadTime);

    animator.SetBool(IsHit, false);                  // died -> pushed begins

    // Wait until the Animator is really back in "pushed" and the blend has finished.
    yield return null;                               // give the Animator a frame to start the transition
    float timeout = recoverTimeout;
    while (timeout > 0f)
    {
      var info = animator.GetCurrentAnimatorStateInfo(0);
      if (info.IsName(pushedStateName) && !animator.IsInTransition(0)) break;
      timeout -= Time.deltaTime;
      yield return null;
    }

    PickNewCrystalType();                            // new element while the crystal is still hidden
    SetCrystalVisible(true);                         // crystal reappears now that the dummy is restored

    if (resumeDelay > 0f) yield return new WaitForSeconds(resumeDelay);

    if (slider != null) slider.enabled = true;
    _dead = false;                                   // can be hit again only once fully recovered
  }

  private void SetCrystalVisible(bool visible)
  {
    if (_crystalRoot != null) _crystalRoot.SetActive(visible);
  }

  // Picks a random crystal type (never the same twice in a row) and records the spell it needs.
  // If nothing valid is configured, NO spell can kill the dummy (and an error is logged).
  private void PickNewCrystalType()
  {
    RequiredSpell = null;
    currentRequiredSpell = "NONE (check Crystal Types)";

    if (crystalTypes == null || crystalTypes.Length == 0)
    {
      Debug.LogError("DummyHitReaction: Crystal Types is empty. Add 4 entries (material + element).");
      return;
    }

    int next = UnityEngine.Random.Range(0, crystalTypes.Length);
    if (crystalTypes.Length > 1)
      while (next == _typeIndex) next = UnityEngine.Random.Range(0, crystalTypes.Length);
    _typeIndex = next;

    var type = crystalTypes[next];
    if (type == null || type.material == null)
    {
      Debug.LogError("DummyHitReaction: Crystal Types entry " + next + " has no material assigned.");
      return;
    }

    if (_crystal != null) _crystal.sharedMaterial = type.material;   // swaps the asset reference
    RequiredSpell = type.element.ToString().ToLowerInvariant();      // "fire", "water", "wind", "lightning"
    currentRequiredSpell = RequiredSpell + "  (" + type.material.name + ")";
  }
}