<div dir="rtl">

# משימת בית — מפתח/ת Full‑Stack סניור (.NET + Angular)

ברוכים הבאים 👋 לפניכם פרויקט קטן לניהול **בקשות חופשה של עובדים**, שנכתב במהירות כ‑POC על ידי מתכנת זוטר. המשימה שלכם היא להפוך אותו למשהו נכון ותחזוקתי, ולהרחיב אותו.

> **זמן מומלץ: ~3 שעות.** לא מצופה מכם לסיים "מאה אחוז". אנחנו רוצים לראות **סדרי עדיפויות ושיקול דעת**, לא שלמות. אם נגמר הזמן — תעדו ב‑`DECISIONS.md` מה הייתם עושים בהמשך.

---

## מה יש בריפו

<div dir="ltr">

```
backend/              .NET 8 Web API + EF Core (PostgreSQL או SQLite) + xUnit / Testcontainers
frontend/             Angular 17 (standalone components)
docker-compose.yml    PostgreSQL + backend API
```

</div>

הקוד **עובד ומתקמפל** כמו שהוא — אבל יש בו בעיות בכוונה. אתם לא כותבים מאפס.

> **הערה על מסד הנתונים — שתי אפשרויות:**
> - **עם Docker (מומלץ):** `docker compose up` מרים PostgreSQL ואת ה‑API יחד. הסכימה נוצרת אוטומטית; אם שיניתם את המודל, `docker compose down -v` יבנה אותה מחדש.
> - **בלי Docker:** הגדירו `Database__Provider=Sqlite` וכל המערכת תרוץ מול קובץ SQLite מקומי. אם שיניתם את המודל, מחקו את `leave.db`.
>
> שתי האפשרויות תקפות לחלוטין — אפשר לבצע את כל המשימות בכל אחת מהן. שימו לב שלמשימה 2 (concurrency) יש הבדל: ל‑PostgreSQL יש טרנזקציות ונעילות אמיתיות, ל‑SQLite פחות. ציינו ב‑`DECISIONS.md` באיזו אפשרות עבדתם.

### הרצה

**הכל ביחד עם Docker** (הדרך המומלצת — צריך Docker):

<div dir="ltr">

```bash
docker compose up --build
# API + PostgreSQL עולים יחד.
# Swagger: http://localhost:5080/swagger
```

</div>

**להריץ רק את ה‑backend מקומית** (צריך .NET 8 SDK + Postgres זמין — למשל `docker compose up db`):

<div dir="ltr">

```bash
cd backend
dotnet run --project src/LeaveManagement.Api
```

</div>

**בלי Docker בכלל** — אותה מערכת מול SQLite מקומי:

<div dir="ltr">

```bash
cd backend
Database__Provider=Sqlite dotnet run --project src/LeaveManagement.Api
```

</div>

<div dir="ltr">

```powershell
# PowerShell
cd backend
$env:Database__Provider="Sqlite"; dotnet run --project src/LeaveManagement.Api
```

</div>

**Tests** — כברירת מחדל מול PostgreSQL דרך Testcontainers (צריך Docker פעיל):

<div dir="ltr">

```bash
cd backend
dotnet test

# בלי Docker — אותם טסטים מול SQLite:
Database__Provider=Sqlite dotnet test
```

</div>

**Frontend** (צריך Node 18+):

<div dir="ltr">

```bash
cd frontend
npm install
npm start
# http://localhost:4200
```

</div>

---

## המשימות

תעדפו לפי הסדר. עדיף 3 משימות שנעשו טוב מ‑6 חצי‑גמורות.

### חלק א' — Backend

1. **תיקון באג (חובה).** קיים באג בבדיקת יתרת ימי החופשה: עובד יכול להגיש בקשת חופשה שחורגת מהמכסה השנתית שלו, והמערכת מקבלת אותה. אתרו, תקנו, והוסיפו **טסט** שמוכיח את התיקון (יש שלד טסטים מוכן ב‑`tests/`).

2. **Endpoint חדש (חובה):** `POST /api/leave-requests/{id}/approve` שמאשר בקשה:
   - אי אפשר לאשר בקשה שכבר אושרה או נדחתה → החזירו קוד שגיאה מתאים.
   - בקשה לא קיימת → קוד מתאים.
   - חשבו על מה קורה אם שתי בקשות מאושרות "במקביל" ועלולות יחד לחרוג מהמכסה — תארו (ולפחות חלקית טפלו) ב‑`DECISIONS.md`. אם אתם עובדים מול PostgreSQL — יש לכם מסד נתונים אמיתי עם טרנזקציות, נצלו את זה. אם בחרתם SQLite — הסבירו מה הייתם עושים מול DB עם נעילות אמיתיות.

