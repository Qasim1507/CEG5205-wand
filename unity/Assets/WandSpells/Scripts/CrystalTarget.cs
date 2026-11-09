using System;
using UnityEngine;

// Attach this to the root GameObject of each crystal prefab.
public class CrystalTarget : MonoBehaviour {
    public enum SpellType { Fire, Water, Wind, Lightning }

    [Tooltip("Only this spell can shatter this crystal.")]
    public SpellType requiredSpell = SpellType.Fire;

    public bool CanShatter(string spellName) {
        return string.Equals(requiredSpell.ToString(), spellName,
            StringComparison.OrdinalIgnoreCase);
    }
}
