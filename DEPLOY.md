# نشر أرشيف الشكابة شاع الدين (Render)

## المهم قبل النشر
- بيانات الأرشيف والمستخدمين تكون على **Neon** أونلاين.
- لا ترفع ملف `appsettings.json` الذي فيه كلمة السر إلى GitHub.
- على Render ضع متغير البيئة: `ConnectionStrings__PostgreSql` = رابط Neon الكامل.
- لـ Firebase Authentication ضع أيضاً: `FIREBASE_API_KEY` و`FIREBASE_PROJECT_ID` و`FIREBASE_AUTH_DOMAIN` (مثال: `my-auth-website-f2410.firebaseapp.com`).
- في Firebase Console فعّل Email/Password، وفعّل قالب تأكيد البريد (Email address verification).

## أوامر البناء على Render (Docker)
استخدم الـ Dockerfile في جذر المشروع.
