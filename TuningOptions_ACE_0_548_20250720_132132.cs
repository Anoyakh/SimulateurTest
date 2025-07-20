public class TuningOptions548 : ITuningOptions
{
    /// <summary>Mort mort mort</summary>
    public float DeathPenalty { get; set; } = 8081.3574f;

    /// <summary>x case contrôlées</summary>
    public float TerritoryWeight { get; set; } = 5.0497f;

    /// <summary>x kill nettes</summary>
    public float KillWeight { get; set; } = 50.5288f;

    /// <summary>Δ somme (100–wetness)</summary>
    public float HealthDifferenceWeight { get; set; } = 0.0764f;

    /// <summary>wettest ennemi</summary>
    public float MaxWetnessWeight { get; set; } = 0.1251f;

    /// <summary>2+ agents dans même splash</summary>
    public float MultiHitPenalty { get; set; } = 39.2052f;

    /// <summary>land in enemy bomb zone</summary>
    public float DangerZonePenalty { get; set; } = 24.1444f;

    /// <summary>penalty for hazardZones (no‑shoot)</summary>
    public float HazardZonePenalty { get; set; } = 5.6161f;

    /// <summary>turn<4 movement penalty</summary>
    public float EarlyGamePenalty { get; set; } = 4.5863f;

    /// <summary>encourage rapprochement vers cibles</summary>
    public float ProximityBonus { get; set; } = 0.3349f;

    /// <summary>Bonus de couverture</summary>
    public float CoverBonus { get; set; } = 3.0501f;

    /// <summary>punition somme des cooldowns</summary>
    public float CooldownPenaltyFactor { get; set; } = 0.1118f;

    /// <summary>pour chaque shoot sans dégât</summary>
    public float WastedShootPenalty { get; set; } = 8.5808f;

    /// <summary>malus prime pour THROW</summary>
    public float ThrowWastePenalty { get; set; } = 12.0152f;

    /// <summary>touche cible</summary>
    public float ThrowCenterHitBonus { get; set; } = 11.8759f;

    /// <summary>touche case adj vide</summary>
    public float ThrowAdjacentHitBonus { get; set; } = 4.8011f;

    /// <summary>ennemi adjacent</summary>
    public float ThrowNearEnemyBonus { get; set; } = 2.1615f;

    // Beam‑search / permu (fixe, non tunable)
    public int TopPermutationsLimit { get; set; } = 600;
}
