<div dir="rtl">

# החלטות מימוש מבחן ניהול חופשות

## 1. החלטות ארכיטקטוניות

- הפרויקט נשאר יישום קטן עם Controller, שירות עסקי ו־EF Core. השירות מרכז חישוב מכסה, ולידציה וטרנזקציית אישור; ה־Controller מתרגם תוצאות עסקיות לתגובות HTTP. לא הוספתי Repository כללי, MediatR או חבילות: DbContext כבר מספק גישה לנתונים ויחידת עבודה.
- תגובות הבקשות משתמשות ב־DTO מפורש ואינן חושפות navigation collections של EF. יצירה נשארה עם HTTP 200 כדי לשמור על חוזה ה־POC; ב־API חדש הייתי בוחר 201 עם כתובת משאב שניתן לקרוא.
- שגיאות צפויות מוחזרות כ־ProblemDetails ונרשמות ב־ILogger. שגיאות בלתי צפויות עוברות דרך exception handler של ASP.NET Core.
- Angular משתמש בשירות HTTP, enums ומודלים קיימים, Reactive Forms וטיפול מפורש בטעינה, שגיאה והצלחה. signals מחזיקים state מקומי; takeUntilDestroyed מבטל subscriptions בעת פירוק ו־finalize מנקה מצבי טעינה.
- אישור מחליף את השורה שהשרת החזיר; יצירה מוסיפה לרשימה המקומית. אין GET אוטומטי לכל הרשימה אחרי כל פעולה. שגיאת 409 מופיעה ליד השורה ומציעה רענון יזום, כי השינוי יכול להגיע ממשתמש אחר.
- הודעות ממשק רוכזו ב־TranslateService וב־translate pipe מקומיים, עם אנגלית ועברית, בלי תלות חדשה. השפה נקראת מ־lang של המסמך; לא נוסף מסך החלפת שפה.

## 2. הבאג ביתרת החופשה

- הקוד המקורי חישב used אבל בדק רק אם מספר הימים בבקשה גדול מהמכסה המלאה. כך עובד עם 18 ימים מאושרים מתוך 20 יכול היה להגיש עוד 3 ימים.
- כתבתי תחילה BalanceRegressionTests.Create_ExceedingRemainingBalance_IsRejected. הוא נכשל מול הקוד המקורי עם OkObjectResult במקום BadRequestObjectResult, ולאחר התיקון עבר.
- ההנחה העסקית: ימים קלנדריים כולל שני הקצוות; רק Vacation מאושרת צורכת מכסה. Pending אינה שומרת יתרה, ולכן חובה לבדוק שוב בעת אישור. Sick ו־Unpaid לא צורכים מכסת Vacation.
- המכסה היא לשנה קלנדרית של הבקשה, ולא לשנת המחשב. בקשה שחוצה שנה מפוצלת בחישוב בין השנים. סופרים גם חופשות שכבר אושרו להמשך אותה שנה. הימים מחושבים מהתאריכים ואינם מתקבלים כמספר שניתן לשנות בלקוח.
- השרת דוחה עובד לא תקין, סוג חסר או לא מוכר, תאריכים חסרים וסדר תאריכים הפוך. סוג החופשה nullable ב־DTO כדי להבחין בין Vacation=0 לבין שדה שלא נשלח.

## 3. אישור בקשה ותחרותיות

