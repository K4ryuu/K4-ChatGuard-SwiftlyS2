using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace K4ChatGuard.Services;

public class NameFilterService
{
	private readonly ISwiftlyCore _core;
	private readonly BlacklistService _blacklistService;
	private readonly ConcurrentDictionary<ulong, byte> _renamePending = new();

	private volatile string _replacementName = "Blocked";

	public NameFilterService(
		ISwiftlyCore core,
		BlacklistService blacklistService,
		string replacementName)
	{
		_core = core;
		_blacklistService = blacklistService;
		UpdateReplacement(replacementName);
	}

	public void UpdateReplacement(string replacementName)
	{
		_replacementName = string.IsNullOrWhiteSpace(replacementName)
			? "Blocked"
			: replacementName.Trim();

		_core.Logger.LogInformation($"Name Filter: Bad names will be replaced with '{_replacementName}'");
	}

	public void CheckAndRenameBadName(IPlayer player)
	{
		if (player.IsFakeClient)
			return;

		var oldName = player.Controller.PlayerName;
		if (string.IsNullOrWhiteSpace(oldName))
			return;

		if (!_blacklistService.ContainsBlacklistedWord(oldName, out var matchedWord))
			return;

		if (!_renamePending.TryAdd(player.SteamID, 1))
			return;

		var newName = $"{_replacementName}#{player.UserID}";

		_core.Logger.LogInformation(
			$"Renaming player '{oldName}' to '{newName}' (SteamID: {player.SteamID}) - contains blacklisted word '{matchedWord}'"
		);

		var localizer = _core.Translation.GetPlayerLocalizer(player);
		var prefix = localizer["prefix"];
		player.SendChat(localizer["name.blocked", prefix, newName]);

		_core.Scheduler.NextWorldUpdate(() =>
		{
			try
			{
				player.Controller.PlayerName = newName;
				player.Controller.PlayerNameUpdated();
			}
			finally
			{
				_renamePending.TryRemove(player.SteamID, out _);
			}
		});
	}
}
