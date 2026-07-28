<img src="src/SystevoTune.App/Assets/systevo-logo.png" width="72" alt="Systevo" align="left" />

# Systevo Tune

[![CI](https://github.com/aliahmed-soc/Systevo-Tune/actions/workflows/ci.yml/badge.svg)](https://github.com/aliahmed-soc/Systevo-Tune/actions/workflows/ci.yml)

A Windows PC tune-up tool with Gaming and Work profiles, in English and Arabic.
By [Systevo](https://systevo.vercel.app) — Infrastructure · Security · Automation.

**The promise the whole thing rests on: no change it makes can be permanent.**

> ### Status: not yet verified on a real machine
>
> The engine is complete and covered by 492 automated tests, but **it has never run on Windows.**
> Every registry path, service name and GUID it uses is tracked in
> [`windows-verified-paths`](.claude/skills/windows-verified-paths/SKILL.md) and split into what
> Microsoft documents and what it does not. Roughly half of what a tune-up tool touches has no
> public Microsoft reference at all.
>
> Do not run this on a machine you care about. The next step is
> [`docs/VM-CHECKLIST.md`](docs/VM-CHECKLIST.md) in a throwaway virtual machine.

---

## What it does

| | |
|---|---|
| **Cleanup** | Temp files, Windows Update cache, Recycle Bin. Scans and shows sizes before deleting anything. |
| **Power plan** | Balanced, High or Ultimate. Resolves what your PC actually has rather than assuming. |
| **Visual effects** | Animations and transparency off for Gaming, on for Work. |
| **Gaming** | Game Mode, Xbox Game Bar recording, hardware-accelerated GPU scheduling. |
| **Startup** | Lists what starts with Windows and switches items off. Never deletes them. |
| **Privacy** | Diagnostic data to required-only, Start menu suggestions and lock screen adverts off. |
| **Profiles** | Gaming and Work presets, or tick changes individually. |

## The undo promise

This is the part that matters, and it is built in rather than bolted on.

1. **Log first, change second.** Every change writes a JSON record — with the old value read from
   the live system — *before* it runs. If the app is killed mid-change, the log still knows.
2. **Nothing is guessed.** Services, paths and packages come from whitelist files. Anything not
   listed is never touched. Guards refuse Defender, the firewall, network, audio and printing,
   and your Documents, Desktop and Downloads folders — even if a whitelist file is edited to name
   them.
3. **Undo All puts everything back**, newest change first, restoring the value that was actually
   there rather than a Windows default.
4. **What cannot be undone says so, up front.** Deleted temp files do not come back, and the app
   tells you that before you tick the box, not after.

Logs live in `C:\ProgramData\SystevoTune\logs` — one JSON file per run, readable in any editor.

## Build

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download) specifically — 8.x — on Windows.
`global.json` pins it, so your machine and CI build with the same compiler; a 9 or 10 SDK on its
own will not do, and `dotnet` will tell you no compatible SDK was found.

```bash
dotnet build SystevoTune.sln
```

```bash
dotnet test SystevoTune.sln
```

### Portable build

Single self-contained `.exe`, no runtime install needed:

```bash
dotnet publish src/SystevoTune.App/SystevoTune.App.csproj -p:PublishProfile=Portable
```

```bash
dotnet publish src/SystevoTune.ConsoleRunner/SystevoTune.ConsoleRunner.csproj -p:PublishProfile=Portable
```

Output lands in `artifacts/publish/`.

### Verifying a build in a VM

The console runner can run the whole safety cycle end to end — snapshot, apply, undo, compare:

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- verify gaming --vm
```

Exit code 0 means the PC came back exactly as it started. `apply`, `undo` and `verify` refuse to
run without `--vm`, so a mistyped command on a real desktop does nothing.

## Screenshots

_Coming once the first VM click-through is done — the app has not been launched yet._

## Layout

```
src/SystevoTune.Engine          all the real logic, zero UI code
src/SystevoTune.App             WPF app, MVVM, dark theme
src/SystevoTune.ConsoleRunner   dev harness, holds the VM verify command
tests/                          492 tests, all against fakes
docs/                           the plan, decisions, and the VM checklist
```

## Licence and trust

Open source because a tool that edits system settings should be readable by the people running
it. Doc [08](docs/08-branding-launch.md) explains the rest of the thinking.

© 2026 Systevo

---

<div dir="rtl" lang="ar">

# سيستيفو تيون

أداة لتحسين أداء أجهزة ويندوز، بملفَّي "الألعاب" و"العمل"، بالعربية والإنجليزية.

**الوعد الذي يقوم عليه كل شيء: لا يوجد تغيير تجريه الأداة لا يمكن التراجع عنه.**

> ### الحالة: لم يتم التحقق منها على جهاز حقيقي بعد
>
> المحرك مكتمل ومغطى بـ 492 اختباراً آلياً، لكنه **لم يعمل قط على ويندوز.** كل مسار في الريجستري
> واسم خدمة ومعرّف GUID مسجَّل ومصنَّف حسب ما توثّقه مايكروسوفت وما لا توثّقه. نحو نصف ما تلمسه
> أداة كهذه ليس له مرجع رسمي من مايكروسوفت.
>
> لا تشغّلها على جهاز يهمّك. الخطوة التالية هي `docs/VM-CHECKLIST.md` داخل جهاز افتراضي مؤقت.

## ما الذي تفعله

تنظيف الملفات المؤقتة وذاكرة التحديثات وسلة المحذوفات، وتبديل خطة الطاقة، وإيقاف المؤثرات
المرئية، وضبط خصائص الألعاب، وإدارة تطبيقات بدء التشغيل، وتقليل بيانات التشخيص وإيقاف اقتراحات
قائمة ابدأ وإعلانات شاشة القفل.

## وعد التراجع

1. **السجل أولاً، ثم التغيير.** كل تغيير يُكتب في ملف JSON — ومعه القيمة القديمة مقروءة من الجهاز
   نفسه — **قبل** تنفيذه.
2. **لا شيء بالتخمين.** الخدمات والمسارات والحزم تأتي من ملفات قوائم بيضاء. وهناك حواجز ترفض
   ديفندر وجدار الحماية والشبكة والصوت والطباعة، ومجلدات المستندات وسطح المكتب والتنزيلات — حتى
   لو عُدِّل ملف القائمة ليضيفها.
3. **"تراجع عن كل شيء"** يعيد الأمور كما كانت، من الأحدث إلى الأقدم، ويستعيد القيمة التي كانت
   موجودة فعلاً لا القيمة الافتراضية لويندوز.
4. **ما لا يمكن التراجع عنه يُقال بوضوح مسبقاً.** الملفات المؤقتة المحذوفة لا تعود، والأداة تخبرك
   بذلك قبل الاختيار لا بعده.

السجلات في `C:\ProgramData\SystevoTune\logs` — ملف JSON لكل تشغيل، يمكن قراءته بأي محرر نصوص.

## البناء

يتطلب حزمة تطوير .NET 8.x تحديداً على ويندوز. ملف `global.json` يثبّت الإصدار، ليبني جهازك
والتكامل المستمر بالمترجم نفسه؛ ولن تكفي حزمة 9 أو 10 وحدها.

```
dotnet build SystevoTune.sln
dotnet test  SystevoTune.sln
```

© 2026 Systevo

</div>
