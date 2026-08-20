using FgScanner.Data;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>Auto-OCR hook: pages saved into a group whose profile has OCR enabled get queued.</summary>
public sealed class ProfileOcrTrigger(ProfileService profileService, OcrQueueService queue)
{
    public async Task EnqueueIfProfileEnabledAsync(Group group)
    {
        if (group.ProfileId is not { } profileId)
        {
            return;
        }

        try
        {
            var profiles = await profileService.ListAsync();
            if (profiles.FirstOrDefault(p => p.Id == profileId)?.OcrEnabled == true)
            {
                await queue.EnqueueGroupAsync(group.Id);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Auto-OCR enqueue for group {Group}", group.Name);
        }
    }
}
