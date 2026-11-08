# Accuracy comparisons

## IslamicFinder Athan 4.5 — Sidon, Lebanon

The supplied reference screenshot uses:

- Local date: 2026-09-20
- Coordinates: 33.5631, 35.3689
- Time zone: GMT+3 with daylight saving enabled (`Asia/Beirut`)
- Method: Egyptian General Authority of Survey
- Asr: Standard/Shafi‘i

The user confirmed that IslamicFinder's only manual change was `Maghrib +5`; every other adjustment was zero. Removing that known correction gives the underlying comparison:

| Prayer | Screenshot | IslamicFinder raw | Raw Adhan | Engine difference |
|---|---:|---:|---:|---:|
| Fajr | 04:56 | 04:56 | 04:52 | +4 min |
| Sunrise | 06:25 | 06:25 | 06:25 | 0 min |
| Dhuhr | 12:33 | 12:33 | 12:33 | 0 min |
| Asr | 16:01 | 16:01 | 16:00 | +1 min |
| Maghrib | 18:44 | 18:39 | 18:38 | +1 min |
| Isha | 19:59 | 19:59 | 20:01 | -2 min |

Sunrise and Dhuhr match exactly, so the discrepancy is not caused by the coordinates, civil date, time zone, or daylight-saving conversion. Because the adjustment state is now known, the remaining values are differences between IslamicFinder's legacy engine and Adhan's Meeus implementation. Both identify the method as Egyptian, but a closed legacy executable does not expose enough information to isolate each difference further into solar formula, refraction constants, method parameters, or minute rounding.

The application must not silently modify the standard Egyptian preset to imitate one legacy timetable. To reproduce the final screenshot in this application, use `Fajr +4`, `Asr +1`, `Maghrib +6` (the one-minute engine difference plus IslamicFinder's configured five minutes), and `Isha -2`. `PrayerCalculatorTests.Calculate_SidonIslamicFinderReference_IsMatchedByExplicitCorrections` locks this comparison to the supplied date and coordinates.
