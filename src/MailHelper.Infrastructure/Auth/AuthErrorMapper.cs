using MailHelper.Core.Domain;
using Microsoft.Identity.Client;

namespace MailHelper.Infrastructure.Auth;

/// <summary>MSAL 异常 → 认证错误码映射（04 章 §5 AUTH 段）。</summary>
public static class AuthErrorMapper
{
    public static string MapException(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => AuthErrorCodes.Cancelled, // AUTH-001：登录取消/窗口关闭
            MsalServiceException service when IsAdminConsentRequired(service) => AuthErrorCodes.AdminConsentRequired, // AUTH-002：EX-01
            _ => AuthErrorCodes.ReauthRequired, // AUTH-003：刷新失败/需要交互/未列举异常（D-28）
        };
    }

    private static bool IsAdminConsentRequired(MsalServiceException exception) =>
        exception.ErrorCode is "65001" or "500021" or "16000" or "16001"
        || exception.Message.Contains("AADSTS65001", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("AADSTS500021", StringComparison.OrdinalIgnoreCase);
}
