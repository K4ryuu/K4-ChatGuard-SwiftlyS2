using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace K4ChatGuard.Services;

public class SpamProtectionService
{
	private sealed class SpamState
	{
		public readonly object Lock = new();
		public readonly Queue<long> Messages = new();
		public long CooldownUntilTicks;
	}

	private readonly ISwiftlyCore _core;
	private readonly ConcurrentDictionary<ulong, SpamState> _playerData = new();

	private volatile int _maxMessages;
	private volatile int _timeWindowSeconds;

	public SpamProtectionService(ISwiftlyCore core, int maxMessages, int timeWindowSeconds)
	{
		_core = core;
		UpdateSettings(maxMessages, timeWindowSeconds);
	}

	public void UpdateSettings(int maxMessages, int timeWindowSeconds)
	{
		_maxMessages = Math.Max(1, maxMessages);
		_timeWindowSeconds = Math.Max(1, timeWindowSeconds);

		_core.Logger.LogInformation($"Spam Protection: {_maxMessages} messages per {_timeWindowSeconds}s");
	}

	public bool IsChatSpam(ulong steamId)
	{
		var state = _playerData.GetOrAdd(steamId, _ => new SpamState());

		var nowTicks = DateTime.UtcNow.Ticks;
		var windowTicks = TimeSpan.FromSeconds(_timeWindowSeconds).Ticks;
		var windowStartTicks = nowTicks - windowTicks;

		lock (state.Lock)
		{
			if (state.CooldownUntilTicks > nowTicks)
				return true;

			while (state.Messages.Count > 0 && state.Messages.Peek() < windowStartTicks)
				state.Messages.Dequeue();

			if (state.Messages.Count >= _maxMessages)
			{
				state.Messages.Clear();
				state.CooldownUntilTicks = nowTicks + windowTicks;
				return true;
			}

			state.Messages.Enqueue(nowTicks);
			return false;
		}
	}

	public void ClearPlayerData(ulong steamId)
	{
		_playerData.TryRemove(steamId, out _);
	}
}