3. **שיפור ארכיטקטוני:** ה‑Controller כיום עושה הכול — גישה ל‑DB, לוגיקה עסקית ו‑validation. שפרו את הפרדת השכבות כפי שאתם רואים נכון. **בלי over‑engineering** — והסבירו את הבחירה.

### חלק ב' — Frontend

4. **טופס הגשת בקשה** עם validation: תאריך ההתחלה אינו מאוחר מתאריך הסיום, חובה לבחור סוג חופשה, אין מספר ימים שלילי. הציגו הודעות שגיאה ברורות.

5. **כפתור האישור** מחובר כרגע בצורה נאיבית (שולח POST ואז מרענן את כל הרשימה, בלי loading / error / success). שדרגו אותו לטיפול נכון: מצב טעינה, טיפול בשגיאה (למשל אישור שנכשל כי הבקשה כבר אושרה), ומשוב הצלחה — בלי `alert` גנרי ובלי רענון עיוור של הכל.

6. **שיפור הקומפוננטה:** הקומפוננטה הקיימת קוראת ל‑HTTP ישירות, מנהלת state ידנית ומשתמשת ב‑`any`. סדרו: service layer, טיפוסים אמיתיים, RxJS נקי בלי memory leaks.

### חלק ג' — שימוש ב‑AI (חשוב!)

אנחנו **מעודדים** שימוש ב‑AI (Copilot / Claude / ChatGPT / Cursor). מעניין אותנו **איך**. ב‑`DECISIONS.md`:

- **2–3 דוגמאות אמיתיות** של איפה AI עזר — כולל ה‑prompt.
- **דוגמה אחת לפחות שבה דחיתם או תיקנתם** הצעה של AI, ו**למה** הוא טעה.
- **בונוס אבטחה:** עברו על הקוד גם בעיניים של אבטחה. אם אתם מזהים בעיה — הסבירו מה הסיכון ותקנו.

---

## מה מגישים

1. **ריפו Git** עם היסטוריית קומיטים אמיתית (לא קומיט ענק אחד).
2. **`DECISIONS.md`** — מלאו את התבנית ב‑`DECISIONS.template.md`:
   - החלטות ארכיטקטוניות מרכזיות ולמה.
   - על מה ויתרתם בגלל הזמן ומה הייתם עושים עם עוד יום.
   - סעיף ה‑AI מחלק ג'.

בהצלחה — אנחנו מחכים לראות איך אתם חושבים 🚀

</div>

## מימוש המבחן

החלטות, הנחות עסקיות, תוצאות הבדיקות ומגבלות האימות מתועדות ב־[DECISIONS.md](DECISIONS.md).

- חישוב מכסה לפי ימים קלנדריים מאושרים בכל שנה, כולל בקשות שחוצות שנה.
- אישור בקשה עם בדיקת יתרה חוזרת ונעילת עובד ב־PostgreSQL.
- חיפוש פרמטרי, תגובות DTO ושגיאות ProblemDetails.
- טופס עם ולידציה ומצבי טעינה, שגיאה והצלחה; אישור מעדכן שורה ללא טעינה חוזרת של הרשימה.
- אין שינוי סכימה. באישור המשתמשת נוספה @angular/localize@17.3.12 עבור Angular i18n המובנה. PostgreSQL הוא היעד; לאחר התקנת Docker עברו כל 37 בדיקות השרת, כולל תחרותיות. הוראות לפורט המקומי 5433 מופיעות ב־DECISIONS.md.

### שפות הממשק

מתוך frontend:

```powershell
npm start           # English, http://localhost:4200
npm run start:he    # Hebrew + RTL, same port; stop English first
npm run extract-i18n
npm run build -- --configuration production  # Builds en-US and he
```

תרגומים: src/locale/messages.he.xlf. טקסט המקור באנגלית מסומן ב־i18n וב־$localize בקוד. אין שירות תרגום או pipe מותאמים; המנגנון הוא Angular i18n של Angular 17.

### עדכון אבטחה

ראו [SECURITY.md](SECURITY.md) לפירוט התיקונים והמגבלות שנותרו. פורטי Compose מוגבלים ל־localhost. Swagger זמין רק כאשר ASPNETCORE_ENVIRONMENT=Development; בהרצת Compose במצב Production הקישור המקורי לעיל אינו זמין. אין שינוי בסכימת הנתונים.
