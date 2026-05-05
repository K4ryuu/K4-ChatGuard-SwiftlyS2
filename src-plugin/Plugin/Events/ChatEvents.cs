using K4ChatGuard.Config;
using K4ChatGuard.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace K4ChatGuard.Events;

public class ChatEvents
{
	private readonly ISwiftlyCore _core;
	private readonly IOptionsMonitor<PluginConfig> _config;
	private readonly BlacklistService _blacklistService;
	private readonly IpFilterService _ipFilterService;
	private readonly SpamProtectionService _spamService;
	private readonly NameFilterService? _nameFilterService;

	public ChatEvents(
		ISwiftlyCore core,
		IOptionsMonitor<PluginConfig> config,
		BlacklistService blacklistService,
		IpFilterService ipFilterService,
		SpamProtectionService spamService,
		NameFilterService? nameFilterService = null)
	{
		_core = core;
		_config = config;
		_blacklistService = blacklistService;
		_ipFilterService = ipFilterService;
		_spamService = spamService;
		_nameFilterService = nameFilterService;

		core.Registrator.Register(this);

		if (_config.CurrentValue.BlockBadNames && _nameFilterService != null)
		{
			core.GameEvent.HookPost<EventPlayerChangename>(OnPlayerChangeName);
			core.GameEvent.HookPost<EventPlayerActivate>(OnClientActivated);
		}
	}

	[ClientChatHookHandler]
	public HookResult OnClientChat(int playerId, string text, bool teamonly)
	{
		var player = _core.PlayerManager.GetPlayer(playerId);
		if (player == null || player.IsFakeClient)
			return HookResult.Continue;

		var cfg = _config.CurrentValue;

		var bypassAll = _core.Permission.PlayerHasPermission(player.SteamID, "chatguard.bypass.all");

		if (!bypassAll && cfg.SpamProtection.Enabled)
		{
			var bypassSpam = _core.Permission.PlayerHasPermission(player.SteamID, "chatguard.bypass.spam");

			if (!bypassSpam && _spamService.IsChatSpam(player.SteamID))
			{
				SendLocalized(player, "spam.chat.warning");
				_core.Logger.LogInformation($"Chat spam detected from {player.Controller.PlayerName} ({player.SteamID})");
				return HookResult.Stop;
			}
		}

		if (ShouldBlockText(player, text))
			return HookResult.Stop;

		if (cfg.BlockBadNames && _nameFilterService != null)
			_nameFilterService.CheckAndRenameBadName(player);

		return HookResult.Continue;
	}

	public HookResult HandleChatMessage(CUserMessageSayText2 msg)
	{
		if (msg.Entityindex <= 0)
			return HookResult.Continue;

		var player = _core.PlayerManager.GetPlayer(msg.Entityindex)
			?? _core.PlayerManager.GetPlayer(msg.Entityindex - 1);

		if (player == null || player.IsFakeClient)
			return HookResult.Continue;

		var text = msg.Param2;

		return ShouldBlockText(player, text)
			? HookResult.Stop
			: HookResult.Continue;
	}

	private bool ShouldBlockText(IPlayer player, string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return false;

		var cfg = _config.CurrentValue;
		var steamId = player.SteamID;

		var bypassAll = _core.Permission.PlayerHasPermission(steamId, "chatguard.bypass.all");

		if (!bypassAll)
		{
			var bypassBlacklist = _core.Permission.PlayerHasPermission(steamId, "chatguard.bypass.blacklist");

			if (!bypassBlacklist && _blacklistService.ContainsBlacklistedWord(text, out var matchedWord))
			{
				SendLocalized(player, "blacklist.blocked");
				_core.Logger.LogInformation($"Blocked blacklisted word '{matchedWord}' from {player.Controller.PlayerName} ({steamId})");
				return true;
			}
		}

		if (!bypassAll && cfg.BlockIpAddresses)
		{
			var bypassIpFilter = _core.Permission.PlayerHasPermission(steamId, "chatguard.bypass.ipfilter");

			if (!bypassIpFilter && _ipFilterService.ContainsIpAddress(text, out var detectedIp))
			{
				SendLocalized(player, "ipfilter.blocked");
				_core.Logger.LogInformation($"Blocked IP address '{detectedIp}' from {player.Controller.PlayerName} ({steamId})");
				return true;
			}
		}

		return false;
	}

	private void SendLocalized(IPlayer player, string key)
	{
		var localizer = _core.Translation.GetPlayerLocalizer(player);
		var prefix = localizer["prefix"];
		player.SendChat(localizer[key, prefix]);
	}

	public HookResult OnClientActivated(EventPlayerActivate @event)
	{
		if (!_config.CurrentValue.BlockBadNames || _nameFilterService == null)
			return HookResult.Continue;

		var player = _core.PlayerManager.GetPlayer(@event.UserId);
		if (player == null)
			return HookResult.Continue;

		_nameFilterService.CheckAndRenameBadName(player);
		return HookResult.Continue;
	}

	[EventListener<EventDelegates.OnClientDisconnected>]
	public void OnClientDisconnected(IOnClientDisconnectedEvent e)
	{
		var player = _core.PlayerManager.GetPlayer(e.PlayerId);
		if (player == null)
			return;

		_spamService.ClearPlayerData(player.SteamID);
	}

	public HookResult OnPlayerChangeName(EventPlayerChangename e)
	{
		var player = e.UserIdPlayer;
		if (player == null || _nameFilterService == null)
			return HookResult.Continue;

		_nameFilterService.CheckAndRenameBadName(player);
		return HookResult.Continue;
	}
}
