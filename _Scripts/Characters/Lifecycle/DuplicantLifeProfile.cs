using UnityEngine;

[CreateAssetMenu(
    fileName = "NewDuplicantLifeProfile",
    menuName = "Duplicants/Life Profile"
)]
public class DuplicantLifeProfile : ScriptableObject
{
    [Header("Capacidades")]
    [Min(1f)] public float maximumEnergy = 100f;
    [Min(1f)] public float maximumHunger = 100f;

    [Header("Metabolismo")]
    [Min(0.01f)] public float energyDrainMultiplier = 1f;
    [Min(0.01f)] public float hungerDrainMultiplier = 1f;
    [Min(0.01f)] public float restRecoveryMultiplier = 1f;
}
