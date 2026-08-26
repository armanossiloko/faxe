using Akka.Actor;
using Faxe.Core.Data;
using Faxe.Core.Models;

namespace Faxe.Flow;

/// <summary>Messages for the Akka-based flow runtime (OTP-style mailboxes).</summary>
public static class FlowMessages
{
    public sealed record StartSignal;

    public sealed record DataItemMsg(int InPort, DataItem Item);

    public sealed record WireSubscription(int OutPort, IActorRef Dest, int InPort);

    public sealed record StartFlow;

    public sealed record StopFlow;

    public sealed record StartGraph(string GraphId, GraphDefinition Definition, IActorRef ReplyTo);

    public sealed record StopGraph(string GraphId, IActorRef ReplyTo);

    public sealed record GraphStarted(string GraphId, IActorRef Graph);

    public sealed record GraphStopped(string GraphId);

    public sealed record GraphFailed(string GraphId, string Error);

    public sealed record Ack;
}
