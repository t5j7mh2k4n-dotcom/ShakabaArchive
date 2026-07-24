# أرشيف الشكابة شاع الدين

برنامج توثيقي إنساني لمواطني وسكان **الشكابة شاع الدين**: مواليد ومناسبات اجتماعية، مع البحث بالرقم الوطني / الجنسية / القبيلة / الحي، وصورة الوثيقة.

## المشاريع

| المشروع | الوصف |
|---------|--------|
| `ShakabaArchive.Web` | نسخة ويب عربية أونلاين (المتصفح) |
| `ShakabaArchive` | تطبيق سطح مكتب WinForms |
| `ShakabaArchive.Core` | النماذج وقاعدة البيانات المشتركة |

## تشغيل الويب

```bash
cd ShakabaArchive.Web
dotnet run
```

افتح الرابط الذي يظهر (مثلاً `https://localhost:7xxx`).

- التصفح والبحث: متاح للجميع
- الإضافة/التعديل: بعد الدخول `admin` / `admin123`

## تشغيل سطح المكتب

```bash
cd ShakabaArchive
dotnet run
```

## التخزين المجاني الأونلاين

### 1) SQLite (افتراضي)
يعمل محلياً بدون إعداد. على الاستضافة احفظ المجلد `App_Data` كقرص دائم إن أمكن.

### 2) PostgreSQL مجاني (موصى به للويب)
1. أنشئ قاعدة على [Neon](https://neon.tech) أو [Supabase](https://supabase.com)
2. في `ShakabaArchive.Web/appsettings.json`:

```json
"ConnectionStrings": {
  "PostgreSql": "Host=...;Database=...;Username=...;Password=..."
}
```

أو عيّن متغير البيئة `DATABASE_URL` / `POSTGRES_CONNECTION` على منصة الاستضافة (Render / Railway / Azure).

### نشر مجاني مقترح
- **Render** أو **Railway**: اربط المستودع، أمر التشغيل `dotnet ShakabaArchive.Web.dll`، وأضف `DATABASE_URL`.

## الحقول
رقم وطني، اسم، أب/أم، جنسية، قبيلة، حي، ميلاد، إقامة، هاتف، صورة وثيقة، مناسبات (ميلاد/زواج/طلاق/وفاة/هجرة/أخرى).
