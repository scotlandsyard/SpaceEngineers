using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.ModAPI.Ingame;

namespace ScriptToPlugin;

/// <summary>
/// Intergrid communication between the plugin's own scripts. The game's IGC lives on the server and only knows
/// real programmable blocks, so scripts here talk to each other through this instead. It follows the game's
/// rules: messages arrive on the next tick, each listener keeps at most 25, and callbacks run the script with
/// UpdateType.IGC. Reach is decided by how the host blocks' grids are connected; antenna range isn't checked, so
/// anything farther than connected grids counts as in antenna range.
/// </summary>
internal class IgcHub
{
	private struct Envelope
	{
		public LocalIgc Source;

		public LocalIgc Target;

		public string Tag;

		public object Data;

		public TransmissionDistance Distance;
	}

	private readonly Dictionary<long, LocalIgc> _contexts = new Dictionary<long, LocalIgc>();

	private readonly List<Envelope> _outbox = new List<Envelope>();

	private readonly List<Envelope> _sending = new List<Envelope>();

	public void Register(LocalIgc context)
	{
		_contexts[context.Address] = context;
	}

	public void Unregister(LocalIgc context)
	{
		if (_contexts.TryGetValue(context.Address, out LocalIgc current) && current == context)
		{
			_contexts.Remove(context.Address);
		}
	}

	public LocalIgc Find(long address)
	{
		return _contexts.TryGetValue(address, out LocalIgc context) && context.IsActive ? context : null;
	}

	public void Broadcast(LocalIgc source, string tag, object data, TransmissionDistance distance)
	{
		_outbox.Add(new Envelope { Source = source, Tag = tag, Data = data, Distance = distance });
	}

	public void Unicast(LocalIgc source, LocalIgc target, string tag, object data)
	{
		_outbox.Add(new Envelope { Source = source, Target = target, Tag = tag, Data = data, Distance = TransmissionDistance.AntennaRelay });
	}

	/// <summary>Hands last tick's messages to their listeners, then runs one waiting callback per script.</summary>
	public void Update()
	{
		if (_outbox.Count > 0)
		{
			_sending.AddRange(_outbox);
			_outbox.Clear();
			foreach (Envelope envelope in _sending)
			{
				Deliver(envelope);
			}
			_sending.Clear();
		}
		foreach (LocalIgc context in _contexts.Values.Where(c => c.HasPendingCallback).ToList())
		{
			context.InvokeOnePendingCallback();
		}
	}

	private void Deliver(Envelope envelope)
	{
		if (!envelope.Source.IsActive)
		{
			return;
		}
		MyIGCMessage message = new MyIGCMessage(envelope.Data, envelope.Tag, envelope.Source.Address);
		if (envelope.Target != null)
		{
			if (envelope.Target.IsActive && IsReachable(envelope.Source, envelope.Target, envelope.Distance))
			{
				envelope.Target.Unicast.Enqueue(message);
			}
			return;
		}
		foreach (LocalIgc target in _contexts.Values)
		{
			if (target == envelope.Source || !target.IsActive)
			{
				continue;
			}
			BroadcastListener listener = target.FindActiveListener(envelope.Tag);
			if (listener != null && IsReachable(envelope.Source, target, envelope.Distance))
			{
				listener.Enqueue(message);
			}
		}
	}

	public static bool IsReachable(LocalIgc from, LocalIgc to, TransmissionDistance distance)
	{
		MyCubeGrid a = from.Program.Host?.CubeGrid;
		MyCubeGrid b = to.Program.Host?.CubeGrid;
		if (a == null || b == null)
		{
			return false;
		}
		TransmissionDistance reach;
		if (a == b || MyCubeGridGroups.Static.Mechanical.HasSameGroup(a, b))
		{
			reach = TransmissionDistance.CurrentConstruct;
		}
		else if (MyCubeGridGroups.Static.Logical.HasSameGroup(a, b))
		{
			reach = TransmissionDistance.ConnectedConstructs;
		}
		else
		{
			reach = TransmissionDistance.AntennaRelay;
		}
		return reach <= distance;
	}
}

/// <summary>One script's IGC. A new one is made each time the script starts, like the game does.</summary>
internal class LocalIgc : IMyIntergridCommunicationSystem
{
	private readonly IgcHub _hub;

	private readonly List<BroadcastListener> _broadcastListeners = new List<BroadcastListener>();

	private readonly List<MessageListener> _pendingCallbacks = new List<MessageListener>();

	public LocalIgc(IgcHub hub, VirtualProgram program)
	{
		_hub = hub;
		Program = program;
		Address = AddressOf(program.Entry.Id);
		Unicast = new UnicastListener(this);
		IsActive = true;
		_hub.Register(this);
	}

	public VirtualProgram Program { get; }

	public long Address { get; }

	public bool IsActive { get; private set; }

	public UnicastListener Unicast { get; }

	public bool HasPendingCallback => _pendingCallbacks.Count > 0;

	long IMyIntergridCommunicationSystem.Me => Address;

