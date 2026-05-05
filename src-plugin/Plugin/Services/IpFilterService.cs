using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace K4ChatGuard.Services;

public partial class IpFilterService
{
	private readonly ISwiftlyCore _core;
	private volatile HashSet<string> _whitelistedIPs = new(StringComparer.OrdinalIgnoreCase);

	[GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?\b", RegexOptions.CultureInvariant)]
	private static partial Regex IPv4Pattern();

	public IpFilterService(ISwiftlyCore core, List<string> whitelistedIPs)
	{
		_core = core;
		UpdateWhitelist(whitelistedIPs);
	}

	public void UpdateWhitelist(List<string> whitelistedIPs)
	{
		_whitelistedIPs = whitelistedIPs
			.Where(ip => !string.IsNullOrWhiteSpace(ip))
			.Select(ip => ip.Trim())
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		_core.Logger.LogInformation($"IP Filter Service initialized ({_whitelistedIPs.Count} whitelisted)");
	}

	public bool ContainsIpAddress(string? message, out string? detectedIp)
	{
		detectedIp = null;

		if (string.IsNullOrWhiteSpace(message))
			return false;

		foreach (Match match in IPv4Pattern().Matches(message))
		{
			var ipWithPort = match.Value;
			var ip = ipWithPort.Split(':', 2)[0];

			if (!IsValidIPv4(ip))
				continue;

			if (IsWhitelisted(ip, ipWithPort))
				continue;

			detectedIp = ipWithPort;
			return true;
		}

		return false;
	}

	private bool IsWhitelisted(string ip, string ipWithPort)
	{
		var whitelist = _whitelistedIPs;

		return whitelist.Contains(ip)
			|| whitelist.Contains(ipWithPort)
			|| (whitelist.Contains("localhost") && ip == "127.0.0.1");
	}

	private static bool IsValidIPv4(string ip)
	{
		var parts = ip.Split('.');
		if (parts.Length != 4)
			return false;

		foreach (var part in parts)
		{
			if (!int.TryParse(part, out var num) || num < 0 || num > 255)
				return false;
		}

		return true;
	}
}
