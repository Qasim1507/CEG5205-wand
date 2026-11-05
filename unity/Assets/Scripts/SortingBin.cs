using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class SortingBin : MonoBehaviour
{
    BoxCollider m_Interior;

    void Awake()
    {
        m_Interior = GetComponent<BoxCollider>();
    }

    public bool Contains(GameObject box)
    {
        if (box == null)
            return false;

        var boxCollider = box.GetComponent<Collider>();
        return boxCollider != null && m_Interior.bounds.Contains(boxCollider.bounds.center);
    }
}
