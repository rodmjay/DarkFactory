using DarkFactory.Core;
using DarkFactory.Data;
using ModelContextProtocol;

namespace DarkFactory.Mcp.Tools;

/// <summary>
/// The SDK redacts exception messages on the way out — every type except
/// <see cref="McpException"/> reaches the caller as a bare "An error
/// occurred invoking '...'". That is the right default, because an
/// unexpected exception can carry anything.
///
/// These types are the opposite case: their entire purpose is to be read by
/// whoever made the call, and they are written to carry no secrets — a URL,
/// schema violations, a list of blocking run ids, a missing team. Listing
/// them here rather than catching <see cref="Exception"/> keeps the
/// distinction deliberate: adding a type to this list is a decision that
/// its message is safe to publish.
/// </summary>
internal static class Errors
{
    public static async Task<T> Surfacing<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ServerRegistrationException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (ServerInUseException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (TeamNotConfiguredException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (ModelGatewayException ex)
        {
            // "No model gateway is configured" and "deployment X returned
            // HTTP 429" are both things the caller needs to see. The
            // messages name deployments and configuration keys, never
            // endpoints or credentials — see IModelGateway.cs.
            throw new McpException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // The services raise these for "no such conversation", "only
            // approved amendments can seed a run", and similar — every one
            // of which is a statement about the caller's request, not about
            // the factory's internals.
            throw new McpException(ex.Message);
        }
    }
}
