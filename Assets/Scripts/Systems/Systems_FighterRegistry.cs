using UnityEngine;

namespace PoSumo
{
    /// Every fighter's character sheet, in one Resources asset, so a built
    /// player can find a fighter BY NAME at runtime. Character sheets live under
    /// Assets/Agents/, not Resources/, and a player build only contains what a
    /// scene or a Resources asset references — so without this, a training env
    /// built from SCN_TRAIN_MATT could never become Tong.
    ///
    /// Read only by Systems_TrainingFighter. Asset: Resources/FighterRegistry.asset.
    [CreateAssetMenu(menuName = "PoSumo/Fighter Registry")]
    public sealed class Systems_FighterRegistry : ScriptableObject
    {
        public const string RESOURCE_PATH = "FighterRegistry";

        public Agent_CharacterDefinition[] fighters = new Agent_CharacterDefinition[0];

        public Agent_CharacterDefinition Find(string behaviorName)
        {
            for (int fighterIndex = 0; fighterIndex < fighters.Length; fighterIndex++)
            {
                Agent_CharacterDefinition fighter = fighters[fighterIndex];
                if (fighter != null && string.Equals(fighter.behaviorName, behaviorName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return fighter;
                }
            }
            return null;
        }
    }
}
