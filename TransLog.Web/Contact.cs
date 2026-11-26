namespace TransLog.Web;

/// <summary>Public contact details (from appsettings.json, section "Contact").</summary>
public static class Contact
{
    public static string WhatsAppLink(IConfiguration config)
    {
        var digits = new string((config["Contact:WhatsApp"] ?? "").Where(char.IsDigit).ToArray());
        var text = Uri.EscapeDataString("Hi, I'd like to register my company on TransLog.");
        return $"https://wa.me/{digits}?text={text}";
    }

    public static string WhatsAppDisplay(IConfiguration config) => config["Contact:WhatsAppDisplay"] ?? "";
}
