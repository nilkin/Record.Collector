using System.Text.RegularExpressions;

namespace RecordWatcher.Core.Services;
public static class Utils
{
    public static string Normalize(string unnormalized)
    {
        var normalized = unnormalized.Trim();

        if (string.IsNullOrWhiteSpace(unnormalized))
        {
            return unnormalized;
        }

        if (normalized.StartsWith("Ext."))
        {
            return normalized.Replace("Ext.", "");
        }

        var patterns = new string[] { "00994", "+9940", "+994", "994" };

        foreach (var pattern in patterns)
        {
            if (normalized.StartsWith(pattern))
            {
                var regex = new Regex(Regex.Escape(pattern));
                string phoneNumber = regex.Replace(normalized, "0", 1);
                return phoneNumber;
            }
        }

        if (normalized.StartsWith("90") && normalized.Length > 5)
        {
            return normalized[1..];
        }

        if (normalized.Length == 7)
        {
            return "012" + normalized;
        }

        if (normalized.Length == 9)
        {
            return "0" + normalized;
        }

        if (normalized.Length == 9)
        {
            return "0" + normalized;
        }

        return unnormalized;
    }

    public static bool IsPhoneNumber(string phoneNum, out string normalized)
    {
        phoneNum = Normalize(phoneNum ?? "");

        if (phoneNum.Where(c => !Char.IsDigit(c)).Count() < 1 && phoneNum.Length > 6)
        {
            normalized = phoneNum;
            return true;
        }

        normalized = null;
        return false;
    }
}