- POST /api/leave-requests/{id}/approve מחזיר 404 לבקשה חסרה, 409 לסטטוס שאינו Pending או ליתרה לא מספקת, ו־200 עם DTO מעודכן בהצלחה.
- בחירת היעד היא PostgreSQL 16 דרך docker-compose.yml המקורי. לא נדרש שרת ענן, שינוי סכימה או התקנת חבילת DB נוספת.
- ב־PostgreSQL נפתחת טרנזקציית READ COMMITTED וננעלת שורת העובד עם SELECT FOR UPDATE פרמטרי. אחרי הנעילה נטענת שוב הבקשה ונבדקים הסטטוס והיתרה, ורק אז מתבצעים SaveChanges ו־Commit.
- נעילת עובד משותפת לכל בקשותיו מונעת משתי בקשות שונות לקבל אישור על סמך אותה יתרה. נעילת שורת הבקשה בלבד לא הייתה מגינה על המכסה המשותפת. הנעילה פועלת בין מופעי API שונים דרך ה־DB ואינה מסתמכת על lock בזיכרון התהליך.
- READ COMMITTED נבחר במכוון: לאחר המתנה לנעילה, קריאת היתרה צריכה לראות את אישור הטרנזקציה הקודמת. כל נתיב עתידי שמשנה יתרה או מאשר חופשות צריך להשתתף באותו פרוטוקול נעילה.
- SQLite משתמש בטרנזקציה Serializable כחלופה מקומית. הוא אינו תחליף לבדיקת נעילות PostgreSQL; תחת עומס כתיבה יכולה להופיע SQLITE_BUSY שתדרוש מדיניות retry מוגבלת. לא הוספתי retry כללי שעשוי להסתיר שגיאה עסקית.
- שני טסטים ייעודיים מפעילים אישורים דרך DbContexts נפרדים: שתי בקשות שונות החורגות יחד מהמכסה, ואישור כפול של אותה בקשה. הם דורשים PostgreSQL ונקבעים כ־Skipped במצב SQLite.
- מגבלת אימות בפועל: Docker לא זמין במחשב. ניסיון dotnet test בברירת המחדל נכשל בחיבור ל־docker_engine. ההרצה המאומתת היא SQLite; איני טוען שהטסטים של PostgreSQL עברו.

מקורות טכניים שנבדקו:
- https://www.postgresql.org/docs/16/sql-select.html
- https://learn.microsoft.com/en-us/ef/core/saving/transactions

## 4. על מה ויתרתי בגלל הזמן

- לא שיניתי סכימה, קובצי תצורה או גרסאות חבילות. EnsureCreated המקורי נשאר. במוצר מתמשך הייתי עובר ל־migrations עם שדרוג נתונים מבוקר.
- השארתי את פעולות השרת סינכרוניות בהתאם לשלד. השיפור הבא הוא EF async מקצה לקצה עם CancellationToken, pagination וחיפוש מוגבל בהיקף.
- אין כרגע אימות משתמש או הרשאות מנהל. זו מגבלה של השלד; לפני חשיפה אמיתית נדרשים authentication, בדיקת גישה לעובד והרשאת אישור. לא המצאתי מנגנון זהות חדש למבחן.
- מדיניות חגים, סופי שבוע, חפיפת בקשות וצבירת יתרה בין שנים לא הוגדרה, ולכן לא המצאתי אותה. בקשות מאושרות חופפות נספרות כבקשות נפרדות לפי ההנחה הנוכחית.
- עם עוד יום: להריץ PostgreSQL וכל טסטי התחרותיות בפועל, להוסיף CI, בדיקות E2E לטופס ולאישור, authorization וטיפול ממוקד ב־deadlock/lock timeout. גם למדוד את שאילתת היתרה לפני הוספת אינדקסים או אופטימיזציה.

## 5. שימוש ב־AI

### איפה AI עזר כולל prompts

הציטוטים הם קטעים אמיתיים מהבקשה שנשלחה ל־Codex באותה שיחה, ולא prompts משוחזרים או שיחות מומצאות.

1. prompt: "בשלב הראשון תעזור לי להעלות ל GIT לרפוסיטוורי חדש."
   תוצאה: בדיקת היסטוריה ו־remote, שמירת קומיט הפתיחה וחיבור הריפו ל־GitHub. לאחר מכן נשמרו קומיטים נפרדים של השינויים.
2. prompt: "ואז להבין איך הכי נכון להתנהל מול DB, מה הכי חכם יעיל ופשוט"
   תוצאה: השוואת SQLite ו־PostgreSQL על סמך דרישת התחרותיות, בחירת PostgreSQL ב־Compose, ואבחון ש־Docker אינו זמין. SQLite שימש לאימות מקומי בלי לטעון שהוא מאמת נעילות PostgreSQL.
