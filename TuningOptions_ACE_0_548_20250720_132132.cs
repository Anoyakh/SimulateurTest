public class TuningOptionsCustom : ITuningOptions
{
    /// <summary>Mort mort mort</summary>
    public float DeathPenalty { get; set; } = 10000f;

    /// <summary>x case contrôlées</summary>
    public float TerritoryWeight { get; set; } = 2f;

    /// <summary>x kill nettes</summary>
    public float KillWeight { get; set; } = 55f;

    /// <summary>Δ somme (100–wetness)</summary>
    public float HealthDifferenceWeight { get; set; } = 0.075f;

    /// <summary>wettest ennemi</summary>
    public float MaxWetnessWeight { get; set; } = 0.15f;

    /// <summary>2+ agents dans même splash</summary>
    public float MultiHitPenalty { get; set; } = 44f;

    /// <summary>land in enemy bomb zone</summary>
    public float DangerZonePenalty { get; set; } = 33f;

    /// <summary>penalty for hazardZones (no‑shoot)</summary>
    public float HazardZonePenalty { get; set; } = 4f;

    /// <summary>turn<4 movement penalty</summary>
    public float EarlyGamePenalty { get; set; } = 4.5f;

    /// <summary>encourage rapprochement vers cibles</summary>
    public float ProximityBonus { get; set; } = 0.4f;

    /// <summary>Bonus de couverture</summary>
    public float CoverBonus { get; set; } = 2.9f;

    /// <summary>punition somme des cooldowns</summary>
    public float CooldownPenaltyFactor { get; set; } = 0.11f;

    /// <summary>pour chaque shoot sans dégât</summary>
    public float WastedShootPenalty { get; set; } = 8.5f;

    /// <summary>malus prime pour THROW</summary>
    public float ThrowWastePenalty { get; set; } = 8.3f;

    /// <summary>touche cible</summary>
    public float ThrowCenterHitBonus { get; set; } = 9.5f;

    /// <summary>touche case adj vide</summary>
    public float ThrowAdjacentHitBonus { get; set; } = 3.95f;

    /// <summary>ennemi adjacent</summary>
    public float ThrowNearEnemyBonus { get; set; } = 2.6f;

    // Beam‑search / permu (fixe, non tunable)
    public int TopPermutationsLimit { get; set; } = 600;
}
