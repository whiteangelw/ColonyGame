using System;
using UnityEngine;

[Serializable]
public sealed class DuplicantIntentPhraseSet
{
    public bool enabled = true;
    public bool bypassGlobalCooldown;
    public Color color = Color.white;

    [TextArea(1, 3)]
    public string[] phrases = Array.Empty<string>();

    public string GetRandomPhrase(string previousPhrase)
    {
        if (!enabled || phrases == null || phrases.Length == 0)
        {
            return string.Empty;
        }

        if (phrases.Length == 1) return phrases[0] ?? string.Empty;

        int index = UnityEngine.Random.Range(0, phrases.Length);
        string selected = phrases[index] ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(previousPhrase)
            && selected == previousPhrase)
        {
            selected = phrases[(index + 1) % phrases.Length]
                ?? string.Empty;
        }

        return selected;
    }
}

[CreateAssetMenu(
    fileName = "NewDuplicantIntentPhrases",
    menuName = "Duplicants/Intent Phrase Catalog")]
public sealed class DuplicantIntentPhraseCatalogSO : ScriptableObject
{
    [Header("Apresentação")]
    [Min(0f)] public float minimumSecondsBetweenMessages = 2.5f;
    public Vector3 worldOffset = new Vector3(0f, 0.8f, 0f);

    [Header("Trabalho")]
    public DuplicantIntentPhraseSet dig = Create(
        new Color(0.85f, 0.75f, 0.45f),
        "Vou cavar ali.", "Hora de abrir caminho.");
    public DuplicantIntentPhraseSet build = Create(
        new Color(0.35f, 0.8f, 1f),
        "Vou construir isso.", "Vamos levantar essa estrutura.");
    public DuplicantIntentPhraseSet haul = Create(
        new Color(1f, 0.75f, 0.25f),
        "Vou transportar isso.", "Melhor guardar esse recurso.");
    public DuplicantIntentPhraseSet dismantle = Create(
        new Color(1f, 0.55f, 0.25f),
        "Vou desmontar isso.", "Isso precisa sair daqui.");
    public DuplicantIntentPhraseSet harvest = Create(
        new Color(0.45f, 1f, 0.45f),
        "Vou colher aquelas frutas.", "Parece uma boa colheita.");

    [Header("Alimentação")]
    public DuplicantIntentPhraseSet seekFood = Create(
        new Color(1f, 0.8f, 0.35f),
        "Vou procurar algo para comer.", "Preciso encontrar comida.");
    public DuplicantIntentPhraseSet eat = Create(
        new Color(0.55f, 1f, 0.45f),
        "Hora de comer.", "Isso deve ajudar.");
    public DuplicantIntentPhraseSet hungry = Create(
        new Color(1f, 0.75f, 0.35f),
        "Estou ficando com fome.");
    public DuplicantIntentPhraseSet starving = CreateUrgent(
        new Color(1f, 0.3f, 0.2f),
        "Preciso de comida agora.");

    [Header("Descanso")]
    public DuplicantIntentPhraseSet seekBed = Create(
        Color.cyan,
        "Preciso encontrar uma cama.", "Vou descansar um pouco.");
    public DuplicantIntentPhraseSet sleep = Create(
        Color.cyan,
        "Finalmente posso descansar.", "Só mais alguns minutos...");
    public DuplicantIntentPhraseSet emergencyRest = CreateUrgent(
        new Color(0.25f, 0.9f, 1f),
        "Não consigo continuar agora.");
    public DuplicantIntentPhraseSet tired = Create(
        new Color(0.6f, 0.85f, 1f),
        "Estou ficando cansado.");

    [Header("Falhas")]
    public DuplicantIntentPhraseSet workFailed = CreateUrgent(
        new Color(1f, 0.45f, 0.25f),
        "Não consigo fazer isso agora.", "Preciso tentar outra coisa.");

    public DuplicantIntentPhraseSet Get(DuplicantIntentType type)
    {
        switch (type)
        {
            case DuplicantIntentType.Dig: return dig;
            case DuplicantIntentType.Build: return build;
            case DuplicantIntentType.Haul: return haul;
            case DuplicantIntentType.Dismantle: return dismantle;
            case DuplicantIntentType.Harvest: return harvest;
            case DuplicantIntentType.SeekFood: return seekFood;
            case DuplicantIntentType.Eat: return eat;
            case DuplicantIntentType.Hungry: return hungry;
            case DuplicantIntentType.Starving: return starving;
            case DuplicantIntentType.SeekBed: return seekBed;
            case DuplicantIntentType.Sleep: return sleep;
            case DuplicantIntentType.EmergencyRest: return emergencyRest;
            case DuplicantIntentType.Tired: return tired;
            case DuplicantIntentType.WorkFailed: return workFailed;
            default: return null;
        }
    }

    private static DuplicantIntentPhraseSet Create(
        Color color,
        params string[] phrases)
    {
        return new DuplicantIntentPhraseSet
        {
            color = color,
            phrases = phrases
        };
    }

    private static DuplicantIntentPhraseSet CreateUrgent(
        Color color,
        params string[] phrases)
    {
        DuplicantIntentPhraseSet set = Create(color, phrases);
        set.bypassGlobalCooldown = true;
        return set;
    }
}