	IMyUnicastListener IMyIntergridCommunicationSystem.UnicastListener => Unicast;

	/// <summary>A stable address made from the script's id, so other scripts can keep it between sessions.</summary>
	public static long AddressOf(string id)
	{
		long value = long.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long parsed) ? parsed : (id ?? "").GetHashCode() & 0x7FFFFFFFL;
		return 0x5350000000000000L | value;
	}

	public void Dispose()
	{
		IsActive = false;
		_pendingCallbacks.Clear();
		_hub.Unregister(this);
	}

	internal BroadcastListener FindActiveListener(string tag)
	{
		foreach (BroadcastListener listener in _broadcastListeners)
		{
			if (listener.IsActive && listener.Tag == tag)
			{
				return listener;
			}
		}
		return null;
	}

	internal void RegisterForCallback(MessageListener listener)
	{
		if (!_pendingCallbacks.Contains(listener))
		{
			_pendingCallbacks.Add(listener);
		}
	}

	internal void UnregisterFromCallback(MessageListener listener)
	{
		_pendingCallbacks.Remove(listener);
	}

	internal void InvokeOnePendingCallback()
	{
		if (_pendingCallbacks.Count > 0)
		{
			_pendingCallbacks[0].InvokeCallback();
		}
	}

	public bool IsEndpointReachable(long address, TransmissionDistance transmissionDistance = TransmissionDistance.AntennaRelay)
	{
		LocalIgc target = _hub.Find(address);
		return target != null && IgcHub.IsReachable(this, target, transmissionDistance);
	}

	public void SendBroadcastMessage<TData>(string tag, TData data, TransmissionDistance transmissionDistance = TransmissionDistance.AntennaRelay)
	{
		_hub.Broadcast(this, tag, data, transmissionDistance);
	}

	public bool SendUnicastMessage<TData>(long addressee, string tag, TData data)
	{
		LocalIgc target = _hub.Find(addressee);
		if (target == null || target == this || !IgcHub.IsReachable(this, target, TransmissionDistance.AntennaRelay))
		{
			return false;
		}
		_hub.Unicast(this, target, tag, data);
		return true;
	}

	public IMyBroadcastListener RegisterBroadcastListener(string tag)
	{
		BroadcastListener listener = _broadcastListeners.FirstOrDefault(l => l.Tag == tag);
		if (listener == null)
		{
			listener = new BroadcastListener(this, tag);
			_broadcastListeners.Add(listener);
		}
		listener.IsActive = true;
		return listener;
	}

	public void DisableBroadcastListener(IMyBroadcastListener broadcastListener)
	{
		if (!(broadcastListener is BroadcastListener listener) || listener.Context != this)
		{
			throw new ArgumentException(nameof(broadcastListener));
		}
		listener.IsActive = false;
		listener.DisableMessageCallback();
		// Like the game, a disabled listener stays listed while it still has messages to read.
		if (!listener.HasPendingMessage)
		{
			_broadcastListeners.Remove(listener);
		}
	}

	public void GetBroadcastListeners(List<IMyBroadcastListener> broadcastListeners, Func<IMyBroadcastListener, bool> collect = null)
	{
		foreach (BroadcastListener listener in _broadcastListeners)
		{
			if (collect == null || collect(listener))
			{
				broadcastListeners.Add(listener);
			}
		}
	}
}

internal abstract class MessageListener : IMyMessageProvider
{
	private readonly Queue<MyIGCMessage> _messages = new Queue<MyIGCMessage>();

	private string _callback;

	protected MessageListener(LocalIgc context)
	{
		Context = context;
	}

	public LocalIgc Context { get; }

	public int MaxWaitingMessages => 25;

	public bool HasPendingMessage => _messages.Count > 0;

	public void Enqueue(MyIGCMessage message)
	{
		if (_messages.Count >= MaxWaitingMessages)
		{
			_messages.Dequeue();
		}
		_messages.Enqueue(message);
		if (_callback != null)
		{
			Context.RegisterForCallback(this);
		}
	}

	public virtual MyIGCMessage AcceptMessage()
	{
		return _messages.Count > 0 ? _messages.Dequeue() : default;
	}

	public void SetMessageCallback(string argument = "")
	{
		_callback = argument ?? throw new ArgumentNullException(nameof(argument));
	}

	public void DisableMessageCallback()
	{
		Context.UnregisterFromCallback(this);
		_callback = null;
	}

	public void InvokeCallback()
	{
		Context.UnregisterFromCallback(this);
		if (_callback != null)
		{
			Context.Program.Run(_callback, UpdateType.IGC);
		}
	}
}

internal class BroadcastListener : MessageListener, IMyBroadcastListener
{
	public BroadcastListener(LocalIgc context, string tag)
		: base(context)
	{
		Tag = tag;
	}

	public string Tag { get; }

	public bool IsActive { get; set; }
}

internal class UnicastListener : MessageListener, IMyUnicastListener
{
	public UnicastListener(LocalIgc context)
		: base(context)
	{
	}
}
