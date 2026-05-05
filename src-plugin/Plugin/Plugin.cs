using K4ChatGuard.Config;
using K4ChatGuard.Events;
using K4ChatGuard.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.NetMessages;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.ProtobufDefinitions;
using Microsoft.Extensions.Logging;

namespace K4ChatGuard;

[PluginMetadata(Id = "k4.chatguard", Version = "1.0.2", Name = "K4 - ChatGuard", Author = "K4ryuu", Description = "Advanced chat protection with blacklist filtering, IP blocking, and spam prevention for CS2.")]
public sealed partial class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
	public static new ISwiftlyCore Core { get; private set; } = null!;

	internal IOptionsMonitor<PluginConfig> Config { get; private set; } = null!;
	internal BlacklistService BlacklistService { get; private set; } = null!;
	internal IpFilterService IpFilterService { get; private set; } = null!;
	internal SpamProtectionService SpamService { get; private set; } = null!;
	internal NameFilterService? NameFilterService { get; private set; }

	private ChatEvents? _chatEvents;
	private IDisposable? _configReload;

	public override void Load(bool hotReload)
	{
		Core = base.Core;

		const string ConfigFileName = "config.json";
		const string ConfigSection = "K4ChatGuard";

		Core.Configuration
			.InitializeJsonWithModel<PluginConfig>(ConfigFileName, ConfigSection)
			.Configure(cfg => cfg.AddJsonFile(
				Core.Configuration.GetConfigPath(ConfigFileName),
				optional: false,
				reloadOnChange: true
			));

		ServiceCollection services = new();
		services.AddSwiftly(Core)
			.AddOptionsWithValidateOnStart<PluginConfig>()
			.BindConfiguration(ConfigSection);

		var provider = services.BuildServiceProvider();
		Config = provider.GetRequiredService<IOptionsMonitor<PluginConfig>>();

		var cfg = Config.CurrentValue;

		Core.Logger.LogInformation($"ChatGuard config loaded words: {string.Join(", ", cfg.BlacklistedWords)}");

		BlacklistService = new BlacklistService(Core, cfg.BlacklistedWords);
		IpFilterService = new IpFilterService(Core, cfg.WhitelistedIPs);
		SpamService = new SpamProtectionService(
			Core,
			cfg.SpamProtection.MaxMessages,
			cfg.SpamProtection.TimeWindowSeconds
		);

		if (cfg.BlockBadNames)
		{
			NameFilterService = new NameFilterService(
				Core,
				BlacklistService,
				cfg.BlockedNameReplacement
			);
		}

		_configReload = Config.OnChange(newCfg =>
		{
			Core.Logger.LogInformation($"ChatGuard config reloaded words: {string.Join(", ", newCfg.BlacklistedWords)}");

			BlacklistService.UpdateWords(newCfg.BlacklistedWords);
			IpFilterService.UpdateWhitelist(newCfg.WhitelistedIPs);
			SpamService.UpdateSettings(
				newCfg.SpamProtection.MaxMessages,
				newCfg.SpamProtection.TimeWindowSeconds
			);

			NameFilterService?.UpdateReplacement(newCfg.BlockedNameReplacement);
		});

		_chatEvents = new ChatEvents(Core, Config, BlacklistService, IpFilterService, SpamService, NameFilterService);
	}

	public override void Unload()
	{
		_configReload?.Dispose();
		_configReload = null;
	}

	[ServerNetMessageHandler]
	public HookResult OnChatMessage(CUserMessageSayText2 msg)
	{
		return _chatEvents?.HandleChatMessage(msg) ?? HookResult.Continue;
	}
}
