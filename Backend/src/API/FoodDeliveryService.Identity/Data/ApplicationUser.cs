using Microsoft.AspNetCore.Identity;

namespace FoodDeliveryService.Identity.Data;

public sealed class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    // True for admin-provisioned accounts (restaurant managers, etc.) that were created without a
    // usable password and are waiting for the invitee to set one via the emailed activation link.
    // Cleared when the invitation is accepted (see api/users/set-password). Customers, who
    // self-register with a real password, are created with this false.
    public bool MustChangePassword { get; set; }
}
