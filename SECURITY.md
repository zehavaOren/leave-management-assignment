# סקירת אבטחה

נבדק ב־19 באוקטובר 2026. זהו יישום מבחן מקומי; אין עדיין authentication או authorization. הגבלת קצב ו־CORS אינם תחליף לבדיקת זהות והרשאות.

## תיקוני קוד שבוצעו

- הגבלת API ל־120 בקשות לדקה לכל כתובת RemoteIpAddress, ללא תור. כותרת X-Forwarded-For אינה משמשת כמקור זהות ולכן שינוי שלה אינו עוקף את המגבלה. חריגה מחזירה 429 ו־Retry-After.
- הגבלת גוף בקשה ל־16 KiB ב־Kestrel, כולל בקשות chunked; גודל Content-Length גדול נדחה לפני עיבוד. זהו גבול טכני ל־JSON הקטן של הבקשה, ולא מגבלה חדשה על ימי חופשה.
- הגבלת שם בחיפוש ל־100 תווים כדי למנוע קלט חריג בשאילתת substring.
- Cache-Control: no-store לתגובות API המכילות נתוני עובדים וחופשות, ו־X-Content-Type-Options: nosniff.
- Swagger מוגש רק ב־Development. בהרצת הפיתוח המקומית נשמרת גישה ל־Swagger; ב־Production הנתיב אינו מפורסם.
- תגובות כשל בלתי צפוי אינן חושפות detail או extension של exception, גם בסביבת Development. השרת ממשיך לרשום חריגות בלוג.
- SQL Injection בחיפוש כבר תוקן בשלב קודם באמצעות LINQ פרמטרי. בדיקה נוספת מוכיחה ש־status, days ו־employee שנוספים ל־JSON אינם משנים את סטטוס האישור, מספר הימים או מכסת העובד.

## עדכוני תצורה ותלויות

### פרסום Docker למחשב המקומי בלבד

בקובץ docker-compose.yml:

```diff
-      - "5432:5432"
+      - "127.0.0.1:5432:5432"
-      - "5080:5080"
+      - "127.0.0.1:5080:5080"
```

ברירת המחדל הקודמת פרסמה את הפורטים לכל ממשקי הרשת. כעת הם מוגבלים ל־localhost. לא שונו נתונים, volumes או סיסמאות.

### עדכוני NuGet במסגרת .NET 8

הסריקה הראשונית של dotnet list package --vulnerable --include-transitive מצאה חולשות High ב־Microsoft.Extensions.Caching.Memory 8.0.0, ב־System.Text.Json 8.0.0 וב־SQLitePCLRaw.lib.e_sqlite3 2.1.6.

גרסאות שהותקנו, ללא מעבר לגרסת .NET ראשית אחרת:

| חבילה | גרסה מותקנת |
|---|---|
| Microsoft.EntityFrameworkCore | 8.0.32 |
| Microsoft.EntityFrameworkCore.Sqlite | 8.0.32 |
| Microsoft.AspNetCore.Mvc.Testing | 8.0.32 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 8.0.11 |
| System.Text.Json — הפניה מפורשת לתיקון תלות עקיפה | 8.0.6 |
| SQLitePCLRaw.lib.e_sqlite3 — הפניה מפורשת לתיקון תלות עקיפה | 2.1.14 |

לאחר restore וסריקה חוזרת, לא נמצאו חבילות פגיעות ידועות בשני פרויקטי .NET, כולל תלויות עקיפות. עדכון EF הסיר גם את תלות Caching.Memory הפגיעה. הבדיקות עברו מול PostgreSQL ו־SQLite; אין שינוי סכימה.

### מקור חבילות

- נוסף frontend/.npmrc עם registry=https://registry.npmjs.org/, ואין קישורי resolved מסוג HTTP ב־package-lock.json.

## ממצאים שנותרו תחת מגבלת Angular 17

npm audit --omit=dev --registry=https://registry.npmjs.org דיווח על 8 חבילות ייצור עם ממצאים: 4 High ו־4 Moderate. התיקון שהכלי מציע כרוך בשדרוג Angular ראשי, ולכן לא הורץ npm audit fix --force.

בחלק מהאזהרות מדובר ביכולות שאינן בשימוש במימוש הנוכחי: אין SSR/HttpTransferCache, אין תבניות או קובצי תרגום שמוזנים בידי משתמש, אין innerHTML או bypassSecurityTrust, כתובות ה־HTTP קבועות ופורמט התאריך קבוע. אין בכך הצהרה שהחבילות אינן פגיעות; ללא שינוי דרישת Angular 17 לא ניתן לטעון שדוח התלויות נקי.

## אימות

- 49 בדיקות שרת עברו מול PostgreSQL ו־Testcontainers, כולל ארבע בדיקות התחרותיות. בדיקות HTTP עצמן משתמשות ב־SQLite מבודד בזיכרון.
- נוספו בדיקות ל־no-store, nosniff, היעדר Swagger ב־Production, תגובות 500 ללא פרטים פנימיים בשתי הסביבות, 413, 429, חיפוש ארוך ו־overposting.
- בדיקת HTTP אמיתית מול Kestrel עם גוף chunked שחורג מהמגבלה החזירה 413. נוספה בדיקת רגרסיה לשימור קוד 413 בטיפול בחריגות.
- במצב SQLite עברו 45 בדיקות וארבע בדיקות PostgreSQL דולגו כמתוכנן.
- כל 20 בדיקות Angular עברו ובניית production הושלמה באנגלית ובעברית.
- בקר בדיקות האבטחה קיים רק בפרויקט הבדיקות ואינו חשוף ביישום האמיתי (404).

הגבלת הקצב היא בזיכרון ולכל מופע שרת. הפצה מאחורי proxy או במספר מופעים דורשת הגדרת proxy מהימן ופתרון משותף בהתאם לסביבה.

## לפני חשיפה אמיתית

נדרשים מנגנון זהות והרשאות עובד/מנהל, סודות מחוץ לקוד, TLS בסביבת ההפצה, עדכוני תלויות ו־runtime ובדיקת הרשאות DB. אלה אינם ממומשים על ידי כותרות אבטחה או rate limiting. היישום אינו מוצג כמוכן לחשיפה ציבורית.

## מקורות לממצאי התלויות

- https://github.com/advisories/GHSA-qj66-m88j-hmgj
- https://github.com/advisories/GHSA-hh2w-p6rv-4g7w
- https://github.com/advisories/GHSA-8g4q-xg66-9fp4
- https://github.com/advisories/GHSA-2m69-gcr7-jv3q
- פלט npm audit של הפרויקט, ללא התקנת תיקונים בכפייה.
