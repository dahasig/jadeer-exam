# جدير (Jadeer) — ASP.NET Core

نسخة كاملة بلغة .NET 8 من منصة اختبارات جدير (نفس الصفحات والتصميم والهيكل)، مع معالجة ملاحظات الأمن السيبراني.

المصدر الأصلي Java في `C:\Users\IT\RC-Exam` لم يُعدَّل.

## التشغيل

```powershell
cd "C:\Users\IT\new exam"
copy appsettings.Development.json.example appsettings.Development.json
# عدّل كلمة مرور MySQL في appsettings.Development.json
dotnet run --urls http://127.0.0.1:5088
```

أو: `start-jadeer.bat`

افتح `/Account/Login` (العربية افتراضياً).

| الدور | المستخدم | كلمة المرور |
|--------|-----------|-------------|
| مدير | admin | Admin@123 |
| مراقب | examiner | Examiner@123 |
| مصحح | grader | Grader@123 |
| مرشح | candidate | Candidate@123 |

تفعيل MFA (TOTP): بعد الدخول `/Account/MfaSetup`

## الأمن (من ملاحظات المصنف)

- مكدس .NET بدل غير المعتمد
- MFA (TOTP)
- منع Path Traversal عبر SafePath
- منع Log Injection عبر AuditLogger
- تعقيم HTML بمكتبة HtmlSanitizer (بدون regex معرض لـ ReDoS/XSS)
- رؤوس أمنية: CSP, X-Frame-Options, HSTS, SameSite cookies
