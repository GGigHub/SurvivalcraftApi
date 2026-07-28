using System.Text.Json;

namespace Survivalcraft.Testing.Control;

public sealed class TypedCommandRouter {
    private readonly Dictionary<string, ICommandRoute> routes =
        new(StringComparer.Ordinal);

    public IReadOnlyList<AutomationCommandDescriptor> Commands => routes.Values
        .Select(route => route.Descriptor)
        .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal)
        .ToArray();

    public void Register<TCommand, TResult>(
        AutomationCommandDescriptor descriptor,
        Func<AutomationCommandContext, TCommand, TResult> handler) {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Name);
        if (!routes.TryAdd(descriptor.Name, new CommandRoute<TCommand, TResult>(descriptor, handler))) {
            throw new InvalidOperationException(
                $"Automation command '{descriptor.Name}' is already registered.");
        }
    }

    public ControlResponse Dispatch(
        ControlRequest request,
        long responseSequence,
        string nodeRole) {
        ArgumentNullException.ThrowIfNull(request);
        if (!routes.TryGetValue(request.Command, out ICommandRoute? route)) {
            return ControlProtocol.Failure(
                request,
                "unknown_command",
                $"Unknown control command '{request.Command}'.",
                sequence: responseSequence);
        }

        try {
            AutomationCommandContext context = new(request.Id, responseSequence, nodeRole);
            object? result = route.Invoke(context, request);
            return ControlProtocol.Success(request, result, responseSequence);
        }
        catch (AutomationCommandException exception) {
            return ControlProtocol.Failure(
                request,
                exception.Code,
                exception.Message,
                exception.Details,
                responseSequence);
        }
        catch (ControlProtocolException exception) {
            return ControlProtocol.Failure(
                request,
                exception.Code,
                exception.Message,
                sequence: responseSequence);
        }
        catch (JsonException exception) {
            return ControlProtocol.Failure(
                request,
                "invalid_payload",
                $"Command '{request.Command}' payload is invalid.",
                exception.Message,
                responseSequence);
        }
        catch (Exception exception) {
            return ControlProtocol.Failure(
                request,
                "command_failed",
                exception.Message,
                exception.ToString(),
                responseSequence);
        }
    }

    private interface ICommandRoute {
        AutomationCommandDescriptor Descriptor { get; }
        object? Invoke(AutomationCommandContext context, ControlRequest request);
    }

    private sealed class CommandRoute<TCommand, TResult>(
        AutomationCommandDescriptor descriptor,
        Func<AutomationCommandContext, TCommand, TResult> handler) : ICommandRoute {
        public AutomationCommandDescriptor Descriptor { get; } = descriptor;

        public object? Invoke(AutomationCommandContext context, ControlRequest request) =>
            handler(context, request.DeserializePayload<TCommand>());
    }
}
