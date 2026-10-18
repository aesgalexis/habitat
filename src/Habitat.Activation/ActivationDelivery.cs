namespace Habitat.Activation;

public sealed class ActivationDelivery(
    Func<ActivationRequest, Task<ActivationReply?>> send,
    Action start,
    Func<Task> pause)
{
    public async Task<ActivationReply> DeliverAsync(string? text)
    {
        var request = new ActivationRequest(Guid.NewGuid(), Text: text);
        var reply = await send(request);
        if (reply is not null) return reply;
        start();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await pause();
            reply = await send(request);
            if (reply is not null) return reply;
        }
        return new(false, "HABITAT could not be opened.");
    }
}
