namespace ZombiePlague.Infrastructure;

internal static class ZombieIds
{
	public const string ClanId = "zombie_horde";
	public const string PartyTemplateId = "zombie_horde_template";
	public const string PartyIdPrefix = "zombie_plague_party";
	public const int MinTier = 1;
	public const int MaxTier = 6;

	/// <summary>Prefix shared by every zombie troop, generated or hand written.</summary>
	public const string TroopIdPrefix = "zombie_";

	/// <summary>The strong starter troop from zombie_troops.xml - see StartingTroopId.</summary>
	public const string PatientZeroTroopId = "zombie_patient_zero";

	/// <summary>Face templates from ModuleData/zombie_bodyproperties.xml.</summary>
	public static readonly string[] BodyPropertyIds = { "zombie_male", "zombie_female" };

	/// <summary>
	/// Index into the human skin colour palette. Indices 0-23 are the vanilla tones;
	/// ModuleData/skins.xml appends four green ones at 24-27. If that append does not
	/// take effect the index is out of range and the tint is skipped - see
	/// ZombieTroopSetup.
	/// </summary>
	public static int GreenSkinColorIndex { get; set; } = 25;

	/// <summary>
	/// The campaign-start crash this was originally added to isolate (see
	/// SiegeRelationChangeGuardPatch) is fixed, so this now defaults on: a new
	/// campaign spawns its starting horde(s) automatically after
	/// ZombieBehaviorConfig.SpawnDelayDays (see
	/// ZombiePlagueCampaignBehavior.TrySpawnStartingHordes). Toggle at runtime
	/// with zombie.toggle_autospawn, or force an immediate manual spawn with
	/// zombie.spawn_near_player.
	/// </summary>
	public static bool AutoSpawnOnNewGame { get; set; } = true;

	/// <summary>
	/// Debug/testing toggle: while on, the player's own map spotting range is
	/// replaced with ZombieBehaviorConfig.FarsightSeeingRange (see
	/// PlayerFarsightPatch), so zombie hordes stay visible without having to
	/// stay in normal sight range. Toggle at runtime with zombie.toggle_farsight.
	/// </summary>
	public static bool FarsightEnabled { get; set; }

	public static string TroopId(int tier)
	{
		int clamped = tier < MinTier ? MinTier : (tier > MaxTier ? MaxTier : tier);
		return "zombie_tier_" + clamped;
	}

	/// <summary>Id of the 1:1 zombie clone of a vanilla troop.</summary>
	public static string VariantId(string sourceTroopId)
	{
		return TroopIdPrefix + sourceTroopId;
	}

	/// <summary>
	/// Resolves ZombieBehaviorConfig.StartingTroopTypeIndex (an MCM dropdown
	/// index) to the actual troop id for the initial horde spawn.
	/// </summary>
	public static string StartingTroopId(int index)
	{
		return index switch
		{
			1 => "zombie_looter",
			2 => "zombie_imperial_legionary",
			_ => PatientZeroTroopId,
		};
	}
}
