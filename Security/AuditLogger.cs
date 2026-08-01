using Microsoft.Extensions.Logging;

namespace Jadeer.Exam.Security;

/// <summary>
/// Security/audit logging with CR/LF stripped from user-derived values (log injection fix).
/// </summary>
public class AuditLogger
{
    private readonly ILogger _security;

    public AuditLogger(ILoggerFactory loggerFactory)
    {
        _security = loggerFactory.CreateLogger("SECURITY");
    }

    public void LoginSuccess(string? username, string? ip) =>
        _security.LogInformation("event=LOGIN_SUCCESS user={User} ip={Ip}", Sanitize(username), Sanitize(ip));

    public void LoginFailure(string? username, string? ip) =>
        _security.LogWarning("event=LOGIN_FAILURE user={User} ip={Ip}", Sanitize(username), Sanitize(ip));

    public void Logout(string? username, string? ip) =>
        _security.LogInformation("event=LOGOUT user={User} ip={Ip}", Sanitize(username), Sanitize(ip));

    public void SessionTimeout(string? username, string? ip) =>
        _security.LogInformation("event=SESSION_TIMEOUT user={User} ip={Ip}", Sanitize(username), Sanitize(ip));

    public void AccessDenied(string? username, string? path, string? ip) =>
        _security.LogWarning("event=ACCESS_DENIED user={User} path={Path} ip={Ip}",
            Sanitize(username), Sanitize(path), Sanitize(ip));

    public void InputRejected(string? username, string? field, string? reason) =>
        _security.LogWarning("event=INPUT_REJECTED user={User} field={Field} reason={Reason}",
            Sanitize(username), Sanitize(field), Sanitize(reason));

    public void FileRejected(string? username, string? reason) =>
        _security.LogWarning("event=FILE_REJECTED user={User} reason={Reason}",
            Sanitize(username), Sanitize(reason));

    public void ContentPublished(string? username, string? resource, long? id) =>
        _security.LogInformation("event=CONTENT_PUBLISH user={User} resource={Resource} id={Id}",
            Sanitize(username), Sanitize(resource), id);

    public void OauthLogin(string? subject, string? email) =>
        _security.LogInformation("event=OAUTH_LOGIN subject={Subject} email={Email}",
            Sanitize(subject), Sanitize(email));

    public void Info(string messageTemplate, params object?[] args) =>
        _security.LogInformation(messageTemplate, args.Select(a => a is string s ? Sanitize(s) : a).ToArray());

    /// <summary>Strip CR/LF/TAB from user-controlled values before logging.</summary>
    public static string Sanitize(string? value)
    {
        if (value == null)
        {
            return "-";
        }

        return value
            .Replace("\r", "_", StringComparison.Ordinal)
            .Replace("\n", "_", StringComparison.Ordinal)
            .Replace("\t", "_", StringComparison.Ordinal);
    }
}
