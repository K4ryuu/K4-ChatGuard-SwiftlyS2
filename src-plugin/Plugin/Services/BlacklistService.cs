using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using System.Text;

namespace K4ChatGuard.Services;

public class BlacklistService
{
	private readonly ISwiftlyCore _core;

	private volatile string[] _words = [];
	private volatile string[] _compactWords = [];

	public BlacklistService(ISwiftlyCore core, List<string> blacklistedWords)
	{
		_core = core;
		UpdateWords(blacklistedWords);
	}

	public void UpdateWords(List<string> blacklistedWords)
	{
		var words = blacklistedWords
			.Where(w => !string.IsNullOrWhiteSpace(w))
			.Select(w => w.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		var compactWords = words
			.Select(Compact)
			.Where(w => w.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		_words = words;
		_compactWords = compactWords;

		_core.Logger.LogInformation($"Loaded {_words.Length} blacklisted words");
	}

	public bool ContainsBlacklistedWord(string? message, out string? matchedWord)
	{
		matchedWord = null;

		if (string.IsNullOrWhiteSpace(message))
			return false;

		var words = _words;
		if (words.Length == 0)
			return false;

		// Fast path: no allocation, normal contains check.
		foreach (var word in words)
		{
			if (message.AsSpan().IndexOf(word.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
			{
				matchedWord = word;
				return true;
			}
		}

		// Slow path only if needed: catches names/messages like f.u.c.k or f u c k.
		var compactMessage = Compact(message);
		if (compactMessage.Length == 0)
			return false;

		foreach (var word in _compactWords)
		{
			if (compactMessage.AsSpan().IndexOf(word.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
			{
				matchedWord = word;
				return true;
			}
		}

		return false;
	}

	private static string Compact(string input)
	{
		StringBuilder sb = new(input.Length);

		foreach (var c in input)
		{
			if (char.IsLetterOrDigit(c))
				sb.Append(char.ToLowerInvariant(c));
		}

		return sb.ToString();
	}
}