3. prompt: "בשלב השלישי תקרא טוב טוב את ההנחיות וננתחיל לתכנן ולבצע"
   תוצאה: קריאת מסמך המבחן והשלד, בניית תכנית שקיבלה אישור, כתיבת טסט רגרסיה לפני התיקון, מימוש השירות והטופס ובדיקת תוצאות ההרצות.

### איפה תוקנה הצעה של AI

- בטיוטת חישוב היתרה הראשונה, AI סינן בקשות מאושרות לפי StartDate <= end של הבקשה החדשה. זה שגוי: גם חופשה בדצמבר שכבר אושרה צורכת מכסה של בקשה בינואר באותה שנה. התנאי תוקן לסוף השנה האחרונה בבקשה ונוסף הטסט Create_CountsApprovedDaysLaterInSameYear. זו דוגמה ממשית לתיקון טיוטה, לא סיפור על הצעה שלא ניתנה.
- בטיוטת הטרנזקציה הוחלף SERIALIZABLE ב־READ COMMITTED עבור PostgreSQL כדי שהקריאה אחרי המתנה לנעילת העובד תראה את המצב החדש. הטסטים של PostgreSQL עדיין מחייבים הרצה אמיתית.

### אבטחה

- בקומיט המקורי 35dd8ed, בקובץ backend/src/LeaveManagement.Api/Controllers/LeaveRequestsController.cs, בפונקציה Search, חובר name ישירות למחרוזת SQL והועבר ל־FromSqlRaw. קלט יכול היה לשנות את השאילתה ולחשוף או לשבש נתונים בהרשאות חשבון ה־DB.
- החיפוש הוחלף ב־LINQ עם Contains, המתורגם לשאילתה פרמטרית. הטסט Search_InjectionPayloadIsTreatedAsLiteralText מאמת שמחרוזת הזרקה אינה מחזירה את כל הנתונים ושחיפוש רגיל עדיין עובד.
- SQL הנעילה החדש משתמש ב־FromSqlInterpolated כך ש־employeeId נשלח כפרמטר.
- פרטי הגישה leave/leave שייכים לסביבת הפיתוח המקורית. בסביבה אמיתית נדרשים סודות חיצוניים והרשאות DB מצומצמות. לא שיניתי את קובצי התצורה או את פרטי הגישה במסגרת זו.

## 6. הרצה ואימות

פקודות PowerShell משורש הריפו:

```powershell
# PostgreSQL + API; requires installed and running Docker
 docker compose up --build

# In a separate terminal: PostgreSQL integration and concurrency tests
 Remove-Item Env:Database__Provider -ErrorAction SilentlyContinue
 dotnet test backend/LeaveManagement.sln

# Without Docker: verified local alternative
 $env:Database__Provider = 'Sqlite'
 dotnet test backend/LeaveManagement.sln
 dotnet run --project backend/src/LeaveManagement.Api

# Frontend, separate terminal
 cd frontend
 npm ci
 npm start

# Verification
 npm run build -- --configuration production
 npm test -- --watch=false --browsers=ChromeHeadless
```

- API: http://localhost:5080 ; Swagger: http://localhost:5080/swagger ; UI: http://localhost:4200 .
- הבדיקות המאומתות: 21 בדיקות שרת הצליחו גם בבניית Release ו־2 בדיקות PostgreSQL דולגו במצב SQLite; 9 בדיקות Angular הצליחו ב־Chrome Headless; build ייצור של Angular עבר.
- בדיקות HTTP מפעילות את היישום האמיתי עם SQLite משותף בזיכרון ונפרד לכל בדיקה, כולל JSON validation, יצירה, אישור, 409 ו־404.
- בוצעה גם בדיקת תצוגה בדפדפן מול API מקומי ובדיקה חזותית ברוחב 390 פיקסלים. זו אינה טענת כיסוי E2E אוטומטי.
- לא שונו package.json, package-lock.json, קובצי תצורה או מודלי סכימת הנתונים.

</div>

