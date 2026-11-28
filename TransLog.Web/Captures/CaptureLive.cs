using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Captures;

/// <summary>
/// Part 5b: live slip updates for My slips and the Captures list. Clients only listen; there is nothing to call.
/// On connect a driver joins his own group and an office user his company's group. Nothing is written to the database
/// per connection. SysAdmins (no company) join nothing.
/// </summary>
[Authorize]
public sealed class CaptureHub : Hub
{
    public const string Path = "/hubs/captures";

    public static string CompanyGroup(int companyId) => "company-" + companyId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string UserGroup(int userId) => "user-" + userId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user is not null)
        {
            if (user.IsInRole(Roles.Driver))
            {
                if (user.GetUserId() is int userId)
                    await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
            }
            else if (user.GetCompanyId() is int companyId)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, CompanyGroup(companyId));
            }
        }
        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Part 5b: sends the live messages. <c>capture</c> = {id, status, reading} when a slip changes (the AI starts or ends
/// a read); <c>added</c> = {id} when a new slip arrives. The page then fetches that one row again from the server,
/// so everything shown still goes through the tenant filter. Never throws: a failed message only means a refresh.
/// </summary>
public sealed class CaptureLive(IHubContext<CaptureHub> hub, ILogger<CaptureLive> log)
{
    public const string Changed = "capture";
    public const string Added = "added";

    /// <summary>To the company's office users and to the given users (the sender and the driver's login).</summary>
    public Task ChangedAsync(int companyId, int captureId, CaptureStatus status, bool reading, params int?[] userIds) =>
        SendAsync(Changed, companyId, userIds, new { id = captureId, status = (int)status, reading });

    public Task AddedAsync(int companyId, int captureId, params int?[] userIds) =>
        SendAsync(Added, companyId, userIds, new { id = captureId });

    private async Task SendAsync(string method, int companyId, IEnumerable<int?> userIds, object message)
    {
        try
        {
            var groups = new List<string> { CaptureHub.CompanyGroup(companyId) };
            groups.AddRange(userIds.OfType<int>().Distinct().Select(CaptureHub.UserGroup));
            await hub.Clients.Groups(groups).SendAsync(method, message);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Live message {Method} for company {CompanyId} not sent", method, companyId);
        }
    }
}
