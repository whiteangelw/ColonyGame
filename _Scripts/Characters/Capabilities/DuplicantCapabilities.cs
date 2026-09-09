using UnityEngine;

public class DuplicantCapabilities : MonoBehaviour
{
    [SerializeField]
    private DuplicantCapabilityProfile baseProfile;

    // Propriedade para acessar o perfil ativo
    public DuplicantCapabilityProfile Profile => baseProfile;

    /// <summary>
    /// Valida se uma intersecção ou pulo está dentro dos limites físicos do personagem.
    /// </summary>
    public bool CanPerformJump(int gapWidth)
    {
        return gapWidth <= baseProfile.maxJumpGap;
    }

    /// <summary>
    /// Valida se uma escalada/degrau está dentro dos limites de altura do personagem.
    /// </summary>
    public bool CanStepUp(int height)
    {
        return height <= baseProfile.maxStepUp;
    }

    /// <summary>
    /// Valida se o nó alvo está dentro da área de alcance (4 blocos a partir dos pés).
    /// </summary>
    public bool IsInReachRange(Vector2Int feetPosition, Vector2Int targetPosition)
    {
        int dx = Mathf.Abs(feetPosition.x - targetPosition.x);
        int dy = Mathf.Abs(feetPosition.y - targetPosition.y);

        // Regra do alcance customizável: 2 para cima, 1 para baixo, 2 para os lados
        bool horizontalValid = dx <= 2;
        bool verticalValid = (targetPosition.y >= feetPosition.y - 1) && (targetPosition.y <= feetPosition.y + 2);

        return horizontalValid && verticalValid;
    }
}