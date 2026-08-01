using OtpNet;

namespace Jadeer.Exam.Security;

public class MfaService
{
    public string GenerateSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(key);
    }

    public string GetOtpAuthUri(string username, string secret, string issuer = "Jadeer Exam")
    {
        var label = Uri.EscapeDataString($"{issuer}:{username}");
        var iss = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={secret}&issuer={iss}&digits=6&period=30";
    }

    public bool VerifyCode(string? secret, string? code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
            return false;

        code = code.Trim().Replace(" ", "", StringComparison.Ordinal);
        if (code.Length != 6 || !code.All(char.IsDigit))
            return false;

        try
        {
            var key = Base32Encoding.ToBytes(secret.Trim());
            var totp = new Totp(key);
            return totp.VerifyTotp(code, out _, new VerificationWindow(1, 1));
        }
        catch
        {
            return false;
        }
    }
}
