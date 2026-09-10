using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace ZombiePlague.Infrastructure;

/// <summary>
/// Keeps the breakdown of the most recent zombie battle so the growth can be
/// checked against expectation instead of merely watched. Read it in game with
/// "zombie.last_battle".
///
/// Session-scoped diagnostics only, deliberately not saved.
/// </summary>
internal static class ZombieBattleReport
{
	private static string _lastReport = "Noch keine Zombie-Schlacht in dieser Sitzung.";

	public static string Last => _lastReport;

	public static void Record(
		MobileParty zombieParty,
		Dictionary<CharacterObject, int> died,
		Dictionary<CharacterObject, int> routed,
		Dictionary<CharacterObject, int> wounded,
		Dictionary<CharacterObject, int> captured,
		Dictionary<CharacterObject, int> total)
	{
		StringBuilder builder = new();
		builder.AppendLine(zombieParty.Name + " (" + zombieParty.StringId + ")");
		builder.AppendLine("Truppe | tot | geflohen | verwundet | gefangen | -> Zombie");

		int sum = 0;
		foreach (KeyValuePair<CharacterObject, int> entry in total)
		{
			died.TryGetValue(entry.Key, out int d);
			routed.TryGetValue(entry.Key, out int r);
			wounded.TryGetValue(entry.Key, out int w);
			captured.TryGetValue(entry.Key, out int c);

			CharacterObject zombie = ZombieConversion.MapToZombie(entry.Key);
			builder.AppendLine(string.Format(
				"{0} | {1} | {2} | {3} | {4} | {5}x {6}",
				entry.Key.StringId, d, r, w, c, entry.Value,
				zombie == null ? "<keine Variante>" : zombie.StringId));
			sum += entry.Value;
		}

		builder.AppendLine("Summe: " + sum + " neue Zombies");
		_lastReport = builder.ToString();

		ZombieLog.Info("battle report: " + zombieParty.StringId + " gains " + sum
			+ " across " + total.Count + " troop types");
	}
}
